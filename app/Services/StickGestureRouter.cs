namespace CodexController.Services;

public readonly record struct StickGestureSample(
    int VerticalDirection,
    int HorizontalDirection,
    bool HorizontalStarted);

public sealed class StickGestureRouter
{
    private StickAxis _lockedAxis;
    private bool _requiresNeutral;

    public StickGestureSample Update(
        double x,
        double y,
        double deadZone,
        bool invertVertical,
        bool blocked)
    {
        var engageZone = Math.Clamp(deadZone, 0.10, 0.95);
        var releaseZone = engageZone * 0.68;
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        var neutral = absX < releaseZone && absY < releaseZone;

        if (blocked)
        {
            _lockedAxis = StickAxis.None;
            _requiresNeutral = !neutral;
            return default;
        }

        if (_requiresNeutral)
        {
            if (neutral)
            {
                _requiresNeutral = false;
                _lockedAxis = StickAxis.None;
            }

            return default;
        }

        if (neutral)
        {
            _lockedAxis = StickAxis.None;
            return default;
        }

        if (_lockedAxis == StickAxis.None)
        {
            if (absX >= engageZone && absX >= absY * 1.15)
            {
                _lockedAxis = StickAxis.Horizontal;
                var direction = x > 0 ? 1 : -1;
                return new(0, direction, HorizontalStarted: true);
            }

            if (absY >= engageZone && absY >= absX * 1.15)
            {
                _lockedAxis = StickAxis.Vertical;
            }
            else
            {
                return default;
            }
        }

        if (_lockedAxis == StickAxis.Horizontal)
        {
            return default;
        }

        if (absY < releaseZone)
        {
            return default;
        }

        var vertical = y > 0 ? 1 : -1;
        return new(
            invertVertical ? -vertical : vertical,
            0,
            HorizontalStarted: false);
    }

    public void RequireNeutral()
    {
        _requiresNeutral = true;
        _lockedAxis = StickAxis.None;
    }

    public void Reset()
    {
        _requiresNeutral = false;
        _lockedAxis = StickAxis.None;
    }

    private enum StickAxis
    {
        None,
        Horizontal,
        Vertical,
    }
}
