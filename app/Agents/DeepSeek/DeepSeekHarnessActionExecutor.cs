using AgentController.Application.Actions;
using AgentController.Domain.Actions;

namespace CodexController.Agents.DeepSeek;

public sealed class DeepSeekHarnessActionExecutor : IActionExecutor
{
    public const string ExecutorId = "deepseek.harness-direct";

    private static readonly IReadOnlyDictionary<ActionId, string> Actions =
        new Dictionary<ActionId, string>
        {
            [CreateThreadActionContract.Id] = "session/new",
            [ForkThreadActionContract.Id] = "session/fork",
            [ComposerActionContract.SubmitId] = "composer/submit",
            [TurnActionContract.StopId] = "turn/cancel",
            [ApprovalActionContract.AcceptId] = "interaction/approve",
            [ApprovalActionContract.DeclineId] = "interaction/reject",
            [SidebarActionContract.ToggleId] = "layout/toggle-sidebar",
            [NavigationActionContract.BackId] = "layout/close-details",
            [NavigationActionContract.ForwardId] = "layout/open-details",
        };

    private readonly DeepSeekAgentTarget _target;
    private readonly Func<string?>? _blockReason;
    private readonly Func<DateTimeOffset> _clock;

    public DeepSeekHarnessActionExecutor(
        DeepSeekAgentTarget target,
        Func<string?>? blockReason = null,
        Func<DateTimeOffset>? clock = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _blockReason = blockReason;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Id => ExecutorId;

    public ValueTask<ExecutorCapability> ProbeAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var supported =
            request.ActionId == OpenThreadActionContract.Id ||
            request.ActionId == NavigationActionContract.UndoId ||
            Actions.ContainsKey(request.ActionId);
        if (!supported)
        {
            return ValueTask.FromResult(Capability(
                request,
                ExecutorCapabilityStatus.Unsupported,
                "action.unsupported"));
        }

        if (request.ActionId == OpenThreadActionContract.Id &&
            (!request.Parameters.TryGetValue(
                    OpenThreadActionContract.ThreadIdParameter,
                    out var threadId) ||
                string.IsNullOrWhiteSpace(threadId)))
        {
            return ValueTask.FromResult(Capability(
                request,
                ExecutorCapabilityStatus.Blocked,
                "thread.id.missing"));
        }

        if (request.ActionId is { } actionId &&
            (actionId == ApprovalActionContract.AcceptId ||
             actionId == TurnActionContract.StopId) &&
            request.SafetyLevel < ActionSafetyLevel.HighRisk)
        {
            return ValueTask.FromResult(Capability(
                request,
                ExecutorCapabilityStatus.Blocked,
                "action.high-risk-confirmation-required"));
        }

        var blocked = _blockReason?.Invoke();
        if (blocked is not null)
        {
            return ValueTask.FromResult(Capability(
                request,
                ExecutorCapabilityStatus.Blocked,
                blocked));
        }

        return ValueTask.FromResult(Capability(
            request,
            ExecutorCapabilityStatus.Available));
    }

    public async ValueTask<ActionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var capability = await ProbeAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (capability.Status != ExecutorCapabilityStatus.Available)
        {
            return Complete(
                request,
                capability.Status == ExecutorCapabilityStatus.Blocked
                    ? ActionOutcome.Blocked
                    : ActionOutcome.Unsupported,
                capability.ReasonCode);
        }

        DeepSeekHarnessResponse response;
        if (request.ActionId == OpenThreadActionContract.Id)
        {
            response = await _target.ActivateSessionAsync(
                    request.Parameters[
                        OpenThreadActionContract.ThreadIdParameter],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else if (request.ActionId == NavigationActionContract.UndoId)
        {
            response = await _target
                .UndoSessionAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            response = await _target.Client.ExecuteActionAsync(
                    Actions[request.ActionId],
                    _target.CurrentSessionId,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!response.Success)
        {
            var notSent = response.ErrorCode is
                "deepseek.harness.offline" or
                "deepseek.harness.timeout" or
                "deepseek.harness.http-error";
            return Complete(
                request,
                notSent ? ActionOutcome.NotSent : ActionOutcome.Failed,
                response.ErrorCode ?? "deepseek.harness.rejected");
        }

        if (request.ActionId == CreateThreadActionContract.Id ||
            request.ActionId == ForkThreadActionContract.Id)
        {
            // These Harness actions navigate only after their browser-side
            // async session work settles. Refresh the target cache before a
            // following controller action can reuse the previous session id.
            var refresh = await _target.RefreshStateAsync(cancellationToken)
                .ConfigureAwait(false);
            if (refresh.State is null)
            {
                // The topology change already completed, so the old explicit
                // id is now less safe than no id. Let the browser resolve its
                // authoritative current session on the next action.
                _target.InvalidateCurrentSessionCache();
            }
        }

        var completedAt = _clock();
        var verified = response.Status == "completed";
        return new ActionResult(
            request.RequestId,
            request.ActionId,
            verified
                ? ActionOutcome.Succeeded
                : ActionOutcome.AcceptedUnverified,
            Id,
            completedAt,
            [
                new ActionEvidence(
                    verified
                        ? ActionEvidenceKind.UiObservation
                        : ActionEvidenceKind.Transport,
                    Id,
                    verified
                        ? "deepseek.harness.action-completed"
                        : "deepseek.harness.request-accepted",
                    completedAt,
                    confidence: 1),
            ]);
    }

    private ExecutorCapability Capability(
        ActionRequest request,
        ExecutorCapabilityStatus status,
        string? reasonCode = null) =>
        new(Id, request.ActionId, status, Priority: 100, reasonCode);

    private ActionResult Complete(
        ActionRequest request,
        ActionOutcome outcome,
        string? errorCode = null) =>
        new(
            request.RequestId,
            request.ActionId,
            outcome,
            Id,
            _clock(),
            errorCode: errorCode);
}
