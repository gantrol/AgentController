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
            settings.SetActiveHarness("deepseek-harness");
            settings.SetAgentSource("pinned");
            settings.SetSingleTapAgentKeys(true);
            settings.SetKeypadName("DeepSeek 工作区");
            settings.SetWindowPlacement(123.5, 456.25, topmost: false);

            var reloaded = new MicroProfileSettings(path);

            Assert.True(settings.LastSaveSucceeded);
            Assert.Equal(CodexQuickModel.Sol, reloaded.Current.QuickModelA);
            Assert.Equal(CodexQuickModel.Terra, reloaded.Current.QuickModelB);
            Assert.Equal("deepseek-harness", reloaded.Current.ActiveHarnessId);
            Assert.Equal("pinned", reloaded.Current.AgentSource);
            Assert.True(reloaded.Current.SingleTapAgentKeys);
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
}
