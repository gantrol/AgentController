using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AgentController.Application.Actions;
using AgentController.Application.Navigation;
using AgentController.Domain.Actions;
using AgentController.Platform.Windowing;
using CodexController.Agents;
using CodexController.Composition;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation;
using CodexController.Presentation.Dispatch;
using CodexController.Presentation.Feedback;
using CodexController.Services;
using CodexController.Services.Micro;
using CodexController.ViewModels;
using CodexController.Views;
using Forms = System.Windows.Forms;

namespace CodexController;

public partial class MainWindow : Window
{
    private static readonly TimeSpan EncoderStepInterval =
        TimeSpan.FromMilliseconds(24);
    private static readonly TimeSpan EncoderIntentMaximumAge =
        TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan CurrentControlIntentMaximumAge =
        TimeSpan.FromMilliseconds(450);

    private static readonly SidebarScope[] RootSidebarScopes =
    [
        SidebarScope.PinnedTasks,
        SidebarScope.PinnedProjects,
        SidebarScope.Projects,
        SidebarScope.ProjectlessTasks,
    ];
    private const ControllerButtons DialExclusiveFrozenButtons =
        RadialInputMap.FrozenBaseButtons &
        ~(
            ControllerButtons.RightThumb |
            ControllerButtons.A |
            ControllerButtons.B
        );

    private readonly SettingsService _settingsService;
    private readonly IWorkspaceReader _workspaceReader;
    private readonly ISidebarAutomation _sidebarAutomation;
    private readonly IComposerAutomation _composerAutomation;
    private readonly IAgentShortcuts _agentShortcuts;
    private readonly IKeybindingProvisioner? _keybindingProvisioner;
    private readonly XInputService _xInputService;
    private readonly ControllerInteractionCoordinator _controllerInteraction;
    private readonly ControllerHoldCoordinator _controllerHolds;
    private readonly RadialLayerCoordinator _radialLayers;
    private readonly ActionDispatcher _actionDispatcher;
    private readonly ThreadNavigationCoordinator _threadNavigation;
    private readonly BridgeEventHub _bridgeEvents;
    private readonly LocalizationService _localization;
    private readonly MicroInputService _microInput;
    private readonly CodexCurrentControlExecutor _currentControlExecutor;
    private readonly ControllerProfileRegistry _controllerProfiles;
    private readonly IAgentTarget _activeAgent;
    private readonly IForegroundApplication _foregroundApplication;
    private readonly DevicePageViewModel _devicePageViewModel;
    private readonly ConfigPageViewModel _configPageViewModel;
    private readonly SettingsPageViewModel _settingsPageViewModel;
    private readonly BridgeFeedbackPresenter _feedbackPresenter;
    private readonly SemaphoreSlim _sidebarFocusGate = new(1, 1);
    private readonly SemaphoreSlim _dataRefreshGate = new(1, 1);
    private readonly SemaphoreSlim _dialAutomationGate = new(1, 1);
    private readonly ObservableCollection<SidebarEntry> _sidebarEntries = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _dataTimer;
    private readonly DispatcherTimer _radialLearningTimer;
    private readonly ControllerSession _controllerSession = new();
    private readonly ForegroundContinuityGate _foregroundContinuityGate =
        new();
    private readonly SidebarNavigationDirectory _sidebarNavigationDirectory =
        new();
    private readonly PushToTalkAutomationState _pushToTalkAutomation =
        new();
    private readonly EncoderStepAccumulator _encoderSteps = new(3);
    private readonly CodexMicroReadbackObserver _microReadbackObserver =
        new();

    private readonly AppSettings _settings;
    private CodexSnapshot _snapshot = new();
    private SidebarScope _scope = SidebarScope.Projects;
    private RightControlMode _rightMode = RightControlMode.Dial;
    private ControllerButtons _pushToTalkSuppressedButtons;
    private string? _selectedProjectPath;
    private bool _projectTasksPinnedOnly;
    private readonly Dictionary<SidebarScope, string> _rootCursorIds = [];
    private readonly Dictionary<string, string> _projectTaskCursorIds =
        new(StringComparer.OrdinalIgnoreCase);
    private SidebarReturnFrame? _sidebarReturnFrame;
    private ProjectDisclosureLease? _projectDisclosureLease;
    private bool _dictationInjected;
    private ComposerAutomationChannel _dictationAutomationChannel =
        ComposerAutomationChannel.Unknown;
    private string? _dictationInputGlyph;
    private bool _rightStickPressHeld;
    private bool _rightStickHoldTriggered;
    private bool _virtualDialMenuOpen;
    private bool _modelPickerShortcutReady;
    private bool _virtualDialConfirmationPending;
    private bool _virtualDialOpenPending;
    private bool _virtualDialCancelRequested;
    private bool _virtualDialCleanupPending;
    private bool _dialInputReleasePending;
    private bool _composerPickerMenuLikelyOpen;
    private bool _blockedPushToTalkHintShown;
    private bool _bridgeDisabledHintShown;
    private bool _controllerWasConnected;
    private bool _hasSeenController;
    private bool _wakeInProgress;
    private bool _initializing = true;
    private bool _suppressSelectionActivation;
    private bool _exitRequested;
    private long _leftNavigationBlockedUntil;
    private long _rightAdjustmentBlockedUntil;
    private CancellationTokenSource? _sidebarFocusCancellation;
    private ControllerState _latestControllerState;
    private ComposerCatalog? _composerCatalog;
    private int _modelIndex;
    private int _reasoningIndex;
    private int _speedIndex;
    private CancellationTokenSource? _composerPickerCancellation;
    private CancellationTokenSource? _rightStickPressCancellation;
    private CancellationTokenSource? _microReadbackCancellation;
    private CodexMicroReadback _microReadback =
        CodexMicroReadback.Closed;
    private readonly CurrentControlIntentBuffer _currentControlIntents =
        new();
    private readonly CoalescingRequestGate _microReadbackRequests =
        new();
    private int _dialPumpRunning;
    private int _encoderStepPumpRunning;
    private int _virtualDialGeneration;
    private int _dictationPumpRunning;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private OverlayWindow? _overlayWindow;
    private RadialMenuOverlayWindow? _radialMenuOverlayWindow;
    private SidebarNavigationMenuOverlayWindow?
        _sidebarNavigationMenuOverlayWindow;
    private ControllerProfile _activeControllerProfile =
        BuiltInControllerProfiles.Generic;

    private SidebarNavigationState ActiveSidebarNavigation =>
        _sidebarNavigationDirectory.Resolve(
            _scope,
            _selectedProjectPath,
            _projectTasksPinnedOnly);

    internal MainWindow(MainWindowDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _settingsService = dependencies.Settings;
        _settings = dependencies.CurrentSettings;
        _activeAgent = dependencies.ActiveAgent;
        _foregroundApplication = dependencies.ForegroundApplication;
        _workspaceReader = _activeAgent.WorkspaceOrEmpty();
        _sidebarAutomation = _activeAgent.SidebarOrUnavailable();
        _composerAutomation = _activeAgent.ComposerOrUnavailable();
        _agentShortcuts = _activeAgent.Shortcuts;
        _keybindingProvisioner = _activeAgent.Keybindings;
        _xInputService = dependencies.Controller;
        _controllerInteraction = dependencies.ControllerInteraction;
        _controllerHolds = dependencies.ControllerHolds;
        _radialLayers = dependencies.RadialLayers;
        _actionDispatcher = dependencies.ActionDispatcher;
        _threadNavigation = dependencies.ThreadNavigation;
        _threadNavigation.NoticePublished +=
            ThreadNavigation_NoticePublished;
        _bridgeEvents = dependencies.BridgeEvents;
        _localization = dependencies.Localization;
        _microInput = dependencies.MicroInput;
        _currentControlExecutor = new(_microInput);
        _controllerProfiles = dependencies.ControllerProfiles;
        _configPageViewModel = new ConfigPageViewModel(
            OpenAgentShortcuts,
            () => SaveSettings(
                _localization.Strings.Get(
                    StringKeys.MessageShortcutSettingsSaved)));
        _settingsPageViewModel = new SettingsPageViewModel(
            OpenControllerVendorTool,
            OpenAgentSettings,
            () => SaveSettings(
                _localization.Strings.Get(
                    StringKeys.MessageSettingsSaved)),
            ChangeLanguage);
        InitializeComponent();
        DataContext = _localization.Strings;
        ConfigPage.DataContext = _configPageViewModel;
        ConfigPage.Strings = _localization.Strings;
        SettingsPage.DataContext = _settingsPageViewModel;
        SettingsPage.Strings = _localization.Strings;
        SettingsPage.Localization = _localization;
        _feedbackPresenter = new BridgeFeedbackPresenter(
            _bridgeEvents,
            new LocalizedBridgeFeedbackFormatter(
                _localization.Strings,
                _localization.Strings.AppTitle,
                _activeAgent.DisplayName),
            new DelegateOverlayPresenter(PresentBridgeOverlay),
             SynchronizationContext.Current ??
             new DispatcherSynchronizationContext(Dispatcher));
        _devicePageViewModel = new DevicePageViewModel(
            _sidebarEntries,
            _feedbackPresenter.LogRows,
            RefreshDeviceData,
            scope => SetSidebarScope(
                scope,
                showFeedback: false),
            RefreshTutorialDispatch);
        DevicePage.DataContext = _devicePageViewModel;
        _feedbackPresenter.PropertyChanged +=
            FeedbackPresenter_PropertyChanged;

        _statusTimer = new DispatcherTimer
        {
            Interval = BridgeTimings.StatusPoll,
        };
        _statusTimer.Tick += (_, _) => UpdateCodexStatus();

        _dataTimer = new DispatcherTimer
        {
            Interval = BridgeTimings.DataRefresh,
        };
        _dataTimer.Tick += (_, _) => RefreshCodexData(preserveSelection: true);

        _radialLearningTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(
                RadialInputMap.LearningDelayMs),
        };
        _radialLearningTimer.Tick += (_, _) =>
            PromoteRadialLearningCue();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _localization.SetLanguage(_settings.Language);
        _localization.PropertyChanged +=
            Localization_PropertyChanged;
        ApplySettingsToControls();
        UpdateLocalizedUi();
        SetupTrayIcon();
        _overlayWindow = new OverlayWindow();
        _radialMenuOverlayWindow = new RadialMenuOverlayWindow();
        _sidebarNavigationMenuOverlayWindow =
            new SidebarNavigationMenuOverlayWindow();

        _bridgeEvents.Publish(BridgeEventKeys.AppReady);
        ConfigureCodexKeybindings();
        InitializeComposerControls();
        RefreshCodexData(
            preserveSelection: false,
            forceNavigationRebuild: false);
        ShowPage(DevicePage);
        SetSelectedNav(DeviceNavButton);

        _xInputService.StateChanged += XInputService_StateChanged;
        _xInputService.Start();
        _statusTimer.Start();
        _dataTimer.Start();
        _initializing = false;

        FooterStatusText.Text = ControllerHelpText();
    }

    private void XInputService_StateChanged(
        object? sender,
        ControllerState state)
    {
        var virtualDialDeadZone =
            VirtualDialInputPolicy.ResolveDeadZone(
                _settings.DeadZone,
                _activeControllerProfile.Tuning?.StickDeadZone);
        if (_controllerInteraction.EnqueueState(
                state,
                _settings.DeadZone,
                virtualDialDeadZone))
        {
            _ = Dispatcher.BeginInvoke(
                ProcessBufferedControllerStates);
        }
    }

    private void ProcessBufferedControllerStates()
    {
        foreach (var state in _controllerInteraction.DrainStates())
        {
            ProcessControllerState(state);
        }
    }

    private void ConfigureCodexKeybindings()
    {
        _modelPickerShortcutReady = false;
        if (!_settings.BridgeEnabled)
        {
            return;
        }

        if (_keybindingProvisioner is null)
        {
            return;
        }

        var result = _keybindingProvisioner.EnsureBindings(_settings);
        _modelPickerShortcutReady =
            result.CanUseShortcut(_settings.ModelPickerShortcut);
        if (!result.Succeeded)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageAgentKeybindingsWriteFailed,
                _activeAgent.DisplayName,
                _localization.Strings.ErrorLabel(
                    result.Error,
                    result.ErrorDetail)));
            return;
        }

        if (result.Conflicts.Count > 0)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageAgentKeybindingsConflict,
                _activeAgent.DisplayName,
                result.Conflicts[0]));
        }
        else if (result.Changed)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageFallbackKeybindingsWritten,
                _activeAgent.DisplayName));
        }
    }

    private void ProcessControllerState(ControllerState state)
    {
        _latestControllerState = state;
        UpdateControllerProfile(state);
        UpdateControllerVisual(state);

        if (!state.IsConnected)
        {
            if (_controllerWasConnected)
            {
                _controllerWasConnected = false;
                _bridgeEvents.Publish(
                    BridgeEventKeys.ControllerDisconnected,
                    BridgeEventSeverity.Warning,
                    new Dictionary<string, string>
                    {
                        ["autoResume"] = "true",
                        ["device"] =
                            _activeControllerProfile.DisplayName,
                    },
                    new BridgeOverlayMetadata(
                        BridgeOverlayTarget.Footer,
                        CoalesceKey: "controller.connection"));
            }

            PauseControllerInput();
            _controllerInteraction.ClearButtonHistory();
            return;
        }

        if (!_controllerWasConnected)
        {
            _controllerWasConnected = true;
            _controllerSession.Pause(requireNeutral: true);
            _bridgeEvents.Publish(
                BridgeEventKeys.ControllerConnected,
                parameters: new Dictionary<string, string>
                {
                    ["restored"] = _hasSeenController.ToString(),
                    ["requiresNeutral"] = "true",
                    ["device"] =
                        _activeControllerProfile.DisplayName,
                },
                overlay: new BridgeOverlayMetadata(
                    BridgeOverlayTarget.Footer,
                    CoalesceKey: "controller.connection"));

            _hasSeenController = true;
        }

        var pressed = state.Buttons;
        if (
            state.LeftTrigger <=
            BridgeTimings.PushToTalkReleaseThreshold)
        {
            _blockedPushToTalkHintShown = false;
        }

        _radialLayers.DrainSuppressedButtons(pressed);
        _pushToTalkSuppressedButtons &= pressed;
        if (!_settings.BridgeEnabled)
        {
            PresentBridgeDisabledControllerAttempt(state);
            DrainControllerFrame(pressed);
            return;
        }

        var radialModifierHeld =
            _radialLayers.Layer is not null ||
            pressed.HasFlag(ControllerButtons.LeftShoulder) ||
            pressed.HasFlag(ControllerButtons.RightShoulder) ||
            state.RightTrigger >= RadialInputMap.TurnEngageThreshold;
        var wakeEligibleButtons =
            pressed & ~_radialLayers.SuppressedButtons;
        if (radialModifierHeld || IsVirtualDialContextActive)
        {
            wakeEligibleButtons &= ~ControllerButtons.Start;
        }

        if (
            _controllerInteraction.BaseButtonTransition(
                wakeEligibleButtons,
                ControllerButtons.Start) ==
            ControllerButtonTransition.Pressed)
        {
            WakeCodex();
        }

        var foreground = _foregroundApplication.IsForeground;
        TryAutoArmController(foreground);
        var foregroundAllowsInput =
            ObserveCodexForeground(foreground);
        if (
            !_controllerSession.IsArmed ||
            (
                _settings.OnlyWhenCodexForeground &&
                !foregroundAllowsInput
            ))
        {
            PresentBlockedPushToTalkAttempt(
                state,
                foreground,
                waitingForNeutral: false);
            if (!_controllerSession.IsArmed)
            {
                PauseControllerInput(state);
            }

            _controllerInteraction.CommitButtonHistory(pressed, pressed);
            return;
        }

        if (!TryResumeControllerInput(state))
        {
            PresentBlockedPushToTalkAttempt(
                state,
                foreground,
                waitingForNeutral: true);
            _controllerInteraction.CommitButtonHistory(pressed, pressed);
            return;
        }

        if (_dialInputReleasePending)
        {
            if (PushToTalkInputPolicy.ShouldPreemptDialReleaseDrain(
                    state.LeftTrigger,
                    BridgeTimings.PushToTalkEngageThreshold))
            {
                _dialInputReleasePending = false;
                // Preserve held-button history. Clearing it here turns a
                // still-held B into a fresh edge that immediately cancels LT.
                _controllerInteraction.CommitButtonHistory(pressed, pressed);
                _controllerInteraction.ResetRouting();
            }
            else if (!IsControllerNeutral(state))
            {
                DrainControllerFrame(pressed);
                return;
            }
            else
            {
                _dialInputReleasePending = false;
                _controllerInteraction.ClearButtonHistory();
                _controllerInteraction.ResetRouting();
            }
        }

        var physicalEdges =
            _controllerInteraction.PhysicalEdges(pressed);
        // Dial automation owns the native picker state. Controller polling
        // must never synchronously walk the UIA tree.
        var dialContextActive = IsVirtualDialContextActive;

        var pushToTalkTransition = UpdatePushToTalkTrigger(
            state.LeftTrigger,
            blocked: PushToTalkInputPolicy.ShouldBlockTrigger(
                radialLayerActive: _radialLayers.Layer is not null));
        var pushToTalkFrameActive =
            _controllerInteraction.PushToTalkBlocksBaseInput ||
            pushToTalkTransition != AnalogTriggerTransition.None;
        if (pushToTalkTransition == AnalogTriggerTransition.Released)
        {
            _pushToTalkSuppressedButtons |= pressed;
        }
        else if (_controllerInteraction.PushToTalkBlocksBaseInput)
        {
            _pushToTalkSuppressedButtons |=
                PushToTalkInputPolicy.ButtonsToSuppress(pressed);
        }

        ControllerButtons frozenByContext;
        if (pushToTalkFrameActive)
        {
            _radialLayers.ClearRightTriggerCandidate();
            frozenByContext =
                PushToTalkInputPolicy.FrozenBaseButtons;
        }
        else if (dialContextActive)
        {
            if (_radialLayers.Layer is not null ||
                _radialLayers.IsRightTriggerCandidate)
            {
                ResetRadialLayer(clearSuppression: false);
            }

            frozenByContext = DialExclusiveFrozenButtons;
        }
        else
        {
            frozenByContext = ProcessRadialInput(state);
        }

        if (
            frozenByContext.HasFlag(ControllerButtons.RightThumb) &&
            _rightStickPressHeld)
        {
            CancelVirtualDialPressHold();
        }

        var radialInputActive = _radialLayers.Layer is not null;
        var basePressed =
            pressed &
            ~frozenByContext &
            ~_radialLayers.SuppressedButtons &
            ~_pushToTalkSuppressedButtons;
        var baseIntents = _controllerInteraction.ResolveBaseIntents(
            basePressed,
            physicalEdges,
            dialContextActive);
        foreach (var intent in baseIntents)
        {
            ExecuteControllerInteractionIntent(intent);
        }

        _controllerInteraction.CommitButtonHistory(
            basePressed,
            pressed);

        var deadZone = _settings.DeadZone;
        var virtualDialDeadZone =
            VirtualDialInputPolicy.ResolveDeadZone(
                deadZone,
                _activeControllerProfile.Tuning?.StickDeadZone);
        var leftGesture = _controllerInteraction.UpdateLeftStick(
            state.LeftX,
            state.LeftY,
            deadZone,
            blocked:
                radialInputActive ||
                dialContextActive ||
                pushToTalkFrameActive ||
                basePressed.HasFlag(ControllerButtons.LeftThumb) ||
                Environment.TickCount64 < _leftNavigationBlockedUntil);
        var rightGesture = _controllerInteraction.UpdateRightStick(
            state.RightX,
            state.RightY,
            virtualDialDeadZone,
            blocked:
                radialInputActive ||
                pushToTalkFrameActive ||
                basePressed.HasFlag(ControllerButtons.RightThumb) ||
                Environment.TickCount64 < _rightAdjustmentBlockedUntil);

        _controllerInteraction.RepeatAxis(
            "left-y",
            leftGesture.VerticalDirection,
            _settings.RepeatDelayMs,
            _settings.RepeatIntervalMs,
            MoveSidebarSelection);
        if (leftGesture.HorizontalStarted)
        {
            NavigateSidebarHorizontal(leftGesture.HorizontalDirection);
        }
        // Vertical is always the native Micro encoder. Horizontal is a
        // separate gamepad-only operation axis and must never become an
        // encoder detent, regardless of popup state.
        _controllerInteraction.RepeatAnalogAxis(
            "right-y",
            rightGesture.VerticalDirection,
            rightGesture.VerticalMagnitude,
            virtualDialDeadZone,
            _settings.RepeatDelayMs,
            _settings.RepeatIntervalMs,
            direction =>
            {
                var steps =
                    VirtualDialInputPolicy.ResolveVerticalEncoderSteps(
                        direction);
                if (steps != 0)
                {
                    QueueVirtualDialEncoderSteps(steps);
                }
            });
        _controllerInteraction.RepeatAnalogAxis(
            "right-x",
            rightGesture.HorizontalDirection,
            rightGesture.HorizontalMagnitude,
            virtualDialDeadZone,
            _settings.RepeatDelayMs,
            _settings.RepeatIntervalMs,
            direction =>
            {
                var navigation =
                    VirtualDialInputPolicy.ResolveHorizontalNavigation(
                        direction);
                if (navigation is { } resolved)
                {
                    QueueVirtualDialNavigation(resolved);
                }
            });
    }

    private bool IsVirtualDialContextActive =>
        _virtualDialMenuOpen ||
        _virtualDialOpenPending ||
        _virtualDialCancelRequested ||
        _virtualDialCleanupPending;

    private void SetVirtualDialMenuOpen(
        bool isOpen,
        bool requiresConfirmation = false)
    {
        _virtualDialMenuOpen = isOpen;
        _virtualDialConfirmationPending =
            isOpen && requiresConfirmation;
        UpdateVirtualDialContextPresentation();
    }

    private void UpdateVirtualDialContextPresentation()
    {
        _devicePageViewModel.UpdateVirtualDialMenuState(
            _virtualDialMenuOpen,
            _virtualDialConfirmationPending);
    }

    private void DrainControllerFrame(ControllerButtons pressed)
    {
        CancelConversationBoundaryHold();
        _controllerInteraction.CommitButtonHistory(pressed, pressed);
        _controllerInteraction.RequireNeutralRouting();
    }

    private void ClearPendingVirtualDialInput()
    {
        _currentControlIntents.Clear();
        _encoderSteps.Clear();
    }

    private void BeginVirtualDialReleaseDrain()
    {
        _composerPickerMenuLikelyOpen = false;
        _dialInputReleasePending =
            !IsControllerNeutral(_xInputService.LastState);
        ClearPendingVirtualDialInput();
        CancelVirtualDialPressHold();
        ResetRadialLayer(clearSuppression: false);
        _controllerInteraction.RequireNeutralRouting();
    }

    private ControllerButtons ProcessRadialInput(ControllerState state)
    {
        var update = _radialLayers.ProcessFrame(
            state,
            _controllerInteraction.PhysicalEdges(state.Buttons),
            Environment.TickCount64);
        ApplyRadialLayerUpdate(update);
        return update.FrozenButtons;
    }

    private void ApplyRadialLayerUpdate(RadialLayerUpdate update)
    {
        var effects = update.Effects;
        _devicePageViewModel.UpdateTutorialLayer(
            _radialLayers.Layer,
            _radialLayers.IsEngaged,
            _radialLayers.IsCancelled);
        if (effects.HasFlag(RadialLayerEffect.StopLearningTimer))
        {
            _radialLearningTimer.Stop();
        }

        if (effects.HasFlag(RadialLayerEffect.StartLearningTimer))
        {
            _radialLearningTimer.Start();
        }

        if (effects.HasFlag(RadialLayerEffect.EndBaseCancelPress))
        {
            EndBaseCancelPress();
        }

        if (effects.HasFlag(RadialLayerEffect.StopDictationPhysical))
        {
            StopDictation(physicalRelease: true);
        }

        if (effects.HasFlag(RadialLayerEffect.StopDictationSynthetic))
        {
            StopDictation(physicalRelease: false);
        }

        if (effects.HasFlag(RadialLayerEffect.HideMenu))
        {
            _radialMenuOverlayWindow?.HideMenu();
        }

        if (effects.HasFlag(RadialLayerEffect.RefreshMenu))
        {
            RefreshRadialMenu();
        }

        if (effects.HasFlag(RadialLayerEffect.RefreshAgentData))
        {
            RefreshCodexData(preserveSelection: true);
        }

        if (effects.HasFlag(RadialLayerEffect.PresentCanceled))
        {
            ShowFeedback(
                RadialText("组合层", "Chord layer"),
                RadialText(
                    "已取消；松开修饰键后返回基础层。",
                    "Canceled. Release the modifier to return to Base."));
            Pulse(strength: 0.1);
        }

        if (effects.HasFlag(RadialLayerEffect.ActionPanelOpened))
        {
            ShowFeedback(
                RadialText("动作面板", "Action panel"),
                RadialText(
                    "按手柄图中的实体键执行 · B 或 Y 关闭",
                    "Press a mapped controller button · B or Y closes"));
            Pulse(strength: 0.16);
        }

        if (effects.HasFlag(RadialLayerEffect.ActionPanelClosed))
        {
            ShowFeedback(
                RadialText("动作面板", "Action panel"),
                RadialText("已关闭。", "Closed."));
            Pulse(strength: 0.08);
        }

        if (effects.HasFlag(RadialLayerEffect.OpenPreviousTask))
        {
            OpenAdjacentAgentTask(-1);
        }

        if (effects.HasFlag(RadialLayerEffect.OpenNextTask))
        {
            OpenAdjacentAgentTask(1);
        }

        if (effects.HasFlag(RadialLayerEffect.ExecuteFollowUpAction))
        {
            ExecuteRadialAction(update.Action);
        }

        if (effects.HasFlag(RadialLayerEffect.AcknowledgeAction))
        {
            var actionId =
                _radialLayers.HighlightedItemId ??
                RadialInputMap.ActionId(
                    update.Action,
                    _radialLayers.Layer);
            var actionTitle =
                _radialMenuOverlayWindow?.AcknowledgeInputAndFade(actionId) ??
                RadialText("轮盘指令", "Radial command");
            ShowFeedback(
                actionTitle,
                RadialText(
                    "已接收，等待 Codex 响应…",
                    "Received. Waiting for Codex…"));
            Pulse(strength: 0.18);
            _ = ExecuteRadialActionAfterAcknowledgementAsync(update.Action);
        }
    }

    private void PromoteRadialLearningCue()
    {
        ApplyRadialLayerUpdate(
            _radialLayers.PromoteLearningCue(
                _latestControllerState.Buttons));
    }

    private void OpenActionPanel()
    {
        ApplyRadialLayerUpdate(
            _radialLayers.ToggleActionPanel(
                _latestControllerState.Buttons,
                Environment.TickCount64));
    }

    private void EndRadialLayer(ControllerButtons pressed)
    {
        ApplyRadialLayerUpdate(_radialLayers.End(pressed));
    }

    private void ResetRadialLayer(
        bool clearSuppression,
        bool preserveInputAcknowledgement = false)
    {
        ApplyRadialLayerUpdate(
            _radialLayers.Reset(
                clearSuppression,
                preserveInputAcknowledgement));
    }

    private async Task ExecuteRadialActionAfterAcknowledgementAsync(
        RadialInputAction action)
    {
        // Let WPF render the local acknowledgement before any slower Codex
        // automation or deep-link handling begins on the UI thread.
        await Dispatcher.Yield(DispatcherPriority.Background);
        ExecuteRadialAction(action);
    }

    private void ExecuteRadialAction(RadialInputAction action)
    {
        var agentSlot = RadialInputMap.AgentSlotIndex(action);
        if (agentSlot >= 0)
        {
            OpenAgentSlot(agentSlot);
            return;
        }

        switch (action)
        {
            case RadialInputAction.ToggleFast:
                ExecuteFastToggle();
                break;
            case RadialInputAction.Approve:
                ConfirmOrApproveAction();
                break;
            case RadialInputAction.Decline:
                _ = ExecuteUiCommandActionAsync(
                    ApprovalActionContract.DeclineId,
                    RadialText("拒绝更改", "Decline changes"),
                    "controller.radial.command.decline",
                    "radial.command");
                break;
            case RadialInputAction.Fork:
                ExecuteForkAction();
                break;
            case RadialInputAction.PushToTalk:
                if (_radialLayers.TryStartPushToTalk())
                {
                    StartDictation(Glyph(LogicalInput.View));
                }
                break;
            case RadialInputAction.Dispatch:
                SendPrompt(
                    Glyph(LogicalInput.Menu),
                    "controller.radial.dispatch");
                break;
            case RadialInputAction.Steer:
                _ = ExecuteUiCommandActionAsync(
                    TurnActionContract.SteerId,
                    RadialText("加入当前运行", "Steer current turn"),
                    "controller.radial.turn.steer",
                    "radial.turn");
                break;
            case RadialInputAction.Queue:
                _ = ExecuteUiCommandActionAsync(
                    TurnActionContract.QueueId,
                    RadialText("排到下一轮", "Queue next turn"),
                    "controller.radial.turn.queue",
                    "radial.turn");
                break;
            case RadialInputAction.BeginStopHold:
                BeginCancelHold();
                break;
            case RadialInputAction.NewTask:
                ExecuteNewTaskAction();
                break;
            case RadialInputAction.NavigateForward:
                _ = ExecuteActionPanelActionAsync(
                    NavigationActionContract.ForwardId,
                    RadialText("前进", "Forward"),
                    "controller.radial.action-panel.forward");
                break;
            case RadialInputAction.ToggleSidebar:
                _ = ExecuteActionPanelActionAsync(
                    SidebarActionContract.ToggleId,
                    RadialText("切换侧边栏", "Toggle sidebar"),
                    "controller.radial.action-panel.toggle-sidebar");
                break;
            case RadialInputAction.NavigateBack:
                _ = ExecuteActionPanelActionAsync(
                    NavigationActionContract.BackId,
                    RadialText("后退", "Back"),
                    "controller.radial.action-panel.back");
                break;
            case RadialInputAction.ClearComposer:
                ConfirmOrClearComposer();
                break;
            case RadialInputAction.ProjectContext:
                EndRadialLayer(_latestControllerState.Buttons);
                JumpToProjectContext();
                break;
        }
    }

    private async Task ExecuteActionPanelActionAsync(
        ActionId actionId,
        string title,
        string controlId)
    {
        var result = await TryExecuteActionAsync(
            actionId,
            "controller.active",
            controlId,
            "radial.action-panel",
            actionId.Value,
            ActionSafetyLevel.Routine)
            .ConfigureAwait(true);
        var succeeded = result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified;
        EndRadialLayer(_latestControllerState.Buttons);
        AddEvent(
            title +
            ExecutionSuffix(
                succeeded,
                result?.ErrorCode));
        ShowFeedback(
            title,
            succeeded
                ? _localization.Strings.Get(
                    StringKeys.MessageExecuted)
                : ExecutionFailureLabel(result?.ErrorCode));
        Pulse(strength: succeeded ? 0.2 : 0.1);
    }

    private void ConfirmOrClearComposer()
    {
        var title = RadialText(
            "清空当前输入",
            "Clear current input");
        if (!ConfirmRadialAction(
                RadialInputAction.ClearComposer,
                title,
                RadialText(
                    $"再次按 {Glyph(LogicalInput.FaceSouth)} 确认 · " +
                    $"{Glyph(LogicalInput.FaceEast)} 取消",
                    $"Press {Glyph(LogicalInput.FaceSouth)} again " +
                    $"to confirm · {Glyph(LogicalInput.FaceEast)} cancels")))
        {
            return;
        }

        _ = ClearComposerAsync();
    }

    private void ConfirmOrApproveAction()
    {
        var title = RadialText("接受更改", "Approve changes");
        if (!ConfirmRadialAction(
                RadialInputAction.Approve,
                title,
                RadialText(
                    $"再次按 {Glyph(LogicalInput.FaceSouth)} 确认 · " +
                    $"松开 {Glyph(LogicalInput.RightShoulder)} 取消",
                    $"Press {Glyph(LogicalInput.FaceSouth)} again " +
                    $"to confirm · release " +
                    $"{Glyph(LogicalInput.RightShoulder)} to cancel")))
        {
            return;
        }

        _ = ExecuteUiCommandActionAsync(
            ApprovalActionContract.AcceptId,
            title,
            "controller.radial.command.approve",
            "radial.command",
            ActionSafetyLevel.HighRisk);
    }

    private bool ConfirmRadialAction(
        RadialInputAction action,
        string title,
        string confirmationPrompt)
    {
        if (_radialLayers.TryConfirmAction(
                action,
                RadialInputMap.ActionId(
                    action,
                    _radialLayers.Layer),
                TimeSpan.FromSeconds(2.5),
                RefreshRadialMenu))
        {
            return true;
        }

        RefreshRadialMenu();
        ShowFeedback(title, confirmationPrompt);
        Pulse(strength: 0.12);
        return false;
    }

    private async Task ClearComposerAsync()
    {
        var result = await TryExecuteActionAsync(
            ComposerActionContract.ClearId,
            "controller.active",
            "controller.radial.clear-composer",
            "radial.action-panel",
            "composer.clear",
            ActionSafetyLevel.ConfirmationRequired)
            .ConfigureAwait(true);

        EndRadialLayer(_latestControllerState.Buttons);
        var succeeded = result?.Outcome == ActionOutcome.Succeeded;
        var title = RadialText(
            "清空当前输入",
            "Clear current input");
        AddEvent(
            title +
            ExecutionSuffix(
                succeeded,
                result?.ErrorCode));
        ShowFeedback(
            title,
            succeeded
                ? RadialText("已清空。", "Cleared.")
                : ExecutionFailureLabel(result?.ErrorCode));
        Pulse(strength: succeeded ? 0.2 : 0.1);
    }

    private void OpenAgentSlot(int slotIndex)
    {
        var thread = _snapshot.Threads
            .Take(6)
            .ElementAtOrDefault(slotIndex);
        if (thread is null)
        {
            ShowFeedback(
                RadialText("Agent 槽", "Agent slot"),
                RadialText("此槽尚未分配任务。", "This slot is unassigned."));
            Pulse(strength: 0.1);
            return;
        }

        SelectVisibleThread(thread.Id);
        _ = OpenThreadAsync(
            thread.Id,
            thread.Title,
            thread.NativeTitle ?? thread.Title,
            deviceId: "controller.active",
            controlId: "controller.radial.agent-slot");
    }

    private void OpenAdjacentAgentTask(int direction)
    {
        var tasks = _sidebarEntries
            .Where(entry =>
                !string.IsNullOrWhiteSpace(entry.ThreadId))
            .GroupBy(
                entry => entry.ThreadId!,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (tasks.Count == 0)
        {
            tasks = _snapshot.Threads
                .Select(thread => new SidebarEntry(
                    thread.Id,
                    thread.Title,
                    string.Empty,
                    SidebarLayer.Tasks,
                    thread.Id,
                    thread.ProjectPath,
                    thread.NativeTitle))
                .ToList();
        }

        if (tasks.Count == 0)
        {
            ShowFeedback(
                RadialText("任务切换", "Task switch"),
                RadialText("没有可切换的任务。", "No task is available."));
            return;
        }

        var currentId = DevicePage.SelectedEntry?.ThreadId;
        var currentIndex = string.IsNullOrWhiteSpace(currentId)
            ? -1
            : tasks
                .Select((entry, index) => (entry, index))
                .Where(item => string.Equals(
                    item.entry.ThreadId,
                    currentId,
                    StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        var nextIndex = currentIndex < 0
            ? direction < 0
                ? tasks.Count - 1
                : 0
            : (currentIndex + Math.Sign(direction) + tasks.Count) %
              tasks.Count;
        var next = tasks[nextIndex];
        SelectVisibleThread(next.ThreadId!);
        _ = OpenThreadAsync(
            next.ThreadId!,
            next.Title,
            next.NativeTitle ?? next.Title,
            deviceId: "controller.active",
            controlId: "controller.radial.navigate");
    }

    private void SelectVisibleThread(string threadId)
    {
        var index = _sidebarEntries
            .Select((entry, index) => (entry, index))
            .Where(item => string.Equals(
                item.entry.ThreadId,
                threadId,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.index)
            .DefaultIfEmpty(-1)
            .First();
        if (index < 0)
        {
            return;
        }

        ActiveSidebarNavigation.Select(_sidebarEntries, index);
        SelectSidebarIndex(index);
    }

    private void ExecuteFastToggle()
    {
        CancelPendingComposerSelection(cancelComposerPicker: false);
        var previousSpeedIndex = _speedIndex;
        var fast = previousSpeedIndex == 0;
        _speedIndex = fast ? 1 : 0;
        _devicePageViewModel.UpdateRightModeValue(
            _localization.Strings.Format(
                StringKeys.MessageApplyingValue,
                SpeedLabel(_speedIndex)));
        var menuWasLikelyOpen =
            _composerPickerMenuLikelyOpen ||
            IsVirtualDialContextActive;
        var cancellation = BeginComposerPickerAutomation();
        _ = SetSimpleSpeedAsync(
            fast,
            menuWasLikelyOpen,
            previousSpeedIndex,
            cancellation,
            _localization.Strings.ConfigToggleFast);
    }

    private async Task ExecuteUiCommandActionAsync(
        ActionId actionId,
        string title,
        string controlId,
        string context,
        ActionSafetyLevel safetyLevel = ActionSafetyLevel.Routine)
    {
        var result = await TryExecuteActionAsync(
            actionId,
            "controller.active",
            controlId,
            context,
            actionId.Value,
            safetyLevel)
            .ConfigureAwait(true);
        var succeeded = result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified;
        if (succeeded)
        {
            _threadNavigation.ClearUndo();
        }

        AddEvent(
            title +
            ExecutionSuffix(
                succeeded,
                result?.ErrorCode));
        ShowFeedback(
            title,
            succeeded
                ? RadialText("已执行。", "Executed.")
                : ExecutionFailureLabel(result?.ErrorCode));
        Pulse(strength: succeeded ? 0.22 : 0.1);
    }

    private void ExecuteForkAction()
    {
        _ = ExecuteForkActionAsync();
    }

    private async Task ExecuteForkActionAsync()
    {
        var isTurnLayer =
            _radialLayers.Layer == RadialMenuLayerKind.Turn;
        var result = await TryExecuteActionAsync(
            ForkThreadActionContract.Id,
            "controller.active",
            isTurnLayer
                ? "controller.radial.turn.fork"
                : "controller.radial.command.fork",
            isTurnLayer ? "radial.turn" : "radial.command",
            "thread.fork",
            ActionSafetyLevel.Routine)
            .ConfigureAwait(true);
        var title = RadialText("分支任务", "Fork task");
        var succeeded = result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified;
        if (succeeded)
        {
            _threadNavigation.ClearUndo();
        }

        var evidenceCode = result?.Evidence.FirstOrDefault()?.Code;
        var fastPath = evidenceCode switch
        {
            "thread.fork.micro-requested" => "Micro HID",
            "thread.fork.shortcut-sent" => _settings.ForkShortcut,
            _ => null,
        };
        if (succeeded && !string.IsNullOrWhiteSpace(fastPath))
        {
            AddEvent($"{title} · {fastPath}");
            Pulse();
            return;
        }

        AddEvent(
            title +
            ExecutionSuffix(
                succeeded,
                result?.ErrorCode));
        ShowFeedback(
            title,
            succeeded
                ? RadialText("已执行。", "Executed.")
                : ExecutionFailureLabel(result?.ErrorCode));
        Pulse(strength: succeeded ? 0.22 : 0.1);
    }

    private void ExecuteNewTaskAction()
    {
        _ = ExecuteNewTaskActionAsync();
    }

    private async Task ExecuteNewTaskActionAsync()
    {
        var title = RadialText("新建任务", "New task");
        var result = await TryExecuteActionAsync(
            CreateThreadActionContract.Id,
            "controller.active",
            "controller.radial.new-task",
            "radial.action-panel",
            "thread.create",
            ActionSafetyLevel.Routine)
            .ConfigureAwait(true);

        EndRadialLayer(_latestControllerState.Buttons);
        var succeeded = result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified;
        if (succeeded)
        {
            _threadNavigation.ClearUndo();
        }

        AddEvent(
            title +
            ExecutionSuffix(
                succeeded,
                result?.ErrorCode));
        ShowFeedback(
            title,
            succeeded
                ? _localization.Strings.Get(StringKeys.MessageExecuted)
                : ExecutionFailureLabel(result?.ErrorCode));
        Pulse(strength: succeeded ? 0.22 : 0.1);
    }

    private void RefreshRadialMenu()
    {
        if (
            _radialLayers.Layer is not { } layer ||
            _radialLayers.IsCancelled ||
            _radialMenuOverlayWindow is null)
        {
            _radialMenuOverlayWindow?.HideMenu();
            return;
        }

        _radialMenuOverlayWindow.ShowState(
            BuildRadialMenuState(layer));
    }

    private RadialMenuState BuildRadialMenuState(
        RadialMenuLayerKind layer)
    {
        var mode = RadialMenuDisplayModeParser.ParseOrDefault(
            _settings.RadialMenuMode);
        return layer switch
        {
            RadialMenuLayerKind.Agent => new RadialMenuState(
                layer,
                RadialText("Agent 任务", "Agent tasks"),
                Glyph(LogicalInput.LeftShoulder),
                BuildAgentRadialItems(),
                mode,
                isLayerEngaged: true,
                isLearningCueReady: _radialLayers.IsEngaged,
                subtitle: RadialText(
                    "虚拟 Codex Micro 小键盘 · 按对应键切换",
                    "Virtual Codex Micro keypad · Press a mapped key to switch"),
                interactionPhase: _radialLayers.InteractionPhase,
                agentKeypad: BuildAgentKeypadPresentation()),
            RadialMenuLayerKind.Command => new RadialMenuState(
                layer,
                RadialText("Codex 命令", "Codex commands"),
                Glyph(LogicalInput.RightShoulder),
                BuildCommandRadialItems(),
                mode,
                isLayerEngaged: true,
                isLearningCueReady: _radialLayers.IsEngaged,
                subtitle: RadialText(
                    $"{Glyph(LogicalInput.LeftStickPress)} 取消",
                    $"{Glyph(LogicalInput.LeftStickPress)} cancel"),
                interactionPhase: _radialLayers.InteractionPhase,
                learningGuideLabel: RadialText(
                    "ABXY 面键位置",
                    "ABXY face-button layout")),
            RadialMenuLayerKind.Turn => new RadialMenuState(
                layer,
                RadialText("运行中操作", "Active turn"),
                Glyph(LogicalInput.RightTrigger),
                BuildTurnRadialItems(),
                mode,
                isLayerEngaged: true,
                isLearningCueReady: true,
                subtitle: RadialText(
                    "松开 RT 关闭",
                    "Release RT to close"),
                interactionPhase: _radialLayers.InteractionPhase,
                learningGuideLabel: RadialText(
                    "ABXY 面键位置",
                    "ABXY face-button layout")),
            RadialMenuLayerKind.Action => new RadialMenuState(
                layer,
                RadialText("动作面板", "Action panel"),
                Glyph(LogicalInput.FaceNorth),
                BuildActionRadialItems(),
                RadialMenuDisplayMode.Always,
                isLayerEngaged: true,
                isLearningCueReady: true,
                subtitle: RadialText(
                    $"{Glyph(LogicalInput.FaceEast)} / {Glyph(LogicalInput.FaceNorth)} 关闭",
                    $"{Glyph(LogicalInput.FaceEast)} / {Glyph(LogicalInput.FaceNorth)} close"),
                interactionPhase: _radialLayers.InteractionPhase,
                learningGuideLabel: RadialText(
                    "ABXY 面键位置",
                    "ABXY face-button layout")),
            _ => throw new ArgumentOutOfRangeException(
                nameof(layer),
                layer,
                "Unknown radial layer."),
        };
    }

    private IReadOnlyList<RadialMenuItemState>
        BuildAgentRadialItems()
    {
        var threads = _snapshot.Threads.Take(6).ToArray();
        var items = new List<RadialMenuItemState>(6);
        for (var index = 0; index < 6; index++)
        {
            var thread =
                index < threads.Length ? threads[index] : null;
            var binding = AgentRadialSlotLayout.Bindings[index];
            var isCurrent =
                thread is not null &&
                string.Equals(
                    thread.Id,
                    DevicePage.SelectedEntry?.ThreadId,
                    StringComparison.OrdinalIgnoreCase);
            var status = thread is null
                ? ThreadStatus.Unassigned
                : thread.Status;
            items.Add(RadialItem(
                $"agent-slot-{index + 1}",
                binding.Position,
                binding.Input,
                thread?.Title ??
                    RadialText("未分配", "Unassigned"),
                AgentSlotSubtitle(index + 1, status, isCurrent),
                isEnabled: thread is not null,
                isHighlighted: isCurrent,
                status: status));
        }

        return items;
    }

    private IReadOnlyList<RadialMenuItemState>
        BuildCommandRadialItems()
    {
        var dispatch = ResolveDispatchDisplay();
        _devicePageViewModel.UpdateTutorialDispatch(dispatch);
        return ControllerLayerPresentationFactory.Command(
                _localization.EffectiveLanguage,
                new ControllerCommandPresentationOptions(
                    _localization.Strings.ConfigToggleFast,
                    _localization.Strings.ConfigDictation,
                    dispatch.Label,
                    dispatch.Description,
                    Glyph(LogicalInput.FaceSouth),
                    _radialLayers.IsConfirmationPending(
                        RadialInputAction.Approve)))
            .Select(RadialItem)
            .ToArray();
    }

    private IReadOnlyList<RadialMenuItemState>
        BuildTurnRadialItems()
    {
        return ControllerLayerPresentationFactory.Turn(
                _localization.EffectiveLanguage)
            .Select(RadialItem)
            .ToArray();
    }

    private IReadOnlyList<RadialMenuItemState>
        BuildActionRadialItems()
    {
        return ControllerLayerPresentationFactory.Action(
                _localization.EffectiveLanguage,
                new ControllerActionPresentationOptions(
                    Glyph(LogicalInput.FaceSouth),
                    _radialLayers.IsConfirmationPending(
                        RadialInputAction.ClearComposer)))
            .Select(RadialItem)
            .ToArray();
    }

    private RadialMenuItemState RadialItem(
        ControllerLayerItemPresentation item) =>
        RadialItem(
            item.Id,
            item.Position,
            item.Input,
            item.Title,
            item.Description);

    private RadialMenuItemState RadialItem(
        string id,
        RadialMenuSlotPosition position,
        LogicalInput input,
        string title,
        string? subtitle = null,
        bool isEnabled = true,
        bool isHighlighted = false,
        ThreadStatus status = ThreadStatus.Unknown)
    {
        return new RadialMenuItemState(
            id,
            position,
            Glyph(input),
            title,
            subtitle,
            isEnabled,
            isHighlighted:
                isHighlighted ||
                string.Equals(
                    id,
                    _radialLayers.HighlightedItemId,
                    StringComparison.Ordinal),
            logicalInput: input,
            status: status);
    }

    private string AgentSlotSubtitle(
        int slotNumber,
        ThreadStatus status,
        bool isCurrent)
    {
        var statusLabel = status switch
        {
            ThreadStatus.Unassigned => RadialText("未绑定", "Unassigned"),
            ThreadStatus.Idle => RadialText("空闲", "Idle"),
            ThreadStatus.Thinking => RadialText("运行中", "Working"),
            ThreadStatus.CompleteUnread =>
                RadialText("完成 · 未读", "Complete · Unread"),
            ThreadStatus.RequiresInput =>
                RadialText("需要回应", "Needs response"),
            ThreadStatus.Error => RadialText("出错", "Error"),
            _ => RadialText("状态未知", "Status unknown"),
        };
        var subtitle =
            $"{RadialText("槽", "Slot")} {slotNumber} · {statusLabel}";
        return isCurrent
            ? $"{subtitle} · {RadialText("当前选中", "Selected")}"
            : subtitle;
    }

    private AgentKeypadPresentation BuildAgentKeypadPresentation()
    {
        return new AgentKeypadPresentation(
            RadialText(
                "虚拟 Codex Micro 小键盘 · 按对应键切换",
                "Virtual Codex Micro keypad · Press a mapped key to switch"),
            Glyph(LogicalInput.FaceEast),
            RadialText("取消", "Cancel"),
            RadialText(
                "选中键按其状态色脉冲",
                "The selected key pulses in its status color"),
            RadialText("空闲", "Idle"),
            RadialText("运行中", "Working"),
            RadialText("完成未读", "Complete unread"),
            RadialText("需要回应", "Needs response"),
            RadialText("出错", "Error"),
            RadialText("未绑定", "Unassigned"));
    }

    private DispatchDisplay ResolveDispatchDisplay()
    {
        var buttonName =
            _composerAutomation.TryReadDispatchButtonName();
        var normalized = buttonName?.Trim().ToLowerInvariant() ??
                         string.Empty;
        var resolver =
            new DispatchDisplayResolver(_localization.Strings);
        if (
            normalized.Contains("queue", StringComparison.Ordinal) ||
            normalized.Contains("next turn", StringComparison.Ordinal) ||
            normalized.Contains("下一轮", StringComparison.Ordinal) ||
            normalized.Contains("排到", StringComparison.Ordinal))
        {
            return resolver.Resolve(
                DispatchTurnState.Running,
                DispatchFollowUpBehavior.Queue);
        }

        if (
            normalized.Contains("steer", StringComparison.Ordinal) ||
            normalized.Contains("current turn", StringComparison.Ordinal) ||
            normalized.Contains("当前运行", StringComparison.Ordinal) ||
            normalized.Contains("加入当前", StringComparison.Ordinal))
        {
            return resolver.Resolve(
                DispatchTurnState.Running,
                DispatchFollowUpBehavior.Steer);
        }

        if (
            normalized.Contains("send", StringComparison.Ordinal) ||
            normalized.Contains("submit", StringComparison.Ordinal) ||
            normalized.Contains("发送", StringComparison.Ordinal) ||
            normalized.Contains("提交", StringComparison.Ordinal))
        {
            return resolver.Resolve(
                DispatchTurnState.Idle,
                DispatchFollowUpBehavior.Unknown);
        }

        return resolver.Resolve(
            DispatchTurnState.Unknown,
            DispatchFollowUpBehavior.Unknown);
    }

    private void RefreshTutorialDispatch() =>
        _devicePageViewModel.UpdateTutorialDispatch(
            ResolveDispatchDisplay());

    private string RadialText(string zhCn, string enUs)
    {
        return _localization.EffectiveLanguage == AppLanguage.ZhCn
            ? zhCn
            : enUs;
    }

    private void ExecuteControllerInteractionIntent(
        ControllerInteractionIntent intent)
    {
        switch (intent.Kind)
        {
            case ControllerInteractionIntentKind.CycleRootSidebarScope:
                CycleRootSidebarScope();
                break;
            case ControllerInteractionIntentKind.BeginVirtualDialPress:
                BeginVirtualDialPress();
                break;
            case ControllerInteractionIntentKind.EndVirtualDialPress:
                EndVirtualDialPress();
                break;
            case ControllerInteractionIntentKind.NavigateConversationTurn:
                NavigateConversationTurn(intent.ConversationAction);
                BeginConversationBoundaryHold(intent.ConversationAction);
                break;
            case ControllerInteractionIntentKind.EndConversationBoundaryHold:
                EndConversationBoundaryHold(intent.ReleasedButtons);
                break;
            case ControllerInteractionIntentKind.NavigateSidebarHorizontal:
                _controllerInteraction.RequireLeftStickNeutral();
                NavigateSidebarHorizontal(intent.Direction);
                break;
            case ControllerInteractionIntentKind.OpenActionPanel:
                OpenActionPanel();
                break;
            case ControllerInteractionIntentKind.SelectVirtualDialOption:
                SelectVirtualDialOption();
                break;
            case ControllerInteractionIntentKind.OpenSelectedSidebarTask:
                OpenSelectedSidebarTask(
                    deviceId: "controller.active",
                    controlId: "controller.face.south");
                break;
            case ControllerInteractionIntentKind.SendPrompt:
                SendPrompt();
                break;
            case ControllerInteractionIntentKind.BeginBaseCancelPress:
                BeginBaseCancelPress();
                break;
            case ControllerInteractionIntentKind.EndBaseCancelPress:
                EndBaseCancelPress();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(intent));
        }
    }

    private async void WakeCodex()
    {
        if (_wakeInProgress)
        {
            return;
        }

        _wakeInProgress = true;
        _bridgeEvents.Publish(
            BridgeEventKeys.CodexWakeRequested,
            parameters: new Dictionary<string, string>
            {
                ["trigger"] = "menu",
            },
            overlay: new BridgeOverlayMetadata(
                BridgeOverlayTarget.FooterAndToast,
                TimeSpan.FromMilliseconds(1050),
                "codex.wake"));
        try
        {
            var woke = await Task.Run(
                _foregroundApplication.TryActivate);
            if (!woke)
            {
                _controllerSession.Lock();
                _bridgeEvents.Publish(
                    BridgeEventKeys.CodexWakeFailed,
                    BridgeEventSeverity.Error,
                    new Dictionary<string, string>
                    {
                        ["reasonCode"] =
                            AgentAutomationErrorCodes.AgentWindowNotFound,
                    },
                    new BridgeOverlayMetadata(
                        BridgeOverlayTarget.FooterAndToast,
                        TimeSpan.FromMilliseconds(1400),
                        "codex.wake"));
                return;
            }

            _controllerSession.Arm();
            _controllerInteraction.RequireNeutralRouting();
            _leftNavigationBlockedUntil =
                Environment.TickCount64 + BridgeTimings.WakeInputGuardMs;
            _rightAdjustmentBlockedUntil =
                Environment.TickCount64 + BridgeTimings.WakeInputGuardMs;
            _bridgeEvents.Publish(
                BridgeEventKeys.CodexWakeSucceeded,
                BridgeEventSeverity.Success,
                new Dictionary<string, string>
                {
                    ["controllerArmed"] = "true",
                },
                new BridgeOverlayMetadata(
                    BridgeOverlayTarget.FooterAndToast,
                    TimeSpan.FromMilliseconds(1050),
                    "codex.wake"));
            Pulse(strength: 0.3);
            UpdateCodexStatus();
        }
        finally
        {
            _wakeInProgress = false;
        }
    }

    private bool ObserveCodexForeground(bool foreground)
    {
        if (!_settings.OnlyWhenCodexForeground)
        {
            _foregroundContinuityGate.Reset();
            return true;
        }

        var allowsInput = _foregroundContinuityGate.AllowsInput(
            foreground,
            Environment.TickCount64,
            BridgeTimings.ForegroundLossGraceMs);
        if (
            allowsInput ||
            !_controllerSession.IsArmed ||
            !_controllerSession.IsActive)
        {
            return allowsInput;
        }

        // Losing foreground pauses the armed session, but does not lock it.
        // This avoids requiring Menu again after Codex has been open for a while.
        PauseControllerInput(_xInputService.LastState);
        return false;
    }

    private bool TryAutoArmController(bool foreground)
    {
        if (!_controllerSession.TryAutoArm(
                _settings.BridgeEnabled,
                _controllerWasConnected,
                foreground))
        {
            return false;
        }

        _controllerInteraction.RequireNeutralRouting();
        _bridgeEvents.Publish(
            BridgeEventKeys.ControllerArmed,
            parameters: new Dictionary<string, string>
            {
                ["trigger"] = "agent-foreground",
                ["requiresNeutral"] = "true",
            },
            overlay: new BridgeOverlayMetadata(
                BridgeOverlayTarget.Footer,
                CoalesceKey: "controller.session"));
        return true;
    }

    private void PauseControllerInput(
        ControllerState? state = null)
    {
        CancelBaseCancelHold(showFeedback: false);
        CancelConversationBoundaryHold();
        if (_controllerSession.IsActive)
        {
            CancelPendingSidebarFocus();
            CancelPendingComposerSelection();
        }

        ResetRadialLayer(clearSuppression: true);
        ResetPushToTalk(stopDictation: true);
        ResetVirtualDialInput(closeMenu: true);

        _controllerSession.Pause(
            requireNeutral:
                state is null ||
                !IsControllerNeutral(state.Value));

        _controllerInteraction.ResetRouting();
    }

    private bool TryResumeControllerInput(ControllerState state)
    {
        var wasActive = _controllerSession.IsActive;
        if (!_controllerSession.TryActivate(IsControllerNeutral(state)))
        {
            return false;
        }

        if (!wasActive)
        {
            _controllerInteraction.ClearButtonHistory();
            _controllerInteraction.ResetRouting();
            ResetVirtualDialInput(closeMenu: false);
            ResetPushToTalk(stopDictation: true);
        }

        return true;
    }

    private bool IsControllerNeutral(ControllerState state)
    {
        var stickZone = Math.Clamp(
            _settings.DeadZone * 0.45,
            0.18,
            0.34);
        return
            state.IsConnected &&
            state.Buttons == ControllerButtons.None &&
            Math.Abs(state.LeftX) < stickZone &&
            Math.Abs(state.LeftY) < stickZone &&
            Math.Abs(state.RightX) < stickZone &&
            Math.Abs(state.RightY) < stickZone &&
            state.LeftTrigger < 0.12 &&
            state.RightTrigger < 0.12;
    }

    private void NavigateSidebarHorizontal(int direction)
    {
        _controllerInteraction.RequireLeftStickNeutral();
        _leftNavigationBlockedUntil =
            Environment.TickCount64 + BridgeTimings.GestureInputGuardMs;
        var entry = DevicePage.SelectedEntry;
        switch (SidebarNavigationIntentResolver.ResolveHorizontal(
                    _scope,
                    entry,
                    direction))
        {
            case SidebarNavigationIntent.EnterProjectDirectory:
                EnterProjectTasks(
                    entry!.ProjectPath,
                    preferredThreadId: null);
                break;
            case SidebarNavigationIntent.ExitProjectDirectory:
                ExitProjectTasks();
                break;
            case SidebarNavigationIntent.AlreadyAtRoot:
                ShowFeedback(
                    CurrentSidebarContextTitle(),
                    _localization.Strings.Get(
                        StringKeys.MessageAlreadyAtRootScope));
                break;
            case SidebarNavigationIntent.NoChildDirectory:
                ShowFeedback(
                    CurrentSidebarContextTitle(),
                    _localization.Strings.Get(
                        StringKeys.MessageFocusedEntryHasNoChildDirectory));
                break;
        }
    }

    private void NavigateConversationTurn(
        ConversationTurnInputAction action)
    {
        var actionId = ConversationTurnInputMap.ActionIdFor(action);
        if (actionId is null)
        {
            return;
        }

        _ = NavigateConversationTurnAsync(action, actionId.Value);
    }

    private async Task NavigateConversationTurnAsync(
        ConversationTurnInputAction action,
        ActionId actionId)
    {
        var title = action ==
            ConversationTurnInputAction.PreviousUserMessage
                ? RadialText(
                    "上一条用户消息",
                    "Previous user message")
                : RadialText(
                    "下一条用户消息",
                    "Next user message");
        var result = await TryExecuteActionAsync(
            actionId,
            "controller.active",
            action == ConversationTurnInputAction.PreviousUserMessage
                ? "controller.dpad.up"
                : "controller.dpad.down",
            "conversation.navigation",
            actionId.Value,
            ActionSafetyLevel.Routine)
            .ConfigureAwait(true);
        var succeeded = result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified;
        AddEvent(
            title +
            (succeeded
                ? $" · {_localization.Strings.Get(
                    StringKeys.MessageShortcutSent)}"
                : ExecutionSuffix(
                    false,
                    result?.ErrorCode)));
        if (!succeeded)
        {
            ShowFeedback(
                title,
                ExecutionFailureLabel(result?.ErrorCode));
        }

        Pulse(strength: succeeded ? 0.16 : 0.1);
    }

    private void BeginConversationBoundaryHold(
        ConversationTurnInputAction action)
    {
        _controllerHolds.BeginConversationBoundary(
            action,
            BridgeTimings.ConversationTopHoldMs,
            BridgeTimings.ConversationBottomHoldMs,
            CanContinueConversationBoundaryHold,
            ExecuteConversationBoundaryHoldAsync);
    }

    private void EndConversationBoundaryHold(
        ControllerButtons releasedButtons)
    {
        _controllerHolds.EndConversationBoundary(releasedButtons);
    }

    private async Task ExecuteConversationBoundaryHoldAsync(
        ConversationBoundary boundary)
    {
        var actionId = boundary == ConversationBoundary.Top
            ? ConversationActionContract.ScrollTopId
            : ConversationActionContract.ScrollBottomId;
        var result = await TryExecuteActionAsync(
                actionId,
                "controller.active",
                boundary == ConversationBoundary.Top
                    ? "controller.dpad.up.hold"
                    : "controller.dpad.down.hold",
                "conversation.navigation",
                actionId.Value,
                ActionSafetyLevel.Routine)
            .ConfigureAwait(true);
        var succeeded = result?.Outcome == ActionOutcome.Succeeded;
        var title = boundary == ConversationBoundary.Top
            ? RadialText("已置顶", "Jumped to top")
            : RadialText("已置底", "Jumped to bottom");
        AddEvent(
            title +
            ExecutionSuffix(
                succeeded,
                result?.ErrorCode));
        if (!succeeded)
        {
            ShowFeedback(
                title,
                ExecutionFailureLabel(result?.ErrorCode));
        }

        Pulse(strength: succeeded ? 0.18 : 0.1);
    }

    private bool CanContinueConversationBoundaryHold(
        ConversationBoundary boundary)
    {
        var state = _xInputService.LastState;
        var button =
            ConversationBoundaryHoldPolicy.ResolveButton(boundary);
        return
            state.IsConnected &&
            state.Buttons.HasFlag(button) &&
            _settings.BridgeEnabled &&
            _controllerSession.IsActive &&
            _radialLayers.Layer is null &&
            !IsVirtualDialContextActive &&
            !_controllerInteraction.PushToTalkBlocksBaseInput &&
            (
                !_settings.OnlyWhenCodexForeground ||
                _foregroundApplication.IsForeground
            );
    }

    private void CancelConversationBoundaryHold()
    {
        _controllerHolds.CancelConversationBoundary();
    }

    private void CycleRootSidebarScope()
    {
        _controllerInteraction.RequireLeftStickNeutral();
        _leftNavigationBlockedUntil =
            Environment.TickCount64 + BridgeTimings.GestureInputGuardMs;
        var rootScope = _scope == SidebarScope.ProjectTasks
            ? _sidebarReturnFrame?.Scope ?? SidebarScope.Projects
            : ActiveSidebarNavigation
                .SelectedEntry(_sidebarEntries)?
                .NavigationScope ?? _scope;
        var current = Array.IndexOf(RootSidebarScopes, rootScope);
        var rootEntries = _scope == SidebarScope.ProjectTasks
            ? _workspaceReader.BuildUnifiedEntries(_snapshot)
            : _sidebarEntries;
        for (var offset = 1; offset <= RootSidebarScopes.Length; offset++)
        {
            var next =
                (Math.Max(0, current) + offset) %
                RootSidebarScopes.Length;
            var candidate = RootSidebarScopes[next];
            if (rootEntries.Any(entry =>
                    entry.NavigationScope == candidate))
            {
                SetSidebarScope(candidate, showFeedback: true);
                return;
            }
        }
    }

    private void SetSidebarScope(
        SidebarScope scope,
        bool showFeedback,
        string? preferredId = null)
    {
        if (!RootSidebarScopes.Contains(scope))
        {
            return;
        }

        if (_scope == SidebarScope.ProjectTasks)
        {
            RememberCurrentSidebarCursor();
            RestoreProjectDisclosureLease();
            _sidebarReturnFrame = null;
        }
        else
        {
            RememberCurrentSidebarCursor();
        }

        _scope = scope;
        _projectTasksPinnedOnly = false;
        RebuildSidebarEntries();

        preferredId ??= _rootCursorIds.GetValueOrDefault(scope);
        if (
            ActiveSidebarNavigation.TryJumpToScope(
                _sidebarEntries,
                scope,
                preferredId,
                out var selected) &&
            selected is not null)
        {
            SelectSidebarIndex(ActiveSidebarNavigation.SelectedIndex);
            if (selected.Layer == SidebarLayer.Projects)
            {
                _selectedProjectPath = selected.ProjectPath;
            }
        }
        UpdateLayerTabs();
        FocusCurrentSidebarEntry(deferFocus: true);

        if (showFeedback)
        {
            var projectName = CurrentSidebarProjectName();
            var parameters = new Dictionary<string, string>
            {
                ["scope"] = scope.ToString(),
                ["label"] = ScopeLabel(scope),
            };
            if (!string.IsNullOrWhiteSpace(projectName))
            {
                parameters["project"] = projectName;
            }

            _bridgeEvents.Publish(
                BridgeEventKeys.SidebarScopeChanged,
                parameters: parameters,
                overlay: new BridgeOverlayMetadata(
                    BridgeOverlayTarget.Toast,
                    TimeSpan.FromMilliseconds(900),
                    "sidebar.scope"));
            Pulse();
        }
    }

    private void RestoreSidebarSelection(string? preferredId)
    {
        var fallbackIndex = Math.Max(
            0,
            ActiveSidebarNavigation.SelectedIndex);
        var selectedIndex = ActiveSidebarNavigation.Restore(
            _sidebarEntries,
            preferredId,
            fallbackIndex);
        if (selectedIndex < 0)
        {
            DevicePage.ClearSelection();
            return;
        }

        SelectSidebarIndex(selectedIndex);
        var selected = _sidebarEntries[selectedIndex];
        if (selected.Layer == SidebarLayer.Projects)
        {
            _selectedProjectPath = selected.ProjectPath;
        }
    }

    private void FocusCurrentSidebarEntry(bool deferFocus = false)
    {
        var selected = ActiveSidebarNavigation.SelectedEntry(_sidebarEntries);
        if (selected is not null)
        {
            FocusCodexSidebarEntry(selected, deferFocus);
        }
    }

    private void OpenSelectedSidebarTask(
        string deviceId,
        string controlId)
    {
        var entry = DevicePage.SelectedEntry;
        switch (SidebarNavigationIntentResolver.ResolvePrimary(entry))
        {
            case SidebarNavigationIntent.ProjectRequiresHorizontalEntry:
                ShowFeedback(
                    _localization.Strings.ControlPrimary(
                        Glyph(LogicalInput.FaceSouth)),
                    _localization.Strings.Get(
                        StringKeys.MessageUseRightToEnterProject));
                return;
            case SidebarNavigationIntent.EntryUnavailable:
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageAgentSidebar,
                        _activeAgent.DisplayName),
                    _localization.Strings.Get(
                        StringKeys.MessageNoAvailableEntries));
                return;
            case SidebarNavigationIntent.OpenTask:
                break;
            default:
                return;
        }

        _ = OpenThreadAsync(
            entry!.ThreadId!,
            entry.Title,
            entry.NativeTitle ?? entry.Title,
            deviceId,
            controlId);
    }

    private void ActivateSelectedSidebarEntryFromPointer()
    {
        if (DevicePage.SelectedEntry is { IsProject: true })
        {
            NavigateSidebarHorizontal(1);
            return;
        }

        OpenSelectedSidebarTask(
            deviceId: "desktop.pointer",
            controlId: "pointer.primary");
    }

    private void EnterProjectTasks(
        string? projectPath,
        string? preferredThreadId)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            ShowFeedback(
                _localization.Strings.Get(
                    StringKeys.MessageProjectTasks),
                _localization.Strings.Get(
                    StringKeys.MessageTaskHasNoProject));
            return;
        }

        var project = _snapshot.Projects.FirstOrDefault(item =>
            string.Equals(
                item.Path,
                projectPath,
                StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            ShowFeedback(
                _localization.Strings.Get(
                    StringKeys.MessageProjectTasks),
                _localization.Strings.Get(
                    StringKeys.MessageProjectUnavailable));
            return;
        }

        if (
            _scope != SidebarScope.ProjectTasks ||
            !string.Equals(
                _selectedProjectPath,
                projectPath,
                StringComparison.OrdinalIgnoreCase))
        {
            RememberCurrentSidebarCursor();
            RestoreProjectDisclosureLease();
            _sidebarReturnFrame = new SidebarReturnFrame(
                _scope == SidebarScope.ProjectTasks
                    ? SidebarScope.Projects
                    : _scope,
                DevicePage.SelectedEntry is { } selected
                    ? selected.Id
                    : null,
                _selectedProjectPath);
        }

        _selectedProjectPath = project.Path;
        _scope = SidebarScope.ProjectTasks;
        _projectTasksPinnedOnly = false;
        _projectDisclosureLease = new ProjectDisclosureLease(
            project.Name,
            project.IsPinned);
        preferredThreadId ??=
            _projectTaskCursorIds.GetValueOrDefault(project.Path);
        RebuildSidebarEntries(
            forceNavigationRebuild: false,
            fallbackIndexOverride: 0,
            preferredId: preferredThreadId);
        UpdateLayerTabs();
        FocusCurrentSidebarEntry();

        var position = ActiveSidebarNavigation.SelectedIndex >= 0
            ? $"{ActiveSidebarNavigation.SelectedIndex + 1} / {_sidebarEntries.Count}"
            : _localization.Strings.Get(
                StringKeys.MessageNoAvailableTasks);
        AddEvent(_localization.Strings.Format(
            StringKeys.MessageProjectTasksPosition,
            project.Name,
            position));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessageProjectTitle,
                project.Name),
            position);
        Pulse();
    }

    private void ExitProjectTasks()
    {
        if (_scope != SidebarScope.ProjectTasks)
        {
            return;
        }

        RememberCurrentSidebarCursor();
        var frame = _sidebarReturnFrame;
        RestoreProjectDisclosureLease();
        _sidebarReturnFrame = null;
        _selectedProjectPath = frame?.ProjectPath ?? _selectedProjectPath;
        SetSidebarScope(
            frame?.Scope ?? SidebarScope.Projects,
            showFeedback: true,
            preferredId: frame?.EntryId);
    }

    private void JumpToProjectContext()
    {
        if (_scope == SidebarScope.ProjectTasks)
        {
            ToggleProjectPinnedFilter();
            return;
        }

        if (DevicePage.SelectedEntry is not { } entry)
        {
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonProjectTasks,
                    Glyph(LogicalInput.FaceNorth)),
                _localization.Strings.Get(
                    StringKeys.MessageNoLocatableEntry));
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.ProjectPath))
        {
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonProjectTasks,
                    Glyph(LogicalInput.FaceNorth)),
                _localization.Strings.Get(
                    StringKeys.MessageTaskHasNoProject));
            return;
        }

        EnterProjectTasks(entry.ProjectPath, entry.ThreadId);
    }

    private void ToggleProjectPinnedFilter()
    {
        if (string.IsNullOrWhiteSpace(_selectedProjectPath))
        {
            return;
        }

        var previousId = DevicePage.SelectedEntry is { } entry
            ? entry.Id
            : null;
        _projectTasksPinnedOnly = !_projectTasksPinnedOnly;
        RebuildSidebarEntries();
        if (_projectTasksPinnedOnly && _sidebarEntries.Count == 0)
        {
            _projectTasksPinnedOnly = false;
            RebuildSidebarEntries();
            RestoreSidebarSelection(previousId);
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonProjectTasks,
                    Glyph(LogicalInput.FaceNorth)),
                _localization.Strings.Get(
                    StringKeys.MessageProjectHasNoPinnedTasks));
            return;
        }

        RestoreSidebarSelection(previousId);
        FocusCurrentSidebarEntry();
        var label = _projectTasksPinnedOnly
            ? _localization.Strings.Get(
                StringKeys.MessageProjectPinnedOnly)
            : _localization.Strings.Get(
                StringKeys.MessageAllTasks);
        AddEvent(_localization.Strings.Format(
            StringKeys.MessageProjectTaskFilter,
            label));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessageButtonProjectTasks,
                Glyph(LogicalInput.FaceNorth)),
            label);
        Pulse();
    }

    private void RememberCurrentSidebarCursor()
    {
        if (DevicePage.SelectedEntry is not { } selected)
        {
            return;
        }

        if (
            _scope == SidebarScope.ProjectTasks &&
            !string.IsNullOrWhiteSpace(_selectedProjectPath))
        {
            _projectTaskCursorIds[_selectedProjectPath] = selected.Id;
            return;
        }

        if (RootSidebarScopes.Contains(_scope))
        {
            _rootCursorIds[_scope] = selected.Id;
        }
    }

    private void MoveSidebarSelection(int direction)
    {
        if (_sidebarEntries.Count == 0)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageScopeHasNoEntries,
                ScopeLabel(_scope)));
            return;
        }

        if (
            !ActiveSidebarNavigation.TryMove(
                _sidebarEntries,
                direction,
                out var entry) ||
            entry is null)
        {
            ShowSidebarNavigationMenu();
            return;
        }

        SelectSidebarIndex(ActiveSidebarNavigation.SelectedIndex);
        ActivateSelectedEntry(
            entry,
            deferFocus: true,
            showToast: false);
        ShowSidebarNavigationMenu();
    }

    private void SelectSidebarIndex(int index)
    {
        _suppressSelectionActivation = true;
        DevicePage.SelectSidebarIndex(index);
        _suppressSelectionActivation = false;
    }

    private void ActivateSelectedEntry(
        SidebarEntry entry,
        bool deferFocus = false,
        bool showToast = true)
    {
        if (
            _scope != SidebarScope.ProjectTasks &&
            RootSidebarScopes.Contains(entry.NavigationScope) &&
            _scope != entry.NavigationScope)
        {
            _scope = entry.NavigationScope;
            UpdateLayerTabs();
        }

        RememberCurrentSidebarCursor();
        FocusCodexSidebarEntry(entry, deferFocus);
        if (entry.Layer == SidebarLayer.Projects)
        {
            _selectedProjectPath = entry.ProjectPath;
            _devicePageViewModel.UpdateSidebarContextText(entry.Title);
        }

        RememberCurrentSidebarCursor();
        AddEvent($"{ScopeLabel(_scope)} · {entry.Title}");
        if (showToast)
        {
            ShowFeedback(ScopeLabel(_scope), entry.Title);
        }

        Pulse();
    }

    private void ShowSidebarNavigationMenu()
    {
        if (!_settings.ShowOverlay)
        {
            return;
        }

        var rootNavigation = _sidebarNavigationDirectory.Root;
        IReadOnlyList<SidebarEntry> rootEntries;
        string? selectedRootId;
        IReadOnlyList<SidebarEntry>? childEntries = null;
        string? selectedChildId = null;
        string? childTitle = null;
        var childIsActive = false;

        if (_scope == SidebarScope.ProjectTasks)
        {
            rootEntries = rootNavigation.FrozenEntries.Count > 0
                ? rootNavigation.FrozenEntries
                : _workspaceReader.BuildUnifiedEntries(_snapshot);
            var disclosedProject = rootEntries.FirstOrDefault(entry =>
                entry.IsProject &&
                string.Equals(
                    entry.ProjectPath,
                    _selectedProjectPath,
                    StringComparison.OrdinalIgnoreCase));
            selectedRootId = disclosedProject?.Id ??
                             rootNavigation.SelectedEntry(rootEntries)?.Id;
            childEntries = _sidebarEntries;
            selectedChildId = ActiveSidebarNavigation
                .SelectedEntry(_sidebarEntries)?.Id;
            childTitle = disclosedProject?.Title ??
                         CurrentSidebarProjectName() ??
                         ScopeLabel(SidebarScope.ProjectTasks);
            childIsActive = true;
        }
        else
        {
            rootEntries = _sidebarEntries;
            var selectedRoot = rootNavigation.SelectedEntry(rootEntries);
            selectedRootId = selectedRoot?.Id;
            if (selectedRoot is { IsProject: true } &&
                !string.IsNullOrWhiteSpace(selectedRoot.ProjectPath))
            {
                childEntries = _workspaceReader.BuildEntries(
                    _snapshot,
                    SidebarScope.ProjectTasks,
                    selectedRoot.ProjectPath);
                selectedChildId = _projectTaskCursorIds.GetValueOrDefault(
                    selectedRoot.ProjectPath);
                childTitle = selectedRoot.Title;
            }
        }

        if (rootEntries.Count == 0)
        {
            return;
        }

        var menu = SidebarNavigationMenuProjector.Project(
            rootEntries,
            selectedRootId,
            SidebarMenuScopeLabel,
            childEntries,
            selectedChildId,
            childTitle,
            childIsActive);
        _sidebarNavigationMenuOverlayWindow?.ShowState(menu with
        {
            Title = RadialText("侧边栏", "Sidebar"),
            NavigateGlyph = Glyph(LogicalInput.LeftStick),
            NavigateHint = RadialText("移动", "Move"),
            CycleScopeGlyph = Glyph(LogicalInput.LeftStickPress),
            CycleScopeHint = RadialText("区域", "Region"),
            OpenGlyph = Glyph(LogicalInput.FaceSouth),
            OpenHint = RadialText("打开", "Open"),
        });
    }

    private void FocusCodexSidebarEntry(
        SidebarEntry entry,
        bool deferFocus = false)
    {
        CancelPendingSidebarFocus();
        if (!_settings.BridgeEnabled)
        {
            return;
        }

        var projectName = entry.NativeListIndex is not null
            ? null
            : _snapshot.Projects
                .FirstOrDefault(project =>
                    string.Equals(
                        project.Path,
                        entry.ProjectPath ?? _selectedProjectPath,
                        StringComparison.OrdinalIgnoreCase))?.Name;
        var cancellation = new CancellationTokenSource();
        _sidebarFocusCancellation = cancellation;
        var disclosureLease =
            _scope == SidebarScope.ProjectTasks
                ? _projectDisclosureLease
                : null;
        _ = FocusCodexSidebarEntryAsync(
            entry,
            projectName,
            disclosureLease,
            cancellation,
            deferFocus);
    }

    private async Task FocusCodexSidebarEntryAsync(
        SidebarEntry entry,
        string? projectName,
        ProjectDisclosureLease? disclosureLease,
        CancellationTokenSource cancellation,
        bool deferFocus)
    {
        var gateEntered = false;
        try
        {
            if (deferFocus)
            {
                await Task.Delay(
                        BridgeTimings.SidebarFocusSettleMs,
                        cancellation.Token)
                    .ConfigureAwait(true);
            }

            await _sidebarFocusGate.WaitAsync(cancellation.Token)
                .ConfigureAwait(true);
            gateEntered = true;
            var result = await Task.Run(
                    () => _sidebarAutomation.FocusEntry(
                        entry,
                        projectName,
                        _settings,
                        cancellation.Token,
                        disclosureLease),
                    cancellation.Token)
                .ConfigureAwait(true);
            if (
                !result.Succeeded &&
                !cancellation.IsCancellationRequested &&
                !string.Equals(
                    result.Error,
                    AgentAutomationErrorCodes.OperationCanceled,
                    StringComparison.Ordinal))
            {
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageSidebarFocusFailed,
                    _localization.Strings.ErrorLabel(
                        result.Error,
                        result.ErrorDetail)));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer stick movement replaced this focus target.
        }
        finally
        {
            if (gateEntered)
            {
                _sidebarFocusGate.Release();
            }

            if (ReferenceEquals(_sidebarFocusCancellation, cancellation))
            {
                _sidebarFocusCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingSidebarFocus()
    {
        var cancellation = _sidebarFocusCancellation;
        _sidebarFocusCancellation = null;
        cancellation?.Cancel();
    }

    private void RestoreProjectDisclosureLease()
    {
        var lease = _projectDisclosureLease;
        _projectDisclosureLease = null;
        if (lease is null || !_settings.BridgeEnabled)
        {
            return;
        }

        CancelPendingSidebarFocus();
        var result = _sidebarAutomation.RestoreDisclosure(lease);
        if (!result.Succeeded)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageDisclosureRestoreFailed,
                _localization.Strings.ErrorLabel(
                    result.Error,
                    result.ErrorDetail)));
        }
    }

    private async Task OpenThreadAsync(
        string threadId,
        string threadTitle,
        string nativeThreadTitle,
        string deviceId,
        string controlId)
    {
        var result = await _threadNavigation.OpenAsync(
            new ThreadOpenRequest(
                threadId,
                threadTitle,
                nativeThreadTitle,
                deviceId,
                controlId,
                IsActive))
            .ConfigureAwait(true);

        if (result.Outcome == ThreadOpenOutcome.BlockedByForeground)
        {
            return;
        }

        if (result.Outcome == ThreadOpenOutcome.ThreadUnavailable)
        {
            AddEvent(_localization.Strings.Get(
                StringKeys.MessageTaskUnavailableSkipped));
            RefreshCodexData(preserveSelection: true);
            return;
        }

        if (result.Outcome == ThreadOpenOutcome.Requested)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageOpeningThread,
                threadTitle));
            ShowFeedback(
                _localization.Strings.Get(
                    StringKeys.MessageOpeningTask),
                threadTitle);
        }
        else
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageOpenThreadFailed,
                threadTitle));
        }
    }

    private async Task<ActionResult?> TryExecuteActionAsync(
        ActionId actionId,
        string deviceId,
        string controlId,
        string context,
        string idempotencyScope,
        ActionSafetyLevel safetyLevel,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        try
        {
            return await _actionDispatcher.ExecuteAsync(
                actionId,
                deviceId,
                controlId,
                context,
                idempotencyScope,
                safetyLevel,
                parameters).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Localized feedback remains a presentation concern during migration.
            return null;
        }
    }

    private void ThreadNavigation_NoticePublished(
        object? sender,
        ThreadNavigationNotice notice)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(
                () => PresentThreadNavigationNotice(notice)));
            return;
        }

        PresentThreadNavigationNotice(notice);
    }

    private void PresentThreadNavigationNotice(
        ThreadNavigationNotice notice)
    {
        var undoGlyph = Glyph(LogicalInput.FaceEast);
        switch (notice.Kind)
        {
            case ThreadNavigationNoticeKind.UndoUnavailableNonUnique:
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoUnavailableUnique,
                    notice.TargetDisplayTitle));
                break;
            case ThreadNavigationNoticeKind.ArrivalConfirmed:
                if (_scope == SidebarScope.ProjectlessTasks)
                {
                    RefreshCodexData(preserveSelection: true);
                }
                break;
            case ThreadNavigationNoticeKind.UndoAvailable:
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageOpenedUndoAvailable,
                    notice.TargetDisplayTitle,
                    undoGlyph));
                ShowFeedback(
                    _localization.Strings.Get(
                        StringKeys.MessageOpenedTask),
                    _localization.Strings.Format(
                        StringKeys.MessageUndoWithinSeconds,
                        notice.TargetDisplayTitle,
                        (int)BridgeTimings
                            .NavigationUndoWindow.TotalSeconds,
                        undoGlyph));
                break;
            case ThreadNavigationNoticeKind.UndoQueued:
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoQueued,
                    undoGlyph));
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageButtonUndo,
                        undoGlyph),
                    _localization.Strings.Get(
                        StringKeys.MessageUndoAfterOpen));
                Pulse(strength: 0.12);
                break;
            case ThreadNavigationNoticeKind.UndoUnavailableUnconfirmed:
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoUnavailableUnconfirmed,
                    notice.TargetDisplayTitle));
                if (notice.UndoWasRequested)
                {
                    ShowFeedback(
                        _localization.Strings.Format(
                            StringKeys.MessageButtonUndo,
                            undoGlyph),
                        _localization.Strings.Get(
                            StringKeys.MessageUndoUnconfirmed));
                }
                break;
            case ThreadNavigationNoticeKind.UndoPageChanged:
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoPageChanged,
                    undoGlyph));
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageButtonUndo,
                        undoGlyph),
                    _localization.Strings.Get(
                        StringKeys.MessageUndoPageChangedDetail));
                Pulse(strength: 0.12);
                break;
            case ThreadNavigationNoticeKind.UndoSucceeded:
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoSucceeded,
                    undoGlyph,
                    notice.TargetDisplayTitle));
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageButtonUndo,
                        undoGlyph),
                    _localization.Strings.Format(
                        StringKeys.MessageReturnedToPreviousTask,
                        notice.TargetDisplayTitle));
                Pulse(strength: 0.18);
                break;
            case ThreadNavigationNoticeKind.UndoFailed:
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoFailed,
                    undoGlyph,
                    ExecutionFailureLabel(notice.ErrorCode)));
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageButtonUndo,
                        undoGlyph),
                    ExecutionFailureLabel(notice.ErrorCode));
                Pulse(strength: 0.12);
                break;
        }
    }

    private void BeginVirtualDialPress()
    {
        if (
            _rightStickPressHeld ||
            _virtualDialOpenPending ||
            _virtualDialCancelRequested ||
            _virtualDialCleanupPending ||
            _dialInputReleasePending)
        {
            return;
        }

        CancelPendingComposerSelection();
        ClearPendingVirtualDialInput();
        _controllerInteraction.RequireRightStickNeutral();
        _rightStickPressHeld = true;
        _rightStickHoldTriggered = false;
        _rightStickPressCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _rightStickPressCancellation = cancellation;
        _ = PromoteVirtualDialHoldAsync(cancellation);
    }

    private async Task PromoteVirtualDialHoldAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                    BridgeTimings.DialHoldMs,
                    cancellation.Token)
                .ConfigureAwait(true);
            if (
                !_rightStickPressHeld ||
                !ReferenceEquals(
                    _rightStickPressCancellation,
                    cancellation))
            {
                return;
            }

            _rightStickHoldTriggered = true;
            ClearPendingVirtualDialInput();
            if (IsVirtualDialContextActive)
            {
                await CloseVirtualDialMenuAsync(showFeedback: false)
                    .ConfigureAwait(true);
            }

            RestoreWindow();
            ShowPage(SettingsPage);
            SetSelectedNav(SettingsNavButton);
            _devicePageViewModel.UpdateRightMode(
                RightControlMode.Dial,
                _localization.Strings.ComposerDialSettingsOpened);
            Pulse(strength: 0.18);
        }
        catch (OperationCanceledException)
        {
            // R3 was released before the settings hold threshold.
        }
        finally
        {
            if (ReferenceEquals(
                    _rightStickPressCancellation,
                    cancellation))
            {
                _rightStickPressCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void EndVirtualDialPress()
    {
        if (!_rightStickPressHeld)
        {
            return;
        }

        _rightStickPressHeld = false;
        var cancellation = _rightStickPressCancellation;
        _rightStickPressCancellation = null;
        cancellation?.Cancel();
        if (_rightStickHoldTriggered)
        {
            _rightStickHoldTriggered = false;
            return;
        }

        HandleComposerDialShortPress();
    }

    private void HandleComposerDialShortPress()
    {
        _controllerInteraction.RequireRightStickNeutral();
        _ = PressVirtualDialAsync();
    }

    private void QueueVirtualDialEncoderSteps(int steps)
    {
        if (
            steps == 0 ||
            _virtualDialCancelRequested ||
            _virtualDialCleanupPending ||
            _dialInputReleasePending)
        {
            return;
        }

        _encoderSteps.Add(steps, Stopwatch.GetTimestamp());
        if (
            _encoderSteps.Pending != 0 &&
            Interlocked.Exchange(ref _encoderStepPumpRunning, 1) == 0)
        {
            _ = PumpVirtualDialEncoderStepsAsync();
        }
    }

    private async Task PumpVirtualDialEncoderStepsAsync()
    {
        try
        {
            while (true)
            {
                var intent = _encoderSteps.TakeNext(
                    Stopwatch.GetTimestamp(),
                    ToStopwatchTicks(EncoderIntentMaximumAge));
                if (intent is null)
                {
                    return;
                }

                var generation =
                    Volatile.Read(ref _virtualDialGeneration);
                var sendStarted = Stopwatch.GetTimestamp();
                var result = await RunVirtualDialAutomationAsync(
                        () => _composerAutomation.DialStep(
                            intent.Value.Direction,
                            _settings))
                    .ConfigureAwait(true);
                if (
                    Stopwatch.GetTimestamp() - sendStarted >
                    ToStopwatchTicks(EncoderIntentMaximumAge))
                {
                    _encoderSteps.Clear();
                }

                if (
                    generation ==
                        Volatile.Read(ref _virtualDialGeneration) &&
                    !_virtualDialCancelRequested)
                {
                    PresentVirtualDialResult(result);
                    if (result.Succeeded)
                    {
                        QueueMicroReadback();
                    }
                }

                if (!result.Succeeded)
                {
                    _encoderSteps.Clear();
                    return;
                }

                if (_encoderSteps.Pending != 0)
                {
                    await Task.Delay(EncoderStepInterval)
                        .ConfigureAwait(true);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _encoderStepPumpRunning, 0);
            if (
                _encoderSteps.Pending != 0 &&
                Interlocked.Exchange(
                    ref _encoderStepPumpRunning,
                    1) == 0)
            {
                _ = PumpVirtualDialEncoderStepsAsync();
            }
        }
    }

    private static long ToStopwatchTicks(TimeSpan duration) =>
        Math.Max(
            1,
            (long)Math.Round(
                duration.TotalSeconds * Stopwatch.Frequency));

    private void QueueVirtualDialNavigation(
        ComposerDialNavigation navigation)
    {
        if (
            _virtualDialCancelRequested ||
            _virtualDialCleanupPending ||
            _dialInputReleasePending)
        {
            return;
        }

        _currentControlIntents.Offer(
            navigation,
            Volatile.Read(ref _virtualDialGeneration),
            Stopwatch.GetTimestamp());
        if (_virtualDialOpenPending)
        {
            return;
        }

        if (!CanExecuteCurrentControlIntent(navigation))
        {
            QueueMicroReadback();
            return;
        }

        CancelPendingComposerSelection();
        StartVirtualDialNavigationPump();
    }

    private bool CanExecuteCurrentControlIntent(
        ComposerDialNavigation navigation) =>
        (
            navigation == ComposerDialNavigation.Left &&
            _microReadback.IsMenuOpen
        ) ||
        (
            _microReadback.SelectionVerified &&
            !string.IsNullOrWhiteSpace(_microReadback.ItemName)
        );

    private void StartVirtualDialNavigationPump()
    {
        if (Interlocked.Exchange(ref _dialPumpRunning, 1) == 0)
        {
            _ = PumpVirtualDialStepsAsync();
        }
    }

    private async Task PumpVirtualDialStepsAsync()
    {
        try
        {
            while (true)
            {
                var generation =
                    Volatile.Read(ref _virtualDialGeneration);
                var intent = _currentControlIntents.Take(
                    generation,
                    Stopwatch.GetTimestamp(),
                    ToStopwatchTicks(
                        CurrentControlIntentMaximumAge));
                if (intent is null)
                {
                    return;
                }

                var result = await RunVirtualDialAutomationAsync(
                        () => _currentControlExecutor.Execute(
                            _microReadback,
                            intent.Value.Navigation))
                    .ConfigureAwait(true);
                if (
                    generation ==
                        Volatile.Read(ref _virtualDialGeneration) &&
                    !_virtualDialCancelRequested)
                {
                    PresentVirtualDialResult(result);
                    if (result.Succeeded)
                    {
                        QueueMicroReadback();
                    }
                    else if (result.ErrorDetail is
                                 "dial-current-control-unverified" or
                                 "dial-current-control-focus")
                    {
                        QueueMicroReadback();
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref _dialPumpRunning, 0);
            if (
                _currentControlIntents.HasPending &&
                Interlocked.Exchange(ref _dialPumpRunning, 1) == 0)
            {
                _ = PumpVirtualDialStepsAsync();
            }
        }
    }

    private async Task PressVirtualDialAsync()
    {
        if (
            _virtualDialOpenPending ||
            _virtualDialCancelRequested ||
            _virtualDialCleanupPending)
        {
            return;
        }

        CancelPendingComposerSelection();
        _virtualDialOpenPending = true;
        _virtualDialCancelRequested = false;
        _devicePageViewModel.UpdateRightMode(
            RightControlMode.Dial,
            RadialText("正在打开…", "Opening…"));
        var generation =
            Volatile.Read(ref _virtualDialGeneration);
        var result = await RunVirtualDialAutomationAsync(
                () => _composerAutomation.DialPress(_settings))
            .ConfigureAwait(true);
        if (
            generation !=
            Volatile.Read(ref _virtualDialGeneration))
        {
            var cleanup = await RunVirtualDialAutomationAsync(
                    () => _composerAutomation.DialCancel(_settings))
                .ConfigureAwait(true);
            _virtualDialCleanupPending = false;
            _virtualDialOpenPending = false;
            _virtualDialCancelRequested = false;
            if (_pushToTalkAutomation.WantsDictation)
            {
                return;
            }

            SetVirtualDialMenuOpen(
                cleanup.IsMenuOpen,
                cleanup.RequiresConfirmation);
            if (!cleanup.IsMenuOpen)
            {
                BeginVirtualDialReleaseDrain();
            }

            return;
        }

        _virtualDialCleanupPending = false;
        _virtualDialOpenPending = false;
        if (_virtualDialCancelRequested)
        {
            return;
        }

        PresentVirtualDialResult(result);
        if (result.Succeeded)
        {
            QueueMicroReadback();
        }
    }

    private void QueueMicroReadback()
    {
        if (_microReadbackRequests.Request())
        {
            StartMicroReadbackPump();
        }
    }

    private void StartMicroReadbackPump()
    {
        var cancellation = new CancellationTokenSource();
        _microReadbackCancellation = cancellation;
        var generation = Volatile.Read(ref _virtualDialGeneration);
        _ = PumpMicroReadbackAsync(generation, cancellation);
    }

    private async Task PumpMicroReadbackAsync(
        int generation,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (_microReadbackRequests.TryConsume())
            {
                var readback = await _microReadbackObserver
                    .ObserveAsync(cancellation.Token)
                    .ConfigureAwait(true);
                if (
                    cancellation.IsCancellationRequested ||
                    !ReferenceEquals(
                        _microReadbackCancellation,
                        cancellation) ||
                    generation !=
                        Volatile.Read(ref _virtualDialGeneration))
                {
                    return;
                }

                if (
                    readback.Surface == CodexMicroSurfaceKind.None &&
                    !readback.SelectionVerified)
                {
                    readback = CodexMicroReadback.Closed;
                }

                ApplyMicroReadback(readback);

                if (
                    _currentControlIntents.PendingNavigation is
                        { } navigation &&
                    CanExecuteCurrentControlIntent(navigation))
                {
                    CancelPendingComposerSelection();
                    StartVirtualDialNavigationPump();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A dial epoch reset owns cancellation. Ordinary refresh requests
            // are coalesced and never cancel an in-flight observation.
        }
        finally
        {
            if (ReferenceEquals(
                    _microReadbackCancellation,
                    cancellation))
            {
                _microReadbackCancellation = null;
            }

            cancellation.Dispose();

            if (_microReadbackRequests.Complete())
            {
                StartMicroReadbackPump();
            }
        }
    }

    private void ApplyMicroReadback(CodexMicroReadback readback)
    {
        var menuWasOpen = _virtualDialMenuOpen;
        _microReadback = readback;
        SetVirtualDialMenuOpen(
            readback.IsMenuOpen,
            readback.Surface == CodexMicroSurfaceKind.Dialog);
        if (!string.IsNullOrWhiteSpace(readback.DisplayText))
        {
            _devicePageViewModel.UpdateRightMode(
                RightControlMode.Dial,
                readback.DisplayText);
        }

        if (menuWasOpen && !readback.IsMenuOpen)
        {
            BeginVirtualDialReleaseDrain();
        }
    }

    private void SelectVirtualDialOption()
    {
        if (
            !IsVirtualDialContextActive ||
            _virtualDialOpenPending ||
            _virtualDialCancelRequested ||
            _virtualDialCleanupPending)
        {
            return;
        }

        _ = SelectVirtualDialOptionAsync();
    }

    private async Task SelectVirtualDialOptionAsync()
    {
        CancelPendingComposerSelection();
        var generation =
            Volatile.Read(ref _virtualDialGeneration);
        var result = await RunVirtualDialAutomationAsync(
                () => _composerAutomation.DialSelect(
                    _settings))
            .ConfigureAwait(true);
        if (
            generation !=
                Volatile.Read(ref _virtualDialGeneration) ||
            _virtualDialCancelRequested)
        {
            return;
        }

        PresentVirtualDialResult(result);
        if (result.Succeeded && !result.StateVerified)
        {
            QueueMicroReadback();
        }
        else if (result.Succeeded && !result.IsMenuOpen)
        {
            _composerPickerMenuLikelyOpen = false;
            BeginVirtualDialReleaseDrain();
        }
    }

    private void BeginBaseCancelPress()
    {
        if (
            _dictationInjected ||
            _pushToTalkAutomation.WantsDictation ||
            _controllerInteraction.PushToTalkBlocksBaseInput)
        {
            CancelAction();
            return;
        }

        if (IsVirtualDialContextActive)
        {
            CancelActionOrDialMenu();
            return;
        }

        if (_composerPickerMenuLikelyOpen)
        {
            CloseComposerPickerOnly(showProgress: true);
            return;
        }

        var hasPendingLocalAction =
            _composerPickerCancellation is not null;
        if (hasPendingLocalAction)
        {
            CancelAction();
            return;
        }

        if (_threadNavigation.TryRequestUndo())
        {
            return;
        }

        BeginCancelHold();
    }

    private void EndBaseCancelPress()
    {
        CancelBaseCancelHold(showFeedback: true);
    }

    private void BeginCancelHold()
    {
        _controllerHolds.BeginCancelHold(
            BridgeTimings.CancelHoldMs,
            CanContinueCancelHold,
            remaining =>
            {
                ShowCancelHoldCountdown(remaining);
                Pulse(strength: 0.06);
            },
            StopCurrentTurn);
    }

    private bool CanContinueCancelHold()
    {
        var state = _xInputService.LastState;
        return
            state.IsConnected &&
            state.Buttons.HasFlag(ControllerButtons.B) &&
            _settings.BridgeEnabled &&
            _controllerSession.IsActive &&
            (
                !_settings.OnlyWhenCodexForeground ||
                _foregroundApplication.IsForeground
            );
    }

    private void ShowCancelHoldCountdown(int remainingSeconds)
    {
        var glyph = Glyph(LogicalInput.FaceEast);
        _overlayWindow?.ShowMessage(
            RadialText(
                $"长按 {glyph} 取消会话",
                $"Hold {glyph} to cancel the turn"),
            RadialText(
                $"{remainingSeconds} 秒 · 松开可中止",
                $"{remainingSeconds}s · release to abort"),
            TimeSpan.FromMilliseconds(1150));
    }

    private void CancelBaseCancelHold(bool showFeedback)
    {
        if (!_controllerHolds.CancelBaseCancelHold())
        {
            return;
        }

        if (showFeedback)
        {
            _overlayWindow?.ShowMessage(
                RadialText("取消会话", "Cancel turn"),
                RadialText("已中止倒计时。", "Countdown aborted."),
                TimeSpan.FromMilliseconds(850));
        }
    }

    private void CloseComposerPickerOnly(bool showProgress = false)
    {
        if (
            _virtualDialCancelRequested ||
            _virtualDialCleanupPending)
        {
            return;
        }

        if (showProgress)
        {
            ShowComposerExitPending();
        }

        _virtualDialCancelRequested = true;
        ClearPendingVirtualDialInput();
        _ = CloseVirtualDialMenuAsync(
            showFeedback: false,
            fallbackToBaseCancel: false);
    }

    private void CancelActionOrDialMenu()
    {
        if (
            _dictationInjected ||
            _controllerInteraction.PushToTalkBlocksBaseInput)
        {
            CancelAction();
            return;
        }

        if (
            _virtualDialCancelRequested ||
            _virtualDialCleanupPending)
        {
            return;
        }

        var expectedDialContext = IsVirtualDialContextActive;
        ShowComposerExitPending();
        _virtualDialCancelRequested = true;
        ClearPendingVirtualDialInput();
        _ = CloseVirtualDialMenuAsync(
            showFeedback: true,
            fallbackToBaseCancel: !expectedDialContext);
    }

    private void ShowComposerExitPending()
    {
        var title = RadialText("正在退出…", "Closing…");
        var detail = RadialText(
            "请稍候，B 键暂时不可用。",
            "Please wait; B is temporarily disabled.");
        _devicePageViewModel.UpdateRightMode(
            RightControlMode.Dial,
            title);
        AddEvent($"{title} · {detail}");
        _overlayWindow?.ShowMessage(
            title,
            detail,
            TimeSpan.FromMilliseconds(2200));
    }

    private async Task CloseVirtualDialMenuAsync(
        bool showFeedback,
        bool fallbackToBaseCancel = false)
    {
        var hadContext = IsVirtualDialContextActive;
        var generation =
            Volatile.Read(ref _virtualDialGeneration);
        var result = await RunVirtualDialAutomationAsync(
                () => _composerAutomation.DialCancel(_settings))
            .ConfigureAwait(true);
        if (
            generation !=
            Volatile.Read(ref _virtualDialGeneration))
        {
            return;
        }

        SetVirtualDialMenuOpen(
            result.IsMenuOpen,
            result.RequiresConfirmation);
        _composerPickerMenuLikelyOpen = result.IsMenuOpen;
        _virtualDialOpenPending = false;
        _virtualDialCancelRequested = false;
        if (
            fallbackToBaseCancel &&
            result.Succeeded &&
            !result.MenuWasPresent)
        {
            BeginCancelHold();
            return;
        }

        if (result.Succeeded)
        {
            if (hadContext && !result.IsMenuOpen)
            {
                BeginVirtualDialReleaseDrain();
            }

            _devicePageViewModel.UpdateRightMode(
                RightControlMode.Dial,
                _localization.Strings.ComposerDialReady);
            if (showFeedback)
            {
                if (result.IsMenuOpen)
                {
                    PresentVirtualDialResult(result);
                }
            }

            return;
        }

        if (showFeedback)
        {
            PresentVirtualDialResult(result);
        }
    }

    private async Task<ComposerDialResult>
        RunVirtualDialAutomationAsync(
            Func<ComposerDialResult> action)
    {
        await _dialAutomationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            return await Task.Run(action).ConfigureAwait(true);
        }
        finally
        {
            _dialAutomationGate.Release();
        }
    }

    private void PresentVirtualDialResult(ComposerDialResult result)
    {
        if (!result.Succeeded)
        {
            if (result.StateVerified)
            {
                SetVirtualDialMenuOpen(
                    result.IsMenuOpen,
                    result.RequiresConfirmation);
            }
            var failure = VirtualDialFailureLabel(result);
            _devicePageViewModel.UpdateRightMode(
                RightControlMode.Dial,
                failure);
            AddEvent(
                VirtualDialEventText(failure, result));
            if (ShouldShowVirtualDialFailure(result))
            {
                ShowFeedback(
                    _localization.Strings.VirtualDial,
                    failure);
            }

            Pulse(strength: 0.08);
            return;
        }

        if (result.StateVerified)
        {
            SetVirtualDialMenuOpen(
                result.IsMenuOpen,
                result.RequiresConfirmation);
        }

        _rightMode = RightControlMode.Dial;
        var value = string.IsNullOrWhiteSpace(result.ControlName)
            ? _localization.Strings.ComposerDialReady
            : result.ControlName;
        _devicePageViewModel.UpdateRightMode(
            RightControlMode.Dial,
            value);
        AddEvent(
            VirtualDialEventText(value, result));
        Pulse(strength: result.IsMenuOpen ? 0.18 : 0.12);
    }

    private string VirtualDialEventText(
        string value,
        ComposerDialResult result)
    {
        var text =
            $"{_localization.Strings.VirtualDial} · {value}";
        return result.ElapsedMilliseconds is { } elapsed
            ? $"{text} · {elapsed} ms"
            : text;
    }

    private static bool ShouldShowVirtualDialFailure(
        ComposerDialResult result)
    {
        return result.ErrorDetail is not
            (
                "dial-selection-unverified" or
                "dial-step-no-selection-change"
            );
    }

    private string VirtualDialFailureLabel(ComposerDialResult result)
    {
        return result.ErrorDetail switch
        {
            "dial-explicit-turn-action" =>
                RadialText(
                    "Steer / Queue 请使用 RT+X / RT+Y",
                    "Use RT+X / RT+Y for Steer / Queue"),
            "dial-destructive-action-blocked" =>
                RadialText(
                    "删除属于受保护操作，请使用 Codex 原生队列菜单",
                    "Delete is protected; use the native Codex queue menu"),
            "dial-selection-unverified" =>
                RadialText(
                    "先左右拨动，看到选项高亮后再按下 RS",
                    "Move left or right until an option is highlighted, then press RS"),
            "dial-step-no-selection-change" =>
                RadialText(
                    "没有可选项 · B 关闭 · RS 重新打开",
                    "No selectable option · B close · RS reopen"),
            "dial-enter-with-right" =>
                RadialText(
                    "这是一个子菜单 · 右推右摇杆进入",
                    "This item has a submenu · move the right stick right"),
            "dial-no-submenu" =>
                RadialText(
                    "这是具体选项 · 按 A 确认",
                    "This is a concrete option · press A to select"),
            "dial-closed-horizontal-only" =>
                RadialText(
                    "菜单关闭时只用左右切换控件",
                    "Use left or right to switch controls while closed"),
            "dial-initial-focus" =>
                RadialText(
                    "菜单已打开，但选项未能高亮 · B 关闭后重试",
                    "The picker opened but no option was highlighted · close with B and retry"),
            "dial-native-input" =>
                RadialText(
                    "Codex 未接收原生导航键，请确认窗口仍在前台",
                    "Codex did not receive the native navigation key; keep it in the foreground"),
            "dial-current-control-unverified" =>
                RadialText(
                    "当前控件没有可验证高亮；先上下选择后再左右操作",
                    "The current control has no verified highlight; select it with up/down first"),
            "dial-current-control-focus" =>
                RadialText(
                    "当前高亮与 Codex 键盘焦点不一致，未发送左右动作",
                    "The visible selection and Codex keyboard focus differ; no horizontal input was sent"),
            "dial-current-control-no-change" =>
                RadialText(
                    "Codex 没有确认当前控件的数值变化",
                    "Codex did not confirm a value change on the current control"),
            "dial-native-navigation-unverified" =>
                RadialText(
                    "导航键已发送，但未能读回高亮项",
                    "The navigation key was sent, but the highlighted item could not be verified"),
            "dial-native-navigation-closed" =>
                RadialText(
                    "导航时选择器意外关闭，请按 RS 重开",
                    "The picker closed during navigation; press RS to reopen it"),
            "dial-submenu-not-open" =>
                RadialText(
                    "右推后未检测到子菜单",
                    "No submenu appeared after moving right"),
            "dial-popup-close" =>
                RadialText(
                    "选择器没有完全关闭 · 再按一次 B 或在 Codex 中关闭",
                    "The picker did not fully close · press B again or close it in Codex"),
            "dial-popup-not-owned" =>
                RadialText(
                    "Codex 中另一个菜单已打开，请先在 Codex 中关闭",
                    "Another Codex menu is open; close it in Codex first"),
            "dial-popup-focus-lost" =>
                RadialText(
                    "选择器焦点已丢失，请关闭后按 RS 重开",
                    "Picker focus was lost; close it, then press RS to reopen"),
            _ => ExecutionFailureLabel(
                result.Error,
                result.ErrorDetail),
        };
    }

    private void ResetVirtualDialInput(bool closeMenu)
    {
        var hadPendingOpen = _virtualDialOpenPending;
        Interlocked.Increment(ref _virtualDialGeneration);
        if (hadPendingOpen)
        {
            _virtualDialCleanupPending = true;
        }

        _virtualDialOpenPending = false;
        _virtualDialCancelRequested = false;
        _dialInputReleasePending = false;
        _microReadbackRequests.ClearPending();
        var readbackCancellation = _microReadbackCancellation;
        _microReadbackCancellation = null;
        readbackCancellation?.Cancel();
        _microReadback = CodexMicroReadback.Closed;
        CancelVirtualDialPressHold();
        ClearPendingVirtualDialInput();

        var hadOpenMenu =
            _virtualDialMenuOpen ||
            _composerPickerMenuLikelyOpen;
        SetVirtualDialMenuOpen(false);
        if (closeMenu)
        {
            // Mode changes take effect immediately. Do not leave the old
            // Advanced picker marker behind, otherwise the first Simple
            // Power/Fast input is incorrectly routed back through UIA.
            _composerPickerMenuLikelyOpen = false;
        }

        if (
            closeMenu &&
            hadOpenMenu &&
            _settings.BridgeEnabled &&
            _foregroundApplication.IsForeground)
        {
            _ = CloseVirtualDialMenuAsync(showFeedback: false);
        }
    }

    private void CancelVirtualDialPressHold()
    {
        _rightStickPressHeld = false;
        _rightStickHoldTriggered = false;
        var cancellation = _rightStickPressCancellation;
        _rightStickPressCancellation = null;
        cancellation?.Cancel();
    }

    private string CurrentPowerSelectionDisplay()
    {
        if (
            _composerCatalog is null ||
            _composerCatalog.Models.Count == 0)
        {
            return _localization.Strings.ComposerAgentNotForeground(
                _activeAgent.DisplayName);
        }

        var model = _composerCatalog.Models[Math.Clamp(
            _modelIndex,
            0,
            _composerCatalog.Models.Count - 1)];
        var efforts = CurrentEfforts();
        return efforts.Count == 0
            ? model.DisplayName
            : $"{model.DisplayName} {efforts[Math.Clamp(
                _reasoningIndex,
                0,
                efforts.Count - 1)]}";
    }

    private void InitializeMicroEncoderPresentation()
    {
        _rightMode = RightControlMode.Dial;
        UpdateRightModeUi();
    }

    private async Task SetSimpleSpeedAsync(
        bool fast,
        bool menuWasLikelyOpen,
        int previousSpeedIndex,
        CancellationTokenSource cancellation,
        string title)
    {
        try
        {
            var result = await RunComposerPickerAutomationAsync(
                    token => _composerAutomation.SetSimpleSpeedAsync(
                        fast,
                        allowShortcutFastPath: true,
                        _settings,
                        token),
                    cancellation.Token)
                .ConfigureAwait(true);
            if (
                cancellation.IsCancellationRequested ||
                result.Error == AgentAutomationErrorCodes.OperationCanceled)
            {
                return;
            }

            if (menuWasLikelyOpen && result.Succeeded)
            {
                result = result with { IsMenuOpen = true };
            }
            else if (!result.Succeeded)
            {
                _speedIndex = previousSpeedIndex;
            }

            PresentSimplePickerResult(
                title,
                result);
        }
        catch (OperationCanceledException)
        {
            // A newer Simple picker action owns the menu now.
        }
        catch (Exception exception)
        {
            _speedIndex = previousSpeedIndex;
            PresentSimplePickerResult(
                title,
                new ComposerPickerResult(
                    false,
                    Error: AgentAutomationErrorCodes.Unexpected,
                    ErrorDetail: exception.Message));
        }
        finally
        {
            if (cancellation.IsCancellationRequested)
            {
                _speedIndex = previousSpeedIndex;
            }

            CompleteComposerPickerAutomation(cancellation);
        }
    }

    private CancellationTokenSource BeginComposerPickerAutomation()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(
            ref _composerPickerCancellation,
            cancellation);
        previous?.Cancel();
        return cancellation;
    }

    private void CompleteComposerPickerAutomation(
        CancellationTokenSource cancellation)
    {
        Interlocked.CompareExchange(
            ref _composerPickerCancellation,
            null,
            cancellation);
        cancellation.Dispose();
    }

    private void CancelComposerPickerAutomation()
    {
        var cancellation = Interlocked.Exchange(
            ref _composerPickerCancellation,
            null);
        cancellation?.Cancel();
    }

    private async Task<ComposerPickerResult>
        RunComposerPickerAutomationAsync(
            Func<CancellationToken, Task<ComposerPickerResult>> action,
            CancellationToken cancellationToken)
    {
        await _dialAutomationGate
            .WaitAsync(cancellationToken)
            .ConfigureAwait(true);
        try
        {
            return await action(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _dialAutomationGate.Release();
        }
    }

    private void PresentSimplePickerResult(
        string title,
        ComposerPickerResult result)
    {
        ObserveComposerPickerMenu(result);
        if (!result.Succeeded)
        {
            var failure = SimplePickerFailureLabel(result);
            _devicePageViewModel.UpdateRightModeValue(failure);
            AddEvent($"{title} · {failure}");
            ShowComposerPickerOverlayIfNeeded(
                title,
                failure,
                result);
            Pulse(strength: 0.1);
            return;
        }

        var value = string.IsNullOrWhiteSpace(result.Value)
            ? CurrentPowerSelectionDisplay()
            : result.Value;
        _devicePageViewModel.UpdateRightModeValue(value);
        AddEvent($"{title} · {value}");
        ShowComposerPickerOverlayIfNeeded(title, value, result);
        Pulse(strength: 0.18);
    }

    private void ShowComposerPickerOverlayIfNeeded(
        string title,
        string value,
        ComposerPickerResult result)
    {
        if (ComposerPickerOverlayPolicy.ShouldShow(result))
        {
            ShowFeedback(title, value);
        }
    }

    private string SimplePickerFailureLabel(ComposerPickerResult result)
    {
        return result.ErrorDetail switch
        {
            "composer-power-no-change-right" =>
                RadialText(
                    "Power 未变化；可能已到当前账户的最高档，或界面仍在更新",
                    "Power did not change; it may be at this account's highest level or the UI may still be updating"),
            "composer-power-no-change-left" =>
                RadialText(
                    "Power 未变化；可能已到当前账户的最低档，或界面仍在更新",
                    "Power did not change; it may be at this account's lowest level or the UI may still be updating"),
            "composer-picker-view:simple" =>
                RadialText(
                    "当前 Codex 界面未提供这个降级操作；可用 Micro 旋钮打开官方菜单",
                    "The current Codex UI does not expose this fallback action; use the Micro encoder to open the official menu"),
            "composer-picker-view:advanced" =>
                RadialText(
                    "无法打开当前账户的官方模型菜单",
                    "Could not open the official model menu for the current account"),
            "composer-model-picker-keybinding-conflict" =>
                RadialText(
                    "模型选择快捷键未就绪或有冲突；请检查设置并重启 Codex",
                    "The model picker shortcut is unavailable or conflicts; check Settings and restart Codex"),
            "composer-model-picker-refocus" =>
                RadialText(
                    "未能结束模型选择会话；请保持 Codex 在前台后再按 B/R3",
                    "Could not end the model picker session; keep Codex in front and press B/R3 again"),
            "composer-speed-option" =>
                RadialText(
                    "当前账户或模型没有提供这个速度选项",
                    "This speed option is not available for the current account or model"),
            "composer-speed-readback" =>
                RadialText(
                    "界面没有确认速度切换；未把它当作已执行",
                    "The UI did not confirm the speed change, so it was not treated as executed"),
            "composer-power-focus" or "composer-power-input" =>
                RadialText(
                    "未能控制当前简易菜单的 Power 横条",
                    "Could not control the live Power slider in the Simple picker"),
            _ => ExecutionFailureLabel(
                result.Error,
                result.ErrorDetail),
        };
    }

    private void ObserveComposerPickerMenu(
        ComposerPickerResult result)
    {
        if (result.IsMenuOpen)
        {
            _composerPickerMenuLikelyOpen = true;
        }
        else if (result.Succeeded)
        {
            _composerPickerMenuLikelyOpen = false;
        }
    }

    private void CancelPendingComposerSelection(
        bool cancelComposerPicker = true)
    {
        if (cancelComposerPicker)
        {
            CancelComposerPickerAutomation();
        }

        if (_composerCatalog is not null)
        {
            UpdateRightModeUi();
        }
    }

    private AnalogTriggerTransition UpdatePushToTalkTrigger(
        double value,
        bool blocked)
    {
        var transition =
            _controllerInteraction.UpdatePushToTalk(value, blocked);
        if (transition == AnalogTriggerTransition.Pressed)
        {
            StartDictation(Glyph(LogicalInput.LeftTrigger));
        }
        else if (transition == AnalogTriggerTransition.Released)
        {
            StopDictation(physicalRelease: true);
        }
        else if (transition == AnalogTriggerTransition.Canceled)
        {
            StopDictation(physicalRelease: false);
        }

        return transition;
    }

    private void PresentBlockedPushToTalkAttempt(
        ControllerState state,
        bool foreground,
        bool waitingForNeutral)
    {
        if (
            state.LeftTrigger <
                BridgeTimings.PushToTalkEngageThreshold ||
            _blockedPushToTalkHintShown)
        {
            return;
        }

        _blockedPushToTalkHintShown = true;
        var menuGlyph = Glyph(LogicalInput.Menu);
        var detail = !_controllerSession.IsArmed
                ? _localization.Strings.Format(
                    StringKeys.StatusAgentForegroundLocked,
                    _activeAgent.DisplayName,
                    menuGlyph)
                : !foreground
                    ? _localization.Strings.Format(
                        StringKeys.StatusAgentNotForeground,
                        _activeAgent.DisplayName,
                        menuGlyph)
                    : waitingForNeutral
                        ? _localization.Strings.Format(
                            StringKeys.StatusAgentForegroundNeutral,
                            _activeAgent.DisplayName)
                        : _localization.Strings.Get(
                            StringKeys.MessageNotExecuted);
        AddEvent(detail);
        ShowFeedback(
            _localization.Strings.ControlHoldToTalk(
                Glyph(LogicalInput.LeftTrigger)),
            detail);
        Pulse(strength: 0.12);
    }

    private void PresentBridgeDisabledControllerAttempt(
        ControllerState state)
    {
        if (
            _bridgeDisabledHintShown ||
            !BridgeInputGate.HasControlIntent(
                state,
                _settings.DeadZone))
        {
            return;
        }

        _bridgeDisabledHintShown = true;
        var title = _localization.Strings.Get(
            StringKeys.MessageSafePreview);
        var detail = _localization.Strings.Get(
            StringKeys.MessageBridgeSafePreview);
        AddEvent($"{title} · {detail}");
        ShowFeedback(title, detail);
        Pulse(strength: 0.12);
    }

    private void ResetPushToTalk(bool stopDictation)
    {
        var wasPressed =
            _controllerInteraction.CancelPushToTalkUntilReleased();
        _pushToTalkSuppressedButtons = ControllerButtons.None;
        if (
            stopDictation &&
            (wasPressed ||
             _dictationInjected ||
             _pushToTalkAutomation.WantsDictation))
        {
            StopDictation(physicalRelease: false);
        }
    }

    private void StartDictation()
    {
        StartDictation(Glyph(LogicalInput.LeftTrigger));
    }

    private void StartDictation(string voiceGlyph)
    {
        _dictationInputGlyph = voiceGlyph;
        DevicePage.SetVoiceHalo(active: true);
        _pushToTalkAutomation.RequestStart();
        EnsurePushToTalkAutomationPump();
    }

    private void StopDictation(bool physicalRelease = false)
    {
        if (!_pushToTalkAutomation.IsDictating)
        {
            DevicePage.SetVoiceHalo(active: false);
        }

        var voiceGlyph =
            _dictationInputGlyph ?? Glyph(LogicalInput.LeftTrigger);
        _pushToTalkAutomation.RequestStop();
        AddEvent(
            physicalRelease
                ? _localization.Strings.Format(
                    StringKeys.MessageReleaseEndingDictation,
                    voiceGlyph)
                : RadialText(
                    $"{voiceGlyph} · 正在结束语音识别",
                    $"{voiceGlyph} · ending dictation"));
        ShowFeedback(
            _localization.Strings.ControlHoldToTalk(voiceGlyph),
            _localization.Strings.Get(
                StringKeys.MessageReleaseEndingRecording));
        EnsurePushToTalkAutomationPump();
    }

    private void EnsurePushToTalkAutomationPump()
    {
        if (Interlocked.Exchange(ref _dictationPumpRunning, 1) == 0)
        {
            _ = PumpPushToTalkAutomationAsync();
        }
    }

    private async Task PumpPushToTalkAutomationAsync()
    {
        try
        {
            while (true)
            {
                var action =
                    _pushToTalkAutomation.BeginNextAction();
                if (action == PushToTalkAutomationAction.None)
                {
                    return;
                }

                switch (action)
                {
                    case PushToTalkAutomationAction.StartDictation:
                    {
                        var result =
                            await ExecuteDictationAutomationAsync(
                                    microPressed: true,
                                    BridgeTimings.DictationStartTimeoutMs,
                                    PushToTalkAutomationPolicy
                                        .AllowsShortcutFallback(action),
                                    PushToTalkAutomationPolicy
                                        .StartActionNames,
                                    ComposerAutomationChannel.Unknown,
                                    closeDialBeforeFallback:
                                        IsVirtualDialContextActive)
                                .ConfigureAwait(true);
                        _pushToTalkAutomation.Complete(
                            action,
                            result.Executed);
                        _dictationAutomationChannel =
                            _pushToTalkAutomation.IsDictating
                                ? result.Automation.Channel
                                : ComposerAutomationChannel.Unknown;
                        _dictationInjected =
                            _pushToTalkAutomation.IsDictating;
                        DevicePage.SetVoiceHalo(
                            active: _dictationInjected);
                        PresentDictationStartResult(result);
                        break;
                    }
                    case PushToTalkAutomationAction.StopDictation:
                    {
                        var sessionChannel =
                            _dictationAutomationChannel;
                        var result =
                            await ExecuteDictationAutomationAsync(
                                    microPressed: false,
                                    BridgeTimings.DictationStopTimeoutMs,
                                    PushToTalkAutomationPolicy
                                        .AllowsShortcutFallback(action),
                                    PushToTalkAutomationPolicy
                                        .StopActionNames,
                                    _dictationAutomationChannel,
                                    closeDialBeforeFallback: false)
                                .ConfigureAwait(true);
                        var stopped =
                            result.Executed ||
                            sessionChannel !=
                                ComposerAutomationChannel.MicroHid &&
                            _composerAutomation.IsActionAvailable(
                                PushToTalkAutomationPolicy
                                    .StartActionNames
                                    .ToArray());
                        _pushToTalkAutomation.Complete(
                            action,
                            stopped);
                        if (!_pushToTalkAutomation.IsDictating)
                        {
                            _dictationAutomationChannel =
                                ComposerAutomationChannel.Unknown;
                        }

                        _dictationInjected =
                            _pushToTalkAutomation.IsDictating;
                        DevicePage.SetVoiceHalo(
                            active: _dictationInjected);
                        PresentDictationStopResult(
                            stopped && !result.Executed
                                ? new(
                                    true,
                                    result.Automation)
                                : result);
                        break;
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref _dictationPumpRunning, 0);
            if (
                !_pushToTalkAutomation.WantsDictation &&
                !_pushToTalkAutomation.IsDictating)
            {
                _dictationAutomationChannel =
                    ComposerAutomationChannel.Unknown;
                _dictationInputGlyph = null;
                DevicePage.SetVoiceHalo(active: false);
            }

            if (
                _pushToTalkAutomation.HasPendingAction &&
                Interlocked.Exchange(ref _dictationPumpRunning, 1) == 0)
            {
                _ = PumpPushToTalkAutomationAsync();
            }
        }
    }

    private async Task<ComposerDialResult>
        CloseVirtualDialForPushToTalkAsync()
    {
        Interlocked.Increment(ref _virtualDialGeneration);
        ClearPendingVirtualDialInput();
        CancelVirtualDialPressHold();
        _virtualDialOpenPending = false;
        _virtualDialCancelRequested = false;
        _virtualDialCleanupPending = false;
        SetVirtualDialMenuOpen(false);
        _controllerInteraction.ResetRepeats();
        _controllerInteraction.RequireRightStickNeutral();

        var closeTask = RunVirtualDialAutomationAsync(
            () => _composerAutomation.DialCancel(_settings));
        try
        {
            var result = await closeTask
                .WaitAsync(
                    TimeSpan.FromMilliseconds(
                        BridgeTimings.DictationDialCloseTimeoutMs))
                .ConfigureAwait(true);
            SetVirtualDialMenuOpen(
                result.Succeeded && result.IsMenuOpen,
                result.Succeeded &&
                    result.RequiresConfirmation);
            return result;
        }
        catch (TimeoutException)
        {
            // Never block the Dispatcher behind a stalled UIA tree walk.
            SetVirtualDialMenuOpen(
                true,
                _virtualDialConfirmationPending);
            return new(
                false,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.OperationCanceled,
                ErrorDetail: "dictation-dial-close-timeout");
        }
    }

    private async Task<DictationAutomationExecution>
        ExecuteDictationAutomationAsync(
            bool microPressed,
            int timeoutMs,
            bool allowShortcutFallback,
            IReadOnlyList<string> actionNames,
            ComposerAutomationChannel requiredChannel,
            bool closeDialBeforeFallback)
    {
        var microSession =
            requiredChannel == ComposerAutomationChannel.MicroHid;
        var mayStartMicroSession =
            requiredChannel == ComposerAutomationChannel.Unknown &&
            _settings.BridgeEnabled &&
            (
                !_settings.OnlyWhenCodexForeground ||
                _foregroundApplication.IsForeground
            );
        if (microSession || mayStartMicroSession)
        {
            var rearmingUnconfirmedPress =
                microPressed &&
                _microInput.HasUnconfirmedPushToTalkState;
            var micro = _microInput.SendPushToTalk(microPressed);
            if (
                (
                    microSession &&
                    !microPressed &&
                    micro is
                        MicroReportSendResult.NotSent or
                        MicroReportSendResult.OutcomeUnknown
                ) ||
                (
                    rearmingUnconfirmedPress &&
                    micro == MicroReportSendResult.NotSent
                ))
            {
                await Task.Delay(
                        BridgeTimings.MicroReleaseRetryDelayMs)
                    .ConfigureAwait(true);
                micro = _microInput.SendPushToTalk(pressed: false);
            }

            if (micro is
                MicroReportSendResult.Accepted or
                MicroReportSendResult.OutcomeUnknown)
            {
                return new(
                    true,
                    new ComposerAutomationResult(
                        true,
                        Channel: ComposerAutomationChannel.MicroHid));
            }

            if (micro == MicroReportSendResult.Rejected)
            {
                return new(
                    false,
                    new ComposerAutomationResult(
                        false,
                        AgentAutomationErrorCodes.Unexpected,
                        "micro.input-rejected",
                        ComposerAutomationChannel.MicroHid));
            }

            if (microSession)
            {
                return new(
                    false,
                    new ComposerAutomationResult(
                        false,
                        AgentAutomationErrorCodes.InputInjectionFailed,
                        "micro.input-not-sent",
                        ComposerAutomationChannel.MicroHid));
            }
        }

        if (closeDialBeforeFallback && allowShortcutFallback)
        {
            var closeResult =
                await CloseVirtualDialForPushToTalkAsync()
                    .ConfigureAwait(true);
            if (!closeResult.Succeeded || closeResult.IsMenuOpen)
            {
                PresentDictationDialCloseFailure(closeResult);
                return new(
                    false,
                    new ComposerAutomationResult(
                        false,
                        closeResult.Error ??
                            AgentAutomationErrorCodes.ElementUnsupported,
                        closeResult.ErrorDetail ??
                            "dictation-dial-close"));
            }
        }

        using var cancellation = new CancellationTokenSource();
        var automationTask = _composerAutomation.InvokeActionAsync(
            _settings,
            timeoutMs,
            cancellation.Token,
            actionNames.ToArray());
        ComposerAutomationResult automation;
        try
        {
            automation = await automationTask
                .WaitAsync(
                    TimeSpan.FromMilliseconds(timeoutMs + 120))
                .ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            cancellation.Cancel();
            automation = new(
                false,
                AgentAutomationErrorCodes.OperationCanceled,
                "dictation-uia-timeout");
        }
        catch (OperationCanceledException)
        {
            automation = new(
                false,
                AgentAutomationErrorCodes.OperationCanceled,
                "dictation-uia-canceled");
        }

        var fallback = false;
        if (
            allowShortcutFallback &&
            !automation.Succeeded &&
            automation.Error !=
                AgentAutomationErrorCodes.OperationCanceled)
        {
            fallback = await Task.Run(() =>
                    _agentShortcuts.Execute(
                        _settings.DictationShortcut,
                        _settings))
                .ConfigureAwait(true);
            if (fallback)
            {
                automation = new(
                    true,
                    Channel: ComposerAutomationChannel.KeyboardInput);
            }
        }

        return new(
            automation.Succeeded || fallback,
            automation);
    }

    private void PresentDictationDialCloseFailure(
        ComposerDialResult result)
    {
        var error = result.Error ??
                    AgentAutomationErrorCodes.ElementNotFound;
        var detail = result.ErrorDetail ??
                     (result.IsMenuOpen
                         ? "dictation-dial-still-open"
                         : "dictation-dial-close-failed");
        PresentDictationStartResult(
            new DictationAutomationExecution(
                false,
                new ComposerAutomationResult(
                    false,
                    error,
                    detail)));
    }

    private void PresentDictationStartResult(
        DictationAutomationExecution result)
    {
        var voiceGlyph =
            _dictationInputGlyph ?? Glyph(LogicalInput.LeftTrigger);
        if (result.Executed)
        {
            _threadNavigation.ClearUndo();
        }
        else
        {
            DevicePage.SetVoiceHalo(active: false);
        }

        AddEvent(
            _localization.Strings.Format(
                StringKeys.MessageStartDictation,
                voiceGlyph) +
            ExecutionSuffix(
                result.Executed,
                result.Automation.Error,
                result.Automation.ErrorDetail));
        if (!result.Executed)
        {
            ShowFeedback(
                _localization.Strings.ControlHoldToTalk(voiceGlyph),
                ExecutionFailureLabel(
                    result.Automation.Error));
            Pulse(strength: 0.08);
            return;
        }

        if (
            result.Executed &&
            !_pushToTalkAutomation.WantsDictation)
        {
            return;
        }

        ShowFeedback(
            _localization.Strings.ControlHoldToTalk(voiceGlyph),
            _localization.Strings.Format(
                StringKeys.MessageRecordingReleaseToStop,
                voiceGlyph));
        Pulse();
    }

    private void PresentDictationStopResult(
        DictationAutomationExecution result)
    {
        var voiceGlyph =
            _dictationInputGlyph ?? Glyph(LogicalInput.LeftTrigger);
        AddEvent(
            _localization.Strings.Format(
                StringKeys.MessageReleaseEndDictation,
                voiceGlyph) +
            ExecutionSuffix(
                result.Executed,
                result.Automation.Error,
                result.Automation.ErrorDetail));
        ShowFeedback(
            _localization.Strings.ControlHoldToTalk(voiceGlyph),
            result.Executed
                ? _localization.Strings.Get(
                    StringKeys.MessageRecordingEnded)
                : ExecutionFailureLabel(
                    result.Automation.Error));
    }

    private sealed record DictationAutomationExecution(
        bool Executed,
        ComposerAutomationResult Automation);

    private void SendPrompt()
    {
        SendPrompt(
            Glyph(LogicalInput.FaceWest),
            "controller.face.west");
    }

    private void SendPrompt(string sendGlyph, string controlId)
    {
        _ = SendPromptAsync(sendGlyph, controlId);
    }

    private async Task SendPromptAsync(
        string sendGlyph,
        string controlId)
    {
        var result = await TryExecuteActionAsync(
            ComposerActionContract.SubmitId,
            "controller.active",
            controlId,
            "composer.input",
            "composer.submit",
            ActionSafetyLevel.Routine)
            .ConfigureAwait(true);

        var succeeded = result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified;
        if (succeeded)
        {
            _threadNavigation.ClearUndo();
        }

        AddEvent(
            _localization.Strings.Format(
                StringKeys.MessageSendPrompt,
                sendGlyph) +
            ExecutionSuffix(
                succeeded,
                result?.ErrorCode));
        ShowFeedback(
            _localization.Strings.ControlSend(sendGlyph),
            succeeded
                ? _localization.Strings.Get(
                    StringKeys.MessageSent)
                : ExecutionFailureLabel(result?.ErrorCode));
        Pulse(strength: 0.28);
    }

    private void CancelAction()
    {
        if (
            _dictationInjected ||
            _pushToTalkAutomation.WantsDictation ||
            _controllerInteraction.PushToTalkBlocksBaseInput)
        {
            _controllerInteraction.CancelPushToTalkUntilReleased();
            _pushToTalkSuppressedButtons |=
                _latestControllerState.Buttons &
                ~ControllerButtons.B;
            CancelPendingSidebarFocus();
            CancelPendingComposerSelection();
            if (!_pushToTalkAutomation.IsDictating)
            {
                DevicePage.SetVoiceHalo(active: false);
            }

            _pushToTalkAutomation.RequestStop();
            EnsurePushToTalkAutomationPump();
            _threadNavigation.ClearUndo();
            var cancelGlyph = Glyph(LogicalInput.FaceEast);
            AddEvent(
                _localization.Strings.Format(
                    StringKeys.MessageAbortDictation,
                    cancelGlyph));
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonCancel,
                    cancelGlyph),
                _localization.Strings.Get(
                    StringKeys.MessageReleaseEndingRecording));
            Pulse(strength: 0.18);
            return;
        }

        var hadPendingSelection =
            _composerPickerCancellation is not null;
        CancelPendingSidebarFocus();
        CancelPendingComposerSelection();
        if (hadPendingSelection)
        {
            var cancelGlyph = Glyph(LogicalInput.FaceEast);
            var message = _localization.Strings.Format(
                StringKeys.MessagePendingSelectionUndone,
                cancelGlyph);
            AddEvent(message);
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonUndo,
                    cancelGlyph),
                _localization.Strings.FeedbackSelectionCanceled);
            Pulse(strength: 0.18);
            return;
        }

        if (_threadNavigation.TryRequestUndo())
        {
            return;
        }

        var automation = _composerAutomation.Cancel(_settings);
        var executed = automation.Succeeded;
        var cancelButtonGlyph = Glyph(LogicalInput.FaceEast);
        AddEvent(
            _localization.Strings.Format(
                StringKeys.MessageCancel,
                cancelButtonGlyph) +
            ExecutionSuffix(
                executed,
                automation.Error,
                automation.ErrorDetail));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessageButtonCancel,
                cancelButtonGlyph),
            executed
                ? _localization.Strings.Get(
                    StringKeys.MessageCanceled)
                : ExecutionFailureLabel(
                    automation.Error));
        Pulse(strength: 0.18);
    }

    private void StopCurrentTurn()
    {
        _ = StopCurrentTurnAsync();
    }

    private async Task StopCurrentTurnAsync()
    {
        var result = await TryExecuteActionAsync(
            TurnActionContract.StopId,
            "controller.active",
            "controller.face.east.hold",
            "turn.running",
            "turn.stop",
            ActionSafetyLevel.HighRisk)
            .ConfigureAwait(true);
        var cancelGlyph = Glyph(LogicalInput.FaceEast);
        if (result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified)
        {
            _threadNavigation.ClearUndo();
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageCurrentOperationStopped,
                cancelGlyph));
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonCancel,
                    cancelGlyph),
                _localization.Strings.Get(
                    StringKeys.MessageCurrentOperationStoppedDetail));
            Pulse(strength: 0.18);
            return;
        }

        AddEvent(
            _localization.Strings.Format(
                StringKeys.MessageCurrentOperationStopped,
                cancelGlyph) +
            ExecutionSuffix(
                false,
                result?.ErrorCode));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessageButtonCancel,
                cancelGlyph),
            ExecutionFailureLabel(
                result?.ErrorCode));
        Pulse(strength: 0.18);
    }

    private void UpdateControllerVisual(ControllerState state)
    {
        _devicePageViewModel.UpdateControllerState(state);
        ControllerStatusDot.SetResourceReference(
            System.Windows.Shapes.Shape.FillProperty,
            state.IsConnected
                ? "Brush.Status.Success"
                : "Brush.Status.Idle");
        ControllerStatusText.Text =
            _devicePageViewModel.ControllerStatusText;
        DevicePage.RenderControllerState(
            state,
            _settings.DeadZone);
    }

    private void UpdateControllerProfile(ControllerState state)
    {
        if (!state.IsConnected)
        {
            return;
        }

        var profile = _controllerProfiles.Resolve(
            _xInputService.LastIdentity);
        if (string.Equals(
                profile.Id,
                _activeControllerProfile.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _activeControllerProfile = profile;
        UpdateLocalizedUi();
    }

    private string Glyph(LogicalInput input)
    {
        return _activeControllerProfile.GetGlyph(input);
    }

    private string ControllerHelpText()
    {
        return _localization.Strings.ControllerHelp(
            Glyph(LogicalInput.Menu),
            Glyph(LogicalInput.LeftStickPress),
            Glyph(LogicalInput.FaceNorth),
            Glyph(LogicalInput.RightStickPress),
            Glyph(LogicalInput.FaceSouth),
            Glyph(LogicalInput.LeftTrigger),
            Glyph(LogicalInput.FaceWest),
            Glyph(LogicalInput.FaceEast));
    }

    private void UpdateLocalizedUi()
    {
        var strings = _localization.Strings;
        var agentName = _activeAgent.DisplayName;
        var projectGlyph = Glyph(LogicalInput.FaceNorth);
        var wakeGlyph = Glyph(LogicalInput.Menu);
        var leftPressGlyph = Glyph(LogicalInput.LeftStickPress);
        var rightPressGlyph = Glyph(LogicalInput.RightStickPress);

        _devicePageViewModel.UpdateContext(
            strings,
            agentName,
            _activeControllerProfile);
        RefreshTutorialDispatch();
        _configPageViewModel.UpdateContext(
            strings,
            agentName,
            leftPressGlyph,
            projectGlyph,
            rightPressGlyph,
            canOpenAgentShortcuts: _activeAgent.DeepLinks is not null);
        _settingsPageViewModel.UpdateContext(
            strings,
            agentName,
            wakeGlyph,
            _activeControllerProfile.VendorTool is null
                ? null
                : _activeControllerProfile.DisplayName,
            canOpenVendorTool:
                _activeControllerProfile.VendorTool is not null,
            canOpenAgentSettings: _activeAgent.DeepLinks is not null);

        Title = strings.AppTitle;
        AppSubtitleText.Text = strings.AppSubtitle(
            _activeControllerProfile.DisplayName,
            agentName);
        FooterVersionText.Text =
            $"v0.7 · {strings.StatusLocalBridge}";

        UpdateSelectedScopeText();
        UpdateRightModeUi();
        UpdateCodexStatus();
        UpdateControllerVisual(_xInputService.LastState);
        if (_feedbackPresenter.Footer is null)
        {
            FooterStatusText.Text = ControllerHelpText();
        }

        RebuildTrayMenu();
        RefreshRadialMenu();
    }

    private async void RefreshCodexData(
        bool preserveSelection,
        bool forceNavigationRebuild = false)
    {
        if (!await _dataRefreshGate.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var selectedId =
                preserveSelection &&
                DevicePage.SelectedEntry is { } selected
                    ? selected.Id
                    : null;
            var snapshotTask = Task.Run(_workspaceReader.LoadSnapshot);
            var currentTitleTask = selectedId is null
                ? Task.Run(_sidebarAutomation.TryGetCurrentThreadTitle)
                : Task.FromResult<string?>(null);
            await Task.WhenAll(snapshotTask, currentTitleTask)
                .ConfigureAwait(true);
            var snapshot = snapshotTask.Result;
            var currentThread = SidebarNavigationState.FindCurrentThread(
                snapshot,
                currentTitleTask.Result);

            _snapshot = snapshot;
            if (selectedId is null && currentThread is not null)
            {
                selectedId = currentThread.Id;
                PrepareSidebarAnchor(currentThread);
            }
            else if (string.IsNullOrWhiteSpace(_selectedProjectPath))
            {
                _selectedProjectPath =
                    _snapshot.Projects.FirstOrDefault()?.Path;
            }

            RebuildSidebarEntries(
                forceNavigationRebuild,
                preferredId: selectedId);

            var activeEntry =
                ActiveSidebarNavigation.SelectedEntry(_sidebarEntries);
            if (
                _scope != SidebarScope.ProjectTasks &&
                activeEntry is not null &&
                RootSidebarScopes.Contains(activeEntry.NavigationScope))
            {
                _scope = activeEntry.NavigationScope;
            }

            UpdateLayerTabs();
            if (_radialLayers.Layer == RadialMenuLayerKind.Agent)
            {
                RefreshRadialMenu();
            }
            FooterStatusText.Text = _localization.Strings.Format(
                StringKeys.MessageDataLoaded,
                _snapshot.Threads.Count,
                _snapshot.Projects.Count,
                _snapshot.ArchivedThreadCount,
                _snapshot.UnavailableThreadCount);
        }
        catch (Exception exception)
        {
            FooterStatusText.Text =
                _localization.Strings.StatusAgentDataLoadFailedFor(
                    _activeAgent.DisplayName);
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageDataLoadFailed,
                exception.Message));
        }
        finally
        {
            _dataRefreshGate.Release();
        }
    }

    private void PrepareSidebarAnchor(CodexThread currentThread)
    {
        if (currentThread.IsPinned)
        {
            _scope = SidebarScope.PinnedTasks;
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentThread.ProjectPath))
        {
            var project = _snapshot.Projects.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Path,
                    currentThread.ProjectPath,
                    StringComparison.OrdinalIgnoreCase));
            if (project is not null)
            {
                _selectedProjectPath = project.Path;
                _scope = SidebarScope.ProjectTasks;
                _projectTasksPinnedOnly = false;
                var rootScope = project.IsPinned
                    ? SidebarScope.PinnedProjects
                    : SidebarScope.Projects;
                _sidebarReturnFrame = new SidebarReturnFrame(
                    rootScope,
                    project.Path,
                    project.Path);
                _projectDisclosureLease = new ProjectDisclosureLease(
                    project.Name,
                    project.IsPinned);
                return;
            }
        }

        _scope = SidebarScope.ProjectlessTasks;
    }

    private void RebuildSidebarEntries(
        bool forceNavigationRebuild = false,
        int? fallbackIndexOverride = null,
        string? preferredId = null)
    {
        preferredId ??= DevicePage.SelectedEntry?.Id;
        var fallbackIndex = fallbackIndexOverride ??
                            Math.Max(
                                0,
                                ActiveSidebarNavigation.SelectedIndex);
        var entries = (_scope == SidebarScope.ProjectTasks
                ? _workspaceReader.BuildEntries(
                    _snapshot,
                    _scope,
                    _selectedProjectPath)
                : _workspaceReader.BuildUnifiedEntries(_snapshot))
            .ToList();
        if (
            _scope == SidebarScope.ProjectTasks &&
            _projectTasksPinnedOnly)
        {
            entries = entries
                .Where(entry => entry.IsPinned)
                .ToList();
        }

        var navigation = ActiveSidebarNavigation;
        navigation.Synchronize(
            entries,
            preferredId,
            fallbackIndex,
            forceNavigationRebuild);

        _sidebarEntries.Clear();
        foreach (var entry in navigation.FrozenEntries)
        {
            _sidebarEntries.Add(entry);
        }

        if (_sidebarEntries.Count == 0)
        {
            navigation.Clear();
            DevicePage.ClearSelection();
        }
        else
        {
            SelectSidebarIndex(navigation.SelectedIndex);
        }

        UpdateSelectedScopeText();
    }

    private void UpdateSelectedScopeText()
    {
        UpdateDeviceSidebarPresentation();
    }

    private void AddEvent(string text)
    {
        _bridgeEvents.Publish(
            BridgeEventKeys.LegacyMessage,
            parameters: new Dictionary<string, string>
            {
                ["text"] = text,
            });
    }

    private void ShowFeedback(string title, string value)
    {
        if (!_settings.ShowOverlay)
        {
            return;
        }

        var waitingForForeground =
            IsForegroundWaitFeedback(value);
        if (
            waitingForForeground &&
            !_foregroundContinuityGate.TryPresentWaitNotice())
        {
            return;
        }

        _overlayWindow?.ShowMessage(title, value);
    }

    private bool IsForegroundWaitFeedback(string value)
    {
        var strings = _localization.Strings;
        return
            string.Equals(
                value,
                strings.Format(
                    StringKeys.MessageWaitingForAgentForeground,
                    _activeAgent.DisplayName),
                StringComparison.Ordinal) ||
            string.Equals(
                value,
                strings.AgentNotForeground(
                    _activeAgent.DisplayName,
                    Glyph(LogicalInput.Menu)),
                StringComparison.Ordinal) ||
            string.Equals(
                value,
                strings.Format(
                    StringKeys.ComposerAgentNotForeground,
                    _activeAgent.DisplayName),
                StringComparison.Ordinal);
    }

    private void PresentBridgeOverlay(BridgeOverlayRequest request)
    {
        if (!_settings.ShowOverlay)
        {
            return;
        }

        if (
            IsForegroundWaitFeedback(request.Value) &&
            !_foregroundContinuityGate.TryPresentWaitNotice())
        {
            return;
        }

        _overlayWindow?.ShowMessage(
            request.Title,
            request.Value,
            request.Duration);
    }

    private void FeedbackPresenter_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName == nameof(BridgeFeedbackPresenter.Footer) &&
            _feedbackPresenter.Footer is { } footer)
        {
            FooterStatusText.Text = footer.Text;
        }
    }

    private void Pulse(double strength = 0.22)
    {
        if (_settings.HapticFeedback)
        {
            _xInputService.Pulse(strength);
        }
    }

    private void InitializeComposerControls()
    {
        _composerCatalog = _composerAutomation.LoadCatalog();
        _modelIndex = Math.Clamp(
            _composerCatalog.InitialModelIndex,
            0,
            Math.Max(0, _composerCatalog.Models.Count - 1));
        var efforts = CurrentEfforts();
        _reasoningIndex = FindValueIndex(
            efforts,
            _composerCatalog.InitialEffort);
        _speedIndex =
            string.Equals(
                _composerCatalog.InitialSpeed,
                "Fast",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        InitializeMicroEncoderPresentation();
    }

    private IReadOnlyList<string> CurrentEfforts()
    {
        return _composerCatalog?.EffortsForModel(_modelIndex)
               ?? [];
    }

    private static int FindValueIndex(
        IReadOnlyList<string> values,
        string target)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(
                    values[index],
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private void UpdateRightModeUi()
    {
        var value = _rightMode switch
        {
            RightControlMode.Dial =>
                CurrentPowerSelectionDisplay(),
            RightControlMode.Reasoning =>
                CurrentReasoningDisplay(),
            RightControlMode.Model =>
                _composerCatalog is not null &&
                _composerCatalog.Models.Count > 0
                    ? _composerCatalog.Models[Math.Clamp(
                        _modelIndex,
                        0,
                        _composerCatalog.Models.Count - 1)].DisplayName
                    : _localization.Strings.ComposerAgentNotForeground(
                        _activeAgent.DisplayName),
            RightControlMode.Speed => SpeedLabel(_speedIndex),
            _ => string.Empty,
        };
        _devicePageViewModel.UpdateRightMode(
            _rightMode,
            value);
    }

    private string CurrentReasoningDisplay()
    {
        var efforts = CurrentEfforts();
        return efforts.Count == 0
            ? _localization.Strings.ComposerAgentNotForeground(
                _activeAgent.DisplayName)
            : ComposerTargetLabel(
                ComposerSettingKind.Effort,
                efforts[Math.Clamp(
                    _reasoningIndex,
                    0,
                    efforts.Count - 1)]);
    }

    private string SpeedLabel(int index)
    {
        return _localization.Strings.SpeedValue(
            index > 0 ? "fast" : "standard");
    }

    private string ComposerKindLabel(ComposerSettingKind kind)
    {
        return kind switch
        {
            ComposerSettingKind.Model => _localization.Strings.Model,
            ComposerSettingKind.Effort =>
                _localization.Strings.ReasoningEffort,
            ComposerSettingKind.Speed => _localization.Strings.Speed,
            _ => string.Empty,
        };
    }

    private string ComposerTargetLabel(
        ComposerSettingKind kind,
        string target)
    {
        return kind switch
        {
            ComposerSettingKind.Effort =>
                _localization.Strings.ReasoningValue(target),
            ComposerSettingKind.Speed =>
                _localization.Strings.SpeedValue(target),
            _ => target,
        };
    }

    private void UpdateLayerTabs()
    {
        UpdateDeviceSidebarPresentation();
    }

    private void UpdateDeviceSidebarPresentation()
    {
        var activeScope = _scope;
        string? projectName = null;
        if (_scope == SidebarScope.ProjectTasks)
        {
            var project = _snapshot.Projects.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Path,
                    _selectedProjectPath,
                    StringComparison.OrdinalIgnoreCase));
            projectName = project?.Name;
            activeScope = project?.IsPinned == true
                ? SidebarScope.PinnedProjects
                : SidebarScope.Projects;
        }

        _devicePageViewModel.UpdateSidebarScope(
            _scope,
            activeRootScope: activeScope,
            selectedProjectName: projectName,
            projectTasksPinnedOnly: _projectTasksPinnedOnly);
    }

    private void ApplySettingsToControls()
    {
        BridgeEnabledCheckBox.IsChecked = _settings.BridgeEnabled;
        _settingsPageViewModel.Load(_settings);
        _configPageViewModel.Load(_settings);
    }

    private void ReadControlsIntoSettings()
    {
        _settings.BridgeEnabled = BridgeEnabledCheckBox.IsChecked == true;
        _settingsPageViewModel.ApplyTo(_settings);
        _configPageViewModel.ApplyTo(_settings);
        _settings.Language = _localization.SettingValue;
    }

    private void SaveSettings(string eventText)
    {
        ReadControlsIntoSettings();
        _settingsService.Save(_settings);
        ConfigureCodexKeybindings();
        RefreshRadialMenu();
        AddEvent(eventText);
        FooterStatusText.Text = eventText;
    }

    private void UpdateCodexStatus()
    {
        var foreground = _foregroundApplication.IsForeground;
        TryAutoArmController(foreground);
        _ = ObserveCodexForeground(foreground);

        if (
            _settings.BridgeEnabled &&
            _controllerSession.IsArmed &&
            _controllerWasConnected &&
            (!_settings.OnlyWhenCodexForeground || foreground))
        {
            _ = TryResumeControllerInput(_xInputService.LastState);
        }

        var strings = _localization.Strings;
        var wakeGlyph = Glyph(LogicalInput.Menu);
        var statusText =
            !_settings.BridgeEnabled
                ? strings.Get(
                    StringKeys.MessageBridgeSafePreview)
                : !_controllerWasConnected
                ? strings.WaitingForReconnect
                : foreground
                    ? !_controllerSession.IsArmed
                        ? strings.AgentForegroundLocked(
                            _activeAgent.DisplayName,
                            wakeGlyph)
                        : !_controllerSession.IsActive
                            ? strings.AgentForegroundNeutral(
                                _activeAgent.DisplayName)
                            : strings.AgentForegroundArmed(
                                _activeAgent.DisplayName)
                    : !_settings.OnlyWhenCodexForeground
                        ? _controllerSession.IsArmed
                            ? strings.BackgroundArmed
                            : strings.BackgroundLocked(wakeGlyph)
                        : _controllerSession.IsArmed
                            ? strings.AgentAwayPaused(
                                _activeAgent.DisplayName)
                            : strings.AgentNotForeground(
                                _activeAgent.DisplayName,
                                wakeGlyph);
        var isActive =
            _settings.BridgeEnabled &&
            _controllerSession.IsArmed &&
            _controllerWasConnected &&
            _controllerSession.IsActive &&
            (!_settings.OnlyWhenCodexForeground || foreground);
        _devicePageViewModel.UpdateAgentStatus(
            statusText,
            isActive);
    }

    private void ShowPage(FrameworkElement page)
    {
        DevicePage.Visibility =
            page == DevicePage ? Visibility.Visible : Visibility.Collapsed;
        ConfigPage.Visibility =
            page == ConfigPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility =
            page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetSelectedNav(
        System.Windows.Controls.RadioButton selected)
    {
        selected.IsChecked = true;
    }

    private string ScopeLabel(SidebarScope scope)
    {
        return _localization.Strings.ScopeValue(scope.ToString());
    }

    private string CurrentSidebarContextTitle()
    {
        var projectName = CurrentSidebarProjectName();
        return string.IsNullOrWhiteSpace(projectName)
            ? _localization.Strings.Format(
                StringKeys.MessageAgentSidebar,
                _activeAgent.DisplayName)
            : _localization.Strings.Format(
                StringKeys.MessageProjectTitle,
                projectName);
    }

    private string? CurrentSidebarProjectName()
    {
        var selected = DevicePage.SelectedEntry;
        if (selected is { IsProject: true })
        {
            return selected.Title;
        }

        var projectPath = selected?.ProjectPath;
        if (string.IsNullOrWhiteSpace(projectPath) &&
            _scope == SidebarScope.ProjectTasks)
        {
            projectPath = _selectedProjectPath;
        }

        return string.IsNullOrWhiteSpace(projectPath)
            ? null
            : _snapshot.Projects.FirstOrDefault(project =>
                string.Equals(
                    project.Path,
                    projectPath,
                    StringComparison.OrdinalIgnoreCase))?.Name;
    }

    private string SidebarMenuScopeLabel(SidebarScope scope)
    {
        if (
            scope != SidebarScope.ProjectTasks ||
            string.IsNullOrWhiteSpace(_selectedProjectPath))
        {
            return ScopeLabel(scope);
        }

        var projectName = _snapshot.Projects.FirstOrDefault(project =>
            string.Equals(
                project.Path,
                _selectedProjectPath,
                StringComparison.OrdinalIgnoreCase))?.Name;
        projectName = string.IsNullOrWhiteSpace(projectName)
            ? ScopeLabel(scope)
            : projectName.Trim();
        var filter = _localization.Strings.Get(
            _projectTasksPinnedOnly
                ? StringKeys.MessageProjectPinnedOnly
                : StringKeys.MessageAllTasks);
        return $"{projectName} › {filter}";
    }

    private string ModeLabel(RightControlMode mode)
    {
        return mode switch
        {
            RightControlMode.Dial =>
                _localization.Strings.VirtualDial,
            RightControlMode.Reasoning =>
                _localization.Strings.ReasoningEffort,
            RightControlMode.Model => _localization.Strings.Model,
            RightControlMode.Speed => _localization.Strings.Speed,
            _ => string.Empty,
        };
    }

    private static string Arrow(int direction, bool horizontal)
    {
        return horizontal
            ? direction > 0 ? "→" : "←"
            : direction > 0 ? "↑" : "↓";
    }

    private string ExecutionSuffix(
        bool executed,
        string? error = null,
        string? errorDetail = null)
    {
        if (executed)
        {
            return $" · {_localization.Strings.Get(
                StringKeys.MessageExecuted)}";
        }

        return $" · {ExecutionFailureLabel(error, errorDetail)}";
    }

    private string ExecutionFailureLabel(
        string? error,
        string? errorDetail = null)
    {
        if (!_settings.BridgeEnabled)
        {
            return _localization.Strings.Get(
                StringKeys.MessageBridgeSafePreview);
        }

        if (
            _settings.OnlyWhenCodexForeground &&
            !_foregroundApplication.IsForeground)
        {
            return _localization.Strings.Format(
                StringKeys.MessageWaitingForAgentForeground,
                _activeAgent.DisplayName);
        }

        return string.IsNullOrWhiteSpace(error)
            ? _localization.Strings.Get(
                StringKeys.MessageNotExecuted)
            : _localization.Strings.ErrorLabel(
                error,
                errorDetail);
    }

    private void SetupTrayIcon()
    {
        _trayIconImage = LoadTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Agent Controller",
            Icon = _trayIconImage ?? System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();
        RebuildTrayMenu();
    }

    private void RebuildTrayMenu()
    {
        if (_trayIcon is null)
        {
            return;
        }

        var strings = _localization.Strings;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(
            strings.TrayOpenApplication,
            null,
            (_, _) => RestoreWindow());
        menu.Items.Add(
            strings.TrayOpenAgent(_activeAgent.DisplayName),
            null,
            (_, _) => WakeCodex());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(
            strings.TrayExit,
            null,
            (_, _) => ExitApplication());

        var previousMenu = _trayIcon.ContextMenuStrip;
        _trayIcon.ContextMenuStrip = menu;
        previousMenu?.Dispose();
    }

    private static System.Drawing.Icon? LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AgentController.ico"));
        if (resource is null)
        {
            return null;
        }

        using (resource.Stream)
        using (var icon = new System.Drawing.Icon(resource.Stream))
        {
            return (System.Drawing.Icon)icon.Clone();
        }
    }

    private void RestoreWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIconImage?.Dispose();
        _trayIconImage = null;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void DeviceNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(DevicePage);
        SetSelectedNav(DeviceNavButton);
    }

    private void ConfigNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(ConfigPage);
        SetSelectedNav(ConfigNavButton);
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SettingsPage);
        SetSelectedNav(SettingsNavButton);
    }

    private void BridgeEnabledCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        var enabled = BridgeEnabledCheckBox.IsChecked == true;
        if (!enabled)
        {
            PauseControllerInput(_xInputService.LastState);
            _controllerInteraction.CommitButtonHistory(
                _xInputService.LastState.Buttons,
                _xInputService.LastState.Buttons);
            _bridgeDisabledHintShown = true;
        }
        else
        {
            _bridgeDisabledHintShown = false;
        }

        _settings.BridgeEnabled = enabled;
        _settingsService.Save(_settings);
        ConfigureCodexKeybindings();
        var eventText = enabled
            ? _localization.Strings.Get(
                StringKeys.MessageBridgeEnabled)
            : _localization.Strings.Get(
                StringKeys.MessageBridgeSafePreview);
        AddEvent(eventText);
        FooterStatusText.Text = eventText;
        if (!enabled)
        {
            ShowFeedback(
                _localization.Strings.Get(
                    StringKeys.MessageSafePreview),
                eventText);
        }

        UpdateCodexStatus();
    }

    private void RefreshDeviceData()
    {
        RefreshCodexData(
            preserveSelection: true,
            forceNavigationRebuild: false);
        AddEvent(_localization.Strings.Format(
            StringKeys.MessageAgentDataRefreshed,
            _activeAgent.DisplayName));
    }

    private void SidebarList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (
            _suppressSelectionActivation ||
            DevicePage.SelectedEntry is not { } entry)
        {
            return;
        }

        ActiveSidebarNavigation.Select(
            _sidebarEntries,
            DevicePage.SelectedIndex);
        ActivateSelectedEntry(entry, deferFocus: true);
    }

    private void SidebarList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        ActivateSelectedSidebarEntryFromPointer();
        e.Handled = true;
    }

    private void SidebarList_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedSidebarTask(
                deviceId: "desktop.keyboard",
                controlId: "keyboard.enter");
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right)
        {
            NavigateSidebarHorizontal(
                e.Key == Key.Right ? 1 : -1);
            e.Handled = true;
        }
    }

    private void OpenAgentShortcuts()
    {
        if (_activeAgent.DeepLinks is not { } deepLinks)
        {
            AddEvent(_localization.Strings.ErrorLabel(
                AgentAutomationErrorCodes.CapabilityUnavailable));
            return;
        }

        deepLinks.OpenKeyboardShortcuts();
        AddEvent(_localization.Strings.Format(
            StringKeys.MessageAgentShortcutsOpened,
            _activeAgent.DisplayName));
    }

    private void OpenAgentSettings()
    {
        if (_activeAgent.DeepLinks is not { } deepLinks)
        {
            AddEvent(_localization.Strings.ErrorLabel(
                AgentAutomationErrorCodes.CapabilityUnavailable));
            return;
        }

        deepLinks.OpenSettings();
    }

    private void OpenControllerVendorTool()
    {
        if (_activeControllerProfile.VendorTool is not { } vendorTool)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = vendorTool.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch
        {
            AddEvent(_localization.Strings.Get(
                StringKeys.MessageControllerSoftwareOpenFailed));
        }
    }

    private void Localization_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName is not nameof(LocalizationService.Catalog) and
            not nameof(LocalizationService.SelectedLanguage))
        {
            return;
        }

        _settings.Language = _localization.SettingValue;
        _feedbackPresenter.Refresh();
        UpdateLocalizedUi();
        if (e.PropertyName == nameof(LocalizationService.Catalog))
        {
            RebuildSidebarEntries();
        }
    }

    private void ChangeLanguage(string settingValue)
    {
        _localization.SetLanguage(settingValue);
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (
            WindowState == WindowState.Minimized &&
            _settings.MinimizeToTray)
        {
            ResetRadialLayer(clearSuppression: true);
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested && _settings.MinimizeToTray)
        {
            ResetRadialLayer(clearSuppression: true);
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();
            AddEvent(_localization.Strings.Get(
                StringKeys.MessageWindowHiddenBackground));
            return;
        }

        _statusTimer.Stop();
        _dataTimer.Stop();
        _radialLearningTimer.Stop();
        CancelBaseCancelHold(showFeedback: false);
        CancelConversationBoundaryHold();
        ResetRadialLayer(clearSuppression: true);
        ResetVirtualDialInput(closeMenu: true);
        CancelPendingSidebarFocus();
        CancelPendingComposerSelection();
        _pushToTalkAutomation.Reset();
        _dictationInjected = false;
        _dictationAutomationChannel =
            ComposerAutomationChannel.Unknown;
        _dictationInputGlyph = null;
        _threadNavigation.NoticePublished -=
            ThreadNavigation_NoticePublished;
        _threadNavigation.ClearUndo();
        _xInputService.StateChanged -= XInputService_StateChanged;
        _localization.PropertyChanged -=
            Localization_PropertyChanged;
        _trayIcon?.Dispose();
        _trayIconImage?.Dispose();
        _feedbackPresenter.PropertyChanged -=
            FeedbackPresenter_PropertyChanged;
        _feedbackPresenter.Dispose();
        _overlayWindow?.Close();
        _radialMenuOverlayWindow?.Close();
        _sidebarNavigationMenuOverlayWindow?.Close();

        if (!_exitRequested)
        {
            _exitRequested = true;
            _ = Dispatcher.BeginInvoke(
                System.Windows.Application.Current.Shutdown);
        }
    }

    private sealed record SidebarReturnFrame(
        SidebarScope Scope,
        string? EntryId,
        string? ProjectPath);

}
