using System.Collections.ObjectModel;

namespace CodexController.Core.Bridge;

public enum BridgeEventSeverity
{
    Debug,
    Info,
    Success,
    Warning,
    Error,
}

public enum BridgeOverlayTarget
{
    Footer,
    Toast,
    FooterAndToast,
}

public sealed record BridgeOverlayMetadata(
    BridgeOverlayTarget Target,
    TimeSpan? Duration = null,
    string? CoalesceKey = null);

public sealed record BridgeEvent
{
    public BridgeEvent(
        BridgeEventKey key,
        DateTimeOffset timestamp,
        BridgeEventSeverity severity,
        IReadOnlyDictionary<string, string>? parameters = null,
        BridgeOverlayMetadata? overlay = null)
    {
        if (!key.IsInitialized)
        {
            throw new ArgumentException(
                "A bridge event key is required.",
                nameof(key));
        }

        Key = key;
        Timestamp = timestamp;
        Severity = severity;
        Parameters = CopyParameters(parameters);
        Overlay = overlay;
    }

    public BridgeEventKey Key { get; }
    public DateTimeOffset Timestamp { get; }
    public BridgeEventSeverity Severity { get; }
    public IReadOnlyDictionary<string, string> Parameters { get; }
    public BridgeOverlayMetadata? Overlay { get; }

    private static IReadOnlyDictionary<string, string> CopyParameters(
        IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return EmptyParameters;
        }

        return new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(
                parameters,
                StringComparer.Ordinal));
    }

    private static readonly IReadOnlyDictionary<string, string>
        EmptyParameters =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(
                    StringComparer.Ordinal));
}
