using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexComposerActionExecutor : CodexActionExecutorBase
{
    public const string ExecutorId = "codex.composer-command";

    private readonly Func<ComposerAutomationResult>? _submit;
    private readonly Func<ComposerAutomationResult>? _clear;
    private readonly Func<ComposerAutomationResult>? _stop;

    public CodexComposerActionExecutor(
        Func<ComposerAutomationResult>? submit,
        Func<ComposerAutomationResult>? clear,
        Func<ComposerAutomationResult>? stop = null,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _submit = submit;
        _clear = clear;
        _stop = stop;
    }

    protected override ExecutorCapability ProbeCore(ActionRequest request) =>
        CapabilityFor(request);

    protected override ActionResult ExecuteCore(ActionRequest request)
    {
        var capability = CapabilityFor(request);
        if (capability.Status != ExecutorCapabilityStatus.Available)
        {
            return Unavailable(request, capability);
        }

        var automation = OperationFor(request.ActionId)!();
        if (!automation.Succeeded)
        {
            return Complete(
                request,
                AutomationFailureOutcome(automation.Error),
                automation.Error);
        }

        return CompleteSuccessful(request, automation);
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

        return CompleteWithEvidence(
            request,
            descriptor.Value.Outcome,
            descriptor.Value.EvidenceKind,
            descriptor.Value.EvidenceCode);
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

    private readonly record struct SuccessDescriptor(
        ActionOutcome Outcome,
        ActionEvidenceKind EvidenceKind,
        string EvidenceCode);
}
