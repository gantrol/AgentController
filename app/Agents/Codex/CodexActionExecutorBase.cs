using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public abstract class CodexActionExecutorBase : IActionExecutor
{
    private readonly Func<DateTimeOffset> _clock;

    protected CodexActionExecutorBase(
        string id,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id.Trim();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Id { get; }

    public ValueTask<ExecutorCapability> ProbeAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(ProbeCore(request));
    }

    public ValueTask<ActionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteCoreAsync(request, cancellationToken);
    }

    protected abstract ExecutorCapability ProbeCore(ActionRequest request);

    protected abstract ValueTask<ActionResult> ExecuteCoreAsync(
        ActionRequest request,
        CancellationToken cancellationToken);

    protected ExecutorCapability RouteCapability(
        ActionRequest request,
        bool supportsAction,
        bool executionAvailable,
        string unavailableReasonCode,
        Func<string?>? blockReason = null,
        ActionSafetyLevel requiredSafety =
            ActionSafetyLevel.Routine)
    {
        if (!supportsAction)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "action.unsupported");
        }

        if (!executionAvailable)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: unavailableReasonCode);
        }

        if (request.SafetyLevel < requiredSafety)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Blocked,
                Priority: 100,
                ReasonCode:
                    requiredSafety == ActionSafetyLevel.HighRisk
                        ? "action.high-risk-confirmation-required"
                        : "action.confirmation-required");
        }

        var reason = blockReason?.Invoke();
        return new ExecutorCapability(
            Id,
            request.ActionId,
            reason is null
                ? ExecutorCapabilityStatus.Available
                : ExecutorCapabilityStatus.Blocked,
            Priority: 100,
            ReasonCode: reason);
    }

    protected ActionResult Unavailable(
        ActionRequest request,
        ExecutorCapability capability) =>
        Complete(
            request,
            capability.Status switch
            {
                ExecutorCapabilityStatus.Blocked => ActionOutcome.Blocked,
                ExecutorCapabilityStatus.Incompatible =>
                    ActionOutcome.Incompatible,
                _ => ActionOutcome.Unsupported,
            },
            capability.ReasonCode);

    protected ActionResult AcceptedUnverified(
        ActionRequest request,
        ActionEvidenceKind evidenceKind,
        string evidenceCode) =>
        CompleteWithEvidence(
            request,
            ActionOutcome.AcceptedUnverified,
            evidenceKind,
            evidenceCode);

    protected ActionResult CompleteWithEvidence(
        ActionRequest request,
        ActionOutcome outcome,
        ActionEvidenceKind evidenceKind,
        string evidenceCode)
    {
        var completedAt = _clock();
        return new ActionResult(
            request.RequestId,
            request.ActionId,
            outcome,
            Id,
            completedAt,
            [
                new ActionEvidence(
                    evidenceKind,
                    Id,
                    evidenceCode,
                    completedAt,
                    confidence: 1),
            ]);
    }

    protected ActionResult Complete(
        ActionRequest request,
        ActionOutcome outcome,
        string? errorCode = null) =>
        new(
            request.RequestId,
            request.ActionId,
            outcome,
            Id,
            _clock(),
            errorCode: errorCode);

    protected static ActionOutcome AutomationFailureOutcome(
        string? errorCode) =>
        errorCode switch
        {
            AgentAutomationErrorCodes.BridgeSafePreview or
            AgentAutomationErrorCodes.AgentNotForeground or
            AgentAutomationErrorCodes.ComposerEmpty =>
                ActionOutcome.Blocked,
            AgentAutomationErrorCodes.CapabilityUnavailable =>
                ActionOutcome.Unsupported,
            AgentAutomationErrorCodes.AgentWindowNotFound or
            AgentAutomationErrorCodes.ElementNotFound or
            AgentAutomationErrorCodes.FocusRejected or
            AgentAutomationErrorCodes.InputInjectionFailed or
            AgentAutomationErrorCodes.OperationCanceled =>
                ActionOutcome.NotSent,
            _ => ActionOutcome.Failed,
        };
}
