namespace CodexController.Services;

public readonly record struct StickGestureSample(
    int VerticalDirection,
    int HorizontalDirection,
    bool HorizontalStarted,
    double VerticalMagnitude = 0,
    double HorizontalMagnitude = 0);

public sealed class StickGestureRouter
{
    private StickAxis _lockedAxis;
    private int _lockedDirection;
    private bool _requiresNeutral;

    public StickGestureSample Update(
        double x,
        double y,
        double deadZone,
        bool invertVertical,
        bool blocked)
    {
        var engageZone = StickGestureClassifier.EngageZone(deadZone);
        var releaseZone = StickGestureClassifier.ReleaseZone(deadZone);
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        var neutral = absX < releaseZone && absY < releaseZone;

        if (blocked)
        {
            _lockedAxis = StickAxis.None;
            _lockedDirection = 0;
            _requiresNeutral = !neutral;
            return default;
        }

        if (_requiresNeutral)
        {
            if (neutral)
            {
                _requiresNeutral = false;
                _lockedAxis = StickAxis.None;
                _lockedDirection = 0;
            }

            return default;
        }

        if (neutral)
        {
            _lockedAxis = StickAxis.None;
            _lockedDirection = 0;
            return default;
        }

        if (_lockedAxis == StickAxis.None)
        {
            if (
                absX >= engageZone &&
                absX >= absY * StickGestureClassifier.DominanceRatio)
            {
                _lockedAxis = StickAxis.Horizontal;
                _lockedDirection = Math.Sign(x);
                return new(
                    0,
                    _lockedDirection,
                    HorizontalStarted: true,
                    HorizontalMagnitude: Math.Clamp(absX, 0, 1));
            }

            if (
                absY >= engageZone &&
                absY >= absX * StickGestureClassifier.DominanceRatio)
            {
                _lockedAxis = StickAxis.Vertical;
                _lockedDirection = Math.Sign(y);
            }
            else
            {
                return default;
            }
        }

        if (_lockedAxis == StickAxis.Horizontal)
        {
            if (absX < releaseZone)
            {
                return default;
            }

            var direction = Math.Sign(x);
            if (direction != _lockedDirection)
            {
                return default;
            }

            return new(
                0,
                _lockedDirection,
                HorizontalStarted: false,
                HorizontalMagnitude: Math.Clamp(absX, 0, 1));
        }

        if (absY < releaseZone)
        {
            return default;
        }

        var vertical = Math.Sign(y);
        if (vertical != _lockedDirection)
        {
            return default;
        }

        return new(
            invertVertical ? -vertical : vertical,
            0,
            HorizontalStarted: false,
            VerticalMagnitude: Math.Clamp(absY, 0, 1));
    }

    public void RequireNeutral()
    {
        _requiresNeutral = true;
        _lockedAxis = StickAxis.None;
        _lockedDirection = 0;
    }

    public void Reset()
    {
        _requiresNeutral = false;
        _lockedAxis = StickAxis.None;
        _lockedDirection = 0;
    }

    private enum StickAxis
    {
        None,
        Horizontal,
        Vertical,
    }
}

internal enum StickGestureRegion
{
    Neutral,
    Ambiguous,
    HorizontalNegative,
    HorizontalPositive,
    VerticalNegative,
    VerticalPositive,
}

internal static class StickGestureClassifier
{
    internal const double DominanceRatio = 1.15;

    internal static double EngageZone(double deadZone) =>
        Math.Clamp(deadZone, 0.10, 0.95);

    internal static double ReleaseZone(double deadZone) =>
        EngageZone(deadZone) * 0.68;

    internal static StickGestureRegion Classify(
        double x,
        double y,
        double deadZone)
    {
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        if (
            absX < ReleaseZone(deadZone) &&
            absY < ReleaseZone(deadZone))
        {
            return StickGestureRegion.Neutral;
        }

        var engageZone = EngageZone(deadZone);
        if (
            absX >= engageZone &&
            absX >= absY * DominanceRatio)
        {
            return x < 0
                ? StickGestureRegion.HorizontalNegative
                : StickGestureRegion.HorizontalPositive;
        }

        if (
            absY >= engageZone &&
            absY >= absX * DominanceRatio)
        {
            return y < 0
                ? StickGestureRegion.VerticalNegative
                : StickGestureRegion.VerticalPositive;
        }

        return StickGestureRegion.Ambiguous;
    }
}
