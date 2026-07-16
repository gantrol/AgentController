namespace CodexController.Services;

public readonly record struct StickGestureSample(
    int VerticalDirection,
    int HorizontalDirection,
    bool HorizontalStarted);

public sealed class StickGestureRouter
{
    private StickAxis _lockedAxis;
    private int _horizontalDirection;
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
            _horizontalDirection = 0;
            _requiresNeutral = !neutral;
            return default;
        }

        if (_requiresNeutral)
        {
            if (neutral)
            {
                _requiresNeutral = false;
                _lockedAxis = StickAxis.None;
                _horizontalDirection = 0;
            }

            return default;
        }

        if (neutral)
        {
            _lockedAxis = StickAxis.None;
            _horizontalDirection = 0;
            return default;
        }

        if (_lockedAxis == StickAxis.None)
        {
            if (absX >= engageZone && absX >= absY * 1.15)
            {
                _lockedAxis = StickAxis.Horizontal;
                _horizontalDirection = x > 0 ? 1 : -1;
                return new(
                    0,
                    _horizontalDirection,
                    HorizontalStarted: true);
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
            if (absX < releaseZone)
            {
                return default;
            }

            return new(
                0,
                _horizontalDirection,
                HorizontalStarted: false);
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
        _horizontalDirection = 0;
    }

    public void Reset()
    {
        _requiresNeutral = false;
        _lockedAxis = StickAxis.None;
        _horizontalDirection = 0;
    }

    private enum StickAxis
    {
        None,
        Horizontal,
        Vertical,
    }
}
