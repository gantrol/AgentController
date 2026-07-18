using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class DialGestureTrackerTests
{
    [Fact]
    public void ShortPointerGestureBecomesEncoderTap()
    {
        var tracker = new DialGestureTracker();

        tracker.Begin(40);
        var update = tracker.Move(44);

        Assert.Equal(0, update.Steps);
        Assert.False(update.BeganDragging);
        Assert.True(tracker.End());
    }

    [Fact]
    public void VerticalDragProducesDetentsAndSuppressesTap()
    {
        var tracker = new DialGestureTracker();

        tracker.Begin(50);
        var upward = tracker.Move(25);

        Assert.True(upward.BeganDragging);
        Assert.Equal(1, upward.Steps);
        Assert.False(tracker.End());

        tracker.Begin(20);
        var downward = tracker.Move(48);

        Assert.True(downward.BeganDragging);
        Assert.Equal(-1, downward.Steps);
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
