using System.Text.Json;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public void NewSettingsDefaultToSimpleComposerMode()
    {
        Assert.Equal(
            ComposerDialModes.Simple,
            new AppSettings().ComposerDialMode);
    }

    [Fact]
    public void LoadNormalizesInvalidLanguageAndBlankAgentId()
    {
        using var store = new TemporarySettingsStore();
        store.Write(new AppSettings
        {
            Language = "not-a-language",
            ActiveAgentId = " \t ",
        });

        var settings = store.Service.Load();

        Assert.Equal("auto", settings.Language);
        Assert.Equal("codex", settings.ActiveAgentId);
    }

    [Theory]
    [InlineData(null, "learning")]
    [InlineData("", "learning")]
    [InlineData("unknown", "learning")]
    [InlineData(" ALWAYS ", "always")]
    [InlineData("Learning", "learning")]
    [InlineData("OFF", "off")]
    public void LoadNormalizesRadialMenuMode(
        string? mode,
        string expected)
    {
        using var store = new TemporarySettingsStore();
        store.Write(new AppSettings
        {
            RadialMenuMode = mode!,
        });

        var settings = store.Service.Load();

        Assert.Equal(expected, settings.RadialMenuMode);
    }

    [Theory]
    [InlineData(null, "simple")]
    [InlineData("", "simple")]
    [InlineData("unknown", "simple")]
    [InlineData(" SIMPLE ", "simple")]
    [InlineData("Advanced", "advanced")]
    public void LoadNormalizesComposerDialMode(
        string? mode,
        string expected)
    {
        using var store = new TemporarySettingsStore();
        store.Write(new AppSettings
        {
            ComposerDialMode = mode!,
        });

        var settings = store.Service.Load();

        Assert.Equal(expected, settings.ComposerDialMode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void LoadFallsBackToDefaultsForUnsupportedVersions(
        int version)
    {
        using var store = new TemporarySettingsStore();
        store.Write(new AppSettings
        {
            Version = version,
            Language = "en-US",
            ActiveAgentId = "other-agent",
            DeadZone = 0.77,
            RepeatDelayMs = 555,
        });

        var settings = store.Service.Load();
        var defaults = new AppSettings();

        Assert.Equal(defaults.Version, settings.Version);
        Assert.Equal(defaults.Language, settings.Language);
        Assert.Equal(
            defaults.ActiveAgentId,
            settings.ActiveAgentId);
        Assert.Equal(defaults.DeadZone, settings.DeadZone);
        Assert.Equal(
            defaults.RepeatDelayMs,
            settings.RepeatDelayMs);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LoadMigratesLegacyComposerModeToSimple(int version)
    {
        using var store = new TemporarySettingsStore();
        store.Write(new AppSettings
        {
            Version = version,
            ComposerDialMode = ComposerDialModes.Advanced,
            DeadZone = 0.71,
        });

        var settings = store.Service.Load();

        Assert.Equal(3, settings.Version);
        Assert.Equal(
            ComposerDialModes.Simple,
            settings.ComposerDialMode);
        Assert.Equal(0.71, settings.DeadZone);
    }

    [Theory]
    [InlineData(-1.0, int.MinValue, int.MinValue, 0.35, 220, 140)]
    [InlineData(0.35, 220, 140, 0.35, 220, 140)]
    [InlineData(0.80, 600, 300, 0.80, 600, 300)]
    [InlineData(2.0, int.MaxValue, int.MaxValue, 0.80, 600, 300)]
    public void LoadClampsTuningValuesToSupportedBounds(
        double deadZone,
        int repeatDelayMs,
        int repeatIntervalMs,
        double expectedDeadZone,
        int expectedRepeatDelayMs,
        int expectedRepeatIntervalMs)
    {
        using var store = new TemporarySettingsStore();
        store.Write(new AppSettings
        {
            DeadZone = deadZone,
            RepeatDelayMs = repeatDelayMs,
            RepeatIntervalMs = repeatIntervalMs,
        });

        var settings = store.Service.Load();

        Assert.Equal(expectedDeadZone, settings.DeadZone);
        Assert.Equal(
            expectedRepeatDelayMs,
            settings.RepeatDelayMs);
        Assert.Equal(
            expectedRepeatIntervalMs,
            settings.RepeatIntervalMs);
    }

    [Theory]
    [MemberData(nameof(NonFiniteValues))]
    public void SaveReplacesNonFiniteDeadZoneWithDefault(
        double deadZone)
    {
        using var store = new TemporarySettingsStore();
        var source = new AppSettings
        {
            DeadZone = deadZone,
        };

        store.Service.Save(source);

        Assert.Equal(
            new AppSettings().DeadZone,
            store.Service.Load().DeadZone);
        AssertNonFiniteValueUnchanged(
            deadZone,
            source.DeadZone);
    }

    [Fact]
    public void LoadRestoresDefaultsForNullOrBlankShortcuts()
    {
        using var store = new TemporarySettingsStore();
        store.Write(new AppSettings
        {
            ReasoningDownShortcut = null!,
            ReasoningUpShortcut = string.Empty,
            PlanToggleShortcut = " ",
            ModelPickerShortcut = " ",
            FastToggleShortcut = "\t",
            ForkShortcut = string.Empty,
            DictationShortcut = "\r\n",
            CancelShortcut = "  ",
        });

        var settings = store.Service.Load();
        var defaults = new AppSettings();

        Assert.Equal(
            defaults.ReasoningDownShortcut,
            settings.ReasoningDownShortcut);
        Assert.Equal(
            defaults.ReasoningUpShortcut,
            settings.ReasoningUpShortcut);
        Assert.Equal(
            defaults.PlanToggleShortcut,
            settings.PlanToggleShortcut);
        Assert.Equal(
            defaults.ModelPickerShortcut,
            settings.ModelPickerShortcut);
        Assert.Equal(
            defaults.FastToggleShortcut,
            settings.FastToggleShortcut);
        Assert.Equal(
            defaults.ForkShortcut,
            settings.ForkShortcut);
        Assert.Equal(
            defaults.DictationShortcut,
            settings.DictationShortcut);
        Assert.Equal(
            defaults.CancelShortcut,
            settings.CancelShortcut);
    }

    [Fact]
    public void SavePersistsNormalizedCopyWithoutMutatingSource()
    {
        using var store = new TemporarySettingsStore();
        var source = new AppSettings
        {
            Version = 99,
            Language = "invalid-language",
            ActiveAgentId = " Mixed-Agent ",
            RadialMenuMode = " ALWAYS ",
            ComposerDialMode = " ADVANCED ",
            StartWithWindows = true,
            DeadZone = double.NaN,
            RepeatDelayMs = 1,
            RepeatIntervalMs = 999,
            ReasoningDownShortcut = " ",
        };

        store.Service.Save(source);

        Assert.Equal(99, source.Version);
        Assert.Equal("invalid-language", source.Language);
        Assert.Equal(" Mixed-Agent ", source.ActiveAgentId);
        Assert.Equal(" ALWAYS ", source.RadialMenuMode);
        Assert.Equal(" ADVANCED ", source.ComposerDialMode);
        Assert.True(double.IsNaN(source.DeadZone));
        Assert.Equal(1, source.RepeatDelayMs);
        Assert.Equal(999, source.RepeatIntervalMs);
        Assert.Equal(" ", source.ReasoningDownShortcut);

        var persisted = store.Service.Load();
        Assert.Equal(3, persisted.Version);
        Assert.Equal("auto", persisted.Language);
        Assert.Equal("mixed-agent", persisted.ActiveAgentId);
        Assert.Equal("always", persisted.RadialMenuMode);
        Assert.Equal("advanced", persisted.ComposerDialMode);
        Assert.Equal(0.58, persisted.DeadZone);
        Assert.Equal(220, persisted.RepeatDelayMs);
        Assert.Equal(300, persisted.RepeatIntervalMs);
        Assert.Equal(
            "F17",
            persisted.ReasoningDownShortcut);
        Assert.Equal(
            [true],
            store.StartupRegistrationUpdates);
    }

    public static IEnumerable<object[]> NonFiniteValues
    {
        get
        {
            yield return [double.NaN];
            yield return [double.PositiveInfinity];
            yield return [double.NegativeInfinity];
        }
    }

    private static void AssertNonFiniteValueUnchanged(
        double expected,
        double actual)
    {
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(actual));
            return;
        }

        Assert.Equal(expected, actual);
    }

    private sealed class TemporarySettingsStore : IDisposable
    {
        private static readonly string TestRoot =
            Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "AgentController.Tests"));

        public TemporarySettingsStore()
        {
            Root = Path.Combine(
                TestRoot,
                Guid.NewGuid().ToString("N"));
            var legacyPath = Path.Combine(
                Root,
                "legacy",
                "settings.json");

            Service = new SettingsService(
                Path.Combine(Root, "current"),
                legacyPath,
                value => StartupRegistrationUpdates.Add(value));
        }

        public string Root { get; }

        public SettingsService Service { get; }

        public List<bool> StartupRegistrationUpdates { get; } = [];

        public void Write(AppSettings settings)
        {
            Directory.CreateDirectory(Service.SettingsDirectory);
            File.WriteAllText(
                Service.SettingsPath,
                JsonSerializer.Serialize(settings));
        }

        public void Dispose()
        {
            var fullRoot = Path.GetFullPath(Root);
            var expectedPrefix =
                TestRoot.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            if (
                !fullRoot.StartsWith(
                    expectedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Refusing to remove a directory outside the test root.");
            }

            if (Directory.Exists(fullRoot))
            {
                Directory.Delete(fullRoot, recursive: true);
            }
        }
    }
}
