using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexNavigationUndoActionExecutor :
    CodexActionExecutorBase
{
    public const string ExecutorId = "codex.navigation-undo";

    private readonly Func<string?>? _blockReason;
    private readonly Func<SidebarAutomationResult>? _goBack;

    public CodexNavigationUndoActionExecutor(
        Func<string?>? blockReason,
        Func<SidebarAutomationResult>? goBack,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _blockReason = blockReason;
        _goBack = goBack;
    }

    protected override ExecutorCapability ProbeCore(
        ActionRequest request) =>
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

        var automation = _goBack!();
        return automation.Succeeded
            ? AcceptedUnverified(
                request,
                ActionEvidenceKind.UiObservation,
                "navigation.undo.control-invoked")
            : Complete(
                request,
                AutomationFailureOutcome(automation.Error),
                automation.Error);
    }

    private ExecutorCapability CapabilityFor(ActionRequest request) =>
        RouteCapability(
            request,
            request.ActionId == NavigationActionContract.UndoId,
            _goBack is not null,
            "agent.navigation-undo.unavailable",
            _blockReason);
}
