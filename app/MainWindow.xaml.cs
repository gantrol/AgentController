using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CodexController.Agents;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation.Dispatch;
using CodexController.Presentation.Feedback;
using CodexController.Services;
using CodexController.ViewModels;
using CodexController.Views;
using Forms = System.Windows.Forms;

namespace CodexController;

public partial class MainWindow : Window
{
    private static readonly SidebarScope[] RootSidebarScopes =
    [
        SidebarScope.PinnedTasks,
        SidebarScope.PinnedProjects,
        SidebarScope.Projects,
        SidebarScope.ProjectlessTasks,
    ];
    private const ControllerButtons DialExclusiveFrozenButtons =
        RadialInputMap.FrozenBaseButtons &
        ~(ControllerButtons.RightThumb | ControllerButtons.B);

    private readonly SettingsService _settingsService;
    private readonly IWorkspaceReader _workspaceReader;
    private readonly ISidebarAutomation _sidebarAutomation;
    private readonly IComposerAutomation _composerAutomation;
    private readonly IAgentShortcuts _agentShortcuts;
    private readonly IKeybindingProvisioner? _keybindingProvisioner;
    private readonly XInputService _xInputService;
    private readonly AxisRepeater _axisRepeater;
    private readonly StickGestureRouter _leftStickRouter;
    private readonly StickGestureRouter _rightStickRouter;
    private readonly BridgeEventHub _bridgeEvents;
    private readonly LocalizationService _localization;
    private readonly ControllerProfileRegistry _controllerProfiles;
    private readonly IAgentTarget _activeAgent;
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
    private readonly ControllerStateBuffer _controllerStateBuffer = new();
    private readonly ControllerSession _controllerSession = new();
    private readonly ForegroundContinuityGate _foregroundContinuityGate =
        new();
    private readonly SidebarNavigationDirectory _sidebarNavigationDirectory =
        new();
    private readonly AnalogTriggerLatch _pushToTalkTrigger = new(
        BridgeTimings.PushToTalkEngageThreshold,
        BridgeTimings.PushToTalkReleaseThreshold);
    private readonly PushToTalkAutomationState _pushToTalkAutomation =
        new();

    private AppSettings _settings = new();
    private CodexSnapshot _snapshot = new();
    private SidebarScope _scope = SidebarScope.Projects;
    private RightControlMode _rightMode = RightControlMode.Dial;
    private ControllerButtons _previousButtons;
    private ControllerButtons _previousPhysicalButtons;
    private ControllerButtons _radialSuppressedButtons;
    private ControllerButtons _pushToTalkSuppressedButtons;
    private string? _selectedProjectPath;
    private bool _projectTasksPinnedOnly;
    private readonly Dictionary<SidebarScope, string> _rootCursorIds = [];
    private readonly Dictionary<string, string> _projectTaskCursorIds =
        new(StringComparer.OrdinalIgnoreCase);
    private SidebarReturnFrame? _sidebarReturnFrame;
    private ProjectDisclosureLease? _projectDisclosureLease;
    private bool _dictationInjected;
    private string? _dictationInputGlyph;
    private bool _radialLayerEngaged;
    private bool _radialLayerCancelled;
    private bool _radialActionTriggered;
    private bool _radialPushToTalkActive;
    private bool _rightTriggerCandidate;
    private bool _rightStickPressHeld;
    private bool _rightStickHoldTriggered;
    private bool _virtualDialMenuOpen;
    private bool _virtualDialOpenPending;
    private bool _virtualDialCancelRequested;
    private bool _virtualDialCleanupPending;
    private bool _dialInputReleasePending;
    private bool _blockedPushToTalkHintShown;
    private bool _controllerWasConnected;
    private bool _hasSeenController;
    private bool _wakeInProgress;
    private bool _initializing = true;
    private bool _suppressSelectionActivation;
    private bool _exitRequested;
    private long _leftNavigationBlockedUntil;
    private long _rightAdjustmentBlockedUntil;
    private long _radialLayerStartedAt;
    private CancellationTokenSource? _sidebarFocusCancellation;
    private ControllerState _latestControllerState;
    private ComposerCatalog? _composerCatalog;
    private int _modelIndex;
    private int _committedModelIndex;
    private int _reasoningIndex;
    private int _committedReasoningIndex;
    private int _speedIndex;
    private int _committedSpeedIndex;
    private ComposerSettingKind? _pendingComposerKind;
    private string? _pendingComposerTarget;
    private CancellationTokenSource? _composerCommitCancellation;
    private CancellationTokenSource? _navigationConfirmCancellation;
    private CancellationTokenSource? _rightStickPressCancellation;
    private NavigationUndoState? _navigationUndo;
    private int _pendingDialDelta;
    private int _dialPumpRunning;
    private int _virtualDialGeneration;
    private int _dictationPumpRunning;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private OverlayWindow? _overlayWindow;
    private RadialMenuOverlayWindow? _radialMenuOverlayWindow;
    private SidebarNavigationWheelOverlayWindow?
        _sidebarNavigationWheelOverlayWindow;
    private RadialMenuLayerKind? _radialLayer;
    private string? _radialHighlightedItemId;
    private ControllerProfile _activeControllerProfile =
        BuiltInControllerProfiles.Generic;

    private SidebarNavigationState ActiveSidebarNavigation =>
        _sidebarNavigationDirectory.Resolve(
            _scope,
            _selectedProjectPath,
            _projectTasksPinnedOnly);

    public MainWindow(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _settingsService = services.Settings;
        _activeAgent = services.ActiveAgent;
        _workspaceReader = _activeAgent.WorkspaceOrEmpty();
        _sidebarAutomation = _activeAgent.SidebarOrUnavailable();
        _composerAutomation = _activeAgent.ComposerOrUnavailable();
        _agentShortcuts = _activeAgent.Shortcuts;
        _keybindingProvisioner = _activeAgent.Keybindings;
        _xInputService = services.Controller;
        _axisRepeater = services.AxisRepeater;
        _leftStickRouter = services.LeftStickRouter;
        _rightStickRouter = services.RightStickRouter;
        _bridgeEvents = services.BridgeEvents;
        _localization = services.Localization;
        _controllerProfiles = services.ControllerProfiles;
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
                showFeedback: false));
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
        _settings = _settingsService.Load();
        _localization.SetLanguage(_settings.Language);
        _localization.PropertyChanged +=
            Localization_PropertyChanged;
        ApplySettingsToControls();
        UpdateLocalizedUi();
        SetupTrayIcon();
        _overlayWindow = new OverlayWindow();
        _radialMenuOverlayWindow = new RadialMenuOverlayWindow();
        _sidebarNavigationWheelOverlayWindow =
            new SidebarNavigationWheelOverlayWindow();

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
        if (_controllerStateBuffer.Enqueue(state))
        {
            _ = Dispatcher.BeginInvoke(
                ProcessBufferedControllerStates);
        }
    }

    private void ProcessBufferedControllerStates()
    {
        foreach (var state in _controllerStateBuffer.Drain())
        {
            ProcessControllerState(state);
        }
    }

    private void ConfigureCodexKeybindings()
    {
        if (!_settings.BridgeEnabled)
        {
            return;
        }

        if (_keybindingProvisioner is null)
        {
            return;
        }

        var result = _keybindingProvisioner.EnsureBindings(_settings);
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
            _previousButtons = ControllerButtons.None;
            _previousPhysicalButtons = ControllerButtons.None;
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

        _radialSuppressedButtons &= pressed;
        _pushToTalkSuppressedButtons &= pressed;
        var radialModifierHeld =
            _radialLayer is not null ||
            pressed.HasFlag(ControllerButtons.LeftShoulder) ||
            pressed.HasFlag(ControllerButtons.RightShoulder) ||
            state.RightTrigger >= RadialInputMap.TurnEngageThreshold;
        var wakeEligibleButtons =
            pressed & ~_radialSuppressedButtons;
        if (radialModifierHeld || IsVirtualDialContextActive)
        {
            wakeEligibleButtons &= ~ControllerButtons.Start;
        }

        HandleButtonEdge(
            wakeEligibleButtons,
            ControllerButtons.Start,
            onDown: WakeCodex);

        var foreground = _activeAgent.Presence.IsForeground;
        var foregroundAllowsInput =
            ObserveCodexForeground(foreground);
        if (
            !_settings.BridgeEnabled ||
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
            if (
                !_settings.BridgeEnabled ||
                !_controllerSession.IsArmed)
            {
                PauseControllerInput(state);
            }

            _previousButtons = pressed;
            _previousPhysicalButtons = pressed;
            return;
        }

        if (!TryResumeControllerInput(state))
        {
            PresentBlockedPushToTalkAttempt(
                state,
                foreground,
                waitingForNeutral: true);
            _previousButtons = pressed;
            _previousPhysicalButtons = pressed;
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
                _previousButtons = pressed;
                _previousPhysicalButtons = pressed;
                _axisRepeater.Reset();
                _leftStickRouter.Reset();
                _rightStickRouter.Reset();
            }
            else if (!IsControllerNeutral(state))
            {
                DrainControllerFrame(pressed);
                return;
            }
            else
            {
                _dialInputReleasePending = false;
                _previousButtons = ControllerButtons.None;
                _previousPhysicalButtons = ControllerButtons.None;
                _axisRepeater.Reset();
                _leftStickRouter.Reset();
                _rightStickRouter.Reset();
            }
        }

        var physicalDownEdges =
            pressed & ~_previousPhysicalButtons;
        var physicalUpEdges =
            _previousPhysicalButtons & ~pressed;
        // Dial automation owns the native picker state. Controller polling
        // must never synchronously walk the UIA tree.
        var dialContextActive = IsVirtualDialContextActive;

        var pushToTalkTransition = UpdatePushToTalkTrigger(
            state.LeftTrigger,
            blocked: PushToTalkInputPolicy.ShouldBlockTrigger(
                radialLayerActive: _radialLayer is not null));
        var pushToTalkFrameActive =
            _pushToTalkTrigger.BlocksBaseInput ||
            pushToTalkTransition != AnalogTriggerTransition.None;
        if (pushToTalkTransition == AnalogTriggerTransition.Released)
        {
            _pushToTalkSuppressedButtons |= pressed;
        }
        else if (_pushToTalkTrigger.BlocksBaseInput)
        {
            _pushToTalkSuppressedButtons |=
                PushToTalkInputPolicy.ButtonsToSuppress(pressed);
        }

        ControllerButtons frozenByContext;
        if (pushToTalkFrameActive)
        {
            _rightTriggerCandidate = false;
            frozenByContext =
                PushToTalkInputPolicy.FrozenBaseButtons;
        }
        else if (dialContextActive)
        {
            if (_radialLayer is not null || _rightTriggerCandidate)
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

        var radialInputActive = _radialLayer is not null;
        var basePressed =
            pressed &
            ~frozenByContext &
            ~_radialSuppressedButtons &
            ~_pushToTalkSuppressedButtons;
        HandleButtonEdge(
            basePressed,
            ControllerButtons.LeftThumb,
            onDown: CycleRootSidebarScope);
        if (
            physicalDownEdges.HasFlag(ControllerButtons.RightThumb) &&
            basePressed.HasFlag(ControllerButtons.RightThumb))
        {
            BeginVirtualDialPress();
        }

        if (physicalUpEdges.HasFlag(ControllerButtons.RightThumb))
        {
            EndVirtualDialPress();
        }

        HandleButtonEdge(
            basePressed,
            ControllerButtons.DPadLeft,
            onDown: () =>
            {
                _leftStickRouter.RequireNeutral();
                NavigateSidebarHorizontal(-1);
            });
        HandleButtonEdge(
            basePressed,
            ControllerButtons.DPadRight,
            onDown: () =>
            {
                _leftStickRouter.RequireNeutral();
                NavigateSidebarHorizontal(1);
            });
        HandleButtonEdge(
            basePressed,
            ControllerButtons.Y,
            onDown: JumpToProjectContext);
        HandleButtonEdge(
            basePressed,
            ControllerButtons.A,
            onDown: OpenSelectedSidebarTask);
        HandleButtonEdge(
            basePressed,
            ControllerButtons.X,
            onDown: SendPrompt);
        HandleButtonEdge(
            basePressed,
            ControllerButtons.B,
            onDown: CancelActionOrDialMenu);
        _previousButtons = basePressed;
        _previousPhysicalButtons = pressed;

        var deadZone = _settings.DeadZone;
        var virtualDialDeadZone =
            VirtualDialInputPolicy.ResolveDeadZone(
                deadZone,
                _activeControllerProfile.Tuning?.StickDeadZone);
        var leftGesture = _leftStickRouter.Update(
            state.LeftX,
            state.LeftY,
            deadZone,
            invertVertical: true,
            blocked:
                radialInputActive ||
                dialContextActive ||
                pushToTalkFrameActive ||
                basePressed.HasFlag(ControllerButtons.LeftThumb) ||
                Environment.TickCount64 < _leftNavigationBlockedUntil);
        var rightGesture = _rightStickRouter.Update(
            state.RightX,
            state.RightY,
            virtualDialDeadZone,
            invertVertical: false,
            blocked:
                radialInputActive ||
                pushToTalkFrameActive ||
                basePressed.HasFlag(ControllerButtons.RightThumb) ||
                Environment.TickCount64 < _rightAdjustmentBlockedUntil);

        _axisRepeater.Update(
            "left-y",
            leftGesture.VerticalDirection,
            _settings.RepeatDelayMs,
            _settings.RepeatIntervalMs,
            MoveSidebarSelection);
        if (leftGesture.HorizontalStarted)
        {
            NavigateSidebarHorizontal(leftGesture.HorizontalDirection);
        }
        if (VirtualDialInputPolicy.ShouldQueueStep(
                rightGesture.HorizontalStarted,
                rightGesture.HorizontalDirection))
        {
            QueueVirtualDialStep(rightGesture.HorizontalDirection);
        }
    }

    private bool IsVirtualDialContextActive =>
        _virtualDialMenuOpen ||
        _virtualDialOpenPending ||
        _virtualDialCancelRequested ||
        _virtualDialCleanupPending;

    private void SetVirtualDialMenuOpen(bool isOpen)
    {
        _virtualDialMenuOpen = isOpen;
        UpdateVirtualDialContextPresentation();
    }

    private void UpdateVirtualDialContextPresentation()
    {
        _devicePageViewModel.UpdateVirtualDialMenuState(
            _virtualDialMenuOpen);
    }

    private void DrainControllerFrame(ControllerButtons pressed)
    {
        _previousButtons = pressed;
        _previousPhysicalButtons = pressed;
        _axisRepeater.Reset();
        _leftStickRouter.RequireNeutral();
        _rightStickRouter.RequireNeutral();
    }

    private void BeginVirtualDialReleaseDrain()
    {
        _dialInputReleasePending =
            !IsControllerNeutral(_xInputService.LastState);
        Interlocked.Exchange(ref _pendingDialDelta, 0);
        CancelVirtualDialPressHold();
        ResetRadialLayer(clearSuppression: false);
        _axisRepeater.Reset();
        _leftStickRouter.RequireNeutral();
        _rightStickRouter.RequireNeutral();
    }

    private ControllerButtons ProcessRadialInput(ControllerState state)
    {
        var pressed = state.Buttons;
        var downEdges = pressed & ~_previousPhysicalButtons;
        var upEdges = _previousPhysicalButtons & ~pressed;

        if (_radialLayer is null)
        {
            if (
                !_rightTriggerCandidate &&
                RadialInputMap.IsTurnCandidate(state.RightTrigger))
            {
                _rightTriggerCandidate = true;
            }

            if (_rightTriggerCandidate)
            {
                if (
                    state.RightTrigger <=
                    RadialInputMap.TurnCandidateReleaseThreshold)
                {
                    _radialSuppressedButtons |=
                        pressed &
                        RadialInputMap.FrozenTurnCandidateButtons;
                    _rightTriggerCandidate = false;
                    return RadialInputMap.FrozenTurnCandidateButtons;
                }

                if (
                    RadialInputMap.CanAcceptTurnAction(
                        state.RightTrigger))
                {
                    _rightTriggerCandidate = false;
                    BeginRadialLayer(RadialMenuLayerKind.Turn);
                }
                else
                {
                    return RadialInputMap.FrozenTurnCandidateButtons;
                }
            }
            else if (
                downEdges.HasFlag(ControllerButtons.RightShoulder))
            {
                BeginRadialLayer(RadialMenuLayerKind.Command);
            }
            else if (
                downEdges.HasFlag(ControllerButtons.LeftShoulder))
            {
                BeginRadialLayer(RadialMenuLayerKind.Agent);
            }
        }

        if (_radialLayer is not { } layer)
        {
            return ControllerButtons.None;
        }

        if (
            layer == RadialMenuLayerKind.Agent &&
            !pressed.HasFlag(ControllerButtons.LeftShoulder))
        {
            CompleteShoulderLayer(
                direction: -1,
                pressed);
            return RadialInputMap.FrozenBaseButtons;
        }

        if (
            layer == RadialMenuLayerKind.Command &&
            !pressed.HasFlag(ControllerButtons.RightShoulder))
        {
            CompleteShoulderLayer(
                direction: 1,
                pressed);
            return RadialInputMap.FrozenBaseButtons;
        }

        if (
            layer == RadialMenuLayerKind.Turn &&
            state.RightTrigger <=
                RadialInputMap.TurnReleaseThreshold)
        {
            EndRadialLayer(pressed);
            return RadialInputMap.FrozenBaseButtons;
        }

        if (
            layer == RadialMenuLayerKind.Command &&
            _radialPushToTalkActive &&
            upEdges.HasFlag(ControllerButtons.Back))
        {
            _radialPushToTalkActive = false;
            StopDictation(physicalRelease: true);
        }

        if (_radialLayerCancelled)
        {
            return RadialInputMap.FrozenBaseButtons;
        }

        var action = RadialInputMap.Resolve(layer, downEdges);
        if (action == RadialInputAction.None)
        {
            return RadialInputMap.FrozenBaseButtons;
        }

        if (action == RadialInputAction.Cancel)
        {
            CancelRadialLayer();
            return RadialInputMap.FrozenBaseButtons;
        }

        _radialActionTriggered = true;
        _radialLayerEngaged = true;
        _radialLearningTimer.Stop();
        _radialHighlightedItemId = RadialActionId(action);
        RefreshRadialMenu();
        ExecuteRadialAction(action);
        return RadialInputMap.FrozenBaseButtons;
    }

    private void BeginRadialLayer(RadialMenuLayerKind layer)
    {
        if (_radialLayer is not null)
        {
            return;
        }

        _radialLayer = layer;
        _rightTriggerCandidate = false;
        _radialLayerStartedAt = Environment.TickCount64;
        _radialLayerEngaged = layer == RadialMenuLayerKind.Turn;
        _radialLayerCancelled = false;
        _radialActionTriggered = false;
        _radialPushToTalkActive = false;
        _radialHighlightedItemId = null;

        _radialLearningTimer.Stop();
        if (layer != RadialMenuLayerKind.Turn)
        {
            _radialLearningTimer.Start();
        }

        RefreshRadialMenu();
    }

    private void PromoteRadialLearningCue()
    {
        _radialLearningTimer.Stop();
        if (
            _radialLayer is not
                (RadialMenuLayerKind.Agent or
                 RadialMenuLayerKind.Command) ||
            _radialLayerCancelled)
        {
            return;
        }

        var buttons = _latestControllerState.Buttons;
        var modifierStillHeld =
            _radialLayer == RadialMenuLayerKind.Agent
                ? buttons.HasFlag(ControllerButtons.LeftShoulder)
                : buttons.HasFlag(ControllerButtons.RightShoulder);
        if (!modifierStillHeld)
        {
            return;
        }

        _radialLayerEngaged = true;
        RefreshRadialMenu();
    }

    private void CompleteShoulderLayer(
        int direction,
        ControllerButtons pressed)
    {
        var elapsed =
            Environment.TickCount64 - _radialLayerStartedAt;
        var shouldMoveTask =
            !_radialLayerEngaged &&
            !_radialLayerCancelled &&
            !_radialActionTriggered &&
            elapsed < RadialInputMap.LearningDelayMs;
        EndRadialLayer(pressed);
        if (shouldMoveTask)
        {
            OpenAdjacentAgentTask(direction);
        }
    }

    private void CancelRadialLayer()
    {
        _radialLayerCancelled = true;
        _radialActionTriggered = true;
        _radialLearningTimer.Stop();
        if (_radialPushToTalkActive)
        {
            _radialPushToTalkActive = false;
            StopDictation(physicalRelease: false);
        }

        _radialMenuOverlayWindow?.HideMenu();
        ShowFeedback(
            RadialText("组合层", "Chord layer"),
            RadialText(
                "已取消；松开修饰键后返回基础层。",
                "Canceled. Release the modifier to return to Base."));
        Pulse(strength: 0.1);
    }

    private void EndRadialLayer(ControllerButtons pressed)
    {
        _radialSuppressedButtons |=
            pressed & RadialInputMap.FrozenBaseButtons;
        ResetRadialLayer(clearSuppression: false);
    }

    private void ResetRadialLayer(bool clearSuppression)
    {
        _radialLearningTimer.Stop();
        if (_radialPushToTalkActive)
        {
            _radialPushToTalkActive = false;
            StopDictation(physicalRelease: false);
        }

        _radialLayer = null;
        _rightTriggerCandidate = false;
        _radialLayerEngaged = false;
        _radialLayerCancelled = false;
        _radialActionTriggered = false;
        _radialLayerStartedAt = 0;
        _radialHighlightedItemId = null;
        if (clearSuppression)
        {
            _radialSuppressedButtons = ControllerButtons.None;
        }

        _radialMenuOverlayWindow?.HideMenu();
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
                ExecuteNamedRadialAction(
                    RadialText("接受更改", "Approve changes"),
                    "Approve",
                    "Accept",
                    "Accept changes",
                    "Allow",
                    "Allow once",
                    "Continue");
                break;
            case RadialInputAction.Decline:
                ExecuteNamedRadialAction(
                    RadialText("拒绝更改", "Decline changes"),
                    "Decline",
                    "Reject",
                    "Reject changes",
                    "Deny");
                break;
            case RadialInputAction.Fork:
                ExecuteNamedRadialAction(
                    RadialText("分支任务", "Fork task"),
                    "Fork",
                    "Fork task",
                    "Fork thread",
                    "Branch",
                    "Branch task",
                    "Continue in new task");
                break;
            case RadialInputAction.PushToTalk:
                if (!_radialPushToTalkActive)
                {
                    _radialPushToTalkActive = true;
                    StartDictation(Glyph(LogicalInput.View));
                }
                break;
            case RadialInputAction.Dispatch:
                SendPrompt(Glyph(LogicalInput.Menu));
                break;
            case RadialInputAction.Steer:
                ExecuteNamedRadialAction(
                    RadialText("加入当前运行", "Steer current turn"),
                    "Steer",
                    "Steer current turn",
                    "Add to current turn",
                    "加入当前运行",
                    "加入当前轮次");
                break;
            case RadialInputAction.Queue:
                ExecuteNamedRadialAction(
                    RadialText("排到下一轮", "Queue next turn"),
                    "Queue",
                    "Queue next turn",
                    "Send next",
                    "排到下一轮",
                    "排入下一轮");
                break;
            case RadialInputAction.StopTurn:
                ExecuteNamedRadialAction(
                    RadialText("停止当前运行", "Stop current turn"),
                    "Stop");
                break;
        }
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
        OpenThreadNow(
            thread.Id,
            thread.Title,
            thread.NativeTitle ?? thread.Title);
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
        OpenThreadNow(
            next.ThreadId!,
            next.Title,
            next.NativeTitle ?? next.Title);
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
        var title = _localization.Strings.ConfigToggleFast;
        var executed = _agentShortcuts.Execute(
            _settings.FastToggleShortcut,
            _settings);
        AddEvent(
            title +
            ExecutionSuffix(
                executed,
                executed
                    ? null
                    : AgentAutomationErrorCodes.InputInjectionFailed,
                _settings.FastToggleShortcut));
        ShowFeedback(
            title,
            executed
                ? RadialText("已执行。", "Executed.")
                : ExecutionFailureLabel(
                    AgentAutomationErrorCodes.InputInjectionFailed));
        Pulse(strength: executed ? 0.22 : 0.1);
    }

    private void ExecuteNamedRadialAction(
        string title,
        params string[] actionNames)
    {
        var result = _composerAutomation.InvokeAction(
            _settings,
            actionNames);
        if (result.Succeeded)
        {
            ClearNavigationUndo();
        }

        AddEvent(
            title +
            ExecutionSuffix(
                result.Succeeded,
                result.Error,
                result.ErrorDetail));
        ShowFeedback(
            title,
            result.Succeeded
                ? RadialText("已执行。", "Executed.")
                : ExecutionFailureLabel(result.Error));
        Pulse(strength: result.Succeeded ? 0.22 : 0.1);
    }

    private void RefreshRadialMenu()
    {
        if (
            _radialLayer is not { } layer ||
            _radialLayerCancelled ||
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
                isLearningCueReady: _radialLayerEngaged,
                subtitle: RadialText(
                    $"{Glyph(LogicalInput.FaceEast)} 取消",
                    $"{Glyph(LogicalInput.FaceEast)} cancel")),
            RadialMenuLayerKind.Command => new RadialMenuState(
                layer,
                RadialText("Codex 命令", "Codex commands"),
                Glyph(LogicalInput.RightShoulder),
                BuildCommandRadialItems(),
                mode,
                isLayerEngaged: true,
                isLearningCueReady: _radialLayerEngaged,
                subtitle: RadialText(
                    $"{Glyph(LogicalInput.LeftStickPress)} 取消",
                    $"{Glyph(LogicalInput.LeftStickPress)} cancel")),
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
                    "Release RT to close")),
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
        var positions = new[]
        {
            RadialMenuSlotPosition.Top,
            RadialMenuSlotPosition.Right,
            RadialMenuSlotPosition.Bottom,
            RadialMenuSlotPosition.Left,
            RadialMenuSlotPosition.CenterLeft,
            RadialMenuSlotPosition.CenterRight,
        };
        var glyphs = new[]
        {
            Glyph(LogicalInput.DPadUp),
            Glyph(LogicalInput.DPadRight),
            Glyph(LogicalInput.DPadDown),
            Glyph(LogicalInput.DPadLeft),
            Glyph(LogicalInput.View),
            Glyph(LogicalInput.Menu),
        };
        var items = new List<RadialMenuItemState>(6);
        for (var index = 0; index < 6; index++)
        {
            var thread =
                index < threads.Length ? threads[index] : null;
            items.Add(RadialItem(
                $"agent-slot-{index + 1}",
                positions[index],
                glyphs[index],
                thread?.Title ??
                    RadialText("未分配", "Unassigned"),
                RadialText(
                    $"Agent 槽 {index + 1}",
                    $"Agent slot {index + 1}"),
                isEnabled: thread is not null));
        }

        return items;
    }

    private IReadOnlyList<RadialMenuItemState>
        BuildCommandRadialItems()
    {
        var dispatch = ResolveDispatchDisplay();
        return
        [
            RadialItem(
                "command-fast",
                RadialMenuSlotPosition.Top,
                Glyph(LogicalInput.FaceNorth),
                _localization.Strings.ConfigToggleFast),
            RadialItem(
                "command-decline",
                RadialMenuSlotPosition.Right,
                Glyph(LogicalInput.FaceEast),
                RadialText("拒绝更改", "Decline changes")),
            RadialItem(
                "command-approve",
                RadialMenuSlotPosition.Bottom,
                Glyph(LogicalInput.FaceSouth),
                RadialText("接受更改", "Approve changes")),
            RadialItem(
                "command-fork",
                RadialMenuSlotPosition.Left,
                Glyph(LogicalInput.FaceWest),
                RadialText("分支任务", "Fork task")),
            RadialItem(
                "command-ptt",
                RadialMenuSlotPosition.CenterLeft,
                Glyph(LogicalInput.View),
                _localization.Strings.ConfigDictation,
                RadialText("按住说话", "Hold to talk")),
            RadialItem(
                "command-dispatch",
                RadialMenuSlotPosition.CenterRight,
                Glyph(LogicalInput.Menu),
                dispatch.Label,
                dispatch.Description),
        ];
    }

    private IReadOnlyList<RadialMenuItemState>
        BuildTurnRadialItems()
    {
        var contextual = RadialText(
            "仅在 Codex 显示对应操作时可用",
            "Available only when Codex shows the matching action");
        return
        [
            RadialItem(
                "turn-queue",
                RadialMenuSlotPosition.Top,
                Glyph(LogicalInput.FaceNorth),
                RadialText("排到下一轮", "Queue next turn"),
                contextual),
            RadialItem(
                "turn-stop",
                RadialMenuSlotPosition.Right,
                Glyph(LogicalInput.FaceEast),
                RadialText("停止", "Stop")),
            RadialItem(
                "turn-fork",
                RadialMenuSlotPosition.Bottom,
                Glyph(LogicalInput.FaceSouth),
                RadialText("分支任务", "Fork task")),
            RadialItem(
                "turn-steer",
                RadialMenuSlotPosition.Left,
                Glyph(LogicalInput.FaceWest),
                RadialText("加入当前运行", "Steer current turn"),
                contextual),
        ];
    }

    private RadialMenuItemState RadialItem(
        string id,
        RadialMenuSlotPosition position,
        string inputGlyph,
        string title,
        string? subtitle = null,
        bool isEnabled = true)
    {
        return new RadialMenuItemState(
            id,
            position,
            inputGlyph,
            title,
            subtitle,
            isEnabled,
            isHighlighted: string.Equals(
                id,
                _radialHighlightedItemId,
                StringComparison.Ordinal));
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

    private string RadialActionId(RadialInputAction action)
    {
        var slot = RadialInputMap.AgentSlotIndex(action);
        if (slot >= 0)
        {
            return $"agent-slot-{slot + 1}";
        }

        return action switch
        {
            RadialInputAction.ToggleFast => "command-fast",
            RadialInputAction.Approve => "command-approve",
            RadialInputAction.Decline => "command-decline",
            RadialInputAction.Fork when
                _radialLayer == RadialMenuLayerKind.Turn =>
                "turn-fork",
            RadialInputAction.Fork => "command-fork",
            RadialInputAction.PushToTalk => "command-ptt",
            RadialInputAction.Dispatch => "command-dispatch",
            RadialInputAction.Steer => "turn-steer",
            RadialInputAction.Queue => "turn-queue",
            RadialInputAction.StopTurn => "turn-stop",
            _ => string.Empty,
        };
    }

    private string RadialText(string zhCn, string enUs)
    {
        return _localization.EffectiveLanguage == AppLanguage.ZhCn
            ? zhCn
            : enUs;
    }

    private void HandleButtonEdge(
        ControllerButtons current,
        ControllerButtons button,
        Action onDown,
        Action? onUp = null)
    {
        var isPressed = current.HasFlag(button);
        var wasPressed = _previousButtons.HasFlag(button);
        if (isPressed && !wasPressed)
        {
            onDown();
        }
        else if (!isPressed && wasPressed)
        {
            onUp?.Invoke();
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
            var woke = await Task.Run(_activeAgent.Presence.Wake);
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
            _axisRepeater.Reset();
            _leftStickRouter.RequireNeutral();
            _rightStickRouter.RequireNeutral();
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

    private void PauseControllerInput(
        ControllerState? state = null)
    {
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

        _axisRepeater.Reset();
        _leftStickRouter.Reset();
        _rightStickRouter.Reset();
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
            _previousButtons = ControllerButtons.None;
            _previousPhysicalButtons = ControllerButtons.None;
            _axisRepeater.Reset();
            _leftStickRouter.Reset();
            _rightStickRouter.Reset();
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
        _leftStickRouter.RequireNeutral();
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
                    _localization.Strings.Format(
                        StringKeys.MessageAgentSidebar,
                        _activeAgent.DisplayName),
                    _localization.Strings.Get(
                        StringKeys.MessageAlreadyAtRootScope));
                break;
            case SidebarNavigationIntent.NoChildDirectory:
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageAgentSidebar,
                        _activeAgent.DisplayName),
                    _localization.Strings.Get(
                        StringKeys.MessageFocusedEntryHasNoChildDirectory));
                break;
        }
    }

    private void CycleRootSidebarScope()
    {
        _leftStickRouter.RequireNeutral();
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
            _bridgeEvents.Publish(
                BridgeEventKeys.SidebarScopeChanged,
                parameters: new Dictionary<string, string>
                {
                    ["scope"] = scope.ToString(),
                    ["label"] = ScopeLabel(scope),
                },
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

    private void OpenSelectedSidebarTask()
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

        OpenThreadNow(
            entry!.ThreadId!,
            entry.Title,
            entry.NativeTitle ?? entry.Title);
    }

    private void ActivateSelectedSidebarEntryFromPointer()
    {
        if (DevicePage.SelectedEntry is { IsProject: true })
        {
            NavigateSidebarHorizontal(1);
            return;
        }

        OpenSelectedSidebarTask();
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
            ShowSidebarNavigationWheel();
            return;
        }

        SelectSidebarIndex(ActiveSidebarNavigation.SelectedIndex);
        ActivateSelectedEntry(
            entry,
            deferFocus: true,
            showToast: false);
        ShowSidebarNavigationWheel();
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

    private void ShowSidebarNavigationWheel()
    {
        if (!_settings.ShowOverlay)
        {
            return;
        }

        var wheel = ActiveSidebarNavigation.BuildWheelState(
            _sidebarEntries,
            SidebarWheelScopeLabel);
        if (wheel is not null)
        {
            _sidebarNavigationWheelOverlayWindow?.ShowState(wheel);
        }
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

    private void OpenThreadNow(
        string threadId,
        string threadTitle,
        string nativeThreadTitle)
    {
        if (
            _settings.OnlyWhenCodexForeground &&
            !_activeAgent.Presence.IsForeground &&
            !IsActive)
        {
            return;
        }

        if (!_workspaceReader.IsThreadAvailable(threadId))
        {
            AddEvent(_localization.Strings.Get(
                StringKeys.MessageTaskUnavailableSkipped));
            RefreshCodexData(preserveSelection: true);
            return;
        }

        ClearNavigationUndo();
        var previousTitle = _sidebarAutomation.TryGetCurrentThreadTitle();
        if (_activeAgent.DeepLinks?.OpenThread(threadId) == true)
        {
            RegisterNavigationUndo(
                threadTitle,
                nativeThreadTitle,
                previousTitle);
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

    private void RegisterNavigationUndo(
        string threadTitle,
        string nativeThreadTitle,
        string? previousTitle)
    {
        if (
            string.IsNullOrWhiteSpace(nativeThreadTitle) ||
            string.Equals(
                previousTitle,
                nativeThreadTitle,
                StringComparison.Ordinal))
        {
            return;
        }

        var matchingTitles = _snapshot.Threads.Count(thread =>
            string.Equals(
                thread.NativeTitle ?? thread.Title,
                nativeThreadTitle,
                StringComparison.Ordinal));
        if (matchingTitles != 1)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageUndoUnavailableUnique,
                threadTitle));
            return;
        }

        var state = new NavigationUndoState(
            threadTitle,
            nativeThreadTitle,
            previousTitle);
        _navigationUndo = state;
        var cancellation = new CancellationTokenSource();
        _navigationConfirmCancellation = cancellation;
        _ = ConfirmNavigationUndoAsync(state, cancellation);
    }

    private async Task ConfirmNavigationUndoAsync(
        NavigationUndoState state,
        CancellationTokenSource cancellation)
    {
        var consecutiveMatches = 0;
        var deadline =
            Environment.TickCount64 +
            BridgeTimings.NavigationConfirmTimeoutMs;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var currentTitle = await Task.Run(
                        _sidebarAutomation.TryGetCurrentThreadTitle,
                        cancellation.Token)
                    .ConfigureAwait(true);
                if (
                    string.Equals(
                        currentTitle,
                        state.TargetNativeTitle,
                        StringComparison.Ordinal))
                {
                    consecutiveMatches++;
                    if (consecutiveMatches >= 2)
                    {
                        if (!ReferenceEquals(_navigationUndo, state))
                        {
                            return;
                        }

                        state.Confirmed = true;
                        state.ExpiresAt =
                            DateTimeOffset.UtcNow +
                            BridgeTimings.NavigationUndoWindow;
                        if (_scope == SidebarScope.ProjectlessTasks)
                        {
                            RefreshCodexData(preserveSelection: true);
                        }

                        if (state.UndoRequested)
                        {
                            ExecuteNavigationUndo(state);
                            return;
                        }

                        var undoGlyph = Glyph(LogicalInput.FaceEast);
                        AddEvent(_localization.Strings.Format(
                            StringKeys.MessageOpenedUndoAvailable,
                            state.TargetDisplayTitle,
                            undoGlyph));
                        ShowFeedback(
                            _localization.Strings.Get(
                                StringKeys.MessageOpenedTask),
                            _localization.Strings.Format(
                                StringKeys.MessageUndoWithinSeconds,
                                state.TargetDisplayTitle,
                                (int)BridgeTimings
                                    .NavigationUndoWindow.TotalSeconds,
                                undoGlyph));
                        return;
                    }
                }
                else
                {
                    consecutiveMatches = 0;
                }

                await Task.Delay(
                        BridgeTimings.NavigationConfirmPollMs,
                        cancellation.Token)
                    .ConfigureAwait(true);
            }

            if (ReferenceEquals(_navigationUndo, state))
            {
                _navigationUndo = null;
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoUnavailableUnconfirmed,
                    state.TargetDisplayTitle));
                if (state.UndoRequested)
                {
                    ShowFeedback(
                        _localization.Strings.Format(
                            StringKeys.MessageButtonUndo,
                            Glyph(LogicalInput.FaceEast)),
                        _localization.Strings.Get(
                            StringKeys.MessageUndoUnconfirmed));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A newer navigation or an intentional command invalidated this frame.
        }
        finally
        {
            if (ReferenceEquals(_navigationConfirmCancellation, cancellation))
            {
                _navigationConfirmCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ClearNavigationUndo()
    {
        _navigationUndo = null;
        var cancellation = _navigationConfirmCancellation;
        _navigationConfirmCancellation = null;
        cancellation?.Cancel();
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
        _rightStickRouter.RequireNeutral();
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
            Interlocked.Exchange(ref _pendingDialDelta, 0);
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
            ShowFeedback(
                _localization.Strings.VirtualDial,
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

        _ = PressVirtualDialAsync();
    }

    private void QueueVirtualDialStep(int direction)
    {
        if (
            direction == 0 ||
            _virtualDialCleanupPending ||
            _dialInputReleasePending)
        {
            return;
        }

        CancelPendingComposerSelection();
        Interlocked.Exchange(
            ref _pendingDialDelta,
            Math.Sign(direction));
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
                var delta =
                    Interlocked.Exchange(ref _pendingDialDelta, 0);
                if (delta == 0)
                {
                    return;
                }

                var generation =
                    Volatile.Read(ref _virtualDialGeneration);
                var result = await RunVirtualDialAutomationAsync(
                        () => _composerAutomation.DialStep(
                            delta,
                            _settings))
                    .ConfigureAwait(true);
                if (
                    generation ==
                        Volatile.Read(ref _virtualDialGeneration) &&
                    !_virtualDialCancelRequested)
                {
                    PresentVirtualDialResult(result);
                }
            }
        }
        finally
        {
            Volatile.Write(ref _dialPumpRunning, 0);
            if (
                Volatile.Read(ref _pendingDialDelta) != 0 &&
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
        ShowFeedback(
            _localization.Strings.VirtualDial,
            RadialText(
                "正在打开当前控件…",
                "Opening the current control…"));
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

            SetVirtualDialMenuOpen(cleanup.IsMenuOpen);
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
    }

    private void CancelActionOrDialMenu()
    {
        if (IsVirtualDialContextActive)
        {
            if (
                _virtualDialCancelRequested ||
                _virtualDialCleanupPending)
            {
                return;
            }

            _virtualDialCancelRequested = true;
            Interlocked.Exchange(ref _pendingDialDelta, 0);
            _ = CloseVirtualDialMenuAsync(showFeedback: true);
            return;
        }

        if (_dictationInjected || _pushToTalkTrigger.BlocksBaseInput)
        {
            CancelAction();
            return;
        }

        CancelAction();
    }

    private async Task CloseVirtualDialMenuAsync(bool showFeedback)
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
            result.Succeeded
                ? result.IsMenuOpen
                : result.IsMenuOpen || _virtualDialMenuOpen);
        _virtualDialOpenPending = false;
        _virtualDialCancelRequested = false;
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
                else
                {
                    ShowFeedback(
                        _localization.Strings.VirtualDial,
                        _localization.Strings.ComposerDialCanceled);
                    Pulse(strength: 0.12);
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
        var wasContextActive = IsVirtualDialContextActive;
        if (!result.Succeeded)
        {
            SetVirtualDialMenuOpen(
                result.IsMenuOpen || _virtualDialMenuOpen);
            var failure = VirtualDialFailureLabel(result);
            _devicePageViewModel.UpdateRightMode(
                RightControlMode.Dial,
                failure);
            AddEvent(
                $"{_localization.Strings.VirtualDial} · {failure}");
            ShowFeedback(
                _localization.Strings.VirtualDial,
                failure);
            Pulse(strength: 0.08);
            return;
        }

        SetVirtualDialMenuOpen(result.IsMenuOpen);

        _rightMode = RightControlMode.Dial;
        var value = string.IsNullOrWhiteSpace(result.ControlName)
            ? _localization.Strings.ComposerDialReady
            : result.ControlName;
        _devicePageViewModel.UpdateRightMode(
            RightControlMode.Dial,
            value);
        AddEvent(
            $"{_localization.Strings.VirtualDial} · {value}");
        var instruction =
            wasContextActive && !result.IsMenuOpen
                ? RadialText(
                    $"已选择 {value} · 左右拨动继续选择控件",
                    $"Selected {value} · move the right stick left or right to continue")
                : _localization.Strings.ControlRightStickHint(
                    Glyph(LogicalInput.RightStickPress),
                    Glyph(LogicalInput.FaceEast),
                    result.IsMenuOpen);
        ShowFeedback(
            _localization.Strings.VirtualDial,
            $"{value}\n{instruction}");
        Pulse(strength: result.IsMenuOpen ? 0.18 : 0.12);
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

    private void PresentVirtualDialProbeFailure(
        ComposerDialResult result)
    {
        var failure = ExecutionFailureLabel(
            result.Error,
            result.ErrorDetail);
        _devicePageViewModel.UpdateRightMode(
            RightControlMode.Dial,
            failure);
        AddEvent(
            $"{_localization.Strings.VirtualDial} · {failure}");
        ShowFeedback(
            _localization.Strings.VirtualDial,
            failure);
        Pulse(strength: 0.08);
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
        CancelVirtualDialPressHold();
        Interlocked.Exchange(ref _pendingDialDelta, 0);

        var hadOpenMenu = _virtualDialMenuOpen;
        SetVirtualDialMenuOpen(false);
        if (
            closeMenu &&
            hadOpenMenu &&
            _settings.BridgeEnabled &&
            _activeAgent.Presence.IsForeground)
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

    private void SwitchRightControlMode(
        int direction,
        string? source = null)
    {
        CancelPendingComposerSelection();
        _rightAdjustmentBlockedUntil =
            Environment.TickCount64 + BridgeTimings.GestureInputGuardMs;
        var values = Enum.GetValues<RightControlMode>();
        var current = Array.IndexOf(values, _rightMode);
        var next =
            (current + Math.Sign(direction) + values.Length) %
            values.Length;
        _rightMode = values[next];
        UpdateRightModeUi();
        var gesture =
            source ??
            _localization.Strings.Format(
                StringKeys.MessageRightStickGesture,
                Arrow(direction, horizontal: true));
        AddEvent($"{gesture} · {ModeLabel(_rightMode)}");
        ShowFeedback(
            _localization.Strings.Get(
                StringKeys.MessageRightStickMode),
            ModeLabel(_rightMode));
        Pulse();
    }

    private void AdjustRightMode(int direction)
    {
        switch (_rightMode)
        {
            case RightControlMode.Reasoning:
                AdjustReasoning(direction);
                break;
            case RightControlMode.Model:
                AdjustModel(direction);
                break;
            case RightControlMode.Speed:
                AdjustSpeed(direction);
                break;
        }
    }

    private void AdjustReasoning(int direction)
    {
        var efforts = CurrentEfforts();
        var next = Math.Clamp(
            _reasoningIndex + direction,
            0,
            efforts.Count - 1);
        if (next == _reasoningIndex)
        {
            RefreshPendingComposerDeadline(ComposerSettingKind.Effort);
            return;
        }

        _reasoningIndex = next;
        var target = efforts[_reasoningIndex];
        var displayTarget = ComposerTargetLabel(
            ComposerSettingKind.Effort,
            target);
        _devicePageViewModel.UpdateRightModeValue(
            _localization.Strings.Format(
                StringKeys.MessagePreviewValue,
                displayTarget));
        ScheduleComposerCommit(ComposerSettingKind.Effort, target);
        AddEvent(_localization.Strings.FeedbackSelectionPreviewed(
            ComposerKindLabel(ComposerSettingKind.Effort),
            displayTarget));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessagePreviewValue,
                ComposerKindLabel(ComposerSettingKind.Effort)),
            _localization.Strings.Format(
                StringKeys.MessageSettleToConfirm,
                displayTarget));
        Pulse();
    }

    private void AdjustModel(int direction)
    {
        if (_composerCatalog is null || _composerCatalog.Models.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(
            _modelIndex + direction,
            0,
            _composerCatalog.Models.Count - 1);
        if (next == _modelIndex)
        {
            RefreshPendingComposerDeadline(ComposerSettingKind.Model);
            return;
        }

        _modelIndex = next;
        var target = _composerCatalog.Models[_modelIndex].DisplayName;
        _devicePageViewModel.UpdateRightModeValue(
            _localization.Strings.Format(
                StringKeys.MessagePreviewValue,
                target));
        ScheduleComposerCommit(ComposerSettingKind.Model, target);
        AddEvent(_localization.Strings.FeedbackSelectionPreviewed(
            ComposerKindLabel(ComposerSettingKind.Model),
            target));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessagePreviewValue,
                ComposerKindLabel(ComposerSettingKind.Model)),
            _localization.Strings.Format(
                StringKeys.MessageSettleToConfirm,
                target));
        Pulse();
    }

    private void AdjustSpeed(int direction)
    {
        var next = direction > 0 ? 1 : 0;
        if (next == _speedIndex)
        {
            RefreshPendingComposerDeadline(ComposerSettingKind.Speed);
            return;
        }

        _speedIndex = next;
        var target = _speedIndex > 0 ? "Fast" : "Standard";
        var displayTarget = ComposerTargetLabel(
            ComposerSettingKind.Speed,
            target);
        _devicePageViewModel.UpdateRightModeValue(
            _localization.Strings.Format(
                StringKeys.MessagePreviewValue,
                displayTarget));
        ScheduleComposerCommit(ComposerSettingKind.Speed, target);
        AddEvent(_localization.Strings.FeedbackSelectionPreviewed(
            ComposerKindLabel(ComposerSettingKind.Speed),
            displayTarget));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessagePreviewValue,
                ComposerKindLabel(ComposerSettingKind.Speed)),
            _localization.Strings.Format(
                StringKeys.MessageSettleToConfirm,
                displayTarget));
        Pulse();
    }

    private void ScheduleComposerCommit(
        ComposerSettingKind kind,
        string target)
    {
        _composerCommitCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _composerCommitCancellation = cancellation;
        _pendingComposerKind = kind;
        _pendingComposerTarget = target;
        _ = CommitComposerAfterSettleAsync(kind, target, cancellation);
    }

    private void RefreshPendingComposerDeadline(ComposerSettingKind kind)
    {
        if (
            _pendingComposerKind == kind &&
            !string.IsNullOrWhiteSpace(_pendingComposerTarget))
        {
            ScheduleComposerCommit(kind, _pendingComposerTarget);
        }
    }

    private async Task CommitComposerAfterSettleAsync(
        ComposerSettingKind kind,
        string target,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                    BridgeTimings.ComposerSettleMs,
                    cancellation.Token)
                .ConfigureAwait(true);
            var displayTarget = ComposerTargetLabel(kind, target);
            _devicePageViewModel.UpdateRightModeValue(
                _localization.Strings.Format(
                    StringKeys.MessageApplyingValue,
                    displayTarget));
            var automation = await _composerAutomation
                .SelectAsync(kind, target, _settings, cancellation.Token)
                .ConfigureAwait(true);
            var fallback = false;
            if (!automation.Succeeded)
            {
                fallback = await ExecuteComposerFallbackAsync(
                        kind,
                        cancellation.Token)
                    .ConfigureAwait(true);
            }

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (automation.Succeeded || fallback)
            {
                ClearNavigationUndo();
                if (kind == ComposerSettingKind.Model)
                {
                    await Task.Delay(
                            BridgeTimings.ComposerFallbackSettleMs,
                            cancellation.Token)
                        .ConfigureAwait(true);
                    InitializeComposerControls();
                }
                else
                {
                    MarkComposerCommitted(kind);
                }

                _devicePageViewModel.UpdateRightModeValue(
                    automation.Succeeded
                        ? displayTarget
                        : _localization.Strings.Format(
                            StringKeys.MessageShortcutSentValue,
                            displayTarget));
                var channel =
                    automation.Succeeded
                        ? _localization.Strings.Get(
                            StringKeys.MessageExactSelection)
                        : _localization.Strings.Format(
                            StringKeys.MessageShortcutSentRestart,
                            _activeAgent.DisplayName);
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageComposerSelectionApplied,
                    ComposerKindLabel(kind),
                    displayTarget,
                    channel));
                ShowFeedback(
                    ComposerKindLabel(kind),
                    automation.Succeeded
                        ? displayTarget
                        : _localization.Strings.Get(
                            StringKeys.MessageShortcutSent));
            }
            else
            {
                _devicePageViewModel.UpdateRightModeValue(
                    _localization.Strings.Format(
                        StringKeys.MessageNotExecutedValue,
                        displayTarget));
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageComposerSelectionFailed,
                    ComposerKindLabel(kind),
                    ExecutionFailureLabel(
                        automation.Error,
                        automation.ErrorDetail)));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer stick input replaced this pending selection.
        }
        finally
        {
            if (ReferenceEquals(_composerCommitCancellation, cancellation))
            {
                _composerCommitCancellation = null;
                _pendingComposerKind = null;
                _pendingComposerTarget = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task<bool> ExecuteComposerFallbackAsync(
        ComposerSettingKind kind,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ComposerSettingKind.Model:
            {
                var steps = _modelIndex - _committedModelIndex;
                return steps != 0 &&
                       await _agentShortcuts
                           .StepModelAsync(
                               steps,
                               _settings,
                               cancellationToken)
                           .ConfigureAwait(true);
            }
            case ComposerSettingKind.Effort:
            {
                var steps = _reasoningIndex - _committedReasoningIndex;
                if (steps == 0)
                {
                    return true;
                }

                var shortcut =
                    steps > 0
                        ? _settings.ReasoningUpShortcut
                        : _settings.ReasoningDownShortcut;
                for (var index = 0; index < Math.Abs(steps); index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_agentShortcuts.Execute(
                            shortcut,
                            _settings))
                    {
                        return false;
                    }

                    await Task.Delay(
                            BridgeTimings.ComposerMenuPollMs,
                            cancellationToken)
                        .ConfigureAwait(true);
                }

                return true;
            }
            case ComposerSettingKind.Speed:
                return
                    _speedIndex == _committedSpeedIndex ||
                    _agentShortcuts.Execute(
                        _settings.FastToggleShortcut,
                        _settings);
            default:
                return false;
        }
    }

    private void MarkComposerCommitted(ComposerSettingKind kind)
    {
        switch (kind)
        {
            case ComposerSettingKind.Model:
                _committedModelIndex = _modelIndex;
                var efforts = CurrentEfforts();
                _reasoningIndex = Math.Clamp(
                    _reasoningIndex,
                    0,
                    efforts.Count - 1);
                _committedReasoningIndex = _reasoningIndex;
                break;
            case ComposerSettingKind.Effort:
                _committedReasoningIndex = _reasoningIndex;
                break;
            case ComposerSettingKind.Speed:
                _committedSpeedIndex = _speedIndex;
                break;
        }
    }

    private void CancelPendingComposerSelection()
    {
        var pendingKind = _pendingComposerKind;
        _composerCommitCancellation?.Cancel();
        _composerCommitCancellation = null;
        _pendingComposerKind = null;
        _pendingComposerTarget = null;

        switch (pendingKind)
        {
            case ComposerSettingKind.Model:
                _modelIndex = _committedModelIndex;
                break;
            case ComposerSettingKind.Effort:
                _reasoningIndex = _committedReasoningIndex;
                break;
            case ComposerSettingKind.Speed:
                _speedIndex = _committedSpeedIndex;
                break;
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
        var transition = _pushToTalkTrigger.Update(value, blocked);
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
        var detail = !_settings.BridgeEnabled
            ? ExecutionFailureLabel(
                AgentAutomationErrorCodes.BridgeSafePreview)
            : !_controllerSession.IsArmed
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

    private void ResetPushToTalk(bool stopDictation)
    {
        var wasPressed = _pushToTalkTrigger.CancelUntilReleased();
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
        _pushToTalkAutomation.RequestStart(
            dialContextActive: IsVirtualDialContextActive);
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
                    case PushToTalkAutomationAction.CloseDial:
                    {
                        var closeResult =
                            await CloseVirtualDialForPushToTalkAsync()
                                .ConfigureAwait(true);
                        var closed =
                            closeResult.Succeeded &&
                            !closeResult.IsMenuOpen;
                        _pushToTalkAutomation.Complete(
                            action,
                            closed);
                        if (!closed)
                        {
                            PresentDictationDialCloseFailure(
                                closeResult);
                        }

                        break;
                    }
                    case PushToTalkAutomationAction.StartDictation:
                    {
                        var result =
                            await ExecuteDictationAutomationAsync(
                                    BridgeTimings.DictationStartTimeoutMs,
                                    PushToTalkAutomationPolicy
                                        .AllowsShortcutFallback(action),
                                    PushToTalkAutomationPolicy
                                        .StartActionNames)
                                .ConfigureAwait(true);
                        _pushToTalkAutomation.Complete(
                            action,
                            result.Executed);
                        _dictationInjected =
                            _pushToTalkAutomation.IsDictating;
                        DevicePage.SetVoiceHalo(
                            active: _dictationInjected);
                        PresentDictationStartResult(result);
                        break;
                    }
                    case PushToTalkAutomationAction.StopDictation:
                    {
                        var result =
                            await ExecuteDictationAutomationAsync(
                                    BridgeTimings.DictationStopTimeoutMs,
                                    PushToTalkAutomationPolicy
                                        .AllowsShortcutFallback(action),
                                    PushToTalkAutomationPolicy
                                        .StopActionNames)
                                .ConfigureAwait(true);
                        var stopped =
                            result.Executed ||
                            _composerAutomation.IsActionAvailable(
                                PushToTalkAutomationPolicy
                                    .StartActionNames
                                    .ToArray());
                        _pushToTalkAutomation.Complete(
                            action,
                            stopped);
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
        Interlocked.Exchange(ref _pendingDialDelta, 0);
        CancelVirtualDialPressHold();
        _virtualDialOpenPending = false;
        _virtualDialCancelRequested = false;
        _virtualDialCleanupPending = false;
        SetVirtualDialMenuOpen(false);
        _axisRepeater.Reset();
        _rightStickRouter.RequireNeutral();

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
                result.Succeeded && result.IsMenuOpen);
            return result;
        }
        catch (TimeoutException)
        {
            // Never block the Dispatcher behind a stalled UIA tree walk.
            SetVirtualDialMenuOpen(true);
            return new(
                false,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.OperationCanceled,
                ErrorDetail: "dictation-dial-close-timeout");
        }
    }

    private async Task<DictationAutomationExecution>
        ExecuteDictationAutomationAsync(
            int timeoutMs,
            bool allowShortcutFallback,
            IReadOnlyList<string> actionNames)
    {
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
            ClearNavigationUndo();
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
        SendPrompt(Glyph(LogicalInput.FaceWest));
    }

    private void SendPrompt(string sendGlyph)
    {
        var automation = _composerAutomation.Submit(_settings);
        if (automation.Succeeded)
        {
            ClearNavigationUndo();
        }

        AddEvent(
            _localization.Strings.Format(
                StringKeys.MessageSendPrompt,
                sendGlyph) +
            ExecutionSuffix(
                automation.Succeeded,
                automation.Error,
                automation.ErrorDetail));
        ShowFeedback(
            _localization.Strings.ControlSend(sendGlyph),
            automation.Succeeded
                ? _localization.Strings.Get(
                    StringKeys.MessageSent)
                : ExecutionFailureLabel(
                    automation.Error));
        Pulse(strength: 0.28);
    }

    private void CancelAction()
    {
        if (
            _dictationInjected ||
            _pushToTalkAutomation.WantsDictation ||
            _pushToTalkTrigger.BlocksBaseInput)
        {
            _pushToTalkTrigger.CancelUntilReleased();
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
            ClearNavigationUndo();
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
            _pendingComposerKind is not null ||
            _composerCommitCancellation is not null;
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

        var active = _composerAutomation.InvokeAction(
            _settings,
            "Stop",
            "Cancel",
            "Cancel request");
        if (active.Succeeded)
        {
            ClearNavigationUndo();
            var cancelGlyph = Glyph(LogicalInput.FaceEast);
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

        if (_navigationUndo is { } undo)
        {
            if (
                !undo.Confirmed ||
                undo.ExpiresAt is null)
            {
                undo.UndoRequested = true;
                var cancelGlyph = Glyph(LogicalInput.FaceEast);
                AddEvent(_localization.Strings.Format(
                    StringKeys.MessageUndoQueued,
                    cancelGlyph));
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageButtonUndo,
                        cancelGlyph),
                    _localization.Strings.Get(
                        StringKeys.MessageUndoAfterOpen));
                Pulse(strength: 0.12);
                return;
            }

            if (DateTimeOffset.UtcNow > undo.ExpiresAt)
            {
                ClearNavigationUndo();
            }
            else
            {
                ExecuteNavigationUndo(undo);
                return;
            }
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

    private void ExecuteNavigationUndo(NavigationUndoState undo)
    {
        if (!ReferenceEquals(_navigationUndo, undo))
        {
            return;
        }

        var currentTitle =
            _sidebarAutomation.TryGetCurrentThreadTitle();
        if (
            !string.Equals(
                currentTitle,
                undo.TargetNativeTitle,
                StringComparison.Ordinal))
        {
            ClearNavigationUndo();
            var cancelGlyph = Glyph(LogicalInput.FaceEast);
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageUndoPageChanged,
                cancelGlyph));
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonUndo,
                    cancelGlyph),
                _localization.Strings.Get(
                    StringKeys.MessageUndoPageChangedDetail));
            Pulse(strength: 0.12);
            return;
        }

        var navigation =
            _sidebarAutomation.GoBack(_settings);
        if (navigation.Succeeded)
        {
            var target = undo.TargetDisplayTitle;
            ClearNavigationUndo();
            var cancelGlyph = Glyph(LogicalInput.FaceEast);
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageUndoSucceeded,
                cancelGlyph,
                target));
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonUndo,
                    cancelGlyph),
                _localization.Strings.Format(
                    StringKeys.MessageReturnedToPreviousTask,
                    target));
            Pulse(strength: 0.18);
            return;
        }

        var undoGlyph = Glyph(LogicalInput.FaceEast);
        AddEvent(_localization.Strings.Format(
            StringKeys.MessageUndoFailed,
            undoGlyph,
            _localization.Strings.ErrorLabel(
                navigation.Error,
                navigation.ErrorDetail)));
        ShowFeedback(
            _localization.Strings.Format(
                StringKeys.MessageButtonUndo,
                undoGlyph),
            ExecutionFailureLabel(
                navigation.Error));
        Pulse(strength: 0.12);
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
            $"v0.4b · {strings.StatusLocalBridge}";

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
        _committedModelIndex = _modelIndex;

        var efforts = CurrentEfforts();
        _reasoningIndex = FindValueIndex(
            efforts,
            _composerCatalog.InitialEffort);
        _committedReasoningIndex = _reasoningIndex;
        _speedIndex =
            string.Equals(
                _composerCatalog.InitialSpeed,
                "Fast",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        _committedSpeedIndex = _speedIndex;
        UpdateRightModeUi();
    }

    private IReadOnlyList<string> CurrentEfforts()
    {
        return _composerCatalog?.EffortsForModel(_modelIndex)
               ?? ["Light", "Medium", "High", "Extra High", "Max", "Ultra"];
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
                _localization.Strings.ComposerDialReady,
            RightControlMode.Reasoning =>
                ComposerTargetLabel(
                    ComposerSettingKind.Effort,
                    CurrentEfforts()[Math.Clamp(
                        _reasoningIndex,
                        0,
                        CurrentEfforts().Count - 1)]),
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
        var foreground = _activeAgent.Presence.IsForeground;
        _ = ObserveCodexForeground(foreground);

        if (
            _controllerSession.IsArmed &&
            _controllerWasConnected &&
            (!_settings.OnlyWhenCodexForeground || foreground))
        {
            _ = TryResumeControllerInput(_xInputService.LastState);
        }

        var strings = _localization.Strings;
        var wakeGlyph = Glyph(LogicalInput.Menu);
        var statusText =
            !_controllerWasConnected
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

    private string SidebarWheelScopeLabel(SidebarScope scope)
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
                StringKeys.MessageSafePreview);
        }

        if (
            _settings.OnlyWhenCodexForeground &&
            !_activeAgent.Presence.IsForeground)
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

        _settings.BridgeEnabled = BridgeEnabledCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
        ConfigureCodexKeybindings();
        AddEvent(
            _settings.BridgeEnabled
                ? _localization.Strings.Get(
                    StringKeys.MessageBridgeEnabled)
                : _localization.Strings.Get(
                    StringKeys.MessageBridgeSafePreview));
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
            OpenSelectedSidebarTask();
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
        ResetRadialLayer(clearSuppression: true);
        ResetVirtualDialInput(closeMenu: true);
        CancelPendingSidebarFocus();
        CancelPendingComposerSelection();
        _pushToTalkAutomation.Reset();
        _dictationInjected = false;
        _dictationInputGlyph = null;
        ClearNavigationUndo();
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
        _sidebarNavigationWheelOverlayWindow?.Close();

        if (!_exitRequested)
        {
            _exitRequested = true;
            _ = Dispatcher.BeginInvoke(
                System.Windows.Application.Current.Shutdown);
        }
    }

    private sealed class NavigationUndoState
    {
        public NavigationUndoState(
            string targetDisplayTitle,
            string targetNativeTitle,
            string? previousTitle)
        {
            TargetDisplayTitle = targetDisplayTitle;
            TargetNativeTitle = targetNativeTitle;
            PreviousTitle = previousTitle;
        }

        public string TargetDisplayTitle { get; }
        public string TargetNativeTitle { get; }
        public string? PreviousTitle { get; }
        public bool Confirmed { get; set; }
        public bool UndoRequested { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private sealed record SidebarReturnFrame(
        SidebarScope Scope,
        string? EntryId,
        string? ProjectPath);

}
