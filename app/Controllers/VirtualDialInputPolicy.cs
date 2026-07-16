namespace CodexController.Controllers;

public static class VirtualDialInputPolicy
{
    private const double MinimumDiscreteDeadZone = 0.30;
    private const double MaximumDiscreteDeadZone = 0.42;

    /// <summary>
    /// The virtual dial is a discrete one-dimensional control, so it can use
    /// the controller profile's calibrated threshold without inheriting the
    /// much larger navigation dead zone.
    /// </summary>
    public static double ResolveDeadZone(
        double navigationDeadZone,
        double? profileDeadZone)
    {
        var userZone = double.IsFinite(navigationDeadZone)
            ? Math.Clamp(navigationDeadZone, 0.10, 0.95)
            : MaximumDiscreteDeadZone;
        var calibratedZone =
            profileDeadZone is { } profile && double.IsFinite(profile)
                ? Math.Clamp(
                    profile,
                    MinimumDiscreteDeadZone,
                    MaximumDiscreteDeadZone)
                : MaximumDiscreteDeadZone;
        return Math.Min(userZone, calibratedZone);
    }

    /// <summary>
    /// A virtual dial detent fires once when the stick leaves neutral.
    /// Holding the stick must not repeat keyboard-like actions in Codex.
    /// </summary>
    public static bool ShouldQueueStep(
        bool horizontalStarted,
        int horizontalDirection) =>
        horizontalStarted &&
        horizontalDirection is -1 or 1;
}
