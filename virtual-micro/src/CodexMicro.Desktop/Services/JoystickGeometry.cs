namespace CodexMicro.Desktop.Services;

internal readonly record struct JoystickVector(
    double Angle,
    double Distance,
    double VisualX,
    double VisualY,
    string? Direction);

internal static class JoystickGeometry
{
    internal const double DefaultInputRadius = 24;
    internal const double DefaultVisualTravel = 13;
    internal const double ActivationDistance = 0.5;

    internal static JoystickVector ResolveDelta(
        double deltaX,
        double deltaY,
        double inputRadius = DefaultInputRadius,
        double visualTravel = DefaultVisualTravel)
    {
        if (
            !double.IsFinite(deltaX) ||
            !double.IsFinite(deltaY) ||
            !double.IsFinite(inputRadius) ||
            !double.IsFinite(visualTravel) ||
            inputRadius <= 0 ||
            visualTravel < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputRadius),
                "Joystick geometry requires finite values and positive travel radii.");
        }

        var radius = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (radius <= double.Epsilon)
        {
            return new JoystickVector(0, 0, 0, 0, null);
        }

        var distance = Math.Clamp(radius / inputRadius, 0, 1);
        var angle = Math.Atan2(deltaY, deltaX) / Math.Tau;
        if (angle < 0)
        {
            angle += 1;
        }

        var visibleDistance = distance * visualTravel;
        return new JoystickVector(
            angle,
            distance,
            (deltaX / radius) * visibleDistance,
            (deltaY / radius) * visibleDistance,
            DirectionForAngle(angle));
    }

    internal static string DirectionForAngle(double angle)
    {
        var normalized = angle - Math.Floor(angle);
        if (normalized >= 0.625 && normalized < 0.875)
        {
            return "up";
        }

        if (normalized >= 0.125 && normalized < 0.375)
        {
            return "down";
        }

        if (normalized >= 0.375 && normalized < 0.625)
        {
            return "left";
        }

        return "right";
    }
}
