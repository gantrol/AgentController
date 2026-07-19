using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;
using CodexController.Services.Micro;

namespace CodexController.Agents.Codex;

public sealed class CodexUiCommandActionExecutor :
    CodexActionExecutorBase
{
    public const string ExecutorId = "codex.ui-command";

    private static readonly string[] AcceptActionNames =
    [
        "Approve",
        "Accept",
        "Accept changes",
        "Allow",
        "Allow once",
        "Continue",
    ];

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
    private readonly Func<ActionId, MicroReportSendResult>?
        _tryMicro;

    public CodexUiCommandActionExecutor(
        Func<string?>? blockReason,
        Func<string[], ComposerAutomationResult>? invokeAutomation,
        Func<DateTimeOffset>? clock = null,
        Func<ActionId, MicroReportSendResult>? tryMicro = null)
        : base(ExecutorId, clock)
    {
        _blockReason = blockReason;
        _invokeAutomation = invokeAutomation;
        _tryMicro = tryMicro;
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

        var micro = _tryMicro?.Invoke(request.ActionId) ??
                    MicroReportSendResult.NotSent;
        if (micro is
            MicroReportSendResult.Accepted or
            MicroReportSendResult.OutcomeUnknown)
        {
            return AcceptedUnverified(
                request,
                ActionEvidenceKind.Transport,
                $"{request.ActionId}.micro-requested");
        }

        if (micro == MicroReportSendResult.Rejected)
        {
            return Complete(
                request,
                ActionOutcome.Failed,
                "micro.input-rejected");
        }

        if (_invokeAutomation is null)
        {
            return Complete(
                request,
                ActionOutcome.NotSent,
                "agent.ui-command.not-sent");
        }

        var automation = _invokeAutomation(
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
            _invokeAutomation is not null || _tryMicro is not null,
            "agent.ui-command.unavailable",
            _blockReason,
            RequiredSafetyFor(request.ActionId));

    private static ActionSafetyLevel RequiredSafetyFor(
        ActionId actionId) =>
        actionId == ApprovalActionContract.AcceptId
            ? ActionSafetyLevel.HighRisk
            : ActionSafetyLevel.Routine;

    private static string[]? ActionNamesFor(ActionId actionId)
    {
        if (actionId == ApprovalActionContract.AcceptId)
        {
            return AcceptActionNames;
        }

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
