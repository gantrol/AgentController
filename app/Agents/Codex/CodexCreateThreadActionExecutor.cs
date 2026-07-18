using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexCreateThreadActionExecutor : CodexActionExecutorBase
{
    public const string ExecutorId = "codex.thread-create-fallback";
    public const string Shortcut = "Ctrl+N";

    private static readonly string[] ActionNames =
    [
        "New task",
        "New Task",
        "New chat",
        "New Chat",
        "新建任务",
        "新对话",
    ];

    private readonly Func<string[], ComposerAutomationResult>?
        _invokeAutomation;
    private readonly Func<string, bool>? _executeShortcut;

    public CodexCreateThreadActionExecutor(
        Func<string[], ComposerAutomationResult>? invokeAutomation,
        Func<string, bool>? executeShortcut,
        Func<DateTimeOffset>? clock = null)
        : base(ExecutorId, clock)
    {
        _invokeAutomation = invokeAutomation;
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

        var automation = _invokeAutomation?.Invoke(ActionNames);
        if (automation?.Succeeded == true)
        {
            if (automation.Channel !=
                ComposerAutomationChannel.UiAutomation)
            {
                return Complete(
                    request,
                    ActionOutcome.Failed,
                    "action.evidence.missing");
            }

            return AcceptedUnverified(
                request,
                ActionEvidenceKind.UiObservation,
                "thread.create.control-invoked");
        }

        if (automation is null ||
            string.Equals(
                automation.Error,
                AgentAutomationErrorCodes.ElementNotFound,
                StringComparison.Ordinal))
        {
            if (_executeShortcut?.Invoke(Shortcut) == true)
            {
                return AcceptedUnverified(
                    request,
                    ActionEvidenceKind.Transport,
                    "thread.create.shortcut-sent");
            }

            return Complete(
                request,
                ActionOutcome.NotSent,
                _executeShortcut is null
                    ? automation?.Error ?? "thread.create.not-sent"
                    : AgentAutomationErrorCodes.InputInjectionFailed);
        }

        return Complete(
            request,
            ActionOutcome.Failed,
            automation.Error);
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
    {
        if (request.ActionId != CreateThreadActionContract.Id)
        {
            return new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "action.unsupported");
        }

        return new ExecutorCapability(
            Id,
            request.ActionId,
            _invokeAutomation is null && _executeShortcut is null
                ? ExecutorCapabilityStatus.Unsupported
                : ExecutorCapabilityStatus.Available,
            Priority: 100,
            ReasonCode: _invokeAutomation is null && _executeShortcut is null
                ? "agent.thread-create.unavailable"
                : null);
    }

}
