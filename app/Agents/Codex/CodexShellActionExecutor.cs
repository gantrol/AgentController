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
        => RouteCapability(
            request,
            ShortcutFor(request.ActionId) is not null,
            _executeShortcut is not null,
            "agent.shell-shortcut.unavailable",
            _blockReason);

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

        if (actionId == ConversationActionContract.PreviousUserMessageId)
        {
            return "Alt+Up";
        }

        if (actionId == ConversationActionContract.NextUserMessageId)
        {
            return "Alt+Down";
        }

        return actionId == SidebarActionContract.ToggleId
            ? "Ctrl+B"
            : null;
    }
}
