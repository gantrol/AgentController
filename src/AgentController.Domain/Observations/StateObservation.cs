namespace AgentController.Domain.Observations;

public sealed record StateObservation<T>
    where T : notnull
{
    public StateObservation(
        T value,
        string source,
        long epoch,
        long sequence,
        DateTimeOffset observedAt,
        double confidence)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new ArgumentException(
                "Observation source must not be empty.",
                nameof(source));
        }

        if (epoch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(epoch));
        }

        if (sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        if (double.IsNaN(confidence) || confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence),
                "Confidence must be between 0 and 1.");
        }

        Value = value;
        Source = source.Trim();
        Epoch = epoch;
        Sequence = sequence;
        ObservedAt = observedAt;
        Confidence = confidence;
    }

    public T Value { get; }

    public string Source { get; }

    public long Epoch { get; }

    public long Sequence { get; }

    public DateTimeOffset ObservedAt { get; }

    public double Confidence { get; }
}
