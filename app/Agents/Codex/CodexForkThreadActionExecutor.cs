using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexForkThreadActionExecutor : CodexActionExecutorBase
{
    public const string ExecutorId = "codex.thread-fork-fallback";

    private static readonly string[] ActionNames =
    [
        "Fork",
        "Fork task",
        "Fork thread",
        "Branch",
        "Branch task",
        "Continue in new task",
    ];

    private readonly Func<string?>? _blockReason;
    private readonly Func<bool>? _tryMicro;
    private readonly Func<bool>? _tryShortcut;
    private readonly Func<string[], ComposerAutomationResult>?
        _invokeAutomation;

    public CodexForkThreadActionExecutor(
        Func<string?>? blockReason,
        Func<bool>? tryMicro,
        Func<bool>? tryShortcut,
        Func<string[], ComposerAutomationResult>? invokeAutomation,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _blockReason = blockReason;
        _tryMicro = tryMicro;
        _tryShortcut = tryShortcut;
        _invokeAutomation = invokeAutomation;
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

        if (_tryMicro?.Invoke() == true)
        {
            return AcceptedUnverified(
                request,
                ActionEvidenceKind.Transport,
                "thread.fork.micro-requested");
        }

        if (_tryShortcut?.Invoke() == true)
        {
            return AcceptedUnverified(
                request,
                ActionEvidenceKind.Transport,
                "thread.fork.shortcut-sent");
        }

        var automation = _invokeAutomation?.Invoke(ActionNames);
        if (automation is null)
        {
            return Complete(
                request,
                ActionOutcome.NotSent,
                "thread.fork.not-sent");
        }

        if (!automation.Succeeded)
        {
            return Complete(
                request,
                AutomationFailureOutcome(automation.Error),
                automation.Error);
        }

        return automation.Channel == ComposerAutomationChannel.UiAutomation
            ? AcceptedUnverified(
                request,
                ActionEvidenceKind.UiObservation,
                "thread.fork.control-invoked")
            : Complete(
                request,
                ActionOutcome.Failed,
                "action.evidence.missing");
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
        => RouteCapability(
            request,
            request.ActionId == ForkThreadActionContract.Id,
            _tryMicro is not null ||
            _tryShortcut is not null ||
            _invokeAutomation is not null,
            "agent.thread-fork.unavailable",
            _blockReason);

}
