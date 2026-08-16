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
    }

    [Fact]
    public void SelectingTheOtherSlotSwapsInsteadOfDuplicating()
    {
        var settings = MicroProfileSettings.CreateTransient();

        settings.SetQuickModelA(CodexQuickModel.Luna);

        Assert.Equal(CodexQuickModel.Luna, settings.Current.QuickModelA);
        Assert.Equal(CodexQuickModel.Sol, settings.Current.QuickModelB);
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
            var settings = new MicroProfileSettings(path);
            settings.SetQuickModelB(CodexQuickModel.Terra);
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

            var reloaded = new MicroProfileSettings(path);

            Assert.True(settings.LastSaveSucceeded);
            Assert.Equal(CodexQuickModel.Sol, reloaded.Current.QuickModelA);
            Assert.Equal(CodexQuickModel.Terra, reloaded.Current.QuickModelB);
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
    public void ExternalHarnessCannotChangeCodexDialDirection()
    {
        var settings = MicroProfileSettings.CreateTransient();
        settings.SetActiveHarness("deepseek-harness");

        settings.SetInvertDialDirection(true);

        Assert.False(settings.Current.InvertDialDirection);
    }
}
