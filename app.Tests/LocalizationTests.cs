using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CodexController.Localization;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class LocalizationTests
{
    public static TheoryData<string?, AppLanguage> ParsedLanguages =>
        new()
        {
            { null, AppLanguage.Auto },
            { "", AppLanguage.Auto },
            { "auto", AppLanguage.Auto },
            { "SYSTEM", AppLanguage.Auto },
            { "zh-CN", AppLanguage.ZhCn },
            { "zh_cn", AppLanguage.ZhCn },
            { "zh-Hans", AppLanguage.ZhCn },
            { "en-US", AppLanguage.EnUs },
            { "EN_us", AppLanguage.EnUs },
        };

    [Theory]
    [MemberData(nameof(ParsedLanguages))]
    public void ParsesPersistedLanguageValues(
        string? value,
        AppLanguage expected)
    {
        Assert.True(AppLanguageParser.TryParse(
            value,
            out var parsed));
        Assert.Equal(expected, parsed);
        Assert.Equal(expected, AppLanguageParser.Parse(value));
    }

    [Fact]
    public void InvalidLanguageSafelyFallsBackToAuto()
    {
        Assert.False(AppLanguageParser.TryParse(
            "klingon",
            out var parsed));
        Assert.Equal(AppLanguage.Auto, parsed);
        Assert.Equal(
            AppLanguage.Auto,
            AppLanguageParser.Parse("klingon"));
    }

    [Theory]
    [InlineData(AppLanguage.Auto, "auto")]
    [InlineData(AppLanguage.ZhCn, "zh-CN")]
    [InlineData(AppLanguage.EnUs, "en-US")]
    public void ProducesStableSettingValues(
        AppLanguage language,
        string expected)
    {
        Assert.Equal(expected, language.ToSettingValue());
    }

    [Theory]
    [InlineData("zh-CN", AppLanguage.ZhCn)]
    [InlineData("zh-TW", AppLanguage.ZhCn)]
    [InlineData("en-US", AppLanguage.EnUs)]
    [InlineData("de-DE", AppLanguage.EnUs)]
    public void AutoFollowsSupportedSystemLanguage(
        string cultureName,
        AppLanguage expected)
    {
        var effective =
            AppLanguage.Auto.ResolveEffectiveLanguage(
                CultureInfo.GetCultureInfo(cultureName));

        Assert.Equal(expected, effective);
    }

    [Fact]
    public void ChineseAndEnglishCatalogsCoverTheSameCompleteKeySet()
    {
        IStringCatalog zh = new ZhCatalog();
        IStringCatalog en = new EnCatalog();

        Assert.Equal(
            StringKeys.All.Order(StringComparer.Ordinal),
            zh.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            StringKeys.All.Order(StringComparer.Ordinal),
            en.Keys.Order(StringComparer.Ordinal));
        Assert.All(
            StringKeys.All,
            key =>
            {
                Assert.False(string.IsNullOrWhiteSpace(zh[key]));
                Assert.False(string.IsNullOrWhiteSpace(en[key]));
            });
    }

    [Fact]
    public void CatalogsContainMajorNavigationAndSettingsTerms()
    {
        var zh = new ZhCatalog();
        var en = new EnCatalog();

        Assert.Equal("设备", zh[StringKeys.NavDevice]);
        Assert.Equal("配置", zh[StringKeys.NavConfiguration]);
        Assert.Equal("设置", zh[StringKeys.NavSettings]);
        Assert.Equal(
            "置顶项目",
            zh[StringKeys.SidebarPinnedProjects]);
        Assert.Equal(
            "Display language",
            en[StringKeys.SettingsLanguage]);
        Assert.Equal(
            "Reasoning effort",
            en[StringKeys.TermReasoningEffort]);
        Assert.Equal(
            "Controller disconnected",
            en[StringKeys.DeviceDisconnected]);
        Assert.Equal(
            "学习期显示",
            zh[StringKeys.SettingsRadialMenuLearning]);
        Assert.Equal(
            "简易模式",
            zh[StringKeys.SettingsComposerDialModeSimple]);
        Assert.Equal(
            "Advanced",
            en[StringKeys.SettingsComposerDialModeAdvanced]);
        Assert.Equal(
            "Default dispatch",
            en[StringKeys.DispatchDefault]);
    }

    [Fact]
    public void AgentNamesAndControllerGlyphsAreParameters()
    {
        var zh = new LocalizationService(AppLanguage.ZhCn).Strings;
        var en = new LocalizationService(AppLanguage.EnUs).Strings;

        Assert.Equal(
            "8BitDo Ultimate → Codex",
            zh.AppSubtitle("8BitDo Ultimate", "Codex"));
        Assert.Equal(
            "P1 · 唤醒 Nova",
            zh.ControlWakeAgent("P1", "Nova"));
        Assert.Equal(
            "LB · Hold to talk",
            en.ControlHoldToTalk("LB"));
        Assert.Equal(
            "Bring Studio Agent to front and unlock controller input",
            en.ControlWakeAgentDescription("Studio Agent"));
        Assert.Equal(
            "Menu cycles four root scopes; Y opens the action panel",
            en.ConfigRootProjectDescription("Menu", "Y"));
    }

    [Fact]
    public void RuntimeSwitchKeepsBindableFacadeAndNotifiesIt()
    {
        var service = new LocalizationService(AppLanguage.EnUs);
        var strings = service.Strings;
        var stringChanges = new List<string?>();
        var serviceChanges = new List<string?>();
        strings.PropertyChanged += (_, args) =>
            stringChanges.Add(args.PropertyName);
        service.PropertyChanged += (_, args) =>
            serviceChanges.Add(args.PropertyName);

        service.SelectedLanguage = AppLanguage.ZhCn;

        Assert.Same(strings, service.Strings);
        Assert.Equal(AppLanguage.ZhCn, service.EffectiveLanguage);
        Assert.Equal("zh-CN", service.SettingValue);
        Assert.Equal("设备", strings.NavDevice);
        Assert.Contains(string.Empty, stringChanges);
        Assert.Contains(
            nameof(LocalizationService.SelectedLanguage),
            serviceChanges);
        Assert.Contains(
            nameof(LocalizationService.Catalog),
            serviceChanges);
    }

    [Fact]
    public void LanguageOptionsAreLocalizedAndBindable()
    {
        var service = new LocalizationService(AppLanguage.EnUs);
        var changes = new List<string?>();
        service.PropertyChanged += (_, args) =>
            changes.Add(args.PropertyName);

        Assert.Collection(
            service.LanguageOptions,
            option =>
            {
                Assert.Equal(AppLanguage.Auto, option.Value);
                Assert.Equal("Follow system", option.DisplayName);
            },
            option =>
            {
                Assert.Equal(AppLanguage.ZhCn, option.Value);
                Assert.Equal("简体中文", option.DisplayName);
            },
            option =>
            {
                Assert.Equal(AppLanguage.EnUs, option.Value);
                Assert.Equal("English (US)", option.DisplayName);
            });

        service.SelectedLanguage = AppLanguage.ZhCn;

        Assert.Equal(
            "跟随系统",
            service.LanguageOptions[0].DisplayName);
        Assert.Contains(
            nameof(LocalizationService.LanguageOptions),
            changes);
    }

    [Fact]
    public void BindingHostIndexerRefreshesWhenLanguageChanges()
    {
        var original = LocalizationHost.Current;
        var service = new LocalizationService(AppLanguage.EnUs);
        var changes = new List<string?>();

        try
        {
            LocalizationHost.Use(service);
            LocalizationHost.Strings.PropertyChanged += OnChanged;

            Assert.Equal(
                "Device",
                LocalizationHost.Strings[StringKeys.NavDevice]);

            service.SelectedLanguage = AppLanguage.ZhCn;

            Assert.Equal(
                "设备",
                LocalizationHost.Strings[StringKeys.NavDevice]);
            Assert.Contains("Item[]", changes);
        }
        finally
        {
            LocalizationHost.Strings.PropertyChanged -= OnChanged;
            LocalizationHost.Use(original);
        }

        void OnChanged(
            object? sender,
            PropertyChangedEventArgs args)
        {
            changes.Add(args.PropertyName);
        }
    }

    [Fact]
    public void LocExtensionCreatesDataContextIndependentBinding()
    {
        var original = LocalizationHost.Current;

        try
        {
            LocalizationHost.Use(
                new LocalizationService(AppLanguage.EnUs));
            var extension = new LocExtension(
                StringKeys.ControlWakeAgent)
            {
                Arg0 = "MENU",
                Arg1 = "Codex",
            };

            var binding = extension.CreateBinding();

            Assert.Equal(
                $"[{StringKeys.ControlWakeAgent}]",
                binding.Path.Path);
            Assert.Same(LocalizationHost.Strings, binding.Source);
            Assert.Equal(BindingMode.OneWay, binding.Mode);
            Assert.NotNull(binding.Converter);
            var arguments = Assert.IsType<object?[]>(
                binding.ConverterParameter);
            Assert.Equal(["MENU", "Codex"], arguments);
        }
        finally
        {
            LocalizationHost.Use(original);
        }
    }

    [Fact]
    public void LocExtensionRejectsUnknownKeysAndArgumentGaps()
    {
        Assert.Throws<KeyNotFoundException>(() =>
            new LocExtension("missing.ui.key").CreateBinding());
        Assert.Throws<InvalidOperationException>(() =>
            new LocExtension(StringKeys.AppSubtitle)
            {
                Arg1 = "Codex",
            }.CreateBinding());
    }

    [Fact]
    public void AutoLanguageCanBeRefreshedAfterSystemChange()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        var service = new LocalizationService(
            AppLanguage.Auto,
            () => culture);
        var notifications = 0;
        service.Strings.PropertyChanged += (_, _) =>
            notifications++;

        Assert.Equal(AppLanguage.EnUs, service.EffectiveLanguage);
        Assert.Equal("Device", service.Strings.NavDevice);

        culture = CultureInfo.GetCultureInfo("zh-CN");
        Assert.True(service.RefreshAutoLanguage());

        Assert.Equal(AppLanguage.ZhCn, service.EffectiveLanguage);
        Assert.Equal("设备", service.Strings.NavDevice);
        Assert.Equal(1, notifications);
        Assert.False(service.RefreshAutoLanguage());
    }

    [Fact]
    public void LocalizesFeedbackValuesWithoutLosingUnknownValues()
    {
        var zh = new LocalizationService(AppLanguage.ZhCn).Strings;
        var en = new LocalizationService(AppLanguage.EnUs).Strings;

        Assert.Equal("置顶任务", zh.ScopeValue("pinned_tasks"));
        Assert.Equal("Extra high", en.ReasoningValue("extra_high"));
        Assert.Equal("标准", zh.SpeedValue("normal"));
        Assert.Equal(
            "future-value",
            en.ScopeValue("future-value"));
    }

    [Fact]
    public void MainWindowMessageCatalogIsEnglishWhenEnglishIsSelected()
    {
        var catalog = new EnCatalog();
        var messageKeys = StringKeys.All.Where(key =>
            key.StartsWith("message.", StringComparison.Ordinal));

        Assert.All(
            messageKeys,
            key => Assert.False(
                catalog[key].Any(IsCjk),
                $"{key} contains CJK text: {catalog[key]}"));
    }

    [Fact]
    public void MainWindowStatusMessagesFollowRuntimeLanguageSwitch()
    {
        var localization = new LocalizationService(AppLanguage.EnUs);
        var strings = localization.Strings;

        Assert.Equal(
            "Loaded 12 tasks and 3 projects · filtered 2 archived and 1 unavailable",
            strings.Format(
                StringKeys.MessageDataLoaded,
                12,
                3,
                2,
                1));
        Assert.Equal(
            "Waiting for Studio Agent to enter the foreground",
            strings.Format(
                StringKeys.MessageWaitingForAgentForeground,
                "Studio Agent"));

        localization.SetLanguage(AppLanguage.ZhCn);

        Assert.Equal(
            "已读取 12 个任务、3 个项目 · 已过滤 2 个归档、1 个不可用",
            strings.Format(
                StringKeys.MessageDataLoaded,
                12,
                3,
                2,
                1));
        Assert.Equal(
            "等待 Studio Agent 前台",
            strings.Format(
                StringKeys.MessageWaitingForAgentForeground,
                "Studio Agent"));
    }

    [Fact]
    public void AppSettingsPersistLanguageAsAStableString()
    {
        var settings = new AppSettings();

        Assert.Equal(AppLanguageParser.AutoValue, settings.Language);
        settings.Language = AppLanguage.EnUs.ToSettingValue();
        Assert.Equal("en-US", settings.Language);
    }

    [Fact]
    public void ConcreteCatalogRejectsUnknownKeys()
    {
        var catalog = new EnCatalog();

        Assert.False(catalog.TryGet(
            "future.missing-key",
            out var value));
        Assert.Equal(string.Empty, value);
        Assert.Throws<KeyNotFoundException>(() =>
            catalog.Get("future.missing-key"));
    }

    private static bool IsCjk(char character)
    {
        return character is >= '\u3400' and <= '\u9fff';
    }
}
