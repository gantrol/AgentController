using CodexController.Services;

namespace CodexController.Tests;

public sealed class AnalogRepeatTimingPolicyTests
{
    [Fact]
    public void AtDeadZoneUsesConfiguredGentleTiming()
    {
        var timing = AnalogRepeatTimingPolicy.Resolve(
            magnitude: 0.42,
            engageDeadZone: 0.42,
            heldDurationMs: 2_000,
            configuredDelayMs: 360,
            configuredIntervalMs: 220);

        Assert.Equal(new(360, 220), timing);
    }

    [Fact]
    public void FullTiltStartsAtConfiguredGentleTiming()
    {
        var timing = AnalogRepeatTimingPolicy.Resolve(
            magnitude: 1,
            engageDeadZone: 0.42,
            heldDurationMs: 0,
            configuredDelayMs: 360,
            configuredIntervalMs: 220);

        Assert.Equal(new(360, 220), timing);
    }

    [Fact]
    public void FullTiltReachesFastestTimingAfterTwoSeconds()
    {
        var timing = AnalogRepeatTimingPolicy.Resolve(
            magnitude: 1,
            engageDeadZone: 0.42,
            heldDurationMs: 2_000,
            configuredDelayMs: 360,
            configuredIntervalMs: 220);

        Assert.Equal(new(137, 79), timing);
    }

    [Fact]
    public void FullTiltAtOneSecondIsHalfwayThroughRamp()
    {
        var gentle = AnalogRepeatTimingPolicy.Resolve(
            1,
            0.42,
            0,
            360,
            220);
        var middle = AnalogRepeatTimingPolicy.Resolve(
            1,
            0.42,
            1_000,
            360,
            220);
        var full = AnalogRepeatTimingPolicy.Resolve(
            1,
            0.42,
            2_000,
            360,
            220);

        Assert.InRange(
            middle.InitialDelayMs,
            full.InitialDelayMs + 1,
            gentle.InitialDelayMs - 1);
        Assert.InRange(
            middle.IntervalMs,
            full.IntervalMs + 1,
            gentle.IntervalMs - 1);
    }

    [Fact]
    public void PartialTiltLimitsMaximumMomentumSpeed()
    {
        var partial = AnalogRepeatTimingPolicy.Resolve(
            magnitude: 0.71,
            engageDeadZone: 0.42,
            heldDurationMs: 2_000,
            configuredDelayMs: 360,
            configuredIntervalMs: 220);
        var full = AnalogRepeatTimingPolicy.Resolve(
            magnitude: 1,
            engageDeadZone: 0.42,
            heldDurationMs: 2_000,
            configuredDelayMs: 360,
            configuredIntervalMs: 220);

        Assert.True(partial.InitialDelayMs > full.InitialDelayMs);
        Assert.True(partial.IntervalMs > full.IntervalMs);
    }

    [Fact]
    public void InvalidMagnitudeFallsBackToGentleTiming()
    {
        var timing = AnalogRepeatTimingPolicy.Resolve(
            double.NaN,
            0.42,
            2_000,
            360,
            220);

        Assert.Equal(new(360, 220), timing);
    }
}
