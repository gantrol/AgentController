using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerDialActionPolicyTests
{
    [Theory]
    [InlineData("Project: ai-keyboard")]
    [InlineData("Reasoning effort")]
    [InlineData("Fast")]
    [InlineData("Don't work in a project")]
    public void PickerControlsRemainAvailable(string name)
    {
        Assert.True(ComposerDialActionPolicy.IsPickerControl(name));
        Assert.Null(ComposerDialActionPolicy.BlockReason(name));
    }

    [Theory]
    [InlineData("Steer")]
    [InlineData("Queue next turn")]
    [InlineData("加入当前运行")]
    [InlineData("排到下一轮")]
    public void ExplicitTurnActionsUseTheirDedicatedChord(string name)
    {
        Assert.Equal(
            ComposerDialActionKind.ExplicitTurnAction,
            ComposerDialActionPolicy.Classify(name));
        Assert.Equal(
            "dial-explicit-turn-action",
            ComposerDialActionPolicy.BlockReason(name));
    }

    [Theory]
    [InlineData("Delete queued message")]
    [InlineData("Remove")]
    [InlineData("Discard changes")]
    [InlineData("删除排队消息")]
    public void DestructiveActionsCannotBeConfirmedBySingleR3(string name)
    {
        Assert.Equal(
            ComposerDialActionKind.DestructiveAction,
            ComposerDialActionPolicy.Classify(name));
        Assert.Equal(
            "dial-destructive-action-blocked",
            ComposerDialActionPolicy.BlockReason(name));
    }

    [Fact]
    public void UnknownFocusedItemCannotBeConfirmedBlindly()
    {
        Assert.Equal(
            "dial-selection-unverified",
            ComposerDialActionPolicy.BlockReason(null));
    }
}
