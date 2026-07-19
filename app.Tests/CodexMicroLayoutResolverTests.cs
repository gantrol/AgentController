using CodexController.Services.Micro;

namespace CodexController.Tests;

public sealed class CodexMicroLayoutResolverTests
{
    [Fact]
    public void MissingConfigUsesVerifiedCodexDefaultLayout()
    {
        var resolver = new CodexMicroLayoutResolver(Path.Combine(
            Path.GetTempPath(),
            $"agent-controller-missing-layout-{Guid.NewGuid():N}.toml"));

        Assert.Equal(
            CodexMicroLayoutResolver.ComposerNavigationMode,
            resolver.EncoderMode);
        Assert.True(resolver.AllowsCommand(
            "ACT06",
            "composer.toggleFastMode"));
        Assert.True(resolver.AllowsCommand("ACT07", "approval.approve"));
        Assert.True(resolver.AllowsCommand("ACT08", "approval.decline"));
        Assert.True(resolver.AllowsCommand("ACT09", "forkThread"));
        Assert.True(resolver.AllowsCommand(
            "ACT10_ACT11",
            "dictation.pushToTalk"));
        Assert.True(resolver.AllowsCommand("ACT12", "composer.submit"));
        Assert.False(resolver.AllowsCommand(
            "ACT12",
            "toggleTerminal"));
    }

    [Fact]
    public void CustomInlineAndTableMappingsUseExplicitCommands()
    {
        var layout = CodexMicroLayoutResolver.Parse(
            """
            [desktop.codex-micro-layout]
            encoderMode = "reasoning"
            slots = { ACT07 = { keycapId = "TERM", commandId = "approval.approve" } }

            [desktop.codex-micro-layout.slots.ACT12]
            keycapId = "CODEX"
            commandId = "toggleTerminal"
            """,
            "test-custom-layout");

        Assert.Equal(
            CodexMicroLayoutResolver.ReasoningMode,
            layout.EncoderMode);
        Assert.Equal("test-custom-layout", layout.Source);

        var approve = layout.GetSlot("ACT07");
        Assert.True(approve.IsVerified);
        Assert.Equal("TERM", approve.KeycapId);
        Assert.Equal("approval.approve", approve.CommandId);

        var submit = layout.GetSlot("ACT12");
        Assert.True(submit.IsVerified);
        Assert.Equal("CODEX", submit.KeycapId);
        Assert.Equal("toggleTerminal", submit.CommandId);
    }

    [Fact]
    public void MentionedButUnparseableSlotFailsClosedInIsolation()
    {
        var layout = CodexMicroLayoutResolver.Parse(
            """
            [desktop.codex-micro-layout]
            encoderMode = "unsupported-future-mode"
            ACT08 = "REJ"
            """,
            "test-malformed-layout");

        Assert.False(layout.GetSlot("ACT08").IsVerified);
        Assert.True(layout.GetSlot("ACT07").IsVerified);
        Assert.Equal(
            CodexMicroLayoutResolver.ComposerNavigationMode,
            layout.EncoderMode);
    }
}
