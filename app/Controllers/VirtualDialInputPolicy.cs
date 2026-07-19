using CodexController.Services;

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
    /// The gamepad adds a second axis around Micro's encoder semantics.
    /// Horizontal motion always keeps its literal screen direction so a
    /// left push can never be reinterpreted as an encoder "down" step.
    /// </summary>
    public static ComposerDialNavigation? ResolveHorizontalNavigation(
        int direction) =>
        Math.Sign(direction) switch
        {
            -1 => ComposerDialNavigation.Left,
            1 => ComposerDialNavigation.Right,
            _ => null,
        };

    /// <summary>
    /// Vertical motion is the physical Micro encoder projection. It never
    /// depends on Agent Controller's popup guess: Codex owns composer and
    /// menu traversal for every encoder detent.
    /// </summary>
    public static int ResolveVerticalEncoderSteps(int direction) =>
        Math.Sign(direction);
}
