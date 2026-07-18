using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexCreateThreadActionExecutorTests
{
    [Fact]
    public async Task AutomationInvocationReturnsUiEvidence()
    {
        string[]? invokedNames = null;
        var executor = new CodexCreateThreadActionExecutor(
            names =>
            {
                invokedNames = names;
                return new ComposerAutomationResult(true);
            },
            _ => throw new InvalidOperationException());

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Contains("New task", invokedNames!);
        Assert.Contains("新建任务", invokedNames!);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal("thread.create.control-invoked", evidence.Code);
    }

    [Fact]
    public async Task MissingControlFallsBackToShortcut()
    {
        string? executedShortcut = null;
        var executor = new CodexCreateThreadActionExecutor(
            _ => new ComposerAutomationResult(
                false,
                AgentAutomationErrorCodes.ElementNotFound),
            shortcut =>
            {
                executedShortcut = shortcut;
                return true;
            });

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(CodexCreateThreadActionExecutor.Shortcut, executedShortcut);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal("thread.create.shortcut-sent", evidence.Code);
    }

    [Fact]
    public async Task RejectedShortcutReturnsNotSent()
    {
        var executor = new CodexCreateThreadActionExecutor(
            _ => new ComposerAutomationResult(
                false,
                AgentAutomationErrorCodes.ElementNotFound),
            _ => false);

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.NotSent, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.InputInjectionFailed,
            result.ErrorCode);
    }

    [Fact]
    public async Task NonFallbackFailurePreservesAutomationError()
    {
        var shortcutCalls = 0;
        var executor = new CodexCreateThreadActionExecutor(
            _ => new ComposerAutomationResult(
                false,
                AgentAutomationErrorCodes.AgentNotForeground),
            _ =>
            {
                shortcutCalls++;
                return true;
            });

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(0, shortcutCalls);
        Assert.Equal(ActionOutcome.Failed, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.AgentNotForeground,
            result.ErrorCode);
    }

    [Fact]
    public async Task MissingExecutionRoutesAreUnsupported()
    {
        var executor = new CodexCreateThreadActionExecutor(null, null);

        var result = await executor.ExecuteAsync(CreateRequest());

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal("agent.thread-create.unavailable", result.ErrorCode);
    }

    private static ActionRequest CreateRequest()
    {
        var requestId = Guid.NewGuid();
        return new ActionRequest(
            requestId,
            CreateThreadActionContract.Id,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.dpad.up")),
            InputContext.Parse("radial.action-panel"),
            $"test-{requestId:N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);
    }
}
