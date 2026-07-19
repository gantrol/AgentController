using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public enum ExecutorCapabilityStatus
{
    Available,
    Unsupported,
    Incompatible,
    Blocked,
}

public sealed record ExecutorCapability(
    string ExecutorId,
    ActionId ActionId,
    ExecutorCapabilityStatus Status,
    int Priority,
    string? ReasonCode = null);

public interface IActionExecutor
{
    string Id { get; }

    ValueTask<ExecutorCapability> ProbeAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ActionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default);
}
