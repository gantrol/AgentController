using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexForkThreadActionExecutorTests
{
    [Fact]
    public async Task MicroSuccessStopsFallbackChain()
    {
        var executor = new CodexForkThreadActionExecutor(
            blockReason: null,
            tryMicro: () => true,
            tryShortcut: () => throw new InvalidOperationException(),
            invokeAutomation: _ => throw new InvalidOperationException());

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal("thread.fork.micro-requested", evidence.Code);
    }

    [Fact]
    public async Task ShortcutRunsAfterMicroNotSent()
    {
        var executor = new CodexForkThreadActionExecutor(
            blockReason: null,
            tryMicro: () => false,
            tryShortcut: () => true,
            invokeAutomation: _ => throw new InvalidOperationException());

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal("thread.fork.shortcut-sent", evidence.Code);
    }

    [Fact]
    public async Task UiAutomationRunsAfterFastPathsAreNotSent()
    {
        string[]? actionNames = null;
        var executor = new CodexForkThreadActionExecutor(
            blockReason: null,
            tryMicro: () => false,
            tryShortcut: () => false,
            invokeAutomation: names =>
            {
                actionNames = names;
                return new ComposerAutomationResult(
                    true,
                    Channel: ComposerAutomationChannel.UiAutomation);
            });

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Contains("Fork task", actionNames!);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal("thread.fork.control-invoked", evidence.Code);
    }

    [Fact]
    public async Task BlockedCapabilityTouchesNoExecutionChannel()
    {
        var calls = 0;
        var executor = new CodexForkThreadActionExecutor(
            () => AgentAutomationErrorCodes.AgentNotForeground,
            () =>
            {
                calls++;
                return true;
            },
            () =>
            {
                calls++;
                return true;
            },
            _ =>
            {
                calls++;
                return new ComposerAutomationResult(true);
            });

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(0, calls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.AgentNotForeground,
            result.ErrorCode);
    }

    [Fact]
    public async Task AutomationFailurePreservesNotSentOutcome()
    {
        var executor = new CodexForkThreadActionExecutor(
            blockReason: null,
            tryMicro: () => false,
            tryShortcut: () => false,
            invokeAutomation: _ => new ComposerAutomationResult(
                false,
                AgentAutomationErrorCodes.ElementNotFound));

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.NotSent, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.ElementNotFound,
            result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task MissingExecutionRoutesAreUnsupported()
    {
        var executor = new CodexForkThreadActionExecutor(
            blockReason: null,
            tryMicro: null,
            tryShortcut: null,
            invokeAutomation: null);

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal("agent.thread-fork.unavailable", result.ErrorCode);
    }

    private static ActionRequest CreateRequest()
    {
        var requestId = Guid.NewGuid();
        return new ActionRequest(
            requestId,
            ForkThreadActionContract.Id,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.radial.command.fork")),
            InputContext.Parse("radial.command"),
            $"test-{requestId:N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);
    }
}
