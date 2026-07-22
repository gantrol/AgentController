namespace CodexMicro.Desktop.Services;

internal readonly record struct DialGestureUpdate(
    bool BeganDragging,
    int Steps);

internal sealed class DialGestureTracker
{
    internal const double DragThreshold = 6;
    internal const double StepDistance = 12;
    internal const int WheelStepDelta = 120;

    private enum DragAxis
    {
        None,
        Horizontal,
        Vertical,
    }

    private double _dragOriginX;
    private double _dragOriginY;
    private double _dragRemainder;
    private double _lastDragPosition;
    private int _wheelRemainder;
    private DragAxis _dragAxis;

    public bool IsPointerDown { get; private set; }

    public bool IsDragging { get; private set; }

    public void Begin(double pointerX, double pointerY)
    {
        IsPointerDown = true;
        IsDragging = false;
        _dragAxis = DragAxis.None;
        _dragOriginX = pointerX;
        _dragOriginY = pointerY;
        _lastDragPosition = 0;
        _dragRemainder = 0;
    }

    public DialGestureUpdate Move(double pointerX, double pointerY)
    {
        if (!IsPointerDown)
        {
            return default;
        }

        var beganDragging = false;
        if (!IsDragging)
        {
            var horizontalMovement = pointerX - _dragOriginX;
            var verticalMovement = pointerY - _dragOriginY;
            if (Math.Sqrt(
                    (horizontalMovement * horizontalMovement) +
                    (verticalMovement * verticalMovement)) < DragThreshold)
            {
                return default;
            }

            IsDragging = true;
            beganDragging = true;
            _dragAxis = Math.Abs(horizontalMovement) > Math.Abs(verticalMovement)
                ? DragAxis.Horizontal
                : DragAxis.Vertical;
            _lastDragPosition = _dragAxis == DragAxis.Horizontal
                ? _dragOriginX + (Math.Sign(horizontalMovement) * DragThreshold)
                : _dragOriginY + (Math.Sign(verticalMovement) * DragThreshold);
        }

        // A held pointer may turn the virtual knob along either dominant axis:
        // right/up is clockwise, left/down is counter-clockwise. Locking the
        // axis after the threshold avoids diagonal jitter changing direction.
        var pointerPosition = _dragAxis == DragAxis.Horizontal
            ? pointerX
            : pointerY;
        _dragRemainder += _dragAxis == DragAxis.Horizontal
            ? pointerPosition - _lastDragPosition
            : _lastDragPosition - pointerPosition;
        _lastDragPosition = pointerPosition;
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
        _dragAxis = DragAxis.None;
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
