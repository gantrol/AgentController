namespace CodexMicro.Desktop.Services;

public sealed class DialDirectionSettings
{
    public DialDirectionSettings(bool invertDirection = false)
    {
        InvertDirection = invertDirection;
    }

    public bool InvertDirection { get; set; }

    internal bool ToReportedClockwise(bool physicalClockwise) =>
        InvertDirection ? !physicalClockwise : physicalClockwise;

    internal static int ToReasoningStep(bool reportedClockwise) =>
        reportedClockwise ? -1 : 1;

    internal int ToReasoningSteps(int physicalSteps) =>
        physicalSteps == 0 ? 0 :
            ToReasoningStep(ToReportedClockwise(physicalSteps > 0)) * Math.Abs(physicalSteps);
}
