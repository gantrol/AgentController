using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexComposerActionExecutor : IActionExecutor
{
    public const string ExecutorId = "codex.composer-command";

    private readonly Func<ComposerAutomationResult>? _submit;
    private readonly Func<ComposerAutomationResult>? _clear;
    private readonly Func<ComposerAutomationResult>? _stop;
    private readonly Func<DateTimeOffset> _clock;

    public CodexComposerActionExecutor(
        Func<ComposerAutomationResult>? submit,
        Func<ComposerAutomationResult>? clear,
        Func<ComposerAutomationResult>? stop = null,
        Func<DateTimeOffset>? clock = null)
    {
        _submit = submit;
        _clear = clear;
        _stop = stop;
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
            return ValueTask.FromResult(Complete(
                request,
                capability.Status == ExecutorCapabilityStatus.Blocked
                    ? ActionOutcome.Blocked
                    : ActionOutcome.Unsupported,
                capability.ReasonCode));
        }

        var automation = OperationFor(request.ActionId)!();
        if (!automation.Succeeded)
        {
            return ValueTask.FromResult(Complete(
                request,
                FailureOutcome(automation.Error),
                automation.Error));
        }

        return ValueTask.FromResult(CompleteSuccessful(
            request,
            automation));
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
    {
        var knownAction =
            request.ActionId == ComposerActionContract.SubmitId ||
            request.ActionId == ComposerActionContract.ClearId ||
            request.ActionId == TurnActionContract.StopId;
        var operation = OperationFor(request.ActionId);
        var requiredSafety = RequiredSafetyFor(request.ActionId);
        if (operation is not null && request.SafetyLevel < requiredSafety)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Blocked,
                Priority: 100,
                ReasonCode: requiredSafety == ActionSafetyLevel.HighRisk
                    ? "action.high-risk-confirmation-required"
                    : "action.confirmation-required");
        }

        return new ExecutorCapability(
            Id,
            request.ActionId,
            operation is not null
                ? ExecutorCapabilityStatus.Available
                : ExecutorCapabilityStatus.Unsupported,
            Priority: 100,
            ReasonCode: operation is not null
                ? null
                : knownAction
                    ? "agent.composer-command.unavailable"
                    : "action.unsupported");
    }

    private Func<ComposerAutomationResult>? OperationFor(ActionId actionId)
    {
        if (actionId == ComposerActionContract.SubmitId)
        {
            return _submit;
        }

        if (actionId == ComposerActionContract.ClearId)
        {
            return _clear;
        }

        return actionId == TurnActionContract.StopId ? _stop : null;
    }

    private static ActionSafetyLevel RequiredSafetyFor(ActionId actionId) =>
        actionId == TurnActionContract.StopId
            ? ActionSafetyLevel.HighRisk
            : actionId == ComposerActionContract.ClearId
                ? ActionSafetyLevel.ConfirmationRequired
                : ActionSafetyLevel.Routine;

    private ActionResult CompleteSuccessful(
        ActionRequest request,
        ComposerAutomationResult automation)
    {
        var descriptor = SuccessDescriptorFor(request.ActionId, automation);
        if (descriptor is null)
        {
            return Complete(
                request,
                ActionOutcome.Failed,
                "action.evidence.missing");
        }

        var completedAt = _clock();
        return new ActionResult(
            request.RequestId,
            request.ActionId,
            descriptor.Value.Outcome,
            Id,
            completedAt,
            [
                new ActionEvidence(
                    descriptor.Value.EvidenceKind,
                    Id,
                    descriptor.Value.EvidenceCode,
                    completedAt,
                    confidence: 1),
            ]);
    }

    private static SuccessDescriptor? SuccessDescriptorFor(
        ActionId actionId,
        ComposerAutomationResult automation)
    {
        if (actionId == ComposerActionContract.SubmitId &&
            automation.Channel == ComposerAutomationChannel.KeyboardInput)
        {
            return new(
                ActionOutcome.AcceptedUnverified,
                ActionEvidenceKind.Transport,
                "composer.submit.shortcut-sent");
        }

        if (actionId == ComposerActionContract.ClearId &&
            automation.Channel == ComposerAutomationChannel.UiAutomation &&
            automation.StateVerified)
        {
            return new(
                ActionOutcome.Succeeded,
                ActionEvidenceKind.UiObservation,
                "composer.clear.verified");
        }

        if (actionId == TurnActionContract.StopId &&
            automation.Channel == ComposerAutomationChannel.UiAutomation)
        {
            return new(
                ActionOutcome.AcceptedUnverified,
                ActionEvidenceKind.UiObservation,
                "turn.stop.control-invoked");
        }

        return null;
    }

    private static ActionOutcome FailureOutcome(string? errorCode) =>
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
            AgentAutomationErrorCodes.InputInjectionFailed =>
                ActionOutcome.NotSent,
            _ => ActionOutcome.Failed,
        };

    private ActionResult Complete(
        ActionRequest request,
        ActionOutcome outcome,
        string? errorCode) =>
        new(
            request.RequestId,
            request.ActionId,
            outcome,
            Id,
            _clock(),
            errorCode: errorCode);

    private readonly record struct SuccessDescriptor(
        ActionOutcome Outcome,
        ActionEvidenceKind EvidenceKind,
        string EvidenceCode);
}
