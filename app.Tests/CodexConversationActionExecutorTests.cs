using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexConversationActionExecutorTests
{
    [Theory]
    [InlineData("conversation.scroll-top", ConversationBoundary.Top)]
    [InlineData("conversation.scroll-bottom", ConversationBoundary.Bottom)]
    public async Task VerifiedReadbackReturnsSucceeded(
        string actionIdValue,
        ConversationBoundary expectedBoundary)
    {
        ConversationBoundary? receivedBoundary = null;
        var executor = new CodexConversationActionExecutor(
            blockReason: null,
            (boundary, _) =>
            {
                receivedBoundary = boundary;
                return Task.FromResult(
                    CodexComposerService.UiAutomationSucceeded(
                        stateVerified: true));
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            ActionId.Parse(actionIdValue)));

        Assert.Equal(expectedBoundary, receivedBoundary);
        Assert.Equal(ActionOutcome.Succeeded, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal($"{actionIdValue}.verified", evidence.Code);
    }

    [Theory]
    [InlineData(ComposerAutomationChannel.Unknown, true)]
    [InlineData(ComposerAutomationChannel.UiAutomation, false)]
    [InlineData(ComposerAutomationChannel.KeyboardInput, true)]
    public async Task SuccessWithoutVerifiedUiEvidenceFailsClosed(
        ComposerAutomationChannel channel,
        bool stateVerified)
    {
        var executor = new CodexConversationActionExecutor(
            blockReason: null,
            (_, _) => Task.FromResult(new ComposerAutomationResult(
                true,
                Channel: channel,
                StateVerified: stateVerified)));

        var result = await executor.ExecuteAsync(CreateRequest(
            ConversationActionContract.ScrollTopId));

        Assert.Equal(ActionOutcome.Failed, result.Outcome);
        Assert.Equal("action.evidence.missing", result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task BlockedCapabilityDoesNotStartAutomation()
    {
        var calls = 0;
        var executor = new CodexConversationActionExecutor(
            () => AgentAutomationErrorCodes.AgentNotForeground,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(
                    new ComposerAutomationResult(true));
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            ConversationActionContract.ScrollBottomId));

        Assert.Equal(0, calls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.AgentNotForeground,
            result.ErrorCode);
    }

    [Theory]
    [InlineData(
        AgentAutomationErrorCodes.AgentNotForeground,
        ActionOutcome.Blocked)]
    [InlineData(
        AgentAutomationErrorCodes.ElementNotFound,
        ActionOutcome.NotSent)]
    [InlineData(
        AgentAutomationErrorCodes.OperationCanceled,
        ActionOutcome.NotSent)]
    [InlineData(
        AgentAutomationErrorCodes.CapabilityUnavailable,
        ActionOutcome.Unsupported)]
    [InlineData(
        AgentAutomationErrorCodes.ElementUnsupported,
        ActionOutcome.Failed)]
    public async Task FailureCodeMapsToTerminalOutcome(
        string errorCode,
        ActionOutcome expectedOutcome)
    {
        var executor = new CodexConversationActionExecutor(
            blockReason: null,
            (_, _) => Task.FromResult(
                new ComposerAutomationResult(false, errorCode)));

        var result = await executor.ExecuteAsync(CreateRequest(
            ConversationActionContract.ScrollTopId));

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task MissingAutomationRouteIsUnsupported()
    {
        var executor = new CodexConversationActionExecutor(
            blockReason: null,
            scrollConversation: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            ConversationActionContract.ScrollTopId));

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal(
            "agent.conversation-automation.unavailable",
            result.ErrorCode);
    }

    [Fact]
    public async Task CancellationFlowsIntoAsyncAutomation()
    {
        var started = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new CodexConversationActionExecutor(
            blockReason: null,
            async (_, cancellationToken) =>
            {
                started.TrySetResult(true);
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
                return CodexComposerService.UiAutomationSucceeded(
                    stateVerified: true);
            });
        using var cancellation = new CancellationTokenSource();

        var execution = executor.ExecuteAsync(
            CreateRequest(ConversationActionContract.ScrollTopId),
            cancellation.Token).AsTask();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution);
    }

    private static ActionRequest CreateRequest(ActionId actionId)
    {
        var requestId = Guid.NewGuid();
        return new ActionRequest(
            requestId,
            actionId,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.dpad.up.hold")),
            InputContext.Parse("conversation.navigation"),
            $"test-{requestId:N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);
    }
}
