using System.Windows.Input;
using CodexController.Localization;
using CodexController.Models;

namespace CodexController.ViewModels;

public sealed class SettingsPageViewModel : ObservableObject
{
    private bool _onlyWhenAgentForeground = true;
    private bool _hapticFeedback = true;
    private bool _showOverlay = true;
    private string _radialMenuMode = RadialMenuModes.Learning;
    private IReadOnlyList<LocalizedRadialMenuOption>
        _radialMenuOptions = [];
    private string _composerDialMode = ComposerDialModes.Simple;
    private IReadOnlyList<LocalizedComposerDialModeOption>
        _composerDialModeOptions = [];
    private bool _startWithWindows;
    private bool _minimizeToTray = true;
    private double _deadZone = 0.58;
    private int _repeatDelayMs = 360;
    private int _repeatIntervalMs = 220;
    private string _languageSetting = "auto";
    private string _onlyForegroundText = string.Empty;
    private string _onlyForegroundDescription = string.Empty;
    private string _openVendorToolText = string.Empty;
    private string _openAgentSettingsText = string.Empty;
    private bool _canOpenVendorTool = true;
    private bool _canOpenAgentSettings = true;
    private readonly Action<string> _changeLanguage;
    private readonly RelayCommand _openVendorToolCommand;
    private readonly RelayCommand _openAgentSettingsCommand;

    public SettingsPageViewModel(
        Action openVendorTool,
        Action openAgentSettings,
        Action save,
        Action<string> changeLanguage)
    {
        ArgumentNullException.ThrowIfNull(changeLanguage);
        _changeLanguage = changeLanguage;
        _openVendorToolCommand = new RelayCommand(
            openVendorTool,
            () => CanOpenVendorTool);
        _openAgentSettingsCommand = new RelayCommand(
            openAgentSettings,
            () => CanOpenAgentSettings);
        OpenVendorToolCommand = _openVendorToolCommand;
        OpenAgentSettingsCommand = _openAgentSettingsCommand;
        SaveCommand = new RelayCommand(save);
    }

    public ICommand OpenVendorToolCommand { get; }
    public ICommand OpenAgentSettingsCommand { get; }
    public ICommand SaveCommand { get; }

    public string OnlyForegroundText
    {
        get => _onlyForegroundText;
        private set => SetProperty(ref _onlyForegroundText, value);
    }

    public string OnlyForegroundDescription
    {
        get => _onlyForegroundDescription;
        private set => SetProperty(
            ref _onlyForegroundDescription,
            value);
    }

    public string OpenVendorToolText
    {
        get => _openVendorToolText;
        private set => SetProperty(ref _openVendorToolText, value);
    }

    public string OpenAgentSettingsText
    {
        get => _openAgentSettingsText;
        private set => SetProperty(
            ref _openAgentSettingsText,
            value);
    }

    public bool CanOpenVendorTool
    {
        get => _canOpenVendorTool;
        private set
        {
            if (SetProperty(ref _canOpenVendorTool, value))
            {
                _openVendorToolCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanOpenAgentSettings
    {
        get => _canOpenAgentSettings;
        private set
        {
            if (SetProperty(ref _canOpenAgentSettings, value))
            {
                _openAgentSettingsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool OnlyWhenAgentForeground
    {
        get => _onlyWhenAgentForeground;
        set => SetProperty(ref _onlyWhenAgentForeground, value);
    }

    public bool HapticFeedback
    {
        get => _hapticFeedback;
        set => SetProperty(ref _hapticFeedback, value);
    }

    public bool ShowOverlay
    {
        get => _showOverlay;
        set => SetProperty(ref _showOverlay, value);
    }

    public string RadialMenuMode
    {
        get => _radialMenuMode;
        set => SetProperty(
            ref _radialMenuMode,
            RadialMenuModes.Normalize(value));
    }

    public IReadOnlyList<LocalizedRadialMenuOption>
        RadialMenuOptions
    {
        get => _radialMenuOptions;
        private set => SetProperty(
            ref _radialMenuOptions,
            value);
    }

    public string ComposerDialMode
    {
        get => _composerDialMode;
        set => SetProperty(
            ref _composerDialMode,
            ComposerDialModes.Normalize(value));
    }

    public IReadOnlyList<LocalizedComposerDialModeOption>
        ComposerDialModeOptions
    {
        get => _composerDialModeOptions;
        private set => SetProperty(
            ref _composerDialModeOptions,
            value);
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set => SetProperty(ref _startWithWindows, value);
    }

    public bool MinimizeToTray
    {
        get => _minimizeToTray;
        set => SetProperty(ref _minimizeToTray, value);
    }

    public double DeadZone
    {
        get => _deadZone;
        set
        {
            if (SetProperty(ref _deadZone, Math.Round(value, 2)))
            {
                OnPropertyChanged(nameof(DeadZoneDisplay));
            }
        }
    }

    public int RepeatDelayMs
    {
        get => _repeatDelayMs;
        set
        {
            if (SetProperty(ref _repeatDelayMs, value))
            {
                OnPropertyChanged(nameof(RepeatDelayDisplay));
            }
        }
    }

    public int RepeatIntervalMs
    {
        get => _repeatIntervalMs;
        set
        {
            if (SetProperty(ref _repeatIntervalMs, value))
            {
                OnPropertyChanged(nameof(RepeatIntervalDisplay));
            }
        }
    }

    public string DeadZoneDisplay => DeadZone.ToString("0.00");
    public string RepeatDelayDisplay => $"{RepeatDelayMs} ms";
    public string RepeatIntervalDisplay => $"{RepeatIntervalMs} ms";

    public string LanguageSetting
    {
        get => _languageSetting;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? "auto"
                : value;
            if (SetProperty(ref _languageSetting, normalized))
            {
                _changeLanguage(normalized);
            }
        }
    }

    public void UpdateContext(
        LocalizedStrings strings,
        string agentName,
        string wakeGlyph,
        string? controllerSoftwareName,
        bool canOpenVendorTool = true,
        bool canOpenAgentSettings = true)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        OnlyForegroundText =
            strings.SettingsOnlyForeground(agentName);
        OnlyForegroundDescription =
            strings.SettingsOnlyForegroundDescription(
                wakeGlyph,
                agentName);
        OpenVendorToolText =
            string.IsNullOrWhiteSpace(controllerSoftwareName)
                ? strings.Get(
                    StringKeys.SettingsOpenControllerSoftwareGeneric)
                : strings.SettingsOpenControllerSoftware(
                    controllerSoftwareName);
        OpenAgentSettingsText =
            strings.SettingsOpenAgent(agentName);
        RadialMenuOptions =
        [
            new(
                RadialMenuModes.Always,
                strings.SettingsRadialMenuAlways),
            new(
                RadialMenuModes.Learning,
                strings.SettingsRadialMenuLearning),
            new(
                RadialMenuModes.Off,
                strings.SettingsRadialMenuOff),
        ];
        ComposerDialModeOptions =
        [
            new(
                ComposerDialModes.Simple,
                strings.SettingsComposerDialModeSimple),
            new(
                ComposerDialModes.Advanced,
                strings.SettingsComposerDialModeAdvanced),
        ];
        CanOpenVendorTool = canOpenVendorTool;
        CanOpenAgentSettings = canOpenAgentSettings;
    }

    public void Load(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        OnlyWhenAgentForeground = settings.OnlyWhenCodexForeground;
        HapticFeedback = settings.HapticFeedback;
        ShowOverlay = settings.ShowOverlay;
        RadialMenuMode = settings.RadialMenuMode;
        ComposerDialMode = settings.ComposerDialMode;
        StartWithWindows = settings.StartWithWindows;
        MinimizeToTray = settings.MinimizeToTray;
        DeadZone = settings.DeadZone;
        RepeatDelayMs = settings.RepeatDelayMs;
        RepeatIntervalMs = settings.RepeatIntervalMs;
        LanguageSetting = settings.Language;
    }

    public void ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.OnlyWhenCodexForeground = OnlyWhenAgentForeground;
        settings.HapticFeedback = HapticFeedback;
        settings.ShowOverlay = ShowOverlay;
        settings.RadialMenuMode =
            RadialMenuModes.Normalize(RadialMenuMode);
        settings.ComposerDialMode =
            ComposerDialModes.Normalize(ComposerDialMode);
        settings.StartWithWindows = StartWithWindows;
        settings.MinimizeToTray = MinimizeToTray;
        settings.DeadZone = Math.Round(DeadZone, 2);
        settings.RepeatDelayMs = RepeatDelayMs;
        settings.RepeatIntervalMs = RepeatIntervalMs;
        settings.Language = LanguageSetting;
    }
}

public sealed record LocalizedRadialMenuOption(
    string Value,
    string DisplayName);

public sealed record LocalizedComposerDialModeOption(
    string Value,
    string DisplayName);
