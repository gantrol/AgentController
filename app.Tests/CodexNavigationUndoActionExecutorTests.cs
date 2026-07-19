using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexNavigationUndoActionExecutorTests
{
    [Fact]
    public async Task SuccessfulBackControlInvocationReportsUiEvidence()
    {
        var calls = 0;
        var executor = new CodexNavigationUndoActionExecutor(
            blockReason: null,
            () =>
            {
                calls++;
                return new SidebarAutomationResult(true);
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            NavigationActionContract.UndoId));

        Assert.Equal(1, calls);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal("navigation.undo.control-invoked", evidence.Code);
    }

    [Fact]
    public async Task BlockedCapabilityDoesNotTouchUiAutomation()
    {
        var calls = 0;
        var executor = new CodexNavigationUndoActionExecutor(
            () => AgentAutomationErrorCodes.AgentNotForeground,
            () =>
            {
                calls++;
                return new SidebarAutomationResult(true);
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            NavigationActionContract.UndoId));

        Assert.Equal(0, calls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.AgentNotForeground,
            result.ErrorCode);
    }

    [Theory]
    [InlineData(
        AgentAutomationErrorCodes.AgentWindowNotFound,
        ActionOutcome.NotSent)]
    [InlineData(
        AgentAutomationErrorCodes.NavigationUnavailable,
        ActionOutcome.Failed)]
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
        var executor = new CodexNavigationUndoActionExecutor(
            blockReason: null,
            () => new SidebarAutomationResult(false, errorCode));

        var result = await executor.ExecuteAsync(CreateRequest(
            NavigationActionContract.UndoId));

        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task MissingAutomationRouteIsUnsupported()
    {
        var executor = new CodexNavigationUndoActionExecutor(
            blockReason: null,
            goBack: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            NavigationActionContract.UndoId));

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal(
            "agent.navigation-undo.unavailable",
            result.ErrorCode);
    }

    [Fact]
    public async Task DifferentActionIsUnsupported()
    {
        var executor = new CodexNavigationUndoActionExecutor(
            blockReason: null,
            () => new SidebarAutomationResult(true));

        var result = await executor.ExecuteAsync(CreateRequest(
            NavigationActionContract.BackId));

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
                ControlId.Parse("controller.face.east")),
            InputContext.Parse("navigation.undo"),
            $"test-{requestId:N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);
    }
}
