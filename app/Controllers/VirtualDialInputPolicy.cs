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
    /// A stick has two axes while the Micro encoder has one. Both axes are
    /// therefore projected onto the same previous/next contract: left is the
    /// same as up (clockwise/previous), and right is the same as down
    /// (counter-clockwise/next). Enter and confirmation belong exclusively to
    /// the encoder press (R3), never to a direction.
    /// </summary>
    public static int ResolveHorizontalEncoderSteps(int direction) =>
        -Math.Sign(direction);

    /// <summary>
    /// Vertical motion is the physical Micro encoder projection. It never
    /// depends on Agent Controller's popup guess: Codex owns composer and
    /// menu traversal for every encoder detent.
    /// </summary>
    public static int ResolveVerticalEncoderSteps(int direction) =>
        Math.Sign(direction);
}
