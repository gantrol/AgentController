using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using CodexController.Services;

namespace CodexController.Agents.Codex;

public sealed class CodexCreateThreadActionExecutor : IActionExecutor
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
    private readonly Func<DateTimeOffset> _clock;

    public CodexCreateThreadActionExecutor(
        Func<string[], ComposerAutomationResult>? invokeAutomation,
        Func<string, bool>? executeShortcut,
        Func<DateTimeOffset>? clock = null)
    {
        _invokeAutomation = invokeAutomation;
        _executeShortcut = executeShortcut;
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
            return ValueTask.FromResult(Result(
                request,
                ActionOutcome.Unsupported,
                capability.ReasonCode));
        }

        var automation = _invokeAutomation?.Invoke(ActionNames);
        if (automation?.Succeeded == true)
        {
            if (automation.Channel !=
                ComposerAutomationChannel.UiAutomation)
            {
                return ValueTask.FromResult(Result(
                    request,
                    ActionOutcome.Failed,
                    "action.evidence.missing"));
            }

            return ValueTask.FromResult(Accepted(
                request,
                ActionEvidenceKind.UiObservation,
                "thread.create.control-invoked"));
        }

        if (automation is null ||
            string.Equals(
                automation.Error,
                AgentAutomationErrorCodes.ElementNotFound,
                StringComparison.Ordinal))
        {
            if (_executeShortcut?.Invoke(Shortcut) == true)
            {
                return ValueTask.FromResult(Accepted(
                    request,
                    ActionEvidenceKind.Transport,
                    "thread.create.shortcut-sent"));
            }

            return ValueTask.FromResult(Result(
                request,
                ActionOutcome.NotSent,
                _executeShortcut is null
                    ? automation?.Error ?? "thread.create.not-sent"
                    : AgentAutomationErrorCodes.InputInjectionFailed));
        }

        return ValueTask.FromResult(Result(
            request,
            ActionOutcome.Failed,
            automation.Error));
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

    private ActionResult Accepted(
        ActionRequest request,
        ActionEvidenceKind evidenceKind,
        string evidenceCode)
    {
        var completedAt = _clock();
        return new ActionResult(
            request.RequestId,
            request.ActionId,
            ActionOutcome.AcceptedUnverified,
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

    private ActionResult Result(
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
