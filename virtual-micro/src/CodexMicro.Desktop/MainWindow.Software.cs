using CodexMicro.Desktop.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop;

public partial class MicroSurfaceWindow
{
    private readonly CodexSelectedThreadReader _softwareSelectionReader = new();
    private bool _softwareSelectionReading;
    private readonly Func<Task<bool>> _activateSoftwareApplication;
    private readonly Func<CancellationToken, Task<string?>> _readSoftwareSelection;
    private long _softwareSelectionGeneration;
    private long _softwareNavigationVersion;
    private bool _softwareNavigationPending;
    private string? _softwareNavigationTarget;
    private readonly Dictionary<string, int> _softwareFastPending = new(StringComparer.Ordinal);
    private long _softwareActivityVersion;
    private bool? _softwareConnected;

    private void RestoreSoftwareActivityIdle()
    {
        if (_softwareConnected == true)
            SetLed(ActivityLed, "#9EBDFF", "Idle", title: "Activity");
        else
            SetLed(ActivityLed, NeutralStatusLed, "Idle", title: "Activity");
    }

    private async Task ClearSoftwareActivityAsync(long version)
    {
        await Task.Delay(650);
        if (!_windowClosed && version == _softwareActivityVersion)
            RestoreSoftwareActivityIdle();
    }

    private void ApplyCoreSurface()
    {
        if (!_broker.UsesSoftwareControl) return;
        ActionKey12.ContextMenu = null;
        SettingsMenuItem.Items.Clear();
        SettingsMenuItem.Click += OpenSoftwareSettingsMenuItem_Click;
        KnobSettingsMenuItem.Items.Clear();
        KnobSettingsMenuItem.Click += OpenSoftwareSettingsMenuItem_Click;
        ApplySoftwareConnectionState(_broker.IsReady);
    }

    private async Task ConnectSoftwareAsync()
    {
        if (_connecting || _windowClosed) return;
        _connecting = true;
        try
        {
            var info = await _broker.RecoverCodexLinkAsync();
            if (_windowClosed) return;
            _transportName = info.TransportName;
            ApplySoftwareConnectionState(true);
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            if (_windowClosed) return;
            ApplySoftwareConnectionState(false);
            SetStatus(error.Message);
        }
        finally { _connecting = false; }
    }

    private void ApplySoftwareConnectionState(bool connected)
    {
        var connectionChanged = _softwareConnected != connected;
        _softwareConnected = connected;
        var synchronized = connected && !_softwareNavigationPending &&
            ((_modelToggleService.CurrentThreadState is { } state &&
              state.ThreadId == _modelToggleService.CurrentVisibleThreadId) ||
             (_draftQuickModelContext is { } draft && CaptureDraftPresentationContext() == draft &&
              _draftQuickModelSelection is not null));
        SetLed(RuntimeLed, synchronized ? "#9EBDFF" : "#FFD66E",
            synchronized ? "Synced" : "Waiting", title: "Chat");
        SetLed(DriverLed, connected ? "#9EBDFF" : "#FFD66E",
            connected ? "Connected" : "Disconnected", title: "Codex IPC");
        if (connectionChanged) RestoreSoftwareActivityIdle();
    }

    internal void SelectSoftwareThread(string? threadId)
    {
        if (!_broker.UsesSoftwareControl || (threadId is not null && !Guid.TryParse(threadId, out _))) return;
        // Keep the last confirmed selection while independently showing navigation progress.
        _softwareNavigationPending = true;
        _softwareNavigationTarget = threadId;
        ++_softwareSelectionGeneration;
        var version = ++_softwareNavigationVersion;
        RefreshCurrentCodexThreadPresentation();
        _ = ConfirmSoftwareNavigationAsync(version);
    }

    private static string? ResolveSoftwareThreadId(string? visibleThreadId) => visibleThreadId;

    private void RefreshSoftwareThreadSelection() => _ = ReadSoftwareThreadSelectionAsync();

    private async Task ReadSoftwareThreadSelectionAsync()
    {
        if (!_broker.UsesSoftwareControl || _softwareSelectionReading || _windowClosed) return;
        _softwareSelectionReading = true;
        var generation = _softwareSelectionGeneration;
        try
        {
            var threadId = await _readSoftwareSelection(CancellationToken.None);
            if (_windowClosed || generation != _softwareSelectionGeneration) return;
            var changed = _modelToggleService.CurrentVisibleThreadId != threadId;
            var draft = threadId is null
                ? await _draftComposerModelSelector.CaptureDraftContextAsync(_softwareDraft, CancellationToken.None)
                : null;
            if (_windowClosed || generation != _softwareSelectionGeneration) return;
            if (draft?.Presentation != _softwareDraft?.Presentation) CancelReasoningInput();
            changed |= draft != _softwareDraft;
            _softwareDraft = draft;
            if (_softwareNavigationPending && threadId == _softwareNavigationTarget)
            {
                changed = true;
                _softwareNavigationPending = false;
                _manualUnreadThreads.Clear(threadId);
                SetLed(ActivityLed, "#74D9A0", "Opened", title: "Activity");
            }
            if (_modelToggleService.CurrentVisibleThreadId != threadId)
            {
                _modelToggleService.ObserveSelectedThread(threadId);
            }
            if (changed)
            {
                ++_softwareSelectionGeneration;
                RefreshCurrentCodexThreadPresentation();
            }
        }
        catch (OperationCanceledException) { }
        finally { _softwareSelectionReading = false; }
    }

    private async Task ConfirmSoftwareNavigationAsync(long version)
    {
        // Shell URI dispatch may navigate a background Electron window without raising it.
        // Raise the actual main window so its selection and accessibility state refresh.
        await _activateSoftwareApplication();
        if (_windowClosed || version != _softwareNavigationVersion) return;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!_windowClosed && version == _softwareNavigationVersion && _softwareNavigationPending)
        {
            await ReadSoftwareThreadSelectionAsync();
            if (!_softwareNavigationPending || version != _softwareNavigationVersion || _windowClosed) return;
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(4))
            {
                _softwareNavigationPending = false;
                SetLed(ActivityLed, "#FFD66E", "Navigation unconfirmed", title: "Activity");
                RefreshCurrentCodexThreadPresentation();
                return;
            }
            await Task.Delay(60);
        }
    }

    private async Task<bool> ValidateSoftwareTargetAsync(string? expectedThreadId, CancellationToken token)
    {
        if (_windowClosed || _softwareNavigationPending) return false;
        var generation = _softwareSelectionGeneration;
        if (expectedThreadId is null)
        {
            if (_softwareDraft is not { } draft) return false;
            var current = await _draftComposerModelSelector.CaptureDraftContextAsync(draft, token);
            return !_windowClosed && !_softwareNavigationPending && generation == _softwareSelectionGeneration &&
                current is not null && current.Presentation == draft.Presentation;
        }
        var selected = await _readSoftwareSelection(token);
        if (_windowClosed || _softwareNavigationPending || generation != _softwareSelectionGeneration) return false;
        _modelToggleService.ObserveSelectedThread(selected);
        RefreshCurrentCodexThreadPresentation();
        return selected == expectedThreadId;
    }

    private MicroControlContext CaptureSoftwareContext()
    {
        var threads = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var slot = 0; slot < 6; slot++)
        {
            if (_latestAgentRoster?.GetSlot(slot)?.ThreadId is { } thread)
                threads[$"AG{slot:00}"] = thread;
        }
        return new MicroControlContext(
            _softwareNavigationPending ? null : CurrentCodexAgentThreadId(), threads,
            _layoutObserver.Current, _profileSettings.Current, _softwareSelectionGeneration);
    }

    private async Task HandleSoftwareKeyAsync(string key, bool agentKey)
    {
        var action = agentKey ? null : _layoutObserver.Current.GetSlot(key).ResolvedAction;
        if (action is "composer.increaseReasoningEffort" or "composer.decreaseReasoningEffort")
        {
            // Key bindings use semantic directions; only physical dial input is inverted.
            QueueReasoningSteps(action == "composer.increaseReasoningEffort" ? 1 : -1);
            return;
        }

        // A single Agent tap must navigate; the old focus preference must not consume it.
        var fast = action == "composer.toggleFastMode";
        var fastThread = fast ? CurrentCodexAgentThreadId() : null;
        if (fast)
        {
            if (_actionKeys.TryGetValue(key, out var presentation))
            {
                var scale = new ScaleTransform(1, 1);
                presentation.Icon.RenderTransformOrigin = new Point(.5, .5);
                presentation.Icon.RenderTransform = scale;
                var press = new DoubleAnimation(1, .82, TimeSpan.FromMilliseconds(95))
                { AutoReverse = true, FillBehavior = FillBehavior.Stop };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, press);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, press);
            }
            if (fastThread is not null)
                _softwareFastPending[fastThread] = _softwareFastPending.GetValueOrDefault(fastThread) + 1;
            RefreshSoftwareFeedback();
        }
        try
        {
            await RunActionAsync(() => _broker.TapKeyAsync(key), key);
        }
        finally
        {
            if (fast)
            {
                if (fastThread is not null && --_softwareFastPending[fastThread] == 0)
                    _softwareFastPending.Remove(fastThread);
                RefreshSoftwareFeedback();
            }
        }
    }

    private void RefreshSoftwareFeedback()
    {
        if (!_broker.UsesSoftwareControl || _windowClosed) return;
        ApplySoftwareConnectionState(_broker.IsReady);
        var current = _modelToggleService.CurrentVisibleThreadId;
        var state = _modelToggleService.CurrentThreadState;
        foreach (var (key, presentation) in _actionKeys)
        {
            if (_layoutObserver.Current.GetSlot(key).ResolvedAction != "composer.toggleFastMode") continue;
            var pending = current is not null && _softwareFastPending.ContainsKey(current);
            presentation.Icon.IconBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
                pending ? "#C28B21" : state?.ThreadId == current && CodexServiceTier.IsFast(state?.ServiceTier) ? "#14876D" : "#171717"));
        }
        for (var slot = 0; slot < _agentKeys.Length; slot++)
            _agentKeys[slot].Opacity = SoftwareThreadFeedbackPending(_latestAgentRoster?.GetSlot(slot)?.ThreadId) ? .65 : 1;
        foreach (var key in _monitorKeys)
            key.Opacity = key.Tag is MonitorTask task && SoftwareThreadFeedbackPending(task.Id) ? .65 : 1;
    }

    private bool SoftwareThreadFeedbackPending(string? threadId) => threadId is not null &&
        ((_softwareNavigationPending && _softwareNavigationTarget == threadId) ||
         (_manualUnreadThreads.IsUnread(threadId) && !_manualUnreadThreads.IsConfirmed(threadId)));
}
