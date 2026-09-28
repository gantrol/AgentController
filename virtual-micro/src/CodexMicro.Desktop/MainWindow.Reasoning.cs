using System.Diagnostics;
using System.Windows.Input;
using CodexMicro.Desktop.Services;

namespace CodexMicro.Desktop;

public partial class MicroSurfaceWindow
{
    private readonly CodexDraftComposerModelSelector _reasoningSelector = new();
    private readonly EncoderStepAccumulator _reasoningSteps = new();
    private DialGestureTracker _reasoningWheel = new();
    private CancellationTokenSource? _reasoningCancellation;
    private bool _reasoningPumpRunning;
    private bool _reasoningAdjusting;
    private bool _settingsWheelDuringPress;
    private CancellationTokenSource? _encoderWarningCancellation;
    private long _lastEncoderReasoningAt;
    private volatile bool _encoderWarningPending;
    private CodexModelCatalog? _reasoningCatalog;
    private int _reasoningInputGeneration;
    private sealed record ReasoningPreview(
        string? ThreadId, string ModelId, string? Effort, bool AwaitingObservation = false);
    private ReasoningPreview? _reasoningPreview;
    private (CodexQuickModel Model, string? Effort)? _draftReasoningObservationCandidate;
    private long _draftReasoningFeedbackChangedAt;
    private CodexThreadModelState? _reasoningTarget;
    private CancellationTokenSource? _reasoningFeedbackCancellation;

    private CodexThreadModelState? ResolveReasoningTarget(string? threadId, int steps) =>
        !string.IsNullOrWhiteSpace(threadId) && ResolveReasoningEffort(threadId, steps) is { } effort
            ? new(threadId, _quickModel.Id, effort)
            : null;

    private string? ResolveReasoningEffort(string? threadId, int steps)
    {
        if (!QuickModelThreadIdsEqual(threadId, _quickModelThreadId) ||
            (string.IsNullOrWhiteSpace(threadId) &&
                (_draftQuickModelContext is not { } draft ||
                    _modelToggleService.CaptureForegroundDraftPresentationContext() != draft)))
        {
            return null;
        }

        var catalog = _reasoningCatalog is { IsFresh: true } cached
            ? cached
            : CodexModelCatalog.Load();
        if (!catalog.IsFresh || catalog.Find(_quickModel.Id) is not { Hidden: false } model)
        {
            return null;
        }

        _reasoningCatalog = catalog;
        var efforts = model.SupportedEfforts.ToList();
        var currentEffort = _reasoningPreview?.Effort ?? _quickModelEffort ?? model.DefaultEffort;
        var index = efforts.FindIndex(effort =>
            string.Equals(effort, currentEffort, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        return efforts[(int)Math.Clamp((long)index + steps, 0, efforts.Count - 1)];
    }

    private CancellationTokenSource? PreviewReasoningStep(
        string? threadId, int direction, bool awaitObservation = false)
    {
        if (ResolveReasoningEffort(threadId, direction) is not { } effort)
        {
            return null;
        }

        return ShowReasoningFeedback(new ReasoningPreview(
            threadId, _quickModel.Id, effort, awaitObservation));
    }

    private CancellationTokenSource ShowReasoningFeedback(CodexThreadModelState target) =>
        ShowReasoningFeedback(new ReasoningPreview(target.ThreadId, target.ModelId, target.Effort));

    private CancellationTokenSource ShowReasoningFeedback(ReasoningPreview target)
    {
        _reasoningPreview = target;
        _draftReasoningObservationCandidate = null;
        _draftReasoningFeedbackChangedAt = Stopwatch.GetTimestamp();
        var feedback = new CancellationTokenSource();
        var previous = _reasoningFeedbackCancellation;
        _reasoningFeedbackCancellation = feedback;
        previous?.Cancel();
        UpdateQuotaPresentation();
        _ = ExpireReasoningFeedbackAsync(feedback);
        return feedback;
    }

    private async Task ExpireReasoningFeedbackAsync(CancellationTokenSource feedback)
    {
        try
        {
            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(900), feedback.Token);
            }
            while (_reasoningTarget is not null || _reasoningAdjusting ||
                _encoderWarningPending || _reasoningPreview is { AwaitingObservation: true });
            ClearReasoningFeedback(feedback);
        }
        catch (OperationCanceledException) when (feedback.IsCancellationRequested)
        {
        }
        finally
        {
            feedback.Dispose();
        }
    }

    private void ClearReasoningFeedback(CancellationTokenSource? expected = null)
    {
        if (expected is not null && !ReferenceEquals(expected, _reasoningFeedbackCancellation))
        {
            return;
        }

        _reasoningPreview = null;
        _draftReasoningObservationCandidate = null;
        var feedback = _reasoningFeedbackCancellation;
        _reasoningFeedbackCancellation = null;
        feedback?.Cancel();
        if (feedback is not null && !_windowClosed)
        {
            UpdateQuotaPresentation();
        }
    }

    private async Task<bool> PrepareEncoderWarningWatchAsync(
        IntPtr window, CodexModelToggleService.ForegroundDraftLease? lease)
    {
        if (!_profileSettings.Current.AutoConfirmUltraFullAccess || window == IntPtr.Zero)
        {
            return true;
        }
        if (_encoderWarningCancellation is not null)
        {
            return !_encoderWarningPending;
        }

        var threadId = _modelToggleService.CurrentForegroundVisibleThreadId(window);
        if (lease is null && (string.IsNullOrWhiteSpace(threadId) ||
            CodexDraftModelToggleService.IsDraftThreadId(threadId)))
        {
            return true;
        }

        var cancellation = new CancellationTokenSource();
        _encoderWarningCancellation = cancellation;
        bool IsCurrent() => !cancellation.IsCancellationRequested && !_windowClosed &&
            _profileSettings.Current.AutoConfirmUltraFullAccess &&
            (lease is { } draft
                ? _modelToggleService.IsForegroundDraftLeaseCurrent(draft)
                : string.Equals(threadId,
                    _modelToggleService.CurrentForegroundVisibleThreadId(window),
                    StringComparison.Ordinal));
        try
        {
            if (!await _reasoningSelector.CanWatchNativeUltraAsync(window, IsCurrent, cancellation.Token))
            {
                _encoderWarningCancellation = null;
                cancellation.Dispose();
                return false;
            }

            _lastEncoderReasoningAt = 0;
            _ = ObserveEncoderWarningAsync(window, IsCurrent, cancellation);
            return true;
        }
        catch (Exception exception)
        {
            var canSend = IsCurrent() && CodexWindowActivator.IsForegroundWindow(window);
            _encoderWarningCancellation = null;
            cancellation.Dispose();
            Debug.WriteLine($"Encoder warning observation: {exception.Message}");
            return canSend;
        }
    }

    private async Task ObserveEncoderWarningAsync(
        IntPtr window, Func<bool> isCurrent, CancellationTokenSource cancellation)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            while (isCurrent() && CodexWindowActivator.IsForegroundWindow(window))
            {
                var deliveredAt = _lastEncoderReasoningAt;
                if (Stopwatch.GetElapsedTime(deliveredAt == 0 ? started : deliveredAt) >
                    TimeSpan.FromSeconds(8))
                {
                    return;
                }
                await Task.Delay(180, cancellation.Token);
                if (deliveredAt != 0 && await _reasoningSelector.TryConfirmNativeUltraAsync(
                    window, isCurrent, () =>
                    {
                        _encoderWarningPending = true;
                        Dispatcher.Invoke(() =>
                        {
                            _encoderSteps.Clear();
                            _reasoningSteps.Clear();
                            _reasoningTarget = null;
                            _reasoningCancellation?.Cancel();
                            _quickModelPresentationRevision++;
                            _draftReasoningObservationCandidate = null;
                        });
                    }, cancellation.Token))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Encoder warning observation: {exception.Message}");
        }
        finally
        {
            _encoderWarningPending = false;
            if (ReferenceEquals(_encoderWarningCancellation, cancellation))
            {
                _encoderWarningCancellation = null;
                _quickModelPresentationRevision++;
                _lastDraftQuickModelObservationAt = 0;
                _draftReasoningFeedbackChangedAt = Stopwatch.GetTimestamp();
                _draftReasoningObservationCandidate = null;
            }
            cancellation.Dispose();
        }
    }

    private void Settings_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueReasoningWheelDelta(e.Delta);
    }

    private void QueueReasoningWheelDelta(int delta)
    {
        if (_windowClosed || _pageSwitching || _quickModelSwitching ||
            _harnessModelSwitching || _encoderWarningPending)
        {
            return;
        }

        _settingsWheelDuringPress |= _settingsPointerDownTimestamp != 0;
        var steps = _reasoningWheel.AddWheelDelta(delta);
        if (steps == 0)
        {
            return;
        }

        if (IsCodexHarnessActive())
        {
            var window = CodexWindowActivator.CaptureForegroundWindow();
            var threadId = _modelToggleService.CurrentForegroundVisibleThreadId(window);
            var effortSteps = _dialDirectionSettings.ToReasoningSteps(steps);
            if (window != IntPtr.Zero &&
                ResolveReasoningTarget(threadId, effortSteps) is { } target)
            {
                if (_reasoningTarget != target)
                {
                    _reasoningTarget = target;
                }
                _reasoningSteps.Clear();
                ShowReasoningFeedback(target);
                StartReasoningStepPump();
                return;
            }

            if (_layoutObserver.Current.EncoderMode == "reasoning")
            {
                EnqueueEncoderSteps(steps, "旋钮滚轮");
                return;
            }
        }

        _reasoningSteps.Add(steps, Stopwatch.GetTimestamp());
        StartReasoningStepPump();
    }

    private void StartReasoningStepPump()
    {
        if (!_reasoningPumpRunning)
        {
            _reasoningPumpRunning = true;
            _ = RunDialInputSafelyAsync(PumpReasoningStepsAsync, "旋钮滚轮");
        }
    }

    private async Task PumpReasoningStepsAsync()
    {
        var generation = _reasoningInputGeneration;
        try
        {
            while (!_windowClosed && generation == _reasoningInputGeneration)
            {
                await System.Windows.Threading.Dispatcher.Yield(
                    System.Windows.Threading.DispatcherPriority.Background);
                if (_windowClosed || generation != _reasoningInputGeneration)
                {
                    return;
                }

                if (_reasoningTarget is { } target)
                {
                    await StepReasoningAsync(0, target);
                    continue;
                }

                var intent = _reasoningSteps.TakeNext(
                    Stopwatch.GetTimestamp(), ToStopwatchTicks(EncoderIntentMaximumAge));
                if (intent is null)
                {
                    return;
                }

                var started = Stopwatch.GetTimestamp();
                await StepReasoningAsync(intent.Value.Direction);
                if (generation == _reasoningInputGeneration &&
                    Stopwatch.GetElapsedTime(started) > EncoderIntentMaximumAge)
                {
                    _reasoningSteps.Clear();
                }
            }
        }
        finally
        {
            if (generation == _reasoningInputGeneration)
            {
                _reasoningSteps.Clear();
                _reasoningTarget = null;
            }
            _reasoningPumpRunning = false;
            if (!_windowClosed && (_reasoningTarget is not null || _reasoningSteps.Pending != 0))
            {
                StartReasoningStepPump();
            }
        }
    }

    private async Task StepReasoningAsync(int direction, CodexThreadModelState? target = null)
    {
        if (_quickModelSwitching || _reasoningAdjusting || _windowClosed)
        {
            _reasoningTarget = null;
            return;
        }

        if (!IsCodexHarnessActive())
        {
            var action = direction > 0
                ? MicroHarnessActionIds.ReasoningIncrease
                : MicroHarnessActionIds.ReasoningDecrease;
            await ExecuteHarnessActionAsync(ActiveHarness(), action,
                HarnessActionLabel(action), CurrentHarnessSessionId());
            return;
        }

        _reasoningAdjusting = true;
        var generation = _reasoningInputGeneration;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _reasoningCancellation = cancellation;
        CancellationTokenSource? feedback = target is null ? null : _reasoningFeedbackCancellation;
        var succeeded = false;
        UpdateQuotaPresentation();
        try
        {
            await _encoderInputGate.WaitAsync(cancellation.Token);
            try
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (target is not null && _reasoningTarget is { } latestTarget)
                {
                    target = latestTarget;
                    feedback = _reasoningFeedbackCancellation;
                }
                var window = CodexWindowActivator.CaptureForegroundWindow();
                if (window == IntPtr.Zero)
                {
                    return;
                }

                var threadId = _modelToggleService.CurrentForegroundVisibleThreadId(window);
                if (string.IsNullOrWhiteSpace(threadId))
                {
                    threadId = (await _modelToggleService.RefreshForegroundVisibleThreadSelectionAsync(
                        window, cancellation.Token)).VisibleThreadId;
                }
                if (target is not null && !QuickModelThreadIdsEqual(threadId, target.ThreadId))
                {
                    return;
                }
                var draft = string.IsNullOrWhiteSpace(threadId) ||
                    CodexDraftModelToggleService.IsDraftThreadId(threadId);
                var lease = draft
                    ? await _modelToggleService.CaptureForegroundDraftLeaseAsync(cancellation.Token, window)
                    : null;
                if (draft && lease is null)
                {
                    return;
                }

                bool IsCurrent() => !cancellation.IsCancellationRequested &&
                    generation == _reasoningInputGeneration &&
                    CodexWindowActivator.IsForegroundWindow(window) &&
                    (lease is { } captured
                        ? _modelToggleService.IsForegroundDraftLeaseCurrent(captured)
                        : string.Equals(threadId,
                            _modelToggleService.CurrentForegroundVisibleThreadId(window),
                            StringComparison.Ordinal));

                var effortStep = _dialDirectionSettings.ToReasoningSteps(Math.Sign(direction));
                if (!draft)
                {
                    var catalog = _reasoningCatalog ?? CodexModelCatalog.Load();
                    if (!catalog.IsFresh)
                    {
                        catalog = await CodexDraftModelToggleService.FetchModelCatalogAsync(cancellation.Token);
                    }
                    _reasoningCatalog = catalog;
                    feedback ??= target is null
                        ? PreviewReasoningStep(threadId, effortStep)
                        : ShowReasoningFeedback(target);
                    var state = target is null
                        ? await _modelToggleService.StepCurrentThreadEffortAsync(threadId!,
                            effortStep, catalog, IsCurrent, cancellation.Token)
                        : await _modelToggleService.SetCurrentThreadEffortAsync(target,
                            catalog, IsCurrent, cancellation.Token);
                    if (IsCurrent())
                    {
                        if (_reasoningTarget is null &&
                            ReferenceEquals(feedback, _reasoningFeedbackCancellation))
                        {
                            _reasoningPreview = null;
                        }
                        ApplyQuickModelPresentationState(new(threadId,
                            CodexModelToggleService.ParseModelId(state.ModelId)), state.Effort);
                        succeeded = true;
                    }
                    return;
                }

                feedback ??= target is null
                    ? PreviewReasoningStep(threadId, effortStep)
                    : ShowReasoningFeedback(target);
                var result = target is null
                    ? await _reasoningSelector.StepReasoningAsync(window,
                        effortStep, _profileSettings.Current.AutoConfirmUltraFullAccess,
                        IsCurrent, cancellation.Token)
                    : await _reasoningSelector.SetReasoningAsync(window, target,
                        _profileSettings.Current.AutoConfirmUltraFullAccess,
                        IsCurrent, cancellation.Token);
                if (IsCurrent())
                {
                    if (lease is { } captured)
                    {
                        _modelToggleService.TryPreserveForegroundDraftAfterReasoningStep(captured);
                    }

                    if (_reasoningTarget is null &&
                        ReferenceEquals(feedback, _reasoningFeedbackCancellation))
                    {
                        _reasoningPreview = null;
                    }
                    ApplyQuickModelPresentationState(new(threadId, result.Model), result.Effort);
                    succeeded = true;
                }
            }
            finally
            {
                _encoderInputGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _reasoningInputGeneration)
            {
                _encoderSteps.Clear();
                _reasoningSteps.Clear();
            }
        }
        catch
        {
            if (generation == _reasoningInputGeneration)
            {
                _encoderSteps.Clear();
                _reasoningSteps.Clear();
            }
            throw;
        }
        finally
        {
            _reasoningCancellation = null;
            _reasoningAdjusting = false;
            if (generation == _reasoningInputGeneration &&
                (!succeeded || ReferenceEquals(target, _reasoningTarget)))
            {
                _reasoningTarget = null;
                _reasoningPreview = null;
            }
            if (!succeeded && generation == _reasoningInputGeneration)
            {
                ClearReasoningFeedback();
            }
            if (!_windowClosed)
            {
                UpdateQuotaPresentation();
            }
        }
    }

    private void CancelReasoningInput()
    {
        _reasoningInputGeneration++;
        _reasoningTarget = null;
        ClearReasoningFeedback();
        _encoderWarningCancellation?.Cancel();
        _encoderSteps.Clear();
        _reasoningSteps.Clear();
        _reasoningWheel = new();
        _reasoningCancellation?.Cancel();
        _settingsPointerDownTimestamp = 0;
        _settingsWheelDuringPress = false;
    }
}
