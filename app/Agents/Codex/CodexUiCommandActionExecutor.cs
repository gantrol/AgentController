using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexUiCommandActionExecutor :
    CodexActionExecutorBase
{
    public const string ExecutorId = "codex.ui-command";

    private static readonly string[] DeclineActionNames =
    [
        "Decline",
        "Reject",
        "Reject changes",
        "Deny",
    ];

    private static readonly string[] SteerActionNames =
    [
        "Steer",
        "Steer current turn",
        "Add to current turn",
        "加入当前运行",
        "加入当前轮次",
    ];

    private static readonly string[] QueueActionNames =
    [
        "Queue",
        "Queue next turn",
        "Send next",
        "排到下一轮",
        "排入下一轮",
    ];

    private readonly Func<string?>? _blockReason;
    private readonly Func<string[], ComposerAutomationResult>?
        _invokeAutomation;

    public CodexUiCommandActionExecutor(
        Func<string?>? blockReason,
        Func<string[], ComposerAutomationResult>? invokeAutomation,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _blockReason = blockReason;
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

        var automation = _invokeAutomation!(
            ActionNamesFor(request.ActionId)!);
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
                $"{request.ActionId}.control-invoked")
            : Complete(
                request,
                ActionOutcome.Failed,
                "action.evidence.missing");
    }

    private ExecutorCapability CapabilityFor(ActionRequest request) =>
        RouteCapability(
            request,
            ActionNamesFor(request.ActionId) is not null,
            _invokeAutomation is not null,
            "agent.ui-command.unavailable",
            _blockReason);

    private static string[]? ActionNamesFor(ActionId actionId)
    {
        if (actionId == ApprovalActionContract.DeclineId)
        {
            return DeclineActionNames;
        }

        if (actionId == TurnActionContract.SteerId)
        {
            return SteerActionNames;
        }

        return actionId == TurnActionContract.QueueId
            ? QueueActionNames
            : null;
    }
}
