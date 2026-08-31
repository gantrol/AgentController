using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class MicroProfileSettingsTests
{
    [Fact]
    public void DefaultsToSolAndLuna()
    {
        var settings = MicroProfileSettings.CreateTransient();

        Assert.Equal(CodexQuickModel.Sol, settings.Current.QuickModelA);
        Assert.Equal(CodexQuickModel.Luna, settings.Current.QuickModelB);
        Assert.Null(settings.Current.QuickModelAEffort);
        Assert.Null(settings.Current.QuickModelBEffort);
    }

    [Fact]
    public void SelectingTheOtherSlotSwapsInsteadOfDuplicating()
    {
        var settings = MicroProfileSettings.CreateTransient();
        settings.SetQuickModelAEffort("ultra");
        settings.SetQuickModelBEffort("max");

        settings.SetQuickModelA(CodexQuickModel.Luna);

        Assert.Equal(CodexQuickModel.Luna, settings.Current.QuickModelA);
        Assert.Equal(CodexQuickModel.Sol, settings.Current.QuickModelB);
        Assert.Equal("max", settings.Current.QuickModelAEffort);
        Assert.Equal("ultra", settings.Current.QuickModelBEffort);
    }

    [Fact]
    public void SavesAndReloadsTheConfiguredPair()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-profile-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "profile.json");
        try
        {
            var modelsCachePath = WriteModelsCache(directory);
            var settings = new MicroProfileSettings(path, modelsCachePath);
            settings.SetQuickModelB(CodexQuickModel.Terra);
            settings.SetQuickModelAEffort(" ULTRA ");
            settings.SetQuickModelBEffort("max");
            settings.SetInvertDialDirection(true);
            settings.SetActiveHarness("deepseek-harness");
            settings.SetAgentSource("pinned");
            settings.SetSingleTapAgentKeys(true);
            settings.SetTapToToggleVoice(true);
            settings.SetVoiceSettings(new(
                Provider: MicroVoiceProviders.RemoteWebSocket,
                Language: "zh-CN",
                AutoSubmit: true,
                SetupCompleted: true,
                RemoteUrl: "wss://speech.example.test/v1/stream",
                RemoteModel: "qwen-asr",
                LocalStartMode: MicroLocalVoiceStartModes.KeypadStart,
                LocalHealthUrl: "http://localhost:9876/health",
                LocalLauncherPath: "{AppDir}\\voice\\start.ps1",
                LocalWorkingDirectory: "{LocalAppData}\\CodexMicro\\voice",
                LocalDistribution: "Ubuntu-24.04",
                LocalPythonPath: "/home/user/.venvs/qwen/bin/python",
                LocalReadyTimeoutSeconds: 900,
                LocalStopWithKeypad: false));
            settings.SetKeypadName("DeepSeek 工作区");
            settings.SetWindowPlacement(123.5, 456.25, topmost: false);

            var reloaded = new MicroProfileSettings(path, modelsCachePath);

            Assert.True(settings.LastSaveSucceeded);
            Assert.Equal(CodexQuickModel.Sol, reloaded.Current.QuickModelA);
            Assert.Equal(CodexQuickModel.Terra, reloaded.Current.QuickModelB);
            Assert.Equal("ultra", reloaded.Current.QuickModelAEffort);
            Assert.Equal("max", reloaded.Current.QuickModelBEffort);
            Assert.Equal("deepseek-harness", reloaded.Current.ActiveHarnessId);
            Assert.Equal("pinned", reloaded.Current.AgentSource);
            Assert.True(reloaded.Current.SingleTapAgentKeys);
            Assert.True(reloaded.Current.TapToToggleVoice);
            Assert.True(reloaded.Current.InvertDialDirection);
            Assert.Equal(
                MicroVoiceProviders.RemoteWebSocket,
                reloaded.Current.VoiceSettings.Provider);
            Assert.Equal("zh-CN", reloaded.Current.VoiceSettings.Language);
            Assert.True(reloaded.Current.VoiceSettings.AutoSubmit);
            Assert.True(reloaded.Current.VoiceSettings.SetupCompleted);
            Assert.Equal(
                "wss://speech.example.test/v1/stream",
                reloaded.Current.VoiceSettings.RemoteUrl);
            Assert.Equal("qwen-asr", reloaded.Current.VoiceSettings.RemoteModel);
            Assert.Equal(
                MicroLocalVoiceStartModes.KeypadStart,
                reloaded.Current.VoiceSettings.LocalStartMode);
            Assert.Equal(
                "http://localhost:9876/health",
                reloaded.Current.VoiceSettings.LocalHealthUrl);
            Assert.Equal(
                "{AppDir}\\voice\\start.ps1",
                reloaded.Current.VoiceSettings.LocalLauncherPath);
            Assert.Equal(
                "{LocalAppData}\\CodexMicro\\voice",
                reloaded.Current.VoiceSettings.LocalWorkingDirectory);
            Assert.Equal(
                "Ubuntu-24.04",
                reloaded.Current.VoiceSettings.LocalDistribution);
            Assert.Equal(
                "/home/user/.venvs/qwen/bin/python",
                reloaded.Current.VoiceSettings.LocalPythonPath);
            Assert.Equal(
                900,
                reloaded.Current.VoiceSettings.LocalReadyTimeoutSeconds);
            Assert.False(reloaded.Current.VoiceSettings.LocalStopWithKeypad);
            Assert.Equal("DeepSeek 工作区", reloaded.Current.KeypadName);
            Assert.Equal(123.5, reloaded.Current.WindowLeft);
            Assert.Equal(456.25, reloaded.Current.WindowTop);
            Assert.False(reloaded.Current.WindowTopmost);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ModelCacheLimitsReasoningEffortsForEachModel()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-profile-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var settings = MicroProfileSettings.CreateTransient(
                modelsCachePath: WriteModelsCache(directory));

            Assert.Contains(
                "ultra",
                settings.GetSupportedReasoningEfforts(CodexQuickModel.Sol));
            Assert.Contains(
                "ultra",
                settings.GetSupportedReasoningEfforts(CodexQuickModel.Terra));
            Assert.DoesNotContain(
                "ultra",
                settings.GetSupportedReasoningEfforts(CodexQuickModel.Luna));

            settings.SetQuickModelBEffort("max");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                settings.SetQuickModelBEffort("ultra"));
            Assert.Equal("max", settings.Current.QuickModelBEffort);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ChangingModelClearsAnUnsupportedExplicitEffort()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-profile-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            var settings = MicroProfileSettings.CreateTransient(
                new(
                    CodexQuickModel.Sol,
                    CodexQuickModel.Terra,
                    QuickModelAEffort: "ultra"),
                WriteModelsCache(directory));

            settings.SetQuickModelA(CodexQuickModel.Luna);

            Assert.Equal(CodexQuickModel.Luna, settings.Current.QuickModelA);
            Assert.Null(settings.Current.QuickModelAEffort);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void MissingModelCacheDoesNotDiscardARecognizedEffort()
    {
        var missingCachePath = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-profile-tests",
            Guid.NewGuid().ToString("N"),
            "missing-models-cache.json");
        var settings = MicroProfileSettings.CreateTransient(
            new(
                CodexQuickModel.Sol,
                CodexQuickModel.Luna,
                QuickModelAEffort: "ultra"),
            missingCachePath);

        Assert.Equal("ultra", settings.Current.QuickModelAEffort);
        settings.SetQuickModelAEffort("ultra");
        Assert.Equal("ultra", settings.Current.QuickModelAEffort);
    }

    [Fact]
    public void ExternalHarnessCannotChangeCodexDialDirection()
    {
        var settings = MicroProfileSettings.CreateTransient();
        settings.SetActiveHarness("deepseek-harness");

        settings.SetInvertDialDirection(true);

        Assert.False(settings.Current.InvertDialDirection);
    }

    private static string WriteModelsCache(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "models_cache.json");
        File.WriteAllText(
            path,
            """
            {
              "models": [
                {
                  "slug": "gpt-5.6-sol",
                  "default_reasoning_level": "low",
                  "supported_reasoning_levels": [
                    { "effort": "low" },
                    { "effort": "medium" },
                    { "effort": "high" },
                    { "effort": "xhigh" },
                    { "effort": "max" },
                    { "effort": "ultra" }
                  ]
                },
                {
                  "slug": "gpt-5.6-terra",
                  "default_reasoning_level": "medium",
                  "supported_reasoning_levels": [
                    { "effort": "low" },
                    { "effort": "medium" },
                    { "effort": "high" },
                    { "effort": "xhigh" },
                    { "effort": "max" },
                    { "effort": "ultra" }
                  ]
                },
                {
                  "slug": "gpt-5.6-luna",
                  "default_reasoning_level": "medium",
                  "supported_reasoning_levels": [
                    { "effort": "low" },
                    { "effort": "medium" },
                    { "effort": "high" },
                    { "effort": "xhigh" },
                    { "effort": "max" }
                  ]
                }
              ]
            }
            """);
        return path;
    }
}
