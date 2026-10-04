using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Agents;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Services;
using CodexController.Agents.Codex;
using CodexController.ViewModels;
using CodexController.Views;

namespace CodexController;

public partial class MainWindow
{
    private readonly RadialActionConfirmationState _tutorialConfirmation = new();
    private readonly DispatcherTimer _tutorialConfirmationTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private readonly HashSet<RadialInputAction> _tutorialAvailableActions = [];
    private TutorialVoiceSession? _tutorialVoiceSession;
    private ControllerInputButton? _tutorialVoiceButton => _tutorialVoiceSession?.Button;
    private bool _tutorialVoiceAvailable;
    private bool _tutorialClosing;

    private sealed class TutorialVoiceSession(
        ControllerInputButton button, IComposerAutomation composer, AppSettings settings,
        Task<ComposerAutomationResult> start)
    {
        internal ControllerInputButton Button { get; } = button;
        internal IComposerAutomation Composer { get; } = composer;
        internal AppSettings Settings { get; } = settings;
        internal Task<ComposerAutomationResult> Start { get; } = start;
        internal Task? Stop { get; set; }
    }
    private ControllerInputButton? _tutorialConsumedButton;
    private bool _tutorialVoiceReleasePending;
    private bool _tutorialExecuting;
    private bool _tutorialDialActive;
    private bool _tutorialAvailabilityPending;
    private long _tutorialAvailabilityAt;

    private void InitializeTutorialInput()
    {
        CommandBindings.Add(new CommandBinding(ControllerInputButton.InputCommand,
            (_, e) => e.Handled = true,
            (_, e) =>
            {
                e.CanExecute = e.Parameter is TutorialInput input && CanUseTutorialInput(input);
                e.Handled = true;
            }));
        DevicePage.AddHandler(ControllerInputButton.InputEvent,
            new EventHandler<TutorialInputEventArgs>(TutorialInputChanged));
        _tutorialConfirmationTimer.Tick += (_, _) => ResetTutorialConfirmation();
        _devicePageViewModel.Tutorial.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(_devicePageViewModel.Tutorial.Mode)) return;
            ResetTutorialConfirmation();
            DevicePage.HighlightTutorialInput(null);
            CommandManager.InvalidateRequerySuggested();
            _ = RefreshTutorialAvailabilityAsync(force: true);
        };
        DevicePage.IsVisibleChanged += (_, _) =>
        {
            if (DevicePage.IsVisible) return;
            ResetTutorialConfirmation();
            foreach (var button in ControllerTutorialView.InputButtons(DevicePage)) button.CancelPress();
        };
        Deactivated += (_, _) => ResetTutorialConfirmation();
        DevicePage.SidebarSelectionChanged += (_, _) => ResetTutorialConfirmation();
        Closed += (_, _) => _tutorialConfirmationTimer.Stop();
        Activated += (_, _) => _ = RefreshTutorialAvailabilityAsync(force: true);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_tutorialConfirmationTimer.IsEnabled) return;
            ResetTutorialConfirmation();
            e.Handled = true;
        };
    }

    private bool CanUseTutorialInput(TutorialInput input)
    {
        if (input.IsLayerSelector) return true;
        if (input.Layer is null && input.Input is LogicalInput.Menu or LogicalInput.View) return !_tutorialExecuting;
        if (!_settings.BridgeEnabled) return false;
        if (input.IsVoice)
            return !_tutorialExecuting && !_tutorialVoiceReleasePending &&
                !_pushToTalkAutomation.WantsDictation && !_pushToTalkAutomation.IsDictating &&
                (_tutorialVoiceButton is not null || _tutorialVoiceAvailable);
        if (_tutorialExecuting || _tutorialVoiceButton is not null || _tutorialVoiceReleasePending) return false;
        if (input.Layer is { })
        {
            var slot = RadialInputMap.AgentSlotIndex(input.Action);
            if (slot >= 0) return _snapshot.Threads.Count > slot;
            if (input.Action is RadialInputAction.Approve or RadialInputAction.Decline or
                RadialInputAction.Queue or RadialInputAction.Steer or RadialInputAction.BeginStopHold)
                return _tutorialAvailableActions.Contains(input.Action);
            return input.Action != RadialInputAction.None;
        }
        return input.Input switch
        {
            LogicalInput.FaceSouth => DevicePage.SelectedEntry is not null,
            LogicalInput.LeftStick => input.Direction is >= 1 and <= 4,
            LogicalInput.RightStick => input.Direction is >= 1 and <= 4 &&
                _activeAgent.Composer is not null,
            LogicalInput.DPadLeft or LogicalInput.DPadRight or LogicalInput.Guide => false,
            _ => true,
        };
    }

    private async void TutorialInputChanged(object? sender, TutorialInputEventArgs e)
    {
        e.Handled = true;
        if (e.Phase is TutorialInputPhase.Highlighted or TutorialInputPhase.Unhighlighted)
        {
            DevicePage.HighlightTutorialInput(e.Phase == TutorialInputPhase.Highlighted ? e.Input : null);
            return;
        }
        if (e.Phase is TutorialInputPhase.Ended or TutorialInputPhase.Canceled)
        {
            if (ReferenceEquals(_tutorialVoiceButton, e.Button)) await EndTutorialVoiceAsync();
            if (e.Phase == TutorialInputPhase.Canceled && ReferenceEquals(_tutorialConsumedButton, e.Button))
                _tutorialConsumedButton = null;
            return;
        }
        if (!CanUseTutorialInput(e.Input)) return;
        try
        {
            if (e.Phase == TutorialInputPhase.Started)
            {
                if (_tutorialConfirmation.CancelUnless(e.Input.Action))
                {
                    _tutorialConfirmationTimer.Stop();
                    UpdateTutorialConfirmation();
                }
                _tutorialConsumedButton = null;
                if (e.Input.IsVoice) await BeginTutorialVoiceAsync(e.Button);
                else if (e.Input.Layer is null && e.Input.Input == LogicalInput.FaceEast && TryTutorialLocalCancel())
                    _tutorialConsumedButton = e.Button;
                return;
            }
            if (ReferenceEquals(_tutorialConsumedButton, e.Button)) return;
            await ExecuteTutorialInputAsync(e.Input, e.Phase == TutorialInputPhase.Held);
        }
        catch (Exception)
        {
            ShowFeedback(_devicePageViewModel.Tutorial.InputName(e.Input.Input),
                ExecutionFailureLabel(AgentAutomationErrorCodes.Unexpected));
        }
    }

    private async Task ExecuteTutorialInputAsync(TutorialInput input, bool held)
    {
        if (input.IsLayerSelector)
        {
            var mode = input.Input switch
            {
                LogicalInput.LeftShoulder => ControllerTutorialMode.Agent,
                LogicalInput.RightShoulder => ControllerTutorialMode.Command,
                _ => ControllerTutorialMode.Turn,
            };
            _devicePageViewModel.Tutorial.SelectMode(mode);
            return;
        }
        if (input.Action == RadialInputAction.Cancel)
        {
            _devicePageViewModel.Tutorial.SelectMode(ControllerTutorialMode.Overview);
            return;
        }
        if (input.Action is RadialInputAction.Approve or RadialInputAction.ClearComposer)
        {
            if (!_tutorialConfirmation.TryConfirm(input.Action))
            {
                _tutorialConfirmationTimer.Stop();
                _tutorialConfirmationTimer.Start();
                UpdateTutorialConfirmation();
                return;
            }
        }
        ResetTutorialConfirmation();
        if (input.Layer is null)
        {
            switch (input.Input)
            {
                case LogicalInput.Menu: WakeCodex(); return;
                case LogicalInput.View: SwitchActiveAgent(); return;
                case LogicalInput.LeftStickPress: CycleRootSidebarScope(); return;
                case LogicalInput.LeftStick:
                    if (input.Direction is 1 or 3) MoveSidebarSelection(input.Direction == 1 ? -1 : 1);
                    else NavigateSidebarHorizontal(input.Direction == 4 ? -1 : 1);
                    return;
                case LogicalInput.RightStickPress when held:
                    ShowPage(SettingsPage);
                    SetSelectedNav(SettingsNavButton);
                    return;
                case LogicalInput.FaceNorth:
                    _devicePageViewModel.Tutorial.SelectMode(ControllerTutorialMode.Action);
                    return;
            }
        }
        if (input.Action == RadialInputAction.BeginStopHold && !held) return;
        if (input.Layer is null && input.Input == LogicalInput.FaceEast && !held &&
            !IsVirtualDialContextActive && !_composerPickerMenuLikelyOpen) return;

        var agent = _activeAgent;
        _tutorialExecuting = true;
        try
        {
            // Transfer foreground only after the tap or hold gesture has completed.
            if (!agent.Presence.IsForeground)
            {
                var activated = await Task.Run(agent.Presence.Wake);
                if (!activated || !agent.Presence.IsForeground)
                {
                    ShowFeedback(agent.DisplayName, ExecutionFailureLabel(AgentAutomationErrorCodes.AgentNotForeground));
                    return;
                }
            }
            if (_activeAgent != agent || !_settings.BridgeEnabled || !agent.Presence.IsForeground) return;
            if (input.Layer is not null)
            {
                switch (input.Action)
                {
                    case RadialInputAction.Approve:
                        await ExecuteUiCommandActionAsync(ApprovalActionContract.AcceptId,
                            RadialText("接受更改", "Approve changes"), "tutorial.approve", "tutorial.command", ActionSafetyLevel.HighRisk);
                        break;
                    case RadialInputAction.ClearComposer: await ClearComposerAsync(); break;
                    case RadialInputAction.BeginStopHold: await StopCurrentTurnAsync(); break;
                    case RadialInputAction.ToggleFast: await ExecuteFastToggleAsync(); break;
                    case RadialInputAction.Queue:
                        await ExecuteUiCommandActionAsync(TurnActionContract.QueueId,
                            RadialText("排到下一轮", "Queue next turn"), "tutorial.queue", "tutorial.turn");
                        break;
                    case RadialInputAction.Steer:
                        await ExecuteUiCommandActionAsync(TurnActionContract.SteerId,
                            RadialText("加入当前运行", "Steer current turn"), "tutorial.steer", "tutorial.turn");
                        break;
                    case RadialInputAction.Fork: await ExecuteForkActionAsync(); break;
                    case RadialInputAction.NewTask: await ExecuteNewTaskActionAsync(); break;
                    case RadialInputAction.Dispatch: await SendPromptAsync(Glyph(LogicalInput.Menu), "tutorial.dispatch"); break;
                    case RadialInputAction.Decline:
                        await ExecuteUiCommandActionAsync(ApprovalActionContract.DeclineId,
                            RadialText("拒绝更改", "Decline changes"), "tutorial.decline", "tutorial.command");
                        break;
                    case RadialInputAction.NavigateForward:
                        await ExecuteActionPanelActionAsync(NavigationActionContract.ForwardId,
                            RadialText("前进", "Forward"), "tutorial.forward");
                        break;
                    case RadialInputAction.NavigateBack:
                        await ExecuteActionPanelActionAsync(NavigationActionContract.BackId,
                            RadialText("后退", "Back"), "tutorial.back");
                        break;
                    case RadialInputAction.ToggleSidebar:
                        await ExecuteActionPanelActionAsync(SidebarActionContract.ToggleId,
                            RadialText("切换侧边栏", "Toggle sidebar"), "tutorial.sidebar");
                        break;
                    default: ExecuteRadialAction(input.Action); break;
                }
                return;
            }
            switch (input.Input)
            {
                case LogicalInput.FaceSouth:
                    OpenSelectedSidebarTask(deviceId: "tutorial", controlId: "tutorial.open"); break;
                case LogicalInput.FaceWest: await SendPromptAsync(Glyph(input.Input), "tutorial.send"); break;
                case LogicalInput.FaceEast:
                    if (IsVirtualDialContextActive || _composerPickerMenuLikelyOpen)
                    {
                        await CloseVirtualDialMenuAsync(showFeedback: true, fallbackToBaseCancel: false);
                        if (!IsVirtualDialContextActive) _tutorialDialActive = false;
                    }
                    else if (held) await StopCurrentTurnAsync();
                    break;
                case LogicalInput.RightStickPress:
                    _tutorialDialActive = true;
                    HandleComposerDialShortPress();
                    break;
                case LogicalInput.RightStick:
                    _tutorialDialActive = true;
                    QueueVirtualDialEncoderSteps(input.Direction is 1 or 4 ? -1 : 1); break;
                case LogicalInput.DPadUp:
                case LogicalInput.DPadDown:
                    if (held)
                        await ExecuteConversationBoundaryHoldAsync(input.Input == LogicalInput.DPadUp
                            ? ConversationBoundary.Top : ConversationBoundary.Bottom);
                    else
                    {
                        var action = input.Input == LogicalInput.DPadUp
                            ? ConversationTurnInputAction.PreviousUserMessage : ConversationTurnInputAction.NextUserMessage;
                        await NavigateConversationTurnAsync(action, ConversationTurnInputMap.ActionIdFor(action)!.Value);
                    }
                    break;
            }
        }
        finally
        {
            _tutorialExecuting = false;
            CommandManager.InvalidateRequerySuggested();
            _ = RefreshTutorialAvailabilityAsync(force: true);
        }
    }

    private bool TryTutorialLocalCancel()
    {
        if (_tutorialConfirmationTimer.IsEnabled) { ResetTutorialConfirmation(); return true; }
        if (_dictationInjected || _pushToTalkAutomation.WantsDictation || _composerPickerCancellation is not null)
        { CancelAction(); return true; }
        if (IsVirtualDialContextActive || _composerPickerMenuLikelyOpen) return false;
        return _threadNavigation.TryRequestUndo();
    }

    private async Task BeginTutorialVoiceAsync(ControllerInputButton button)
    {
        if (_tutorialVoiceSession is not null) return;
        // Direct named actions can operate without transferring foreground or injecting keys.
        var settings = new AppSettings { BridgeEnabled = true, OnlyWhenCodexForeground = false };
        var composer = _composerAutomation;
        var session = new TutorialVoiceSession(button, composer, settings,
            composer.InvokeActionAsync(settings, BridgeTimings.DictationStartTimeoutMs,
                CancellationToken.None, PushToTalkAutomationPolicy.StartActionNames.ToArray()));
        _tutorialVoiceSession = session;
        _devicePageViewModel.UpdateVoiceState(VoicePresentationState.Starting);
        try
        {
            var result = await session.Start;
            if (!result.Succeeded)
            {
                button.CancelPress();
                ShowFeedback(_localization.Strings.ConfigDictation, ExecutionFailureLabel(result.Error));
                await EndTutorialVoiceAsync();
            }
            else if (ReferenceEquals(_tutorialVoiceSession, session) && !_tutorialVoiceReleasePending)
            {
                DevicePage.SetVoiceHalo(true);
                _devicePageViewModel.UpdateVoiceState(VoicePresentationState.Recording);
            }
        }
        catch (Exception)
        {
            button.CancelPress();
            await EndTutorialVoiceAsync();
        }
    }

    private Task EndTutorialVoiceAsync()
    {
        var session = _tutorialVoiceSession;
        if (session is null) return Task.CompletedTask;
        return session.Stop ??= StopTutorialVoiceAsync(session);
    }

    private async Task StopTutorialVoiceAsync(TutorialVoiceSession session)
    {
        _tutorialVoiceReleasePending = true;
        _devicePageViewModel.UpdateVoiceState(VoicePresentationState.Stopping);
        var outcome = VoicePresentationState.Idle;
        var started = false;
        try
        {
            var start = await session.Start;
            if (!start.Succeeded)
            {
                outcome = VoicePresentationState.StartFailed;
                return;
            }
            started = true;
            ComposerAutomationResult? result = null;
            var stopped = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                result = await session.Composer.InvokeActionAsync(session.Settings,
                    BridgeTimings.DictationStopTimeoutMs, CancellationToken.None,
                    PushToTalkAutomationPolicy.StopActionNames.ToArray());
                stopped = result.Succeeded || await Task.Run(() => session.Composer.IsActionAvailable(
                    PushToTalkAutomationPolicy.StartActionNames.ToArray()));
                if (stopped) break;
                if (attempt < 2) await Task.Delay(BridgeTimings.MicroReleaseRetryDelayMs);
            }
            if (!stopped && result is { Succeeded: false })
            {
                outcome = VoicePresentationState.StopFailed;
                ShowFeedback(_localization.Strings.ConfigDictation, ExecutionFailureLabel(result.Error));
            }
        }
        catch (Exception)
        {
            outcome = started ? VoicePresentationState.StopFailed : VoicePresentationState.StartFailed;
            ShowFeedback(_localization.Strings.ConfigDictation,
                ExecutionFailureLabel(AgentAutomationErrorCodes.Unexpected));
        }
        finally
        {
            if (ReferenceEquals(_tutorialVoiceSession, session)) _tutorialVoiceSession = null;
            _tutorialVoiceReleasePending = false;
            DevicePage.SetVoiceHalo(outcome == VoicePresentationState.StopFailed);
            _devicePageViewModel.UpdateVoiceState(outcome);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CancelTutorialInteractions()
    {
        ResetTutorialConfirmation();
        foreach (var button in ControllerTutorialView.InputButtons(DevicePage)) button.CancelPress();
        _tutorialAvailableActions.Clear();
        _tutorialVoiceAvailable = false;
        _tutorialAvailabilityAt = 0;
    }

    private void ResetTutorialConfirmation()
    {
        _tutorialConfirmationTimer.Stop();
        _tutorialConfirmation.Reset();
        UpdateTutorialConfirmation();
    }

    private void UpdateTutorialConfirmation()
    {
        foreach (var button in ControllerTutorialView.InputButtons(DevicePage))
        {
            button.IsConfirmationPending = _tutorialConfirmation.IsPending(button.Request.Action);
            System.Windows.Automation.AutomationProperties.SetItemStatus(button,
                button.IsConfirmationPending
                    ? _localization.Strings.Get(StringKeys.ControlPendingConfirmation)
                    : string.Empty);
        }
    }

    private async Task RefreshTutorialAvailabilityAsync(bool force = false)
    {
        if (_tutorialAvailabilityPending || !DevicePage.IsVisible || !IsActive ||
            !force && Environment.TickCount64 - _tutorialAvailabilityAt < 2000) return;
        _tutorialAvailabilityPending = true;
        var agent = _activeAgent;
        var composer = _composerAutomation;
        try
        {
            var available = await Task.Run(() =>
            {
                var actions = new HashSet<RadialInputAction>();
                if (composer.IsActionAvailable(CodexUiCommandActionExecutor.ActionNamesFor(TurnActionContract.QueueId)!)) actions.Add(RadialInputAction.Queue);
                if (composer.IsActionAvailable(CodexUiCommandActionExecutor.ActionNamesFor(TurnActionContract.SteerId)!)) actions.Add(RadialInputAction.Steer);
                if (composer.IsActionAvailable(PlanModeAutomationPolicy.RunningActionNames.ToArray())) actions.Add(RadialInputAction.BeginStopHold);
                if (composer.IsActionAvailable(CodexUiCommandActionExecutor.ActionNamesFor(ApprovalActionContract.AcceptId)!)) actions.Add(RadialInputAction.Approve);
                if (composer.IsActionAvailable(CodexUiCommandActionExecutor.ActionNamesFor(ApprovalActionContract.DeclineId)!)) actions.Add(RadialInputAction.Decline);
                var voice = composer.IsActionAvailable(PushToTalkAutomationPolicy.StartActionNames.ToArray());
                return (actions, voice);
            });
            if (_activeAgent != agent) return;
            _tutorialAvailableActions.Clear();
            _tutorialAvailableActions.UnionWith(available.actions);
            _tutorialVoiceAvailable = available.voice;
        }
        catch (Exception)
        {
            if (_activeAgent == agent)
            {
                _tutorialAvailableActions.Clear();
                _tutorialVoiceAvailable = false;
            }
        }
        finally
        {
            _tutorialAvailabilityAt = Environment.TickCount64;
            _tutorialAvailabilityPending = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
