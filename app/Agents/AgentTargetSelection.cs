using AgentController.Application.Actions;
using AgentController.Domain.Actions;

namespace CodexController.Agents;

public sealed record AgentTargetChangedEventArgs(
    IAgentTarget Previous,
    IAgentTarget Current);

/// <summary>
/// Owns the currently controlled Agent. Selection is intentionally separate
/// from window focus: the View button can switch away from an unavailable or
/// background Agent, after which Menu activates the newly selected target.
/// </summary>
public sealed class AgentTargetSelection
{
    private readonly AgentTargetRegistry _registry;
    private readonly object _gate = new();
    private IAgentTarget _active;

    public AgentTargetSelection(
        AgentTargetRegistry registry,
        string? initialTargetId = null)
    {
        _registry = registry ??
            throw new ArgumentNullException(nameof(registry));
        _active = registry.Resolve(initialTargetId);
    }

    public event EventHandler<AgentTargetChangedEventArgs>? Changed;

    public IReadOnlyList<IAgentTarget> Targets => _registry.Targets;

    public IAgentTarget Active
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public bool SelectNext()
    {
        IAgentTarget previous;
        IAgentTarget current;
        lock (_gate)
        {
            if (_registry.Targets.Count < 2)
            {
                return false;
            }

            previous = _active;
            var currentIndex = _registry.Targets
                .Select((target, index) => (target, index))
                .Where(item => item.target.Id == previous.Id)
                .Select(item => item.index)
                .DefaultIfEmpty(0)
                .First();
            current = _registry.Targets[
                (currentIndex + 1) % _registry.Targets.Count];
            if (current.Id == previous.Id)
            {
                return false;
            }

            _active = current;
        }

        Changed?.Invoke(this, new(previous, current));
        return true;
    }
}

/// <summary>
/// Prevents one Agent's executors from becoming a fallback route while a
/// different Agent is selected. This is the hard boundary that keeps a
/// DeepSeek gesture from leaking into Codex keyboard, UIA, or Micro HID.
/// </summary>
internal sealed class AgentScopedActionExecutor : IActionExecutor
{
    private readonly IActionExecutor _inner;
    private readonly AgentTargetSelection _selection;
    private readonly AgentId _targetId;

    internal AgentScopedActionExecutor(
        IActionExecutor inner,
        AgentTargetSelection selection,
        AgentId targetId)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _selection = selection ??
            throw new ArgumentNullException(nameof(selection));
        _targetId = targetId;
    }

    public string Id => _inner.Id;

    public ValueTask<ExecutorCapability> ProbeAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return _selection.Active.Id == _targetId
            ? _inner.ProbeAsync(request, cancellationToken)
            : ValueTask.FromResult(new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Unsupported,
                Priority: 100,
                ReasonCode: "agent.not-selected"));
    }

    public ValueTask<ActionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (_selection.Active.Id == _targetId)
        {
            return _inner.ExecuteAsync(request, cancellationToken);
        }

        return ValueTask.FromResult(new ActionResult(
            request.RequestId,
            request.ActionId,
            ActionOutcome.Unsupported,
            Id,
            DateTimeOffset.UtcNow,
            errorCode: "agent.not-selected"));
    }
}
