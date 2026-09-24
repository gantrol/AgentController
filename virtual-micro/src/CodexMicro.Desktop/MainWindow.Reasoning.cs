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
        if (_windowClosed || _pageSwitching || _quickModelSwitching || _harnessModelSwitching)
        {
            return;
        }

        _settingsWheelDuringPress |= _settingsPointerDownTimestamp != 0;
        var steps = _reasoningWheel.AddWheelDelta(delta);
        if (steps == 0)
        {
            return;
        }

        if (IsCodexHarnessActive() && _layoutObserver.Current.EncoderMode == "reasoning")
        {
            EnqueueEncoderSteps(steps, "旋钮滚轮");
            return;
        }

        _reasoningSteps.Add(steps, Stopwatch.GetTimestamp());
        if (!_reasoningPumpRunning)
        {
            _reasoningPumpRunning = true;
            _ = RunDialInputSafelyAsync(PumpReasoningStepsAsync, "旋钮滚轮");
        }
    }

    private async Task PumpReasoningStepsAsync()
    {
        try
        {
            while (!_windowClosed)
            {
                var intent = _reasoningSteps.TakeNext(
                    Stopwatch.GetTimestamp(), ToStopwatchTicks(EncoderIntentMaximumAge));
                if (intent is null)
                {
                    return;
                }

                var started = Stopwatch.GetTimestamp();
                await StepReasoningAsync(intent.Value.Direction);
                if (Stopwatch.GetElapsedTime(started) > EncoderIntentMaximumAge)
                {
                    _reasoningSteps.Clear();
                }
            }
        }
        finally
        {
            _reasoningSteps.Clear();
            _reasoningPumpRunning = false;
        }
    }

    private async Task StepReasoningAsync(int direction)
    {
        if (_quickModelSwitching || _reasoningAdjusting || _windowClosed)
        {
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
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        _reasoningCancellation = cancellation;
        UpdateQuotaPresentation();
        try
        {
            await _encoderInputGate.WaitAsync(cancellation.Token);
            try
            {
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
                    (lease is { } captured
                        ? _modelToggleService.IsForegroundDraftLeaseCurrent(captured)
                        : string.Equals(threadId,
                            _modelToggleService.CurrentForegroundVisibleThreadId(window),
                            StringComparison.Ordinal));

                var increase = _dialDirectionSettings.ToReportedClockwise(direction > 0);
                if (!draft)
                {
                    var catalog = _reasoningCatalog ?? CodexModelCatalog.Load();
                    if (!catalog.IsFresh)
                    {
                        catalog = await CodexDraftModelToggleService.FetchModelCatalogAsync(cancellation.Token);
                    }
                    _reasoningCatalog = catalog;
                    var state = await _modelToggleService.StepCurrentThreadEffortAsync(threadId!,
                        increase ? 1 : -1, catalog, IsCurrent, cancellation.Token);
                    if (IsCurrent())
                    {
                        ApplyQuickModelPresentationState(new(threadId,
                            CodexModelToggleService.ParseModelId(state.ModelId)));
                    }
                    return;
                }

                var result = await _reasoningSelector.StepReasoningAsync(window,
                    increase ? 1 : -1, _profileSettings.Current.AutoConfirmUltraFullAccess,
                    IsCurrent, cancellation.Token);
                if (IsCurrent())
                {
                    if (lease is { } captured)
                    {
                        _modelToggleService.TryPreserveForegroundDraftAfterReasoningStep(captured);
                    }

                    ApplyQuickModelPresentationState(new(threadId, result.Model));
                }
            }
            finally
            {
                _encoderInputGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _encoderSteps.Clear();
            _reasoningSteps.Clear();
        }
        catch
        {
            _encoderSteps.Clear();
            _reasoningSteps.Clear();
            throw;
        }
        finally
        {
            _reasoningCancellation = null;
            _reasoningAdjusting = false;
            if (!_windowClosed)
            {
                UpdateQuotaPresentation();
            }
        }
    }

    private void CancelReasoningInput()
    {
        _reasoningInputGeneration++;
        _encoderWarningCancellation?.Cancel();
        _encoderSteps.Clear();
        _reasoningSteps.Clear();
        _reasoningWheel = new();
        _reasoningCancellation?.Cancel();
        _settingsPointerDownTimestamp = 0;
        _settingsWheelDuringPress = false;
    }
}
