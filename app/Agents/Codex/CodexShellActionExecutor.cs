using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexShellActionExecutor : CodexActionExecutorBase
{
    public const string ExecutorId = "codex.shell-shortcut";

    private readonly Func<string?>? _blockReason;
    private readonly Func<string, bool>? _executeShortcut;

    public CodexShellActionExecutor(
        Func<string?>? blockReason,
        Func<string, bool>? executeShortcut,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _blockReason = blockReason;
        _executeShortcut = executeShortcut;
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

        var shortcut = ShortcutFor(request.ActionId)!;
        return _executeShortcut!(shortcut)
            ? AcceptedUnverified(
                request,
                ActionEvidenceKind.Transport,
                $"{request.ActionId}.shortcut-sent")
            : Complete(
                request,
                ActionOutcome.NotSent,
                AgentAutomationErrorCodes.InputInjectionFailed);
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
    {
        if (ShortcutFor(request.ActionId) is null)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "action.unsupported");
        }

        if (_executeShortcut is null)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "agent.shell-shortcut.unavailable");
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

    private static string? ShortcutFor(ActionId actionId)
    {
        if (actionId == NavigationActionContract.BackId)
        {
            return "Ctrl+[";
        }

        if (actionId == NavigationActionContract.ForwardId)
        {
            return "Ctrl+]";
        }

        return actionId == SidebarActionContract.ToggleId
            ? "Ctrl+B"
            : null;
    }
}
