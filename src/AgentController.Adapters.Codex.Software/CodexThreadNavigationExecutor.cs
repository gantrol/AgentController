using CodexMicro.Codex;
using System.ComponentModel;
using AgentController.Application.Actions;
using AgentController.Domain.Actions;

namespace AgentController.Adapters.Codex.Software;

public sealed class CodexThreadNavigationExecutor : IActionExecutor
{
    public const string ExecutorId = "codex.software.navigation";

    private readonly CodexSoftwareClient _client;
    private readonly Func<ActionRequest, string?> _blockReason;
    private readonly Func<DateTimeOffset> _clock;

    public CodexThreadNavigationExecutor(
        CodexSoftwareClient client,
        Func<ActionRequest, string?>? blockReason = null,
        Func<DateTimeOffset>? clock = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _blockReason = blockReason ?? (_ => null);
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

    public async ValueTask<ActionResult> ExecuteAsync(
        ActionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var capability = CapabilityFor(request);
        if (capability.Status != ExecutorCapabilityStatus.Available)
        {
            return Complete(request,
                capability.Status == ExecutorCapabilityStatus.Blocked
                    ? ActionOutcome.Blocked : ActionOutcome.Unsupported,
                capability.ReasonCode);
        }

        var isOpen = request.ActionId == OpenThreadActionContract.Id;
        var action = isOpen ? "thread.open" : "thread.create";
        try
        {
            Task<bool> CanApply() => Task.FromResult(_blockReason(request) is null);
            if (isOpen)
            {
                await _client.OpenThreadAsync(
                    request.Parameters[OpenThreadActionContract.ThreadIdParameter],
                    CanApply,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _client.CreateDraftAsync(CanApply, cancellationToken).ConfigureAwait(false);
            }

            return Complete(request, ActionOutcome.AcceptedUnverified,
                evidenceCode: action + ".requested");
        }
        catch (OperationCanceledException)
        {
            // Navigation has no cancellable await after its shell dispatch.
            return Complete(request, ActionOutcome.NotSent, action + ".canceled");
        }
        catch (ObjectDisposedException)
        {
            return Complete(request, ActionOutcome.NotSent, "codex.software.closed");
        }
        catch (InvalidOperationException)
        {
            return Complete(request, ActionOutcome.NotSent,
                _blockReason(request) ?? action + ".not-sent");
        }
        catch (Exception error) when (error is Win32Exception or ArgumentException or NotSupportedException)
        {
            return Complete(request, ActionOutcome.NotSent, action + ".not-sent");
        }
        catch (IOException)
        {
            return Complete(request, ActionOutcome.AcceptedUnverified,
                action + ".outcome-unknown", action + ".outcome-unknown");
        }
    }

    private ExecutorCapability CapabilityFor(ActionRequest request)
    {
        if (request.ActionId != OpenThreadActionContract.Id &&
            request.ActionId != CreateThreadActionContract.Id)
        {
            return Capability(request, ExecutorCapabilityStatus.Unsupported, "action.unsupported");
        }

        if (request.ActionId == OpenThreadActionContract.Id)
        {
            if (!request.Parameters.TryGetValue(OpenThreadActionContract.ThreadIdParameter, out var threadId) ||
                string.IsNullOrWhiteSpace(threadId))
            {
                return Capability(request, ExecutorCapabilityStatus.Blocked, "thread.id.missing");
            }
            if (!Guid.TryParse(threadId, out _))
            {
                return Capability(request, ExecutorCapabilityStatus.Blocked, "thread.id.invalid");
            }
        }

        var reason = _blockReason(request);
        return Capability(request,
            reason is null ? ExecutorCapabilityStatus.Available : ExecutorCapabilityStatus.Blocked,
            reason);
    }

    private ExecutorCapability Capability(
        ActionRequest request, ExecutorCapabilityStatus status, string? reason = null) =>
        new(Id, request.ActionId, status, Priority: 100, ReasonCode: reason);

    private ActionResult Complete(
        ActionRequest request,
        ActionOutcome outcome,
        string? errorCode = null,
        string? evidenceCode = null)
    {
        var completedAt = _clock();
        return new ActionResult(request.RequestId, request.ActionId, outcome, Id, completedAt,
            evidence: evidenceCode is null ? [] :
            [new ActionEvidence(ActionEvidenceKind.Transport, Id, evidenceCode, completedAt, confidence: 1)],
            errorCode: errorCode);
    }
}
