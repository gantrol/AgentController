using AgentController.Application.Actions;
using AgentController.Domain.Actions;

namespace CodexController.Agents.Codex;

public sealed class CodexOpenThreadActionExecutor : IActionExecutor
{
    public const string ExecutorId = "codex.deep-link";

    private readonly Func<string, bool>? _openThread;
    private readonly Func<DateTimeOffset> _clock;

    public CodexOpenThreadActionExecutor(
        Func<string, bool>? openThread,
        Func<DateTimeOffset>? clock = null)
    {
        _openThread = openThread;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Id => ExecutorId;

    public ValueTask<ExecutorCapability> ProbeAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CapabilityFor(request));
    }

    public ValueTask<ActionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var capability = CapabilityFor(request);
        if (capability.Status != ExecutorCapabilityStatus.Available)
        {
            return ValueTask.FromResult(new ActionResult(
                request.RequestId,
                request.ActionId,
                capability.Status == ExecutorCapabilityStatus.Blocked
                    ? ActionOutcome.Blocked
                    : ActionOutcome.Unsupported,
                Id,
                _clock(),
                errorCode: capability.ReasonCode));
        }

        var opened = _openThread!(
            request.Parameters[OpenThreadActionContract.ThreadIdParameter]);
        var completedAt = _clock();
        return ValueTask.FromResult(new ActionResult(
            request.RequestId,
            request.ActionId,
            opened
                ? ActionOutcome.AcceptedUnverified
                : ActionOutcome.NotSent,
            Id,
            completedAt,
            evidence: opened
                ?
                [
                    new ActionEvidence(
                        ActionEvidenceKind.Transport,
                        Id,
                        "thread.open.requested",
                        completedAt,
                        confidence: 1),
                ]
                : null,
            errorCode: opened ? null : "thread.open.not-sent"));
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
    {
        if (request.ActionId != OpenThreadActionContract.Id)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "action.unsupported");
        }

        if (_openThread is null)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "agent.deep-link.unavailable");
        }

        if (!request.Parameters.TryGetValue(
                OpenThreadActionContract.ThreadIdParameter,
                out var threadId) ||
            string.IsNullOrWhiteSpace(threadId))
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Blocked,
                Priority: 100,
                ReasonCode: "thread.id.missing");
        }

        return new ExecutorCapability(
            Id,
            request.ActionId,
            ExecutorCapabilityStatus.Available,
            Priority: 100);
    }
}
