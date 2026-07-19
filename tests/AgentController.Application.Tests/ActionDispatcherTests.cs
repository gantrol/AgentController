using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using Xunit;

namespace AgentController.Application.Tests;

public sealed class ActionDispatcherTests
{
    [Fact]
    public async Task BuildsCanonicalRequestBeforeRouting()
    {
        var requestId = Guid.Parse(
            "1b3350bb-7a6e-4a33-86b4-460a7ee96462");
        var now = DateTimeOffset.Parse("2026-07-18T18:00:00Z");
        var executor = new RecordingExecutor();
        var dispatcher = new ActionDispatcher(
            new ActionRouter([executor]),
            () => requestId,
            () => now);

        await dispatcher.ExecuteAsync(
            OpenThreadActionContract.Id,
            "test.controller",
            "controller.face.south",
            "sidebar.task",
            "  thread.open:thread-1  ",
            ActionSafetyLevel.Routine,
            new Dictionary<string, string>
            {
                [OpenThreadActionContract.ThreadIdParameter] = "thread-1",
            });

        var request = Assert.IsType<ActionRequest>(executor.Request);
        Assert.Equal(requestId, request.RequestId);
        Assert.Equal(now, request.RequestedAt);
        Assert.Equal(
            $"thread.open:thread-1:{requestId:N}",
            request.IdempotencyKey);
        Assert.Equal("test.controller", request.Source.DeviceId);
        Assert.Equal(
            "controller.face.south",
            request.Source.ControlId.Value);
        Assert.Equal("sidebar.task", request.Context.Value);
        Assert.Equal(
            "thread-1",
            request.Parameters[
                OpenThreadActionContract.ThreadIdParameter]);
    }

    [Fact]
    public async Task RejectsEmptyIdempotencyScopeBeforeRouting()
    {
        var executor = new RecordingExecutor();
        var dispatcher = new ActionDispatcher(
            new ActionRouter([executor]));

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await dispatcher.ExecuteAsync(
                OpenThreadActionContract.Id,
                "test.controller",
                "controller.face.south",
                "sidebar.task",
                " ",
                ActionSafetyLevel.Routine));

        Assert.Null(executor.Request);
    }

    [Fact]
    public async Task CancellationStopsBeforeCreatingRequest()
    {
        var idsCreated = 0;
        var executor = new RecordingExecutor();
        var dispatcher = new ActionDispatcher(
            new ActionRouter([executor]),
            () =>
            {
                idsCreated++;
                return Guid.NewGuid();
            });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await dispatcher.ExecuteAsync(
                OpenThreadActionContract.Id,
                "test.controller",
                "controller.face.south",
                "sidebar.task",
                "thread.open",
                ActionSafetyLevel.Routine,
                cancellationToken: cancellation.Token));

        Assert.Equal(0, idsCreated);
        Assert.Null(executor.Request);
    }

    private sealed class RecordingExecutor : IActionExecutor
    {
        public string Id => "recording";

        public ActionRequest? Request { get; private set; }

        public ValueTask<ExecutorCapability> ProbeAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Available,
                Priority: 100));

        public ValueTask<ActionResult> ExecuteAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(new ActionResult(
                request.RequestId,
                request.ActionId,
                ActionOutcome.Succeeded,
                Id,
                request.RequestedAt));
        }
    }
}
