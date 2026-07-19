namespace AgentController.Domain.Actions;

public enum ActionEvidenceKind
{
    Transport,
    State,
    UiObservation,
}

public sealed record ActionEvidence
{
    public ActionEvidence(
        ActionEvidenceKind kind,
        string source,
        string code,
        DateTimeOffset observedAt,
        double confidence)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException(
                "Evidence source must not be empty.",
                nameof(source));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Evidence code must not be empty.",
                nameof(code));
        }

        if (double.IsNaN(confidence) || confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                "Confidence must be between 0 and 1.");
        }

        Kind = kind;
        Source = source.Trim();
        Code = code.Trim();
        ObservedAt = observedAt;
        Confidence = confidence;
    }

    public ActionEvidenceKind Kind { get; }

    public string Source { get; }

    public string Code { get; }

    public DateTimeOffset ObservedAt { get; }

    public double Confidence { get; }
}
