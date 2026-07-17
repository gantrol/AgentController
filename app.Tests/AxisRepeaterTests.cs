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
    public void IncreasingDeflectionAdvancesPendingRepeat()
    {
        long now = 0;
        var actions = new List<int>();
        var repeater = new AxisRepeater(() => now);

        repeater.Update("right", 1, 360, 220, actions.Add);
        now = 100;
        repeater.Update("right", 1, 140, 80, actions.Add);
        now = 139;
        repeater.Update("right", 1, 140, 80, actions.Add);
        now = 140;
        repeater.Update("right", 1, 140, 80, actions.Add);

        Assert.Equal([1, 1], actions);
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
}
