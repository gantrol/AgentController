using System.Collections.ObjectModel;
using System.Windows.Input;
using CodexController.Controllers;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation.Dispatch;
using CodexController.Presentation.Feedback;

namespace CodexController.ViewModels;

public enum VoicePresentationState
{
    Idle,
    Starting,
    Recording,
    Stopping,
    StartFailed,
    StopFailed,
}

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
    private string _rightModeStatusText = string.Empty;
    private bool _isRightModeError;
    private VoicePresentationState _voiceState;
    private SidebarScope _sidebarScope = SidebarScope.Projects;
    private SidebarScope _activeRootScope = SidebarScope.Projects;
    private string? _selectedProjectName;
    private bool _projectTasksPinnedOnly;
    private bool _isProjectDirectory;
    private string _sidebarProjectName = string.Empty;
    private string _sidebarProjectFilterText = string.Empty;
    private string _fullResetExpirationText = string.Empty;
    private string _fullResetExpirationToolTip = string.Empty;
    private IReadOnlyList<SidebarSectionTab> _sidebarSections = [];

    public DevicePageViewModel(
        ObservableCollection<SidebarEntry> sidebarEntries,
        ReadOnlyObservableCollection<BridgeFeedbackLogRow> recentEvents,
        Action refresh,
        Action<SidebarScope> selectRootScope,
        Action? refreshTutorialDispatch = null)
    {
        ArgumentNullException.ThrowIfNull(sidebarEntries);
        ArgumentNullException.ThrowIfNull(recentEvents);
        ArgumentNullException.ThrowIfNull(refresh);
        ArgumentNullException.ThrowIfNull(selectRootScope);

        SidebarEntries = sidebarEntries;
        RecentEvents = recentEvents;
        Tutorial = new ControllerTutorialViewModel(
            refreshTutorialDispatch);
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

    public ControllerTutorialViewModel Tutorial { get; }

    public ICommand RefreshCommand { get; }

    public ICommand SelectPinnedTasksCommand { get; }

    public ICommand SelectPinnedProjectsCommand { get; }

    public ICommand SelectProjectsCommand { get; }

    public ICommand SelectProjectlessTasksCommand { get; }

    public IReadOnlyList<SidebarSectionTab> SidebarSections
    {
        get => _sidebarSections;
        private set => SetProperty(ref _sidebarSections, value);
    }

    public string FullResetExpirationText
    {
        get => _fullResetExpirationText;
        private set => SetProperty(
            ref _fullResetExpirationText,
            value);
    }

    public string FullResetExpirationToolTip
    {
        get => _fullResetExpirationToolTip;
        private set => SetProperty(
            ref _fullResetExpirationToolTip,
            value);
    }

    public void UpdateFullResetExpiration(
        string text,
        string toolTip)
    {
        FullResetExpirationText = text;
        FullResetExpirationToolTip = toolTip;
    }

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

    public VoicePresentationState VoiceState => _voiceState;

    public bool HasVoiceError => VoiceState is
        VoicePresentationState.StartFailed or VoicePresentationState.StopFailed;

    public bool IsVoiceRecording => VoiceState == VoicePresentationState.Recording;

    public bool HasVoiceFeedback => VoiceState != VoicePresentationState.Idle;

    public void UpdateVoiceState(VoicePresentationState state)
    {
        if (!SetProperty(ref _voiceState, state, nameof(VoiceState))) return;
        OnPropertyChanged(nameof(HasVoiceError));
        OnPropertyChanged(nameof(IsVoiceRecording));
        OnPropertyChanged(nameof(HasVoiceFeedback));
        RefreshVoiceActionTitle();
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

    public bool HasRightModeValue => !_usesConnectionAwareRightModePrompt &&
        !string.IsNullOrWhiteSpace(_rightModeSourceValue);

    public bool HasComposerSelection => _isVirtualDialMenuOpen && HasRightModeValue;

    public bool HasComposerFeedback => _isVirtualDialMenuOpen ||
        !string.IsNullOrEmpty(_rightModeStatusText);

    public string RightModeStatusText
    {
        get
        {
            if (!string.IsNullOrEmpty(_rightModeStatusText)) return _rightModeStatusText;
            if (!_isVirtualDialMenuOpen || _strings is null) return string.Empty;
            return _strings.Get(_isVirtualDialConfirmationPending
                ? StringKeys.ControlPendingConfirmation
                : StringKeys.ComposerMenuOpen);
        }
    }

    public bool IsRightModeWarning => _isRightModeError || _isVirtualDialConfirmationPending;

    public void UpdateRightModeStatus(string text, bool isError = false)
    {
        if (_rightModeStatusText == text && _isRightModeError == isError) return;
        _rightModeStatusText = text;
        _isRightModeError = isError;
        RefreshRightModeStatus();
    }

    public void InvalidateRightModeValue()
    {
        if (_usesConnectionAwareRightModePrompt && _rightModeSourceValue.Length == 0) return;
        _rightModeSourceValue = string.Empty;
        _usesConnectionAwareRightModePrompt = true;
        RefreshRightModeValue();
    }

    private void RefreshRightModeStatus()
    {
        OnPropertyChanged(nameof(RightModeStatusText));
        OnPropertyChanged(nameof(IsRightModeWarning));
        OnPropertyChanged(nameof(HasComposerSelection));
        OnPropertyChanged(nameof(HasComposerFeedback));
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
        PrimaryActionTitle = CompactActionTitle(
            strings.ControlPrimary(PrimaryGlyph));
        RefreshVoiceActionTitle();
        SendActionTitle = CompactActionTitle(
            strings.ControlSend(SendGlyph));
        CancelActionTitle =
            CompactActionTitle(strings.ControlCancelUndo(CancelGlyph));
        ProjectActionTitle =
            CompactActionTitle(
                strings.ControlProjectContext(ProjectGlyph));
        WakeActionTitle =
            CompactActionTitle(
                strings.ControlWakeAgent(WakeGlyph, AgentName));
        WakeActionDescription =
            strings.ControlWakeAgentDescription(AgentName);
        SidebarTitle = strings.SidebarAgent(AgentName);

        Tutorial.UpdateContext(
            strings,
            controllerProfile,
            LeftStickHint,
            RightStickHint);

        RefreshControllerPresentation();
        RefreshRightModeLabel();
        RefreshRightModeValue();
        RefreshSidebarContextText();
    }

    private static string CompactActionTitle(string title)
    {
        var separator = title.IndexOf('·');
        var compact = separator < 0
            ? title.Trim()
            : title[(separator + 1)..].Trim();
        var detailSeparator = compact.IndexOf('·');
        return detailSeparator < 0
            ? compact
            : compact[..detailSeparator].Trim();
    }

    private void RefreshVoiceActionTitle()
    {
        if (_strings is null) return;
        VoiceActionTitle = VoiceState == VoicePresentationState.Idle
            ? CompactActionTitle(_strings.ControlHoldToTalk(VoiceGlyph))
            : _strings.Get(VoiceState switch
            {
                VoicePresentationState.Starting => StringKeys.ControlVoiceStarting,
                VoicePresentationState.Recording => StringKeys.ControlVoiceRecording,
                VoicePresentationState.Stopping => StringKeys.ControlVoiceStopping,
                VoicePresentationState.StartFailed => StringKeys.ControlVoiceStartFailed,
                VoicePresentationState.StopFailed => StringKeys.ControlVoiceStopFailed,
                _ => throw new ArgumentOutOfRangeException(),
            });
    }

    public void UpdateControllerState(ControllerState state)
    {
        Tutorial.ObserveStickPresses(state);
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

    internal void UpdateTutorialLayer(
        RadialMenuLayerKind? layer,
        bool isEngaged,
        bool isCancelled) =>
        Tutorial.UpdateActiveLayer(layer, isEngaged, isCancelled);

    internal void UpdateTutorialDispatch(DispatchDisplay display) =>
        Tutorial.UpdateDispatchPresentation(display);

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
        UpdateRightModeStatus(string.Empty);
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
        UpdateRightModeStatus(string.Empty);
        RefreshRightModeValue();
    }

    public void UpdateVirtualDialMenuState(
        bool isOpen,
        bool requiresConfirmation = false)
    {
        _isVirtualDialMenuOpen = isOpen;
        _isVirtualDialConfirmationPending =
            isOpen && requiresConfirmation;
        RefreshRightModeStatus();
        RefreshRightStickHint();
        if (_strings is not null)
        {
            Tutorial.UpdateContext(
                _strings,
                _controllerProfile,
                LeftStickHint,
                RightStickHint);
        }
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

    public void UpdateSidebarSections(
        IReadOnlyList<SidebarEntry> roots,
        CodexSidebarLayout? layout,
        string? selectedId)
    {
        if (_strings is null) return;

        var selected = roots.FirstOrDefault(entry => entry.Id.Equals(
            selectedId, StringComparison.OrdinalIgnoreCase));
        var groups = roots.GroupBy(entry => entry.SectionKey)
            .ToDictionary(group => group.Key, group => group.First());
        var names = new Dictionary<string, string>
        {
            ["pinned"] = _strings.SidebarPinnedBadge,
            ["threads"] = _strings.SidebarProjects,
            ["chats"] = _strings.SidebarProjectlessTasks,
        };
        if (layout is not null)
            foreach (var section in layout.Sections)
                names[section.Id] = section.Name;

        var order = layout?.OrderedSectionIds ?? groups.Keys.ToArray();
        var sections = order.Select(id =>
        {
            var entry = selected?.SectionKey == id ? selected : groups.GetValueOrDefault(id);
            return new SidebarSectionTab(
                id,
                names.GetValueOrDefault(id) ?? _strings.ScopeValue(entry!.NavigationScope.ToString()),
                entry?.NavigationScope ?? SidebarScope.ProjectlessTasks,
                entry?.Id,
                selected?.SectionKey == id);
        }).ToArray();
        if (!SidebarSections.SequenceEqual(sections)) SidebarSections = sections;
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
        OnPropertyChanged(nameof(HasRightModeValue));
        RefreshRightModeStatus();
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
