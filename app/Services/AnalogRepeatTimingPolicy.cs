namespace CodexController.Services;

public readonly record struct AnalogRepeatTiming(
    int InitialDelayMs,
    int IntervalMs);

public static class AnalogRepeatTimingPolicy
{
    public const int DefaultAccelerationDurationMs = 2_000;

    private const int MinimumDelayMs = 90;
    private const int MinimumIntervalMs = 70;
    private const double FullTiltDelayScale = 0.38;
    private const double FullTiltIntervalScale = 0.36;

    public static AnalogRepeatTiming Resolve(
        double magnitude,
        double engageDeadZone,
        long heldDurationMs,
        int configuredDelayMs,
        int configuredIntervalMs,
        int accelerationDurationMs =
            DefaultAccelerationDurationMs)
    {
        var delay = Math.Max(1, configuredDelayMs);
        var interval = Math.Max(1, configuredIntervalMs);
        var deadZone = double.IsFinite(engageDeadZone)
            ? Math.Clamp(engageDeadZone, 0, 0.95)
            : 0.5;
        var strength = double.IsFinite(magnitude)
            ? Math.Clamp(Math.Abs(magnitude), 0, 1)
            : deadZone;
        var normalizedStrength = Math.Clamp(
            (strength - deadZone) / (1 - deadZone),
            0,
            1);
        var strengthAmount = SmoothStep(normalizedStrength);
        var safeAccelerationDuration =
            Math.Max(1, accelerationDurationMs);
        var normalizedTime = Math.Clamp(
            Math.Max(0, heldDurationMs) /
                (double)safeAccelerationDuration,
            0,
            1);
        var momentumAmount =
            strengthAmount * SmoothStep(normalizedTime);
        var fastestDelay = Math.Min(
            delay,
            Math.Max(
                MinimumDelayMs,
                Round(delay * FullTiltDelayScale)));
        var fastestInterval = Math.Min(
            interval,
            Math.Max(
                MinimumIntervalMs,
                Round(interval * FullTiltIntervalScale)));

        return new(
            Round(Lerp(delay, fastestDelay, momentumAmount)),
            Round(Lerp(interval, fastestInterval, momentumAmount)));
    }

    private static double SmoothStep(double amount) =>
        amount * amount * (3 - (2 * amount));

    private static double Lerp(
        double start,
        double end,
        double amount) =>
        start + ((end - start) * amount);

    private static int Round(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
