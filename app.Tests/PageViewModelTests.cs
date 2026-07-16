using CodexController.Localization;
using CodexController.Models;
using CodexController.ViewModels;

namespace CodexController.Tests;

public sealed class PageViewModelTests
{
    [Fact]
    public void ConfigViewModelRoundTripsAndNormalizesShortcuts()
    {
        var source = new AppSettings
        {
            ReasoningDownShortcut = "F13",
            ReasoningUpShortcut = "F14",
            ModelPickerShortcut = "Ctrl+M",
            FastToggleShortcut = "F15",
            DictationShortcut = "Ctrl+D",
            SubmitShortcut = "Ctrl+Enter",
        };
        var target = new AppSettings();
        var viewModel = new ConfigPageViewModel(
            () => { },
            () => { });

        viewModel.Load(source);
        viewModel.SubmitShortcut = "  ";
        viewModel.ApplyTo(target);

        Assert.Equal("F13", target.ReasoningDownShortcut);
        Assert.Equal("Ctrl+M", target.ModelPickerShortcut);
        Assert.Equal("F22", target.SubmitShortcut);
    }

    [Fact]
    public void SettingsViewModelRoundTripsBehaviorAndTuning()
    {
        var source = new AppSettings
        {
            OnlyWhenCodexForeground = false,
            HapticFeedback = false,
            ShowOverlay = false,
            RadialMenuMode = RadialMenuModes.Off,
            StartWithWindows = true,
            MinimizeToTray = false,
            DeadZone = 0.63,
            RepeatDelayMs = 410,
            RepeatIntervalMs = 185,
        };
        var target = new AppSettings();
        var viewModel = new SettingsPageViewModel(
            () => { },
            () => { },
            () => { },
            _ => { });

        viewModel.Load(source);
        viewModel.ApplyTo(target);

        Assert.False(target.OnlyWhenCodexForeground);
        Assert.False(target.HapticFeedback);
        Assert.False(target.ShowOverlay);
        Assert.Equal(RadialMenuModes.Off, target.RadialMenuMode);
        Assert.True(target.StartWithWindows);
        Assert.False(target.MinimizeToTray);
        Assert.Equal(0.63, target.DeadZone);
        Assert.Equal(410, target.RepeatDelayMs);
        Assert.Equal(185, target.RepeatIntervalMs);
    }

    [Fact]
    public void SliderDisplayPropertiesTrackTheirValues()
    {
        var viewModel = new SettingsPageViewModel(
            () => { },
            () => { },
            () => { },
            _ => { })
        {
            DeadZone = 0.674,
            RepeatDelayMs = 420,
            RepeatIntervalMs = 190,
        };

        Assert.Equal("0.67", viewModel.DeadZoneDisplay);
        Assert.Equal("420 ms", viewModel.RepeatDelayDisplay);
        Assert.Equal("190 ms", viewModel.RepeatIntervalDisplay);
    }

    [Fact]
    public void LanguageSelectionUpdatesSettingsAndNotifiesHost()
    {
        string? selected = null;
        var viewModel = new SettingsPageViewModel(
            () => { },
            () => { },
            () => { },
            value => selected = value);
        var settings = new AppSettings();

        viewModel.LanguageSetting = "en-US";
        viewModel.ApplyTo(settings);

        Assert.Equal("en-US", selected);
        Assert.Equal("en-US", settings.Language);
    }

    [Fact]
    public void PageContextUsesActiveAgentAndControllerGlyphs()
    {
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;
        var config = new ConfigPageViewModel(
            () => { },
            () => { });
        var settings = new SettingsPageViewModel(
            () => { },
            () => { },
            () => { },
            _ => { });

        config.UpdateContext(
            strings,
            "Studio Agent",
            "L3",
            "△",
            "R3");
        settings.UpdateContext(
            strings,
            "Studio Agent",
            "☰",
            "Controller Studio");

        Assert.Contains("Studio Agent", config.Description);
        Assert.Equal("L3 / △", config.RootProjectGlyphs);
        Assert.Equal("R3 / 500 ms R3", config.ModeSwitchGlyphs);
        Assert.Contains(
            "Studio Agent",
            settings.OnlyForegroundText);
        Assert.Contains(
            "Controller Studio",
            settings.OpenVendorToolText);
        Assert.Equal(
            "Open Studio Agent settings",
            settings.OpenAgentSettingsText);
        Assert.Collection(
            settings.RadialMenuOptions,
            option =>
            {
                Assert.Equal(RadialMenuModes.Always, option.Value);
                Assert.Equal("Always show", option.DisplayName);
            },
            option =>
            {
                Assert.Equal(RadialMenuModes.Learning, option.Value);
                Assert.Equal("Learning mode", option.DisplayName);
            },
            option =>
            {
                Assert.Equal(RadialMenuModes.Off, option.Value);
                Assert.Equal("Off", option.DisplayName);
            });
    }

    [Fact]
    public void OptionalCapabilityCommandsDisableUnavailableActions()
    {
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;
        var config = new ConfigPageViewModel(
            () => { },
            () => { });
        var settings = new SettingsPageViewModel(
            () => { },
            () => { },
            () => { },
            _ => { });

        config.UpdateContext(
            strings,
            "Shortcut-only Agent",
            "LS",
            "Y",
            "RS",
            canOpenAgentShortcuts: false);
        settings.UpdateContext(
            strings,
            "Shortcut-only Agent",
            "Menu",
            controllerSoftwareName: null,
            canOpenVendorTool: false,
            canOpenAgentSettings: false);

        Assert.False(config.CanOpenAgentShortcuts);
        Assert.False(config.OpenAgentShortcutsCommand.CanExecute(null));
        Assert.False(settings.CanOpenVendorTool);
        Assert.False(settings.OpenVendorToolCommand.CanExecute(null));
        Assert.False(settings.CanOpenAgentSettings);
        Assert.False(settings.OpenAgentSettingsCommand.CanExecute(null));
    }

}
