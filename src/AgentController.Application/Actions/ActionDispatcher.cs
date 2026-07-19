using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;

namespace AgentController.Application.Actions;

public sealed class ActionDispatcher
{
    private readonly ActionRouter _router;
    private readonly Func<Guid> _requestIdFactory;
    private readonly Func<DateTimeOffset> _clock;

    public ActionDispatcher(
        ActionRouter router,
        Func<Guid>? requestIdFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _requestIdFactory = requestIdFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public ValueTask<ActionResult> ExecuteAsync(
        ActionId actionId,
        string deviceId,
        string controlId,
        string context,
        string idempotencyScope,
        ActionSafetyLevel safetyLevel,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyScope);

        var requestId = _requestIdFactory();
        var request = new ActionRequest(
            requestId,
            actionId,
            new ActionSource(deviceId, ControlId.Parse(controlId)),
            InputContext.Parse(context),
            $"{idempotencyScope.Trim()}:{requestId:N}",
            safetyLevel,
            _clock(),
            parameters);
        return _router.ExecuteAsync(request, cancellationToken);
    }
}
