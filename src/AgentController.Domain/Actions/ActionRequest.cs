using System.Collections.ObjectModel;
using AgentController.Domain.Identifiers;
using AgentController.Domain.Inputs;

namespace AgentController.Domain.Actions;

public enum ActionSafetyLevel
{
    Routine,
    ConfirmationRequired,
    HighRisk,
}

public sealed record ActionSource
{
    public ActionSource(string deviceId, ControlId controlId)
    {
        DeviceId = IdentifierRules.Normalize(deviceId, nameof(deviceId));
        if (!controlId.IsDefined)
        {
            throw new ArgumentException(
                "Control identifier must be defined.",
                nameof(controlId));
        }

        ControlId = controlId;
    }

    public string DeviceId { get; }

    public ControlId ControlId { get; }
}

public sealed class ActionRequest
{
    public ActionRequest(
        Guid requestId,
        ActionId actionId,
        ActionSource source,
        InputContext context,
        string idempotencyKey,
        ActionSafetyLevel safetyLevel,
        DateTimeOffset requestedAt,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        if (requestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Request identifier must not be empty.",
                nameof(requestId));
        }

        if (!actionId.IsDefined)
        {
            throw new ArgumentException(
                "Action identifier must be defined.",
                nameof(actionId));
        }

        if (!context.IsDefined)
        {
            throw new ArgumentException(
                "Input context must be defined.",
                nameof(context));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key must not be empty.",
                nameof(idempotencyKey));
        }

        RequestId = requestId;
        ActionId = actionId;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Context = context;
        IdempotencyKey = idempotencyKey.Trim();
        SafetyLevel = safetyLevel;
        RequestedAt = requestedAt;
        Parameters = CopyParameters(parameters);
    }

    public Guid RequestId { get; }

    public ActionId ActionId { get; }

    public ActionSource Source { get; }

    public InputContext Context { get; }

    public string IdempotencyKey { get; }

    public ActionSafetyLevel SafetyLevel { get; }

    public DateTimeOffset RequestedAt { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }

    private static IReadOnlyDictionary<string, string> CopyParameters(
        IReadOnlyDictionary<string, string>? parameters)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parameters is not null)
        {
            foreach (var pair in parameters)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)
                {
                    throw new ArgumentException(
                        "Action parameter keys and values must not be null or empty.",
                        nameof(parameters));
                }

                copy.Add(pair.Key, pair.Value);
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
