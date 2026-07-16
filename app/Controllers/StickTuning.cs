namespace CodexController.Controllers;

/// <summary>
/// Suggested defaults supplied by a device profile. User settings may
/// override these values.
/// </summary>
public sealed record StickTuning
{
    public StickTuning(
        double stickDeadZone = 0.24,
        double triggerDeadZone = 0.05)
    {
        if (
            !double.IsFinite(stickDeadZone) ||
            stickDeadZone is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stickDeadZone),
                "Stick dead zone must be in the range [0, 1).");
        }

        if (
            !double.IsFinite(triggerDeadZone) ||
            triggerDeadZone is < 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(triggerDeadZone),
                "Trigger dead zone must be in the range [0, 1).");
        }

        StickDeadZone = stickDeadZone;
        TriggerDeadZone = triggerDeadZone;
    }

    public double StickDeadZone { get; }

    public double TriggerDeadZone { get; }
}
