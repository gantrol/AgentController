using System.Windows.Input;
using CodexController.Localization;
using CodexController.Models;

namespace CodexController.ViewModels;

public sealed class ConfigPageViewModel : ObservableObject
{
    private string _reasoningDownShortcut = "F17";
    private string _reasoningUpShortcut = "F18";
    private string _modelPickerShortcut = "Ctrl+Shift+M";
    private string _fastToggleShortcut = "F20";
    private string _dictationShortcut = "Ctrl+Shift+D";
    private string _description = string.Empty;
    private string _leftStickSidebarTitle = string.Empty;
    private string _moveFocusDescription = string.Empty;
    private string _rootProjectGlyphs = "LS / Y";
    private string _rootProjectDescription = string.Empty;
    private string _sidebarBehavior = string.Empty;
    private string _modeSwitchGlyphs = "←→ / RS";
    private string _selectionBehavior = string.Empty;
    private string _agentShortcutsTitle = string.Empty;
    private string _agentShortcutsDescription = string.Empty;
    private string _openAgentShortcutsText = string.Empty;
    private bool _canOpenAgentShortcuts = true;
    private readonly RelayCommand _openAgentShortcutsCommand;
    private readonly Action _save;
    private bool _suppressAutoSave;

    public ConfigPageViewModel(
        Action openAgentShortcuts,
        Action save)
    {
        _save = save;
        _openAgentShortcutsCommand = new RelayCommand(
            openAgentShortcuts,
            () => CanOpenAgentShortcuts);
        OpenAgentShortcutsCommand = _openAgentShortcutsCommand;
        ResetCommand = new RelayCommand(ResetToDefaults);
    }

    public ICommand OpenAgentShortcutsCommand { get; }
    public ICommand ResetCommand { get; }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string LeftStickSidebarTitle
    {
        get => _leftStickSidebarTitle;
        private set => SetProperty(ref _leftStickSidebarTitle, value);
    }

    public string MoveFocusDescription
    {
        get => _moveFocusDescription;
        private set => SetProperty(ref _moveFocusDescription, value);
    }

    public string RootProjectGlyphs
    {
        get => _rootProjectGlyphs;
        private set => SetProperty(ref _rootProjectGlyphs, value);
    }

    public string RootProjectDescription
    {
        get => _rootProjectDescription;
        private set => SetProperty(ref _rootProjectDescription, value);
    }

    public string SidebarBehavior
    {
        get => _sidebarBehavior;
        private set => SetProperty(ref _sidebarBehavior, value);
    }

    public string ModeSwitchGlyphs
    {
        get => _modeSwitchGlyphs;
        private set => SetProperty(ref _modeSwitchGlyphs, value);
    }

    public string SelectionBehavior
    {
        get => _selectionBehavior;
        private set => SetProperty(ref _selectionBehavior, value);
    }

    public string AgentShortcutsTitle
    {
        get => _agentShortcutsTitle;
        private set => SetProperty(ref _agentShortcutsTitle, value);
    }

    public string AgentShortcutsDescription
    {
        get => _agentShortcutsDescription;
        private set => SetProperty(
            ref _agentShortcutsDescription,
            value);
    }

    public string OpenAgentShortcutsText
    {
        get => _openAgentShortcutsText;
        private set => SetProperty(ref _openAgentShortcutsText, value);
    }

    public bool CanOpenAgentShortcuts
    {
        get => _canOpenAgentShortcuts;
        private set
        {
            if (SetProperty(ref _canOpenAgentShortcuts, value))
            {
                _openAgentShortcutsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ReasoningDownShortcut
    {
        get => _reasoningDownShortcut;
        set => SetShortcut(ref _reasoningDownShortcut, value);
    }

    public string ReasoningUpShortcut
    {
        get => _reasoningUpShortcut;
        set => SetShortcut(ref _reasoningUpShortcut, value);
    }

    public string ModelPickerShortcut
    {
        get => _modelPickerShortcut;
        set => SetShortcut(ref _modelPickerShortcut, value);
    }

    public string FastToggleShortcut
    {
        get => _fastToggleShortcut;
        set => SetShortcut(ref _fastToggleShortcut, value);
    }

    public string DictationShortcut
    {
        get => _dictationShortcut;
        set => SetShortcut(ref _dictationShortcut, value);
    }

    public void UpdateContext(
        LocalizedStrings strings,
        string agentName,
        string leftPressGlyph,
        string projectGlyph,
        string rightPressGlyph,
        bool canOpenAgentShortcuts = true)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        Description = strings.ConfigDescription(agentName);
        LeftStickSidebarTitle =
            strings.ConfigLeftStickSidebar(agentName);
        MoveFocusDescription =
            strings.ConfigMoveFocusDescription(agentName);
        RootProjectGlyphs = strings.ConfigRootProjectGlyphs(
            leftPressGlyph,
            projectGlyph);
        RootProjectDescription =
            strings.ConfigRootProjectDescription(
                leftPressGlyph,
                projectGlyph);
        SidebarBehavior =
            strings.ConfigSidebarBehavior(agentName);
        ModeSwitchGlyphs = strings.ConfigModeSwitchGlyphs(
            rightPressGlyph,
            $"500 ms {rightPressGlyph}");
        SelectionBehavior =
            strings.ConfigSelectionBehavior(agentName);
        AgentShortcutsTitle =
            strings.ConfigAgentShortcuts(agentName);
        AgentShortcutsDescription =
            strings.ConfigAgentShortcutsDescription(agentName);
        OpenAgentShortcutsText =
            strings.ConfigOpenAgentShortcuts(agentName);
        CanOpenAgentShortcuts = canOpenAgentShortcuts;
    }

    public void Load(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _suppressAutoSave = true;
        try
        {
            ReasoningDownShortcut = settings.ReasoningDownShortcut;
            ReasoningUpShortcut = settings.ReasoningUpShortcut;
            ModelPickerShortcut = settings.ModelPickerShortcut;
            FastToggleShortcut = settings.FastToggleShortcut;
            DictationShortcut = settings.DictationShortcut;
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    public void ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.ReasoningDownShortcut = Normalize(
            ReasoningDownShortcut,
            "F17");
        settings.ReasoningUpShortcut = Normalize(
            ReasoningUpShortcut,
            "F18");
        settings.ModelPickerShortcut = Normalize(
            ModelPickerShortcut,
            "Ctrl+Shift+M");
        settings.FastToggleShortcut = Normalize(
            FastToggleShortcut,
            "F20");
        settings.DictationShortcut = Normalize(
            DictationShortcut,
            "Ctrl+Shift+D");
    }

    public void ResetToDefaults()
    {
        Load(new AppSettings());
        _save();
    }

    private void SetShortcut(ref string field, string value)
    {
        if (!SetProperty(ref field, value) || _suppressAutoSave)
        {
            return;
        }

        _save();
    }

    private static string Normalize(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : value.Trim();
    }
}
