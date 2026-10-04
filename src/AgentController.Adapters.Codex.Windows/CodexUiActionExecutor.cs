using AgentController.Application.Actions;
using AgentController.Domain.Actions;

namespace AgentController.Adapters.Codex.Windows;

public sealed class CodexUiActionExecutor(ICodexUiController controller, Func<string?>? blockReason = null) : IActionExecutor
{
    public string Id => "codex.windows.ui";

    public ValueTask<ExecutorCapability> ProbeAsync(ActionRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = Operation(request.ActionId);
        var reason = operation is null ? "action.unsupported" : blockReason?.Invoke();
        return ValueTask.FromResult(new ExecutorCapability(Id, request.ActionId,
            operation is null ? ExecutorCapabilityStatus.Unsupported : reason is null ? ExecutorCapabilityStatus.Available : ExecutorCapabilityStatus.Blocked,
            Priority: 200, ReasonCode: reason));
    }

    public async ValueTask<ActionResult> ExecuteAsync(ActionRequest request, CancellationToken cancellationToken = default)
    {
        var operation = Operation(request.ActionId);
        var reason = blockReason?.Invoke();
        if (operation is null || reason is not null)
            return new(request.RequestId, request.ActionId, operation is null ? ActionOutcome.Unsupported : ActionOutcome.Blocked,
                Id, DateTimeOffset.UtcNow, errorCode: reason ?? "action.unsupported");
        var result = await controller.ExecuteAsync(new(operation.Value),
            _ => Task.FromResult(blockReason?.Invoke() is null), cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return new(request.RequestId, request.ActionId, result.Disposition switch
        {
            CodexUiDisposition.Confirmed => ActionOutcome.Succeeded,
            CodexUiDisposition.OutcomeUnknown => ActionOutcome.AcceptedUnverified,
            _ => ActionOutcome.NotSent,
        }, Id, now, evidence: result.Disposition == CodexUiDisposition.NotSent ? [] :
            [new(result.Disposition == CodexUiDisposition.Confirmed ? ActionEvidenceKind.UiObservation : ActionEvidenceKind.Transport,
                Id, result.Code, now, 1)], errorCode: result.Disposition == CodexUiDisposition.Confirmed ? null : result.Code);
    }

    private static CodexUiOperation? Operation(ActionId id) =>
        id == ComposerActionContract.SubmitId ? CodexUiOperation.Submit :
        id == SidebarActionContract.ToggleId ? CodexUiOperation.ToggleSidebar :
        id == NavigationActionContract.BackId ? CodexUiOperation.Back :
        id == NavigationActionContract.ForwardId ? CodexUiOperation.Forward :
        id == ConversationActionContract.ScrollTopId ? CodexUiOperation.ScrollTop :
        id == ConversationActionContract.ScrollBottomId ? CodexUiOperation.ScrollBottom : null;
}
