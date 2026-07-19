using System.Collections.ObjectModel;

namespace AgentController.Domain.Inputs;

public enum GestureKind
{
    Press,
    Release,
    Hold,
    Chord,
    AxisStep,
}

public sealed class Gesture
{
    private Gesture(
        GestureKind kind,
        IEnumerable<ControlId> controls,
        TimeSpan holdThreshold,
        int stepDelta)
    {
        Kind = kind;
        Controls = new ReadOnlyCollection<ControlId>(controls.ToArray());
        HoldThreshold = holdThreshold;
        StepDelta = stepDelta;
    }

    public GestureKind Kind { get; }

    public IReadOnlyList<ControlId> Controls { get; }

    public TimeSpan HoldThreshold { get; }

    public int StepDelta { get; }

    public static Gesture Press(ControlId control) =>
        SingleControl(GestureKind.Press, control);

    public static Gesture Release(ControlId control) =>
        SingleControl(GestureKind.Release, control);

    public static Gesture Hold(ControlId control, TimeSpan threshold)
    {
        EnsureDefined(control);
        if (threshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold),
                "Hold threshold must be positive.");
        }

        return new Gesture(GestureKind.Hold, [control], threshold, 0);
    }

    public static Gesture Chord(IEnumerable<ControlId> controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        var normalized = controls
            .Select(control =>
            {
                EnsureDefined(control);
                return control;
            })
            .Distinct()
            .OrderBy(control => control.Value, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length < 2)
        {
            throw new ArgumentException(
                "A chord requires at least two distinct controls.",
                nameof(controls));
        }

        return new Gesture(GestureKind.Chord, normalized, TimeSpan.Zero, 0);
    }

    public static Gesture AxisStep(ControlId control, int stepDelta)
    {
        EnsureDefined(control);
        if (stepDelta == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stepDelta),
                "Axis step delta must not be zero.");
        }

        return new Gesture(
            GestureKind.AxisStep,
            [control],
            TimeSpan.Zero,
            stepDelta);
    }

    private static Gesture SingleControl(GestureKind kind, ControlId control)
    {
        EnsureDefined(control);
        return new Gesture(kind, [control], TimeSpan.Zero, 0);
    }

    private static void EnsureDefined(ControlId control)
    {
        if (!control.IsDefined)
        {
            throw new ArgumentException(
                "Control identifier must be defined.",
                nameof(control));
        }
    }
}
