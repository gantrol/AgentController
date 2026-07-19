using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class ControllerInteractionCoordinatorTests
{
    [Fact]
    public void PreservesBufferedButtonEdgesInInputOrder()
    {
        var coordinator = new ControllerInteractionCoordinator();

        Assert.True(coordinator.EnqueueState(State(packet: 1)));
        Assert.False(coordinator.EnqueueState(State(
            buttons: ControllerButtons.A,
            packet: 2)));
        Assert.False(coordinator.EnqueueState(State(packet: 3)));

        Assert.Equal(
            [
                ControllerButtons.None,
                ControllerButtons.A,
                ControllerButtons.None,
            ],
            coordinator
                .DrainStates()
                .Select(state => state.Buttons));
    }

    [Fact]
    public void TracksBaseAndPhysicalButtonHistorySeparately()
    {
        var coordinator = new ControllerInteractionCoordinator();
        coordinator.CommitButtonHistory(
            ControllerButtons.A,
            ControllerButtons.A | ControllerButtons.RightShoulder);

        Assert.Equal(
            ControllerButtonTransition.None,
            coordinator.BaseButtonTransition(
                ControllerButtons.A,
                ControllerButtons.A));
        Assert.Equal(
            ControllerButtonTransition.Released,
            coordinator.BaseButtonTransition(
                ControllerButtons.None,
                ControllerButtons.A));

        var edges = coordinator.PhysicalEdges(
            ControllerButtons.A | ControllerButtons.B);
        Assert.Equal(ControllerButtons.B, edges.Down);
        Assert.Equal(ControllerButtons.RightShoulder, edges.Up);
    }

    [Fact]
    public void OwnsPushToTalkHysteresisAcrossFrames()
    {
        var coordinator = new ControllerInteractionCoordinator();

        Assert.Equal(
            AnalogTriggerTransition.Pressed,
            coordinator.UpdatePushToTalk(0.36, blocked: false));
        Assert.True(coordinator.PushToTalkBlocksBaseInput);
        Assert.Equal(
            AnalogTriggerTransition.None,
            coordinator.UpdatePushToTalk(0.25, blocked: false));
        Assert.Equal(
            AnalogTriggerTransition.Released,
            coordinator.UpdatePushToTalk(0.19, blocked: false));
        Assert.False(coordinator.PushToTalkBlocksBaseInput);
    }

    [Fact]
    public void RequireNeutralRoutingBlocksHeldSticksUntilReengaged()
    {
        var coordinator = new ControllerInteractionCoordinator();

        var initial = coordinator.UpdateRightStick(
            0.9,
            0,
            0.5,
            blocked: false);
        coordinator.RequireNeutralRouting();
        var held = coordinator.UpdateRightStick(
            0.9,
            0,
            0.5,
            blocked: false);
        var neutral = coordinator.UpdateRightStick(
            0,
            0,
            0.5,
            blocked: false);
        var reengaged = coordinator.UpdateRightStick(
            -0.9,
            0,
            0.5,
            blocked: false);

        Assert.True(initial.HorizontalStarted);
        Assert.Equal(default, held);
        Assert.Equal(default, neutral);
        Assert.True(reengaged.HorizontalStarted);
        Assert.Equal(-1, reengaged.HorizontalDirection);
    }

    [Fact]
    public void ResetRoutingRestartsRepeatAtTheInitialDetent()
    {
        long now = 1_000;
        var actions = new List<int>();
        var coordinator = new ControllerInteractionCoordinator(() => now);

        coordinator.RepeatAxis("left-y", 1, 360, 220, actions.Add);
        now += 20;
        coordinator.RepeatAxis("left-y", 1, 360, 220, actions.Add);
        coordinator.ResetRouting();
        coordinator.RepeatAxis("left-y", 1, 360, 220, actions.Add);

        Assert.Equal([1, 1], actions);
    }

    private static ControllerState State(
        ControllerButtons buttons = ControllerButtons.None,
        uint packet = 0) =>
        new(
            true,
            0,
            packet,
            "Test",
            buttons,
            0,
            0,
            0,
            0,
            0,
            0);
}
