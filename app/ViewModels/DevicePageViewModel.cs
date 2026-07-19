using System.Collections.ObjectModel;
using System.Windows.Input;
using CodexController.Controllers;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation.Feedback;

namespace CodexController.ViewModels;

/// <summary>
/// Bindable presentation state for the device dashboard. Controller polling,
/// WPF animation, Agent automation, and UI-thread dispatch remain owned by the
/// view and application coordinator.
/// </summary>
public sealed class DevicePageViewModel : ObservableObject
{
    private LocalizedStrings? _strings;
    private ControllerProfile _controllerProfile =
        BuiltInControllerProfiles.Generic;
    private ControllerState _controllerState =
        ControllerState.Disconnected;
    private string _agentName = string.Empty;
    private string _controllerStatusText = string.Empty;
    private string _controllerLiveBadge = string.Empty;
    private string _leftStickHint = string.Empty;
    private string _rightStickHint = string.Empty;
    private string _rightPressGlyph = string.Empty;
    private bool _isVirtualDialMenuOpen;
    private bool _isVirtualDialConfirmationPending;
    private string _primaryGlyph = string.Empty;
    private string _primaryActionTitle = string.Empty;
    private string _voiceGlyph = string.Empty;
    private string _voiceActionTitle = string.Empty;
    private string _sendGlyph = string.Empty;
    private string _sendActionTitle = string.Empty;
    private string _cancelGlyph = string.Empty;
    private string _cancelActionTitle = string.Empty;
    private string _projectGlyph = string.Empty;
    private string _projectActionTitle = string.Empty;
    private string _wakeGlyph = string.Empty;
    private string _wakeActionTitle = string.Empty;
    private string _wakeActionDescription = string.Empty;
    private string _leftTriggerGlyph = string.Empty;
    private string _leftShoulderGlyph = string.Empty;
    private string _rightShoulderGlyph = string.Empty;
    private string _rightTriggerGlyph = string.Empty;
    private string _sidebarTitle = string.Empty;
    private string _sidebarContextText = string.Empty;
    private string _agentStatusText = string.Empty;
    private bool _isAgentStatusActive;
    private RightControlMode _rightMode =
        RightControlMode.Dial;
    private string _rightModeLabel = string.Empty;
    private string _rightModeValue = string.Empty;
    private string _rightModeSourceValue = string.Empty;
    private bool _usesConnectionAwareRightModePrompt = true;
    private SidebarScope _sidebarScope = SidebarScope.Projects;
    private SidebarScope _activeRootScope = SidebarScope.Projects;
    private string? _selectedProjectName;
    private bool _projectTasksPinnedOnly;
    private bool _isProjectDirectory;
    private string _sidebarProjectName = string.Empty;
    private string _sidebarProjectFilterText = string.Empty;

    public DevicePageViewModel(
        ObservableCollection<SidebarEntry> sidebarEntries,
        ReadOnlyObservableCollection<BridgeFeedbackLogRow> recentEvents,
        Action refresh,
        Action<SidebarScope> selectRootScope)
    {
        ArgumentNullException.ThrowIfNull(sidebarEntries);
        ArgumentNullException.ThrowIfNull(recentEvents);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(selectRootScope);

        SidebarEntries = sidebarEntries;
        RecentEvents = recentEvents;
        RefreshCommand = new RelayCommand(refresh);
        SelectPinnedTasksCommand = new RelayCommand(
            () => selectRootScope(SidebarScope.PinnedTasks));
        SelectPinnedProjectsCommand = new RelayCommand(
            () => selectRootScope(SidebarScope.PinnedProjects));
        SelectProjectsCommand = new RelayCommand(
            () => selectRootScope(SidebarScope.Projects));
        SelectProjectlessTasksCommand = new RelayCommand(
            () => selectRootScope(SidebarScope.ProjectlessTasks));
    }

    public ObservableCollection<SidebarEntry> SidebarEntries { get; }

    public ReadOnlyObservableCollection<BridgeFeedbackLogRow>
        RecentEvents
    { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SelectPinnedTasksCommand { get; }

    public ICommand SelectPinnedProjectsCommand { get; }

    public ICommand SelectProjectsCommand { get; }

    public ICommand SelectProjectlessTasksCommand { get; }

    public string AgentName
    {
        get => _agentName;
        private set => SetProperty(ref _agentName, value);
    }

    public string ControllerProfileId => _controllerProfile.Id;

    public string ControllerDisplayName =>
        _controllerProfile.DisplayName;

    public ControllerVisual ControllerVisual =>
        _controllerProfile.Visual;

    public bool IsControllerConnected =>
        _controllerState.IsConnected;

    public string ControllerStatusText
    {
        get => _controllerStatusText;
        private set => SetProperty(ref _controllerStatusText, value);
    }

    public string ControllerLiveBadge
    {
        get => _controllerLiveBadge;
        private set => SetProperty(ref _controllerLiveBadge, value);
    }

    public string LeftStickHint
    {
        get => _leftStickHint;
        private set => SetProperty(ref _leftStickHint, value);
    }

    public string RightStickHint
    {
        get => _rightStickHint;
        private set => SetProperty(ref _rightStickHint, value);
    }

    public string PrimaryGlyph
    {
        get => _primaryGlyph;
        private set => SetProperty(ref _primaryGlyph, value);
    }

    public string PrimaryActionTitle
    {
        get => _primaryActionTitle;
        private set => SetProperty(ref _primaryActionTitle, value);
    }

    public string VoiceGlyph
    {
        get => _voiceGlyph;
        private set => SetProperty(ref _voiceGlyph, value);
    }

    public string VoiceActionTitle
    {
        get => _voiceActionTitle;
        private set => SetProperty(ref _voiceActionTitle, value);
    }

    public string SendGlyph
    {
        get => _sendGlyph;
        private set => SetProperty(ref _sendGlyph, value);
    }

    public string SendActionTitle
    {
        get => _sendActionTitle;
        private set => SetProperty(ref _sendActionTitle, value);
    }

    public string CancelGlyph
    {
        get => _cancelGlyph;
        private set => SetProperty(ref _cancelGlyph, value);
    }

    public string CancelActionTitle
    {
        get => _cancelActionTitle;
        private set => SetProperty(ref _cancelActionTitle, value);
    }

    public string ProjectGlyph
    {
        get => _projectGlyph;
        private set => SetProperty(ref _projectGlyph, value);
    }

    public string ProjectActionTitle
    {
        get => _projectActionTitle;
        private set => SetProperty(ref _projectActionTitle, value);
    }

    public string WakeGlyph
    {
        get => _wakeGlyph;
        private set => SetProperty(ref _wakeGlyph, value);
    }

    public string WakeActionTitle
    {
        get => _wakeActionTitle;
        private set => SetProperty(ref _wakeActionTitle, value);
    }

    public string WakeActionDescription
    {
        get => _wakeActionDescription;
        private set => SetProperty(
            ref _wakeActionDescription,
            value);
    }

    public string LeftTriggerGlyph
    {
        get => _leftTriggerGlyph;
        private set => SetProperty(ref _leftTriggerGlyph, value);
    }

    public string LeftShoulderGlyph
    {
        get => _leftShoulderGlyph;
        private set => SetProperty(ref _leftShoulderGlyph, value);
    }

    public string RightShoulderGlyph
    {
        get => _rightShoulderGlyph;
        private set => SetProperty(ref _rightShoulderGlyph, value);
    }

    public string RightTriggerGlyph
    {
        get => _rightTriggerGlyph;
        private set => SetProperty(ref _rightTriggerGlyph, value);
    }

    public string SidebarTitle
    {
        get => _sidebarTitle;
        private set => SetProperty(ref _sidebarTitle, value);
    }

    public string SidebarContextText
    {
        get => _sidebarContextText;
        private set => SetProperty(ref _sidebarContextText, value);
    }

    public bool IsProjectDirectory
    {
        get => _isProjectDirectory;
        private set => SetProperty(ref _isProjectDirectory, value);
    }

    public bool IsProjectTasksPinnedOnly =>
        IsProjectDirectory && _projectTasksPinnedOnly;

    public string SidebarProjectName
    {
        get => _sidebarProjectName;
        private set => SetProperty(ref _sidebarProjectName, value);
    }

    public string SidebarProjectFilterText
    {
        get => _sidebarProjectFilterText;
        private set => SetProperty(
            ref _sidebarProjectFilterText,
            value);
    }

    public string AgentStatusText
    {
        get => _agentStatusText;
        private set => SetProperty(ref _agentStatusText, value);
    }

    public bool IsAgentStatusActive
    {
        get => _isAgentStatusActive;
        private set => SetProperty(ref _isAgentStatusActive, value);
    }

    public RightControlMode RightMode
    {
        get => _rightMode;
        private set
        {
            if (!SetProperty(ref _rightMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsReasoningMode));
            OnPropertyChanged(nameof(IsModelMode));
            OnPropertyChanged(nameof(IsSpeedMode));
        }
    }

    public bool IsReasoningMode =>
        RightMode == RightControlMode.Reasoning;

    public bool IsModelMode =>
        RightMode == RightControlMode.Model;

    public bool IsSpeedMode =>
        RightMode == RightControlMode.Speed;

    public string RightModeLabel
    {
        get => _rightModeLabel;
        private set => SetProperty(ref _rightModeLabel, value);
    }

    public string RightModeValue
    {
        get => _rightModeValue;
        private set => SetProperty(ref _rightModeValue, value);
    }

    public SidebarScope CurrentSidebarScope
    {
        get => _sidebarScope;
        private set => SetProperty(ref _sidebarScope, value);
    }

    public SidebarScope ActiveRootScope
    {
        get => _activeRootScope;
        private set
        {
            if (!SetProperty(ref _activeRootScope, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsPinnedTasksRootActive));
            OnPropertyChanged(nameof(IsPinnedProjectsRootActive));
            OnPropertyChanged(nameof(IsProjectsRootActive));
            OnPropertyChanged(nameof(IsProjectlessTasksRootActive));
        }
    }

    public bool IsPinnedTasksRootActive =>
        ActiveRootScope == SidebarScope.PinnedTasks;

    public bool IsPinnedProjectsRootActive =>
        ActiveRootScope == SidebarScope.PinnedProjects;

    public bool IsProjectsRootActive =>
        ActiveRootScope == SidebarScope.Projects;

    public bool IsProjectlessTasksRootActive =>
        ActiveRootScope == SidebarScope.ProjectlessTasks;

    public void UpdateContext(
        LocalizedStrings strings,
        string agentName,
        ControllerProfile controllerProfile)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(controllerProfile);

        _strings = strings;
        _controllerProfile = controllerProfile;
        AgentName = agentName.Trim();

        OnPropertyChanged(nameof(ControllerProfileId));
        OnPropertyChanged(nameof(ControllerDisplayName));
        OnPropertyChanged(nameof(ControllerVisual));

        var leftPressGlyph = Glyph(LogicalInput.LeftStickPress);
        var rightPressGlyph = Glyph(LogicalInput.RightStickPress);
        _rightPressGlyph = rightPressGlyph;
        PrimaryGlyph = Glyph(LogicalInput.FaceSouth);
        VoiceGlyph = Glyph(LogicalInput.LeftTrigger);
        SendGlyph = Glyph(LogicalInput.FaceWest);
        CancelGlyph = Glyph(LogicalInput.FaceEast);
        ProjectGlyph = Glyph(LogicalInput.FaceNorth);
        WakeGlyph = Glyph(LogicalInput.Menu);
        LeftTriggerGlyph = Glyph(LogicalInput.LeftTrigger);
        LeftShoulderGlyph = Glyph(LogicalInput.LeftShoulder);
        RightShoulderGlyph = Glyph(LogicalInput.RightShoulder);
        RightTriggerGlyph = Glyph(LogicalInput.RightTrigger);

        LeftStickHint = strings.ControlLeftStickHint(
            leftPressGlyph,
            PrimaryGlyph);
        RefreshRightStickHint();
        PrimaryActionTitle =
            strings.ControlPrimary(PrimaryGlyph);
        VoiceActionTitle = strings.ControlHoldToTalk(VoiceGlyph);
        SendActionTitle = strings.ControlSend(SendGlyph);
        CancelActionTitle =
            strings.ControlCancelUndo(CancelGlyph);
        ProjectActionTitle =
            strings.ControlProjectContext(ProjectGlyph);
        WakeActionTitle =
            strings.ControlWakeAgent(WakeGlyph, AgentName);
        WakeActionDescription =
            strings.ControlWakeAgentDescription(AgentName);
        SidebarTitle = strings.SidebarAgent(AgentName);

        RefreshControllerPresentation();
        RefreshRightModeLabel();
        RefreshRightModeValue();
        RefreshSidebarContextText();
    }

    public void UpdateControllerState(ControllerState state)
    {
        var connectionChanged =
            _controllerState.IsConnected != state.IsConnected;
        _controllerState = state;
        if (connectionChanged)
        {
            OnPropertyChanged(nameof(IsControllerConnected));
            RefreshRightModeValue();
        }

        RefreshControllerPresentation();
    }

    public void UpdateAgentStatus(string statusText, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(statusText);
        AgentStatusText = statusText;
        IsAgentStatusActive = isActive;
    }

    public void UpdateRightMode(
        RightControlMode mode,
        string displayValue)
    {
        ArgumentNullException.ThrowIfNull(displayValue);
        RightMode = mode;
        RefreshRightModeLabel();
        _rightModeSourceValue = displayValue;
        _usesConnectionAwareRightModePrompt =
            IsConnectionAwareRightModePrompt(mode, displayValue);
        RefreshRightModeValue();
    }

    public void UpdateRightModeValue(string displayValue)
    {
        ArgumentNullException.ThrowIfNull(displayValue);
        _rightModeSourceValue = displayValue;
        _usesConnectionAwareRightModePrompt =
            IsConnectionAwareRightModePrompt(
                RightMode,
                displayValue);
        RefreshRightModeValue();
    }

    public void UpdateVirtualDialMenuState(
        bool isOpen,
        bool requiresConfirmation = false)
    {
        _isVirtualDialMenuOpen = isOpen;
        _isVirtualDialConfirmationPending =
            isOpen && requiresConfirmation;
        RefreshRightStickHint();
    }

    public void UpdateSidebarScope(
        SidebarScope scope,
        SidebarScope? activeRootScope = null,
        string? selectedProjectName = null,
        bool projectTasksPinnedOnly = false)
    {
        var resolvedRootScope = scope;
        if (scope == SidebarScope.ProjectTasks)
        {
            if (
                activeRootScope is not
                    (SidebarScope.Projects or
                     SidebarScope.PinnedProjects))
            {
                throw new ArgumentException(
                    "ProjectTasks requires Projects or PinnedProjects " +
                    "as its active root scope.",
                    nameof(activeRootScope));
            }

            resolvedRootScope = activeRootScope.Value;
        }
        else if (!IsRootScope(scope))
        {
            throw new ArgumentOutOfRangeException(
                nameof(scope),
                scope,
                "The sidebar scope is not supported.");
        }

        CurrentSidebarScope = scope;
        ActiveRootScope = resolvedRootScope;
        _selectedProjectName = selectedProjectName;
        _projectTasksPinnedOnly = projectTasksPinnedOnly;
        IsProjectDirectory = scope == SidebarScope.ProjectTasks;
        OnPropertyChanged(nameof(IsProjectTasksPinnedOnly));
        RefreshSidebarContextText();
    }

    public void UpdateSidebarContextText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        SidebarContextText = text;
    }

    private string Glyph(LogicalInput input)
    {
        return _controllerProfile.GetGlyph(input);
    }

    private void RefreshControllerPresentation()
    {
        if (_strings is null)
        {
            return;
        }

        ControllerStatusText = _controllerState.IsConnected
            ? ControllerDisplayName +
              (string.IsNullOrWhiteSpace(_controllerState.Backend)
                  ? string.Empty
                  : $" · {_controllerState.Backend}")
            : _strings.DeviceWaiting;
        ControllerLiveBadge = _controllerState.IsConnected
            ? _strings.DeviceLiveInput
            : _strings.DeviceIdle;
    }

    private void RefreshRightModeLabel()
    {
        if (_strings is null)
        {
            return;
        }

        RightModeLabel = RightMode switch
        {
            RightControlMode.Dial =>
                _strings.VirtualDial,
            RightControlMode.Reasoning =>
                _strings.ReasoningEffort,
            RightControlMode.Model => _strings.Model,
            RightControlMode.Speed => _strings.Speed,
            _ => string.Empty,
        };
    }

    private void RefreshRightStickHint()
    {
        if (_strings is null)
        {
            return;
        }

        RightStickHint = _strings.ControlRightStickHint(
            _rightPressGlyph,
            CancelGlyph,
            PrimaryGlyph,
            _isVirtualDialMenuOpen,
            _isVirtualDialConfirmationPending);
    }

    private void RefreshRightModeValue()
    {
        if (_strings is null)
        {
            return;
        }

        RightModeValue =
            RightMode == RightControlMode.Dial &&
            _usesConnectionAwareRightModePrompt
                ? _controllerState.IsConnected
                    ? _strings.ComposerDialReady
                    : _strings.ComposerConnectController
                : _rightModeSourceValue;
    }

    private bool IsConnectionAwareRightModePrompt(
        RightControlMode mode,
        string displayValue)
    {
        return mode == RightControlMode.Dial &&
               (
                   string.IsNullOrWhiteSpace(displayValue) ||
                   _strings is not null &&
                   string.Equals(
                       displayValue,
                       _strings.ComposerDialReady,
                       StringComparison.Ordinal)
               );
    }

    private void RefreshSidebarContextText()
    {
        if (_strings is null)
        {
            return;
        }

        SidebarProjectName = ProjectNameOrFallback();
        SidebarProjectFilterText = _projectTasksPinnedOnly
            ? _strings.Get(StringKeys.MessageProjectPinnedOnly)
            : _strings.Get(StringKeys.MessageAllTasks);
        SidebarContextText = CurrentSidebarScope switch
        {
            SidebarScope.PinnedTasks =>
                _strings.SidebarPinnedTasks,
            SidebarScope.PinnedProjects =>
                _strings.SidebarPinnedProjects,
            SidebarScope.Projects =>
                _strings.SidebarProjects,
            SidebarScope.ProjectlessTasks =>
                _strings.SidebarProjectlessTasks,
            SidebarScope.ProjectTasks =>
                $"{SidebarProjectName} › {SidebarProjectFilterText}",
            _ => _strings.SidebarAgent(AgentName),
        };
    }

    private string ProjectNameOrFallback()
    {
        return string.IsNullOrWhiteSpace(_selectedProjectName)
            ? _strings!.ScopeValue(
                nameof(SidebarScope.ProjectTasks))
            : _selectedProjectName.Trim();
    }

    private static bool IsRootScope(SidebarScope scope)
    {
        return scope is
            SidebarScope.PinnedTasks or
            SidebarScope.PinnedProjects or
            SidebarScope.Projects or
            SidebarScope.ProjectlessTasks;
    }
}
