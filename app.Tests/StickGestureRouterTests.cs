using CodexController.Services;

namespace CodexController.Tests;

public sealed class StickGestureRouterTests
{
    [Fact]
    public void HorizontalGestureFiresOnceUntilStickReturnsToNeutral()
    {
        var router = new StickGestureRouter();

        var first = router.Update(
            x: 0.9,
            y: 0,
            deadZone: 0.5,
            invertVertical: false,
            blocked: false);
        var held = router.Update(
            x: 0.9,
            y: 0,
            deadZone: 0.5,
            invertVertical: false,
            blocked: false);
        _ = router.Update(
            x: 0,
            y: 0,
            deadZone: 0.5,
            invertVertical: false,
            blocked: false);
        var second = router.Update(
            x: -0.9,
            y: 0,
            deadZone: 0.5,
            invertVertical: false,
            blocked: false);

        Assert.Equal(1, first.HorizontalDirection);
        Assert.True(first.HorizontalStarted);
        Assert.Equal(default, held);
        Assert.Equal(-1, second.HorizontalDirection);
        Assert.True(second.HorizontalStarted);
    }

    [Fact]
    public void BlockedGestureRequiresNeutralBeforeInputCanResume()
    {
        var router = new StickGestureRouter();

        var blocked = router.Update(
            x: 0,
            y: 0.9,
            deadZone: 0.5,
            invertVertical: false,
            blocked: true);
        var stillHeld = router.Update(
            x: 0,
            y: 0.9,
            deadZone: 0.5,
            invertVertical: false,
            blocked: false);
        var neutral = router.Update(
            x: 0,
            y: 0,
            deadZone: 0.5,
            invertVertical: false,
            blocked: false);
        var resumed = router.Update(
            x: 0,
            y: 0.9,
            deadZone: 0.5,
            invertVertical: false,
            blocked: false);

        Assert.Equal(default, blocked);
        Assert.Equal(default, stillHeld);
        Assert.Equal(default, neutral);
        Assert.Equal(1, resumed.VerticalDirection);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, -1)]
    public void VerticalGestureHonorsInvertSetting(
        bool invertVertical,
        int expectedDirection)
    {
        var router = new StickGestureRouter();

        var sample = router.Update(
            x: 0,
            y: 0.9,
            deadZone: 0.5,
            invertVertical,
            blocked: false);

        Assert.Equal(expectedDirection, sample.VerticalDirection);
        Assert.Equal(0, sample.HorizontalDirection);
        Assert.False(sample.HorizontalStarted);
    }
}
