using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexComposerActionExecutor : IActionExecutor
{
    public const string ExecutorId = "codex.composer-command";

    private readonly Func<ComposerAutomationResult>? _submit;
    private readonly Func<ComposerAutomationResult>? _clear;
    private readonly Func<DateTimeOffset> _clock;

    public CodexComposerActionExecutor(
        Func<ComposerAutomationResult>? submit,
        Func<ComposerAutomationResult>? clear,
        Func<DateTimeOffset>? clock = null)
    {
        _submit = submit;
        _clear = clear;
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

        var completedAt = _clock();
        var isSubmit = request.ActionId == ComposerActionContract.SubmitId;
        return ValueTask.FromResult(new ActionResult(
            request.RequestId,
            request.ActionId,
            isSubmit
                ? ActionOutcome.AcceptedUnverified
                : ActionOutcome.Succeeded,
            Id,
            completedAt,
            [
                new ActionEvidence(
                    isSubmit
                        ? ActionEvidenceKind.Transport
                        : ActionEvidenceKind.UiObservation,
                    Id,
                    isSubmit
                        ? "composer.submit.shortcut-sent"
                        : "composer.clear.verified",
                    completedAt,
                    confidence: 1),
            ]));
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
    {
        var knownAction =
            request.ActionId == ComposerActionContract.SubmitId ||
            request.ActionId == ComposerActionContract.ClearId;
        var operation = OperationFor(request.ActionId);
        if (operation is not null &&
            request.ActionId == ComposerActionContract.ClearId &&
            request.SafetyLevel < ActionSafetyLevel.ConfirmationRequired)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Blocked,
                Priority: 100,
                ReasonCode: "action.confirmation-required");
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

        return actionId == ComposerActionContract.ClearId
            ? _clear
            : null;
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
}
