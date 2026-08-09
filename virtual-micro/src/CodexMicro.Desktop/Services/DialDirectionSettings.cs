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
}
