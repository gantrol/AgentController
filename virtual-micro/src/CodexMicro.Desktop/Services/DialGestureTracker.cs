namespace CodexMicro.Desktop.Services;

internal readonly record struct DialGestureUpdate(
    bool BeganDragging,
    int Steps);

internal sealed class DialGestureTracker
{
    internal const double DragThreshold = 6;
    internal const double StepDistance = 12;
    internal const int WheelStepDelta = 120;

    private double _dragOriginY;
    private double _dragRemainder;
    private double _lastDragY;
    private int _wheelRemainder;

    public bool IsPointerDown { get; private set; }

    public bool IsDragging { get; private set; }

    public void Begin(double pointerY)
    {
        IsPointerDown = true;
        IsDragging = false;
        _dragOriginY = pointerY;
        _lastDragY = pointerY;
        _dragRemainder = 0;
    }

    public DialGestureUpdate Move(double pointerY)
    {
        if (!IsPointerDown)
        {
            return default;
        }

        var beganDragging = false;
        if (!IsDragging)
        {
            var totalMovement = pointerY - _dragOriginY;
            if (Math.Abs(totalMovement) < DragThreshold)
            {
                return default;
            }

            IsDragging = true;
            beganDragging = true;
            _lastDragY = _dragOriginY + (Math.Sign(totalMovement) * DragThreshold);
        }

        // Moving upward is clockwise/ArrowUp; moving downward is
        // counter-clockwise/ArrowDown.
        _dragRemainder += _lastDragY - pointerY;
        _lastDragY = pointerY;
        var steps = (int)(_dragRemainder / StepDistance);
        _dragRemainder -= steps * StepDistance;
        return new DialGestureUpdate(beganDragging, steps);
    }

    public bool End()
    {
        if (!IsPointerDown)
        {
            return false;
        }

        var shouldTap = !IsDragging;
        Cancel();
        return shouldTap;
    }

    public void Cancel()
    {
        IsPointerDown = false;
        IsDragging = false;
        _dragRemainder = 0;
    }

    public int AddWheelDelta(int delta)
    {
        _wheelRemainder += delta;
        var steps = _wheelRemainder / WheelStepDelta;
        _wheelRemainder -= steps * WheelStepDelta;
        return steps;
    }
}
