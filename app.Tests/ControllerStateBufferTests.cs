using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class ControllerStateBufferTests
{
    [Fact]
    public void PreservesFastAButtonDownAndUp()
    {
        var buffer = new ControllerStateBuffer();

        Assert.True(buffer.Enqueue(State(packet: 1)));
        Assert.False(buffer.Enqueue(State(
            buttons: ControllerButtons.A,
            packet: 2)));
        Assert.False(buffer.Enqueue(State(packet: 3)));

        var drained = buffer.Drain();

        Assert.Equal(
            [
                ControllerButtons.None,
                ControllerButtons.A,
                ControllerButtons.None,
            ],
            drained.Select(state => state.Buttons));
        Assert.Equal(
            [1u, 2u, 3u],
            drained.Select(state => state.PacketNumber));
    }

    [Fact]
    public void PreservesFastRightThumbTap()
    {
        var buffer = new ControllerStateBuffer();

        buffer.Enqueue(State(packet: 1));
        buffer.Enqueue(State(
            buttons: ControllerButtons.RightThumb,
            packet: 2));
        buffer.Enqueue(State(packet: 3));

        var drained = buffer.Drain();

        Assert.Equal(
            [
                ControllerButtons.None,
                ControllerButtons.RightThumb,
                ControllerButtons.None,
            ],
            drained.Select(state => state.Buttons));
    }

    [Fact]
    public void PreservesConnectionChanges()
    {
        var buffer = new ControllerStateBuffer();

        buffer.Enqueue(ControllerState.Disconnected);
        buffer.Enqueue(State(packet: 1));
        buffer.Enqueue(ControllerState.Disconnected);

        Assert.Equal(
            [false, true, false],
            buffer.Drain().Select(state => state.IsConnected));
    }

    [Fact]
    public void PreservesPushToTalkThresholdCrossingsInOrder()
    {
        var buffer = new ControllerStateBuffer();

        buffer.Enqueue(State(leftTrigger: 0.10, packet: 1));
        buffer.Enqueue(State(leftTrigger: 0.36, packet: 2));
        buffer.Enqueue(State(leftTrigger: 0.42, packet: 3));
        buffer.Enqueue(State(leftTrigger: 0.19, packet: 4));

        var drained = buffer.Drain();

        Assert.Equal(
            [0.10, 0.42, 0.19],
            drained.Select(state => state.LeftTrigger));
        Assert.Equal(
            [1u, 3u, 4u],
            drained.Select(state => state.PacketNumber));
    }

    [Fact]
    public void PreservesRadialTriggerThresholdCrossingsInOrder()
    {
        var buffer = new ControllerStateBuffer();

        buffer.Enqueue(State(rightTrigger: 0.00, packet: 1));
        buffer.Enqueue(State(rightTrigger: 0.13, packet: 2));
        buffer.Enqueue(State(rightTrigger: 0.30, packet: 3));
        buffer.Enqueue(State(rightTrigger: 0.56, packet: 4));
        buffer.Enqueue(State(rightTrigger: 0.40, packet: 5));
        buffer.Enqueue(State(rightTrigger: 0.34, packet: 6));
        buffer.Enqueue(State(rightTrigger: 0.10, packet: 7));
        buffer.Enqueue(State(rightTrigger: 0.07, packet: 8));

        var drained = buffer.Drain();

        Assert.Equal(
            [0.00, 0.30, 0.56, 0.40, 0.34, 0.10, 0.07],
            drained.Select(state => state.RightTrigger));
        Assert.Equal(
            [1u, 3u, 4u, 5u, 6u, 7u, 8u],
            drained.Select(state => state.PacketNumber));
    }

    [Fact]
    public void CoalescesPureAxisUpdatesToLatestState()
    {
        var buffer = new ControllerStateBuffer();

        buffer.Enqueue(State(
            leftX: 0.45,
            leftY: 0.65,
            rightX: -0.35,
            rightY: -0.55,
            leftTrigger: 0.25,
            rightTrigger: 0.20,
            packet: 1));
        buffer.Enqueue(State(
            leftX: 0.55,
            leftY: 0.75,
            rightX: -0.45,
            rightY: -0.65,
            leftTrigger: 0.25,
            rightTrigger: 0.20,
            packet: 2));

        var state = Assert.Single(buffer.Drain());

        Assert.Equal(0.55, state.LeftX);
        Assert.Equal(0.75, state.LeftY);
        Assert.Equal(-0.45, state.RightX);
        Assert.Equal(-0.65, state.RightY);
        Assert.Equal(0.25, state.LeftTrigger);
        Assert.Equal(0.20, state.RightTrigger);
        Assert.Equal(2u, state.PacketNumber);
    }

    [Fact]
    public void PreservesNeutralBetweenOrthogonalRightStickGestures()
    {
        var buffer = new ControllerStateBuffer();

        buffer.Enqueue(State(rightX: 0.9, packet: 1));
        buffer.Enqueue(State(packet: 2));
        buffer.Enqueue(State(rightY: 0.9, packet: 3));

        var drained = buffer.Drain();

        Assert.Equal([1u, 2u, 3u], drained.Select(x => x.PacketNumber));
        Assert.Equal([0.9, 0, 0], drained.Select(x => x.RightX));
        Assert.Equal([0, 0, 0.9], drained.Select(x => x.RightY));
    }

    [Fact]
    public void PreservesFirstAxisWhenDirectionChangesWithoutNeutral()
    {
        var buffer = new ControllerStateBuffer();

        buffer.Enqueue(State(rightX: 0.9, packet: 1));
        buffer.Enqueue(State(rightY: 0.9, packet: 2));

        Assert.Equal(
            [1u, 2u],
            buffer.Drain().Select(x => x.PacketNumber));
    }

    [Fact]
    public void IsBoundedAndKeepsTheNewestEdgeSequence()
    {
        var buffer = new ControllerStateBuffer(capacity: 3);

        buffer.Enqueue(State(packet: 1));
        buffer.Enqueue(State(
            buttons: ControllerButtons.A,
            packet: 2));
        buffer.Enqueue(State(packet: 3));
        buffer.Enqueue(State(
            buttons: ControllerButtons.RightThumb,
            packet: 4));

        var drained = buffer.Drain();

        Assert.Equal(3, drained.Length);
        Assert.Equal(1, buffer.DroppedStateCount);
        Assert.Equal(
            [
                ControllerButtons.A,
                ControllerButtons.None,
                ControllerButtons.RightThumb,
            ],
            drained.Select(state => state.Buttons));
    }

    [Fact]
    public void RequestsOnlyOneDispatcherDrainUntilDrained()
    {
        var buffer = new ControllerStateBuffer();

        Assert.True(buffer.Enqueue(State(packet: 1)));
        Assert.False(buffer.Enqueue(State(
            buttons: ControllerButtons.A,
            packet: 2)));

        Assert.Equal(2, buffer.Drain().Length);

        Assert.True(buffer.Enqueue(State(packet: 3)));
    }

    private static ControllerState State(
        ControllerButtons buttons = ControllerButtons.None,
        double leftX = 0,
        double leftY = 0,
        double rightX = 0,
        double rightY = 0,
        double leftTrigger = 0,
        double rightTrigger = 0,
        uint packet = 0)
    {
        return new ControllerState(
            true,
            0,
            packet,
            "Test",
            buttons,
            leftX,
            leftY,
            rightX,
            rightY,
            leftTrigger,
            rightTrigger);
    }
}
