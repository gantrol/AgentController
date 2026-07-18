using System.Collections.ObjectModel;

namespace AgentController.Domain.Actions;

public enum ActionOutcome
{
    Succeeded,
    NotSent,
    AcceptedUnverified,
    Unsupported,
    Incompatible,
    Blocked,
    Failed,
}

public sealed class ActionResult
{
    public ActionResult(
        Guid requestId,
        ActionId actionId,
        ActionOutcome outcome,
        string executorId,
        DateTimeOffset completedAt,
        IEnumerable<ActionEvidence>? evidence = null,
        string? errorCode = null)
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

        if (string.IsNullOrWhiteSpace(executorId))
        {
            throw new ArgumentException(
                "Executor identifier must not be empty.",
                nameof(executorId));
        }

        RequestId = requestId;
        ActionId = actionId;
        Outcome = outcome;
        ExecutorId = executorId.Trim();
        CompletedAt = completedAt;
        Evidence = new ReadOnlyCollection<ActionEvidence>(
            (evidence ?? []).ToArray());
        ErrorCode = string.IsNullOrWhiteSpace(errorCode)
            ? null
            : errorCode.Trim();
    }

    public Guid RequestId { get; }

    public ActionId ActionId { get; }

    public ActionOutcome Outcome { get; }

    public string ExecutorId { get; }

    public DateTimeOffset CompletedAt { get; }

    public IReadOnlyList<ActionEvidence> Evidence { get; }

    public string? ErrorCode { get; }
}
