using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class MicroDistributionPresetTests
{
    [Fact]
    public void DeepSeekPresetAppliesOnlyFirstRunHarnessDefault()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-distribution-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "distribution-preset.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                path,
                """
                {
                  "schemaVersion": 1,
                  "id": "deepseek-one-click",
                  "defaultHarnessId": "deepseek-harness",
                  "voice": {
                    "defaultProvider": "system",
                    "localQwenStartMode": "keypad-start"
                  },
                  "surface": { "theme": "deepseek-soft-blue" }
                }
                """);

            var preset = Assert.IsType<MicroDistributionPreset>(
                MicroDistributionPreset.TryLoad(path));
            var fallback = new MicroProfileSnapshot(
                CodexQuickModel.Sol,
                CodexQuickModel.Luna);
            var applied = preset.Apply(fallback);

            Assert.Equal("deepseek-one-click", preset.Id);
            Assert.Equal("system", preset.VoiceDefaultProvider);
            Assert.Equal(
                MicroLocalVoiceStartModes.KeypadStart,
                preset.LocalQwenStartMode);
            Assert.Equal(
                "deepseek-soft-blue",
                preset.SurfaceTheme);
            Assert.Equal("deepseek-harness", applied.ActiveHarnessId);
            Assert.Equal(fallback.QuickModelA, applied.QuickModelA);
            Assert.Equal(fallback.QuickModelB, applied.QuickModelB);
            Assert.Equal(
                MicroLocalVoiceStartModes.KeypadStart,
                applied.VoiceSettings.LocalStartMode);
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
