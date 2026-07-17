namespace CodexController.Services;

public readonly record struct AnalogRepeatTiming(
    int InitialDelayMs,
    int IntervalMs);

public static class AnalogRepeatTimingPolicy
{
    private const int MinimumDelayMs = 90;
    private const int MinimumIntervalMs = 70;
    private const double FullTiltDelayScale = 0.38;
    private const double FullTiltIntervalScale = 0.36;

    public static AnalogRepeatTiming Resolve(
        double magnitude,
        double engageDeadZone,
        int configuredDelayMs,
        int configuredIntervalMs)
    {
        var delay = Math.Max(1, configuredDelayMs);
        var interval = Math.Max(1, configuredIntervalMs);
        var deadZone = double.IsFinite(engageDeadZone)
            ? Math.Clamp(engageDeadZone, 0, 0.95)
            : 0.5;
        var strength = double.IsFinite(magnitude)
            ? Math.Clamp(Math.Abs(magnitude), 0, 1)
            : deadZone;
        var normalized = Math.Clamp(
            (strength - deadZone) / (1 - deadZone),
            0,
            1);
        var eased = normalized * normalized * (3 - (2 * normalized));
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
            Round(Lerp(delay, fastestDelay, eased)),
            Round(Lerp(interval, fastestInterval, eased)));
    }

    private static double Lerp(
        double start,
        double end,
        double amount) =>
        start + ((end - start) * amount);

    private static int Round(double value) =>
        (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
