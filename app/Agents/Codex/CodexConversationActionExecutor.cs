using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexConversationActionExecutor :
    CodexActionExecutorBase
{
    public const string ExecutorId = "codex.conversation-automation";

    private readonly Func<string?>? _blockReason;
    private readonly Func<
        ConversationBoundary,
        CancellationToken,
        Task<ComposerAutomationResult>>? _scrollConversation;

    public CodexConversationActionExecutor(
        Func<string?>? blockReason,
        Func<
            ConversationBoundary,
            CancellationToken,
            Task<ComposerAutomationResult>>? scrollConversation,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _blockReason = blockReason;
        _scrollConversation = scrollConversation;
    }

    protected override ExecutorCapability ProbeCore(ActionRequest request) =>
        CapabilityFor(request);

    protected override async ValueTask<ActionResult> ExecuteCoreAsync(
        ActionRequest request,
        CancellationToken cancellationToken)
    {
        var capability = CapabilityFor(request);
        if (capability.Status != ExecutorCapabilityStatus.Available)
        {
            return Unavailable(request, capability);
        }

        var automation = await _scrollConversation!(
                BoundaryFor(request.ActionId)!.Value,
                cancellationToken)
            .ConfigureAwait(false);
        if (!automation.Succeeded)
        {
            return Complete(
                request,
                AutomationFailureOutcome(automation.Error),
                automation.Error);
        }

        if (automation.Channel != ComposerAutomationChannel.UiAutomation ||
            !automation.StateVerified)
        {
            return Complete(
                request,
                ActionOutcome.Failed,
                "action.evidence.missing");
        }

        return CompleteWithEvidence(
            request,
            ActionOutcome.Succeeded,
            ActionEvidenceKind.UiObservation,
            $"{request.ActionId}.verified");
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
    {
        if (BoundaryFor(request.ActionId) is null)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "action.unsupported");
        }

        if (_scrollConversation is null)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "agent.conversation-automation.unavailable");
        }

        var blockReason = _blockReason?.Invoke();
        return new ExecutorCapability(
            Id,
            request.ActionId,
            blockReason is null
                ? ExecutorCapabilityStatus.Available
                : ExecutorCapabilityStatus.Blocked,
            Priority: 100,
            ReasonCode: blockReason);
    }

    private static ConversationBoundary? BoundaryFor(ActionId actionId)
    {
        if (actionId == ConversationActionContract.ScrollTopId)
        {
            return ConversationBoundary.Top;
        }

        return actionId == ConversationActionContract.ScrollBottomId
            ? ConversationBoundary.Bottom
            : null;
    }
}
