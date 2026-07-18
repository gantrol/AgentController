using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexUiCommandActionExecutorTests
{
    [Theory]
    [InlineData("approval.decline", "Decline")]
    [InlineData("turn.steer", "Steer")]
    [InlineData("turn.queue", "Queue")]
    public async Task KnownActionInvokesMappedUiControl(
        string actionIdValue,
        string expectedActionName)
    {
        string[]? receivedNames = null;
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            actionNames =>
            {
                receivedNames = actionNames;
                return CodexComposerService.UiAutomationSucceeded();
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            ActionId.Parse(actionIdValue)));

        Assert.Contains(expectedActionName, receivedNames!);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal(
            $"{actionIdValue}.control-invoked",
            evidence.Code);
    }

    [Fact]
    public async Task BlockedCapabilityDoesNotInvokeUiAutomation()
    {
        var calls = 0;
        var executor = new CodexUiCommandActionExecutor(
            () => AgentAutomationErrorCodes.AgentNotForeground,
            _ =>
            {
                calls++;
                return CodexComposerService.UiAutomationSucceeded();
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            TurnActionContract.SteerId));

        Assert.Equal(0, calls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.AgentNotForeground,
            result.ErrorCode);
    }

    [Fact]
    public async Task SuccessWithoutUiChannelFailsClosed()
    {
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            _ => new ComposerAutomationResult(true));

        var result = await executor.ExecuteAsync(CreateRequest(
            ApprovalActionContract.DeclineId));

        Assert.Equal(ActionOutcome.Failed, result.Outcome);
        Assert.Equal("action.evidence.missing", result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Theory]
    [InlineData(
        AgentAutomationErrorCodes.AgentNotForeground,
        ActionOutcome.Blocked)]
    [InlineData(
        AgentAutomationErrorCodes.ElementNotFound,
        ActionOutcome.NotSent)]
    [InlineData(
        AgentAutomationErrorCodes.CapabilityUnavailable,
        ActionOutcome.Unsupported)]
    [InlineData(
        AgentAutomationErrorCodes.Unexpected,
        ActionOutcome.Failed)]
    public async Task FailureCodeMapsToTerminalOutcome(
        string errorCode,
        ActionOutcome expectedOutcome)
    {
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            _ => new ComposerAutomationResult(false, errorCode));

        var result = await executor.ExecuteAsync(CreateRequest(
            TurnActionContract.QueueId));

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task MissingAutomationRouteIsUnsupported()
    {
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            invokeAutomation: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            TurnActionContract.QueueId));

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal("agent.ui-command.unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task DifferentActionIsUnsupported()
    {
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            _ => CodexComposerService.UiAutomationSucceeded());

        var result = await executor.ExecuteAsync(CreateRequest(
            ActionId.Parse("approval.accept")));

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal("action.unsupported", result.ErrorCode);
    }

    private static ActionRequest CreateRequest(ActionId actionId)
    {
        var requestId = Guid.NewGuid();
        return new ActionRequest(
            requestId,
            actionId,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.radial.command")),
            InputContext.Parse("radial.command"),
            $"test-{requestId:N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);
    }
}
