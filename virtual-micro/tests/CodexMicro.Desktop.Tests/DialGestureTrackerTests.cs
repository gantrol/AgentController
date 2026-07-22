using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class DialGestureTrackerTests
{
    [Fact]
    public void ShortPointerGestureBecomesEncoderTap()
    {
        var tracker = new DialGestureTracker();

        tracker.Begin(20, 40);
        var update = tracker.Move(23, 44);

        Assert.Equal(0, update.Steps);
        Assert.False(update.BeganDragging);
        Assert.True(tracker.End());
    }

    [Fact]
    public void VerticalDragProducesDetentsAndSuppressesTap()
    {
        var tracker = new DialGestureTracker();

        tracker.Begin(40, 50);
        var upward = tracker.Move(40, 25);

        Assert.True(upward.BeganDragging);
        Assert.Equal(1, upward.Steps);
        Assert.False(tracker.End());

        tracker.Begin(40, 20);
        var downward = tracker.Move(40, 48);

        Assert.True(downward.BeganDragging);
        Assert.Equal(-1, downward.Steps);
        Assert.False(tracker.End());
    }

    [Fact]
    public void HeldPointerCanBeginTurningWithAHorizontalDrag()
    {
        var tracker = new DialGestureTracker();

        tracker.Begin(40, 40);
        Assert.Equal(0, tracker.Move(40, 40).Steps);
        Assert.Equal(0, tracker.Move(42, 40).Steps);

        var clockwise = tracker.Move(66, 41);

        Assert.True(clockwise.BeganDragging);
        Assert.Equal(1, clockwise.Steps);
        Assert.False(tracker.End());

        tracker.Begin(60, 40);
        var counterClockwise = tracker.Move(34, 39);

        Assert.True(counterClockwise.BeganDragging);
        Assert.Equal(-1, counterClockwise.Steps);
        Assert.False(tracker.End());
    }

    [Fact]
    public void WheelAccumulatesHighResolutionDeltas()
    {
        var tracker = new DialGestureTracker();

        Assert.Equal(0, tracker.AddWheelDelta(60));
        Assert.Equal(1, tracker.AddWheelDelta(60));
        Assert.Equal(-1, tracker.AddWheelDelta(-120));
    }
}
