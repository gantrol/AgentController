using CodexController.Models;

namespace CodexController.Controllers;

public enum AnalogTriggerTransition
{
    None,
    Pressed,
    Released,
    Canceled,
}

/// <summary>
/// Converts an analog trigger into stable press/release transitions with
/// hysteresis. Blocking or canceling requires a physical release before the
/// trigger can engage again.
/// </summary>
public sealed class AnalogTriggerLatch
{
    private readonly double _engageThreshold;
    private readonly double _releaseThreshold;
    private bool _requiresRelease;

    public AnalogTriggerLatch(
        double engageThreshold,
        double releaseThreshold)
    {
        if (
            !double.IsFinite(engageThreshold) ||
            !double.IsFinite(releaseThreshold) ||
            engageThreshold is <= 0 or > 1 ||
            releaseThreshold < 0 ||
            releaseThreshold >= engageThreshold)
        {
            throw new ArgumentOutOfRangeException(
                nameof(engageThreshold),
                "Trigger thresholds must satisfy " +
                "0 <= release < engage <= 1.");
        }

        _engageThreshold = engageThreshold;
        _releaseThreshold = releaseThreshold;
    }

    public bool IsPressed { get; private set; }

    public bool RequiresRelease => _requiresRelease;

    public bool BlocksBaseInput => IsPressed || _requiresRelease;

    public AnalogTriggerTransition Update(
        double value,
        bool blocked = false)
    {
        value = double.IsFinite(value)
            ? Math.Clamp(value, 0, 1)
            : 0;

        if (blocked)
        {
            var wasPressed = IsPressed;
            IsPressed = false;
            _requiresRelease = value > _releaseThreshold;
            return wasPressed
                ? AnalogTriggerTransition.Canceled
                : AnalogTriggerTransition.None;
        }

        if (_requiresRelease)
        {
            if (value <= _releaseThreshold)
            {
                _requiresRelease = false;
            }

            return AnalogTriggerTransition.None;
        }

        if (!IsPressed && value >= _engageThreshold)
        {
            IsPressed = true;
            return AnalogTriggerTransition.Pressed;
        }

        if (IsPressed && value <= _releaseThreshold)
        {
            IsPressed = false;
            return AnalogTriggerTransition.Released;
        }

        return AnalogTriggerTransition.None;
    }

    public bool CancelUntilReleased()
    {
        var wasPressed = IsPressed;
        IsPressed = false;
        _requiresRelease = true;
        return wasPressed;
    }
}

public static class PushToTalkInputPolicy
{
    public static ControllerButtons FrozenBaseButtons =>
        RadialInputMap.FrozenBaseButtons & ~ControllerButtons.B;

    public static bool ShouldBlockTrigger(bool radialLayerActive) =>
        radialLayerActive;

    public static bool ShouldPreemptDialReleaseDrain(
        double triggerValue,
        double engageThreshold) =>
        double.IsFinite(triggerValue) &&
        triggerValue >= engageThreshold;

    public static ControllerButtons ButtonsToSuppress(
        ControllerButtons pressed) =>
        pressed & ~ControllerButtons.B;
}
