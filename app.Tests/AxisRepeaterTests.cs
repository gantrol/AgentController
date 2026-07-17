using CodexController.Services;

namespace CodexController.Tests;

public sealed class AxisRepeaterTests
{
    [Fact]
    public void FirstDetentMovesImmediatelyThenWaitsForDelay()
    {
        long now = 1_000;
        var actions = new List<int>();
        var repeater = new AxisRepeater(() => now);

        repeater.Update("right", 1, 360, 220, actions.Add);
        now += 359;
        repeater.Update("right", 1, 360, 220, actions.Add);
        now += 1;
        repeater.Update("right", 1, 360, 220, actions.Add);

        Assert.Equal([1, 1], actions);
    }

    [Fact]
    public void FullTiltDoesNotJumpToMaximumSpeedImmediately()
    {
        long now = 0;
        var actions = new List<int>();
        var repeater = new AxisRepeater(() => now);

        repeater.UpdateAnalog(
            "right",
            1,
            1,
            0.42,
            360,
            220,
            actions.Add);
        now = 137;
        repeater.UpdateAnalog(
            "right",
            1,
            1,
            0.42,
            360,
            220,
            actions.Add);

        Assert.Equal([1], actions);
    }

    [Fact]
    public void FullTiltReachesMaximumRepeatIntervalAfterTwoSeconds()
    {
        long now = 0;
        var actions = new List<int>();
        var repeater = new AxisRepeater(() => now);

        UpdateAnalog(repeater, actions);
        now = 360;
        UpdateAnalog(repeater, actions);
        now = 2_000;
        UpdateAnalog(repeater, actions);
        now = 2_078;
        UpdateAnalog(repeater, actions);
        now = 2_079;
        UpdateAnalog(repeater, actions);

        Assert.Equal([1, 1, 1, 1], actions);
    }

    [Fact]
    public void ReturningToNeutralRestartsMomentumRamp()
    {
        long now = 0;
        var actions = new List<int>();
        var repeater = new AxisRepeater(() => now);

        UpdateAnalog(repeater, actions);
        now = 2_000;
        UpdateAnalog(repeater, actions);
        repeater.UpdateAnalog(
            "right",
            0,
            0,
            0.42,
            360,
            220,
            actions.Add);
        now = 3_000;
        UpdateAnalog(repeater, actions);
        now = 3_137;
        UpdateAnalog(repeater, actions);

        Assert.Equal([1, 1, 1], actions);
    }

    [Fact]
    public void DecreasingDeflectionDoesNotBurstCatchUpActions()
    {
        long now = 0;
        var actions = new List<int>();
        var repeater = new AxisRepeater(() => now);

        repeater.Update("right", 1, 100, 70, actions.Add);
        now = 100;
        repeater.Update("right", 1, 100, 70, actions.Add);
        now = 120;
        repeater.Update("right", 1, 360, 220, actions.Add);
        now = 170;
        repeater.Update("right", 1, 360, 220, actions.Add);
        now = 300;
        repeater.Update("right", 1, 360, 220, actions.Add);

        Assert.Equal([1, 1, 1], actions);
    }

    [Fact]
    public void NeutralAndDirectionChangeBothStartNewDetent()
    {
        long now = 0;
        var actions = new List<int>();
        var repeater = new AxisRepeater(() => now);

        repeater.Update("right", 1, 360, 220, actions.Add);
        repeater.Update("right", 0, 360, 220, actions.Add);
        repeater.Update("right", -1, 360, 220, actions.Add);
        repeater.Update("right", 1, 360, 220, actions.Add);

        Assert.Equal([1, -1, 1], actions);
    }

    private static void UpdateAnalog(
        AxisRepeater repeater,
        List<int> actions)
    {
        repeater.UpdateAnalog(
            "right",
            1,
            1,
            0.42,
            360,
            220,
            actions.Add);
    }
}
