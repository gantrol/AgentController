using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using Xunit;

namespace AgentController.Application.Tests;

public sealed class ActionRouterTests
{
    [Fact]
    public async Task SelectsTheHighestPriorityAvailableExecutor()
    {
        var low = new FakeExecutor("low", priority: 10);
        var high = new FakeExecutor("high", priority: 20);
        var router = new ActionRouter([low, high]);

        var result = await router.ExecuteAsync(CreateRequest());

        Assert.Equal("high", result.ExecutorId);
        Assert.Equal(0, low.ExecutionCount);
        Assert.Equal(1, high.ExecutionCount);
    }

    [Fact]
    public async Task ReturnsTheStrongestFailureWhenNoExecutorIsAvailable()
    {
        var router = new ActionRouter(
        [
            new FakeExecutor(
                "unsupported",
                status: ExecutorCapabilityStatus.Unsupported),
            new FakeExecutor(
                "incompatible",
                status: ExecutorCapabilityStatus.Incompatible),
            new FakeExecutor(
                "blocked",
                status: ExecutorCapabilityStatus.Blocked,
                reasonCode: "foreground.required"),
        ]);

        var result = await router.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(ActionRouter.Id, result.ExecutorId);
        Assert.Equal("foreground.required", result.ErrorCode);
    }

    [Fact]
    public async Task EmptyRouterReturnsUnsupportedWithoutExecuting()
    {
        var now = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        var router = new ActionRouter([], () => now);

        var result = await router.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal(now, result.CompletedAt);
        Assert.Equal("action.unsupported", result.ErrorCode);
    }

    [Fact]
    public void RejectsDuplicateExecutorIds()
    {
        var executors =
            new[] { new FakeExecutor("same"), new FakeExecutor("same") };

        Assert.Throws<ArgumentException>(() => new ActionRouter(executors));
    }

    [Fact]
    public async Task RejectsCapabilitiesForAnotherAction()
    {
        var executor = new FakeExecutor(
            "wrong",
            capabilityActionId: ActionId.Parse("another.action"));
        var router = new ActionRouter([executor]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await router.ExecuteAsync(CreateRequest()));
    }

    private static ActionRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            OpenThreadActionContract.Id,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.face.south")),
            InputContext.Parse("sidebar.task"),
            $"test-{Guid.NewGuid():N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>
            {
                [OpenThreadActionContract.ThreadIdParameter] = "thread-1",
            });

    private sealed class FakeExecutor : IActionExecutor
    {
        private readonly ExecutorCapabilityStatus _status;
        private readonly int _priority;
        private readonly string? _reasonCode;
        private readonly ActionId? _capabilityActionId;

        public FakeExecutor(
            string id,
            ExecutorCapabilityStatus status =
                ExecutorCapabilityStatus.Available,
            int priority = 100,
            string? reasonCode = null,
            ActionId? capabilityActionId = null)
        {
            Id = id;
            _status = status;
            _priority = priority;
            _reasonCode = reasonCode;
            _capabilityActionId = capabilityActionId;
        }

        public string Id { get; }

        public int ExecutionCount { get; private set; }

        public ValueTask<ExecutorCapability> ProbeAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutorCapability(
                Id,
                _capabilityActionId ?? request.ActionId,
                _status,
                _priority,
                _reasonCode));

        public ValueTask<ActionResult> ExecuteAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default)
        {
            ExecutionCount++;
            return ValueTask.FromResult(new ActionResult(
                request.RequestId,
                request.ActionId,
                ActionOutcome.Succeeded,
                Id,
                DateTimeOffset.UtcNow));
        }
    }
}
