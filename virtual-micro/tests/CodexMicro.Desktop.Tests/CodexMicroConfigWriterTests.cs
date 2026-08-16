using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexMicroConfigWriterTests
{
    [Fact]
    public void WritesOfficialLayoutSchemaAndPreservesUnrelatedSettings()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-config-writer-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.toml");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """
                model = "gpt-5.6"

                [features]
                apps = true

                [desktop.codex-micro-layout]
                version = 1
                encoderMode = "composer-navigation"

                [desktop.codex-micro-layout.slots.ACT07]
                keycapId = "APPR"
                """);
            var writer = new CodexMicroConfigWriter(path);

            Assert.True(writer.SetSlot(
                "ACT07",
                "LAB",
                new CodexMicroActionBinding(
                    "skill",
                    "review",
                    @"C:\skills\review\SKILL.md")));
            Assert.True(writer.SetEncoderMode("conversation-scroll"));
            Assert.True(writer.SetVoiceButtonMode("realtime"));
            Assert.True(writer.SetSeparateMicrophoneKeys(true));

            var text = File.ReadAllText(path);
            var parsed = CodexMicroLayoutObserver.Parse(text, path);
            Assert.Contains("model = \"gpt-5.6\"", text);
            Assert.Contains("[features]", text);
            Assert.Contains("apps = true", text);
            Assert.Equal("LAB", parsed.GetSlot("ACT07").KeycapId);
            Assert.Equal("skill", parsed.GetSlot("ACT07").Action?.Type);
            Assert.Equal("review", parsed.GetSlot("ACT07").Action?.Id);
            Assert.Equal(
                @"C:\skills\review\SKILL.md",
                parsed.GetSlot("ACT07").Action?.SkillPath);
            Assert.Equal("conversation-scroll", parsed.EncoderMode);
            Assert.Equal("realtime", parsed.VoiceButtonMode);
            Assert.True(parsed.SeparateMicrophoneKeys);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ResetRemovesOnlyMicroTablesAndRestoresCanonicalDefaults()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "codex-micro-config-writer-tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.toml");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, """
                sandbox_mode = "workspace-write"

                [desktop.codex-micro-layout]
                encoderMode = "custom"

                [desktop.codex-micro-layout.slots.ACT06]
                keycapId = "BUG"

                [projects.'D:\\AgentController']
                trust_level = "trusted"
                """);
            var writer = new CodexMicroConfigWriter(path);

            Assert.True(writer.ResetLayout());

            var text = File.ReadAllText(path);
            var parsed = CodexMicroLayoutObserver.Parse(text, path);
            Assert.Contains("sandbox_mode = \"workspace-write\"", text);
            Assert.Contains("trust_level = \"trusted\"", text);
            Assert.Equal("composer-navigation", parsed.EncoderMode);
            Assert.Equal("FAST", parsed.GetSlot("ACT06").KeycapId);
            Assert.Equal("MIC", parsed.GetSlot("ACT10_ACT11").KeycapId);
            Assert.False(parsed.SeparateMicrophoneKeys);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
