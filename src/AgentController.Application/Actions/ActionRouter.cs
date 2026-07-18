using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public sealed class ActionRouter
{
    public const string Id = "application.action-router";

    private readonly IActionExecutor[] _executors;
    private readonly Func<DateTimeOffset> _clock;

    public ActionRouter(
        IEnumerable<IActionExecutor> executors,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(executors);
        _executors = executors.ToArray();
        if (_executors.Any(executor => executor is null))
        {
            throw new ArgumentException(
                "Action executors must not contain null entries.",
                nameof(executors));
        }

        var duplicateId = _executors
            .GroupBy(executor => executor.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateId is not null)
        {
            throw new ArgumentException(
                $"Action executor id '{duplicateId}' is registered more than once.",
                nameof(executors));
        }

        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async ValueTask<ActionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IActionExecutor? selectedExecutor = null;
        ExecutorCapability? selectedCapability = null;
        ExecutorCapability? failureCapability = null;

        foreach (var executor in _executors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capability = await executor
                .ProbeAsync(request, cancellationToken)
                .ConfigureAwait(false);
            ValidateCapability(executor, request, capability);
            if (capability.Status == ExecutorCapabilityStatus.Available)
            {
                if (selectedCapability is null ||
                    IsPreferred(capability, selectedCapability))
                {
                    selectedExecutor = executor;
                    selectedCapability = capability;
                }
            }
            else if (failureCapability is null ||
                     IsPreferredFailure(capability, failureCapability))
            {
                failureCapability = capability;
            }
        }

        if (selectedExecutor is null)
        {
            return new ActionResult(
                request.RequestId,
                request.ActionId,
                FailureOutcome(failureCapability?.Status),
                Id,
                _clock(),
                errorCode: failureCapability?.ReasonCode ??
                    "action.unsupported");
        }

        var result = await selectedExecutor
            .ExecuteAsync(request, cancellationToken)
            .ConfigureAwait(false);
        ValidateResult(selectedExecutor, request, result);
        return result;
    }

    private static bool IsPreferred(
        ExecutorCapability candidate,
        ExecutorCapability current) =>
        candidate.Priority > current.Priority ||
        (candidate.Priority == current.Priority &&
         string.CompareOrdinal(candidate.ExecutorId, current.ExecutorId) < 0);

    private static bool IsPreferredFailure(
        ExecutorCapability candidate,
        ExecutorCapability current)
    {
        var rank = FailureRank(candidate.Status);
        var currentRank = FailureRank(current.Status);
        return rank > currentRank ||
            (rank == currentRank && IsPreferred(candidate, current));
    }

    private static int FailureRank(ExecutorCapabilityStatus status) =>
        status switch
        {
            ExecutorCapabilityStatus.Blocked => 3,
            ExecutorCapabilityStatus.Incompatible => 2,
            ExecutorCapabilityStatus.Unsupported => 1,
            _ => 0,
        };

    private static ActionOutcome FailureOutcome(
        ExecutorCapabilityStatus? status) =>
        status switch
        {
            ExecutorCapabilityStatus.Blocked => ActionOutcome.Blocked,
            ExecutorCapabilityStatus.Incompatible => ActionOutcome.Incompatible,
            _ => ActionOutcome.Unsupported,
        };

    private static void ValidateCapability(
        IActionExecutor executor,
        ActionRequest request,
        ExecutorCapability capability)
    {
        if (!string.Equals(
                capability.ExecutorId,
                executor.Id,
                StringComparison.Ordinal) ||
            capability.ActionId != request.ActionId)
        {
            throw new InvalidOperationException(
                $"Executor '{executor.Id}' returned a capability for a different request.");
        }
    }

    private static void ValidateResult(
        IActionExecutor executor,
        ActionRequest request,
        ActionResult result)
    {
        if (result.RequestId != request.RequestId ||
            result.ActionId != request.ActionId ||
            !string.Equals(
                result.ExecutorId,
                executor.Id,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Executor '{executor.Id}' returned a mismatched action result.");
        }
    }
}
