using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class RadialInputMapTests
{
    [Theory]
    [InlineData(ControllerButtons.DPadUp, RadialInputAction.NewTask)]
    [InlineData(
        ControllerButtons.DPadRight,
        RadialInputAction.NavigateForward)]
    [InlineData(
        ControllerButtons.DPadDown,
        RadialInputAction.ToggleSidebar)]
    [InlineData(
        ControllerButtons.DPadLeft,
        RadialInputAction.NavigateBack)]
    [InlineData(ControllerButtons.A, RadialInputAction.ClearComposer)]
    [InlineData(ControllerButtons.X, RadialInputAction.ProjectContext)]
    [InlineData(ControllerButtons.B, RadialInputAction.Cancel)]
    [InlineData(ControllerButtons.Y, RadialInputAction.Cancel)]
    public void ActionLayerMapsDirectPhysicalActions(
        ControllerButtons button,
        RadialInputAction expected)
    {
        Assert.Equal(
            expected,
            RadialInputMap.Resolve(
                RadialMenuLayerKind.Action,
                button));
    }

    [Theory]
    [InlineData(ControllerButtons.DPadUp, RadialInputAction.AgentSlot1)]
    [InlineData(ControllerButtons.DPadRight, RadialInputAction.AgentSlot2)]
    [InlineData(ControllerButtons.DPadDown, RadialInputAction.AgentSlot3)]
    [InlineData(ControllerButtons.DPadLeft, RadialInputAction.AgentSlot4)]
    [InlineData(ControllerButtons.Back, RadialInputAction.AgentSlot5)]
    [InlineData(ControllerButtons.Start, RadialInputAction.AgentSlot6)]
    public void AgentLayerMapsSixPhysicalSlots(
        ControllerButtons button,
        RadialInputAction expected)
    {
        Assert.Equal(
            expected,
            RadialInputMap.Resolve(
                RadialMenuLayerKind.Agent,
                button));
    }

    [Theory]
    [InlineData(ControllerButtons.Y, RadialInputAction.ToggleFast)]
    [InlineData(ControllerButtons.A, RadialInputAction.Approve)]
    [InlineData(ControllerButtons.B, RadialInputAction.Decline)]
    [InlineData(ControllerButtons.X, RadialInputAction.Fork)]
    [InlineData(ControllerButtons.Back, RadialInputAction.PushToTalk)]
    [InlineData(ControllerButtons.Start, RadialInputAction.Dispatch)]
    public void CommandLayerMapsSixPhysicalSlots(
        ControllerButtons button,
        RadialInputAction expected)
    {
        Assert.Equal(
            expected,
            RadialInputMap.Resolve(
                RadialMenuLayerKind.Command,
                button));
    }

    [Fact]
    public void LayerCancelWinsWhenPressedWithAnotherTarget()
    {
        Assert.Equal(
            RadialInputAction.Cancel,
            RadialInputMap.Resolve(
                RadialMenuLayerKind.Agent,
                ControllerButtons.B |
                ControllerButtons.DPadUp));
        Assert.Equal(
            RadialInputAction.Cancel,
            RadialInputMap.Resolve(
                RadialMenuLayerKind.Command,
                ControllerButtons.LeftThumb |
                ControllerButtons.Y));
    }

    [Theory]
    [InlineData(ControllerButtons.X, RadialInputAction.Steer)]
    [InlineData(ControllerButtons.Y, RadialInputAction.Queue)]
    [InlineData(ControllerButtons.B, RadialInputAction.BeginStopHold)]
    [InlineData(ControllerButtons.A, RadialInputAction.Fork)]
    public void TurnLayerNeverFallsThroughToBaseActions(
        ControllerButtons button,
        RadialInputAction expected)
    {
        Assert.Equal(
            expected,
            RadialInputMap.Resolve(
                RadialMenuLayerKind.Turn,
                button));
        Assert.True(
            RadialInputMap.FrozenBaseButtons.HasFlag(button));
    }

    [Fact]
    public void ThresholdsProvideTriggerHysteresis()
    {
        Assert.Equal(0.12, RadialInputMap.TurnCandidateThreshold);
        Assert.Equal(
            0.08,
            RadialInputMap.TurnCandidateReleaseThreshold);
        Assert.Equal(0.55, RadialInputMap.TurnEngageThreshold);
        Assert.Equal(0.35, RadialInputMap.TurnReleaseThreshold);
        Assert.True(
            RadialInputMap.TurnEngageThreshold >
            RadialInputMap.TurnReleaseThreshold);
        Assert.Equal(180, RadialInputMap.LearningDelayMs);
    }

    [Fact]
    public void HalfPressedRightTriggerFreezesFaceButtonsWithoutDispatching()
    {
        const double trigger = 0.3;
        var physicalButtons =
            ControllerButtons.X | ControllerButtons.DPadDown;
        var baseButtons =
            physicalButtons &
            ~RadialInputMap.FrozenTurnCandidateButtons;

        Assert.True(RadialInputMap.IsTurnCandidate(trigger));
        Assert.False(RadialInputMap.CanAcceptTurnAction(trigger));
        Assert.False(baseButtons.HasFlag(ControllerButtons.X));
        Assert.True(baseButtons.HasFlag(ControllerButtons.DPadDown));
    }

    [Fact]
    public void ActionIdsStayAlignedWithPresentedMenuItems()
    {
        Assert.Equal(
            "agent-slot-6",
            RadialInputMap.ActionId(RadialInputAction.AgentSlot6));
        Assert.Equal(
            "command-ptt",
            RadialInputMap.ActionId(RadialInputAction.PushToTalk));
        Assert.Equal(
            "command-fork",
            RadialInputMap.ActionId(
                RadialInputAction.Fork,
                RadialMenuLayerKind.Command));
        Assert.Equal(
            "turn-fork",
            RadialInputMap.ActionId(
                RadialInputAction.Fork,
                RadialMenuLayerKind.Turn));
    }
}
