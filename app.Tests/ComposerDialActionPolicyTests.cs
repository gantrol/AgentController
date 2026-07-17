using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerDialActionPolicyTests
{
    [Theory]
    [InlineData("Project: ai-keyboard")]
    [InlineData("Reasoning effort")]
    [InlineData("Fast")]
    [InlineData("5.6 Sol Max")]
    [InlineData("Full access")]
    [InlineData("Approve for me")]
    [InlineData("Workspace write")]
    [InlineData("Read only")]
    [InlineData("Don't work in a project")]
    public void PickerControlsRemainAvailable(string name)
    {
        Assert.True(ComposerDialActionPolicy.IsPickerControl(name));
        Assert.Null(ComposerDialActionPolicy.BlockReason(name));
    }

    [Fact]
    public void ExpandableModelPickerPrecedesInvokeOnlyProjectPicker()
    {
        var modelPriority =
            ComposerDialActionPolicy.PickerControlPriority(
                supportsExpandCollapse: true,
                allowInvoke: false);
        var projectPriority =
            ComposerDialActionPolicy.PickerControlPriority(
                supportsExpandCollapse: false,
                allowInvoke: true);

        Assert.True(modelPriority < projectPriority);
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
    [InlineData("Undo")]
    [InlineData("Review")]
    [InlineData("撤销")]
    [InlineData("查看更改")]
    public void ComposerActionButtonsAreNotDialPickers(string name)
    {
        Assert.False(
            ComposerDialActionPolicy.IsPickerControl(name));
        Assert.Equal(
            ComposerDialActionKind.BaseAction,
            ComposerDialActionPolicy.Classify(name));
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
