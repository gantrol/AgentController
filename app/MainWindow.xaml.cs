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
using CodexController.Presentation.Feedback;
using CodexController.Services;
using CodexController.ViewModels;
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
    private readonly ObservableCollection<SidebarEntry> _sidebarEntries = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _dataTimer;
    private readonly object _controllerStateSync = new();
    private readonly ControllerSession _controllerSession = new();

    private AppSettings _settings = new();
    private CodexSnapshot _snapshot = new();
    private SidebarScope _scope = SidebarScope.Projects;
    private RightControlMode _rightMode = RightControlMode.Reasoning;
    private ControllerButtons _previousButtons;
    private string? _selectedProjectPath;
    private int _selectedIndex;
    private bool _projectTasksPinnedOnly;
    private readonly Dictionary<SidebarScope, string> _rootCursorIds = [];
    private readonly Dictionary<string, string> _projectTaskCursorIds =
        new(StringComparer.OrdinalIgnoreCase);
    private SidebarReturnFrame? _sidebarReturnFrame;
    private ProjectDisclosureLease? _projectDisclosureLease;
    private bool _dictationInjected;
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
    private int _controllerDispatchPending;
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
    private CancellationTokenSource? _dictationStopCancellation;
    private CancellationTokenSource? _navigationConfirmCancellation;
    private NavigationUndoState? _navigationUndo;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private OverlayWindow? _overlayWindow;
    private ControllerProfile _activeControllerProfile =
        BuiltInControllerProfiles.Generic;

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

        _bridgeEvents.Publish(BridgeEventKeys.AppReady);
        ConfigureCodexKeybindings();
        InitializeComposerControls();
        RefreshCodexData(preserveSelection: false);
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
        lock (_controllerStateSync)
        {
            _latestControllerState = state;
        }

        if (Interlocked.Exchange(ref _controllerDispatchPending, 1) != 0)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(ProcessLatestControllerState);
    }

    private void ProcessLatestControllerState()
    {
        ControllerState state;
        lock (_controllerStateSync)
        {
            state = _latestControllerState;
            Interlocked.Exchange(ref _controllerDispatchPending, 0);
        }

        ProcessControllerState(state);
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
        HandleButtonEdge(
            pressed,
            ControllerButtons.Start,
            onDown: WakeCodex);

        var foreground = _activeAgent.Presence.IsForeground;
        ObserveCodexForeground(foreground);
        if (
            !_settings.BridgeEnabled ||
            !_controllerSession.IsArmed ||
            (_settings.OnlyWhenCodexForeground && !foreground))
        {
            PauseControllerInput(state);
            _previousButtons = pressed;
            return;
        }

        if (!TryResumeControllerInput(state))
        {
            _previousButtons = pressed;
            return;
        }

        HandleButtonEdge(
            pressed,
            ControllerButtons.LeftThumb,
            onDown: CycleRootSidebarScope);
        HandleButtonEdge(
            pressed,
            ControllerButtons.RightThumb,
            onDown: CycleRightControlMode);
        HandleButtonEdge(
            pressed,
            ControllerButtons.DPadLeft,
            onDown: () =>
            {
                _leftStickRouter.RequireNeutral();
                NavigateSidebarHorizontal(-1);
            });
        HandleButtonEdge(
            pressed,
            ControllerButtons.DPadRight,
            onDown: () =>
            {
                _leftStickRouter.RequireNeutral();
                NavigateSidebarHorizontal(1);
            });
        HandleButtonEdge(
            pressed,
            ControllerButtons.Y,
            onDown: JumpToProjectContext);
        HandleButtonEdge(
            pressed,
            ControllerButtons.A,
            onDown: StartDictation,
            onUp: StopDictation);
        HandleButtonEdge(
            pressed,
            ControllerButtons.X,
            onDown: SendPrompt);
        HandleButtonEdge(
            pressed,
            ControllerButtons.B,
            onDown: CancelAction);
        _previousButtons = pressed;

        var deadZone = _settings.DeadZone;
        var leftGesture = _leftStickRouter.Update(
            state.LeftX,
            state.LeftY,
            deadZone,
            invertVertical: true,
            blocked:
                pressed.HasFlag(ControllerButtons.LeftThumb) ||
                Environment.TickCount64 < _leftNavigationBlockedUntil);
        var rightGesture = _rightStickRouter.Update(
            state.RightX,
            state.RightY,
            deadZone,
            invertVertical: false,
            blocked:
                pressed.HasFlag(ControllerButtons.RightThumb) ||
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
        _axisRepeater.Update(
            "right-y",
            rightGesture.VerticalDirection,
            _settings.RepeatDelayMs,
            _settings.RepeatIntervalMs,
            AdjustRightMode);
        if (rightGesture.HorizontalStarted)
        {
            SwitchRightControlMode(rightGesture.HorizontalDirection);
        }
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

    private void ObserveCodexForeground(bool foreground)
    {
        if (
            !_settings.OnlyWhenCodexForeground ||
            foreground ||
            !_controllerSession.IsArmed)
        {
            return;
        }

        // Losing foreground pauses the armed session, but does not lock it.
        // This avoids requiring Menu again after Codex has been open for a while.
        PauseControllerInput(_xInputService.LastState);
    }

    private void PauseControllerInput(
        ControllerState? state = null)
    {
        if (_controllerSession.IsActive)
        {
            CancelPendingSidebarFocus();
            CancelPendingComposerSelection();
        }

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
            _axisRepeater.Reset();
            _leftStickRouter.Reset();
            _rightStickRouter.Reset();
            if (_dictationInjected)
            {
                StopDictation();
            }
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
        if (direction < 0)
        {
            if (_scope == SidebarScope.ProjectTasks)
            {
                ExitProjectTasks();
            }
            else
            {
                ShowFeedback(
                    _localization.Strings.Format(
                        StringKeys.MessageAgentSidebar,
                        _activeAgent.DisplayName),
                    _localization.Strings.Get(
                        StringKeys.MessageAlreadyAtRootScope));
            }

            return;
        }

        EnterSelectedSidebarEntry();
    }

    private void CycleRootSidebarScope()
    {
        _leftStickRouter.RequireNeutral();
        _leftNavigationBlockedUntil =
            Environment.TickCount64 + BridgeTimings.GestureInputGuardMs;
        var rootScope = _scope == SidebarScope.ProjectTasks
            ? _sidebarReturnFrame?.Scope ?? SidebarScope.Projects
            : _scope;
        var current = Array.IndexOf(RootSidebarScopes, rootScope);
        var next = (Math.Max(0, current) + 1) % RootSidebarScopes.Length;
        SetSidebarScope(RootSidebarScopes[next], showFeedback: true);
    }

    private void SetSidebarScope(
        SidebarScope scope,
        bool showFeedback,
        string? preferredId = null)
    {
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
        _selectedIndex = 0;
        RebuildSidebarEntries();

        preferredId ??= _rootCursorIds.GetValueOrDefault(scope);
        RestoreSidebarSelection(preferredId);
        UpdateLayerTabs();
        FocusCurrentSidebarEntry();

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
        if (_sidebarEntries.Count == 0)
        {
            _selectedIndex = -1;
            DevicePage.ClearSelection();
            return;
        }

        var restored = string.IsNullOrWhiteSpace(preferredId)
            ? -1
            : _sidebarEntries
                .Select((entry, index) => new { entry.Id, index })
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Id,
                        preferredId,
                        StringComparison.OrdinalIgnoreCase))?.index ?? -1;
        _selectedIndex = restored >= 0
            ? restored
            : Math.Clamp(_selectedIndex, 0, _sidebarEntries.Count - 1);
        SelectSidebarIndex(_selectedIndex);

        var selected = _sidebarEntries[_selectedIndex];
        if (selected.Layer == SidebarLayer.Projects)
        {
            _selectedProjectPath = selected.ProjectPath;
        }
    }

    private void FocusCurrentSidebarEntry()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _sidebarEntries.Count)
        {
            FocusCodexSidebarEntry(_sidebarEntries[_selectedIndex]);
        }
    }

    private void EnterSelectedSidebarEntry()
    {
        if (DevicePage.SelectedEntry is not { } entry)
        {
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageAgentSidebar,
                    _activeAgent.DisplayName),
                _localization.Strings.Get(
                    StringKeys.MessageNoAvailableEntries));
            return;
        }

        if (entry.Layer == SidebarLayer.Projects)
        {
            EnterProjectTasks(entry.ProjectPath, preferredThreadId: null);
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.ThreadId))
        {
            return;
        }

        OpenThreadNow(
            entry.ThreadId,
            entry.Title,
            entry.NativeTitle ?? entry.Title);
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
        RebuildSidebarEntries();

        preferredThreadId ??=
            _projectTaskCursorIds.GetValueOrDefault(project.Path);
        RestoreSidebarSelection(preferredThreadId);
        UpdateLayerTabs();
        FocusCurrentSidebarEntry();

        var position = _selectedIndex >= 0
            ? $"{_selectedIndex + 1} / {_sidebarEntries.Count}"
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

        var nextIndex = Math.Clamp(
            _selectedIndex + Math.Sign(direction),
            0,
            _sidebarEntries.Count - 1);
        if (nextIndex == _selectedIndex)
        {
            return;
        }

        _selectedIndex = nextIndex;
        SelectSidebarIndex(_selectedIndex);
        ActivateSelectedEntry(_sidebarEntries[_selectedIndex]);
    }

    private void SelectSidebarIndex(int index)
    {
        _suppressSelectionActivation = true;
        DevicePage.SelectSidebarIndex(index);
        _suppressSelectionActivation = false;
    }

    private void ActivateSelectedEntry(SidebarEntry entry)
    {
        RememberCurrentSidebarCursor();
        FocusCodexSidebarEntry(entry);
        if (entry.Layer == SidebarLayer.Projects)
        {
            _selectedProjectPath = entry.ProjectPath;
            _devicePageViewModel.UpdateSidebarContextText(entry.Title);
        }

        RememberCurrentSidebarCursor();
        AddEvent($"{ScopeLabel(_scope)} · {entry.Title}");
        ShowFeedback(ScopeLabel(_scope), entry.Title);
        Pulse();
    }

    private void FocusCodexSidebarEntry(SidebarEntry entry)
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
            cancellation);
    }

    private async Task FocusCodexSidebarEntryAsync(
        SidebarEntry entry,
        string? projectName,
        ProjectDisclosureLease? disclosureLease,
        CancellationTokenSource cancellation)
    {
        var gateEntered = false;
        try
        {
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

    private void CycleRightControlMode()
    {
        _rightStickRouter.RequireNeutral();
        SwitchRightControlMode(1, source: "R3");
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

    private void StartDictation()
    {
        _dictationStopCancellation?.Cancel();
        _dictationStopCancellation = null;
        DevicePage.SetVoiceHalo(active: true);
        var automation = _composerAutomation.InvokeAction(
            _settings,
            "Dictate",
            "Start dictation");
        _dictationInjected =
            automation.Succeeded ||
            _agentShortcuts.Execute(
                _settings.DictationShortcut,
                _settings);
        if (_dictationInjected)
        {
            ClearNavigationUndo();
        }
        var voiceGlyph = Glyph(LogicalInput.FaceSouth);
        AddEvent(
            _localization.Strings.Format(
                StringKeys.MessageStartDictation,
                voiceGlyph) +
            ExecutionSuffix(
                _dictationInjected,
                automation.Error,
                automation.ErrorDetail));
        ShowFeedback(
            _localization.Strings.ControlHoldToTalk(voiceGlyph),
            _dictationInjected
                ? _localization.Strings.Format(
                    StringKeys.MessageRecordingReleaseToStop,
                    voiceGlyph)
                : ExecutionFailureLabel(
                    automation.Error));
        Pulse();
    }

    private void StopDictation()
    {
        DevicePage.SetVoiceHalo(active: false);
        var shouldStop = _dictationInjected;
        _dictationInjected = false;
        var voiceGlyph = Glyph(LogicalInput.FaceSouth);
        if (!shouldStop)
        {
            AddEvent(_localization.Strings.Format(
                StringKeys.MessageReleaseNoRecording,
                voiceGlyph));
            ShowFeedback(
                _localization.Strings.ControlHoldToTalk(voiceGlyph),
                _localization.Strings.Get(
                    StringKeys.MessageNoActiveRecording));
            return;
        }

        _dictationStopCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _dictationStopCancellation = cancellation;
        AddEvent(_localization.Strings.Format(
            StringKeys.MessageReleaseEndingDictation,
            voiceGlyph));
        ShowFeedback(
            _localization.Strings.ControlHoldToTalk(voiceGlyph),
            _localization.Strings.Get(
                StringKeys.MessageReleaseEndingRecording));
        _ = StopDictationAfterReleaseAsync(cancellation);
    }

    private async Task StopDictationAfterReleaseAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            var automation = await _composerAutomation
                .InvokeActionAsync(
                    _settings,
                    timeoutMs: 1200,
                    cancellation.Token,
                    "Stop dictation",
                    "Stop recording",
                    "Stop listening")
                .ConfigureAwait(true);
            var executed =
                automation.Succeeded ||
                _agentShortcuts.Execute(
                    _settings.DictationShortcut,
                    _settings);
            var voiceGlyph = Glyph(LogicalInput.FaceSouth);
            AddEvent(
                _localization.Strings.Format(
                    StringKeys.MessageReleaseEndDictation,
                    voiceGlyph) +
                ExecutionSuffix(
                    executed,
                    automation.Error,
                    automation.ErrorDetail));
            ShowFeedback(
                _localization.Strings.ControlHoldToTalk(voiceGlyph),
                executed
                    ? _localization.Strings.Get(
                        StringKeys.MessageRecordingEnded)
                    : ExecutionFailureLabel(
                        automation.Error));
        }
        catch (OperationCanceledException)
        {
            // A new dictation session replaced this stop request.
        }
        finally
        {
            if (ReferenceEquals(_dictationStopCancellation, cancellation))
            {
                _dictationStopCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void SendPrompt()
    {
        var automation = _composerAutomation.Submit(_settings);
        if (automation.Succeeded)
        {
            ClearNavigationUndo();
        }

        var sendGlyph = Glyph(LogicalInput.FaceWest);
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
        if (_dictationInjected)
        {
            CancelPendingSidebarFocus();
            CancelPendingComposerSelection();
            _dictationInjected = false;
            DevicePage.SetVoiceHalo(active: false);
            _dictationStopCancellation?.Cancel();
            _dictationStopCancellation = null;
            var stop = _composerAutomation.InvokeAction(
                _settings,
                "Stop dictation",
                "Stop recording",
                "Stop listening");
            var stopped =
                stop.Succeeded ||
                _agentShortcuts.Execute(
                    _settings.DictationShortcut,
                    _settings);
            ClearNavigationUndo();
            var cancelGlyph = Glyph(LogicalInput.FaceEast);
            AddEvent(
                _localization.Strings.Format(
                    StringKeys.MessageAbortDictation,
                    cancelGlyph) +
                ExecutionSuffix(
                    stopped,
                    stop.Error,
                    stop.ErrorDetail));
            ShowFeedback(
                _localization.Strings.Format(
                    StringKeys.MessageButtonCancel,
                    cancelGlyph),
                stopped
                    ? _localization.Strings.Get(
                        StringKeys.MessageDictationStopped)
                    : ExecutionFailureLabel(
                        stop.Error));
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
            $"v0.3 · {strings.StatusLocalBridge}";

        UpdateSelectedScopeText();
        UpdateRightModeUi();
        UpdateCodexStatus();
        UpdateControllerVisual(_xInputService.LastState);
        if (_feedbackPresenter.Footer is null)
        {
            FooterStatusText.Text = ControllerHelpText();
        }

        RebuildTrayMenu();
    }

    private async void RefreshCodexData(bool preserveSelection)
    {
        if (!await _dataRefreshGate.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(_workspaceReader.LoadSnapshot)
                .ConfigureAwait(true);
            var selectedId =
                preserveSelection &&
                DevicePage.SelectedEntry is { } selected
                    ? selected.Id
                    : null;

            _snapshot = snapshot;
            if (string.IsNullOrWhiteSpace(_selectedProjectPath))
            {
                _selectedProjectPath =
                    _snapshot.Projects.FirstOrDefault()?.Path;
            }

            RebuildSidebarEntries();
            if (selectedId is not null)
            {
                var index = _sidebarEntries
                    .Select((entry, index) => new { entry.Id, index })
                    .FirstOrDefault(item =>
                        item.Id.Equals(
                            selectedId,
                            StringComparison.OrdinalIgnoreCase))?.index;
                if (index is not null)
                {
                    _selectedIndex = index.Value;
                    SelectSidebarIndex(_selectedIndex);
                }
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

    private void RebuildSidebarEntries()
    {
        var entries = _workspaceReader.BuildEntries(
                _snapshot,
                _scope,
                _selectedProjectPath)
            .ToList();
        if (
            _scope == SidebarScope.ProjectTasks &&
            _projectTasksPinnedOnly)
        {
            entries = entries
                .Where(entry => entry.IsPinned)
                .ToList();
        }

        if (_scope == SidebarScope.ProjectlessTasks)
        {
            var visibleCount = _sidebarAutomation.TryGetBottomTaskCount();
            if (visibleCount is > 0 && entries.Count > visibleCount.Value)
            {
                entries = entries.Take(visibleCount.Value).ToList();
            }
        }

        _sidebarEntries.Clear();
        foreach (var entry in entries)
        {
            _sidebarEntries.Add(entry);
        }

        if (_sidebarEntries.Count == 0)
        {
            _selectedIndex = -1;
            DevicePage.ClearSelection();
        }
        else
        {
            _selectedIndex = Math.Clamp(
                _selectedIndex,
                0,
                _sidebarEntries.Count - 1);
            SelectSidebarIndex(_selectedIndex);
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

        _overlayWindow?.ShowMessage(title, value);
    }

    private void PresentBridgeOverlay(BridgeOverlayRequest request)
    {
        if (!_settings.ShowOverlay)
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
        AddEvent(eventText);
        FooterStatusText.Text = eventText;
    }

    private void UpdateCodexStatus()
    {
        var foreground = _activeAgent.Presence.IsForeground;
        ObserveCodexForeground(foreground);
        if (
            _controllerSession.IsArmed &&
            _settings.OnlyWhenCodexForeground &&
            !foreground)
        {
            PauseControllerInput(_xInputService.LastState);
        }

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

    private string ModeLabel(RightControlMode mode)
    {
        return mode switch
        {
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
        RefreshCodexData(preserveSelection: true);
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

        _selectedIndex = DevicePage.SelectedIndex;
        ActivateSelectedEntry(entry);
    }

    private void SidebarList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        EnterSelectedSidebarEntry();
        e.Handled = true;
    }

    private void SidebarList_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Right)
        {
            EnterSelectedSidebarEntry();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            NavigateSidebarHorizontal(-1);
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
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();
            AddEvent(_localization.Strings.Get(
                StringKeys.MessageWindowHiddenBackground));
            return;
        }

        _statusTimer.Stop();
        _dataTimer.Stop();
        CancelPendingSidebarFocus();
        CancelPendingComposerSelection();
        _dictationStopCancellation?.Cancel();
        _dictationStopCancellation = null;
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
