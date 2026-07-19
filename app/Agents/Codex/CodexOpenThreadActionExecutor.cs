using AgentController.Application.Actions;
using AgentController.Domain.Actions;

namespace CodexController.Agents.Codex;

public sealed class CodexOpenThreadActionExecutor : CodexActionExecutorBase
{
    public const string ExecutorId = "codex.deep-link";

    private readonly Func<string, bool>? _openThread;

    public CodexOpenThreadActionExecutor(
        Func<string, bool>? openThread,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _openThread = openThread;
    }

    protected override ExecutorCapability ProbeCore(ActionRequest request) =>
        CapabilityFor(request);

    protected override ValueTask<ActionResult> ExecuteCoreAsync(
        ActionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(ExecuteCore(request));

    private ActionResult ExecuteCore(ActionRequest request)
    {
        var capability = CapabilityFor(request);
        if (capability.Status != ExecutorCapabilityStatus.Available)
        {
            return Unavailable(request, capability);
        }

        var opened = _openThread!(
            request.Parameters[OpenThreadActionContract.ThreadIdParameter]);
        return opened
            ? AcceptedUnverified(
                request,
                ActionEvidenceKind.Transport,
                "thread.open.requested")
            : Complete(
                request,
                ActionOutcome.NotSent,
                "thread.open.not-sent");
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
