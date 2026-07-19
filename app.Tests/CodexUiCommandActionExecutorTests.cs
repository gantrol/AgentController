using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Services;
using CodexController.Services.Micro;

namespace CodexController.Tests;

public sealed class CodexUiCommandActionExecutorTests
{
    [Theory]
    [InlineData(MicroReportSendResult.Accepted)]
    [InlineData(MicroReportSendResult.OutcomeUnknown)]
    public async Task MicroDeliveryDoesNotFallBackToUiAutomation(
        MicroReportSendResult sendResult)
    {
        var automationCalls = 0;
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            _ =>
            {
                automationCalls++;
                return CodexComposerService.UiAutomationSucceeded();
            },
            tryMicro: _ => sendResult);

        var result = await executor.ExecuteAsync(CreateRequest(
            ApprovalActionContract.DeclineId));

        Assert.Equal(0, automationCalls);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal("approval.decline.micro-requested", evidence.Code);
    }

    [Fact]
    public async Task MicroRejectionFailsClosedWithoutUiFallback()
    {
        var automationCalls = 0;
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            _ =>
            {
                automationCalls++;
                return CodexComposerService.UiAutomationSucceeded();
            },
            tryMicro: _ => MicroReportSendResult.Rejected);

        var result = await executor.ExecuteAsync(CreateRequest(
            ApprovalActionContract.DeclineId));

        Assert.Equal(0, automationCalls);
        Assert.Equal(ActionOutcome.Failed, result.Outcome);
        Assert.Equal("micro.input-rejected", result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task MicroNotSentFallsBackToUiAutomation()
    {
        var automationCalls = 0;
        var microCalls = 0;
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            _ =>
            {
                automationCalls++;
                return CodexComposerService.UiAutomationSucceeded();
            },
            tryMicro: actionId =>
            {
                Assert.Equal(ApprovalActionContract.DeclineId, actionId);
                microCalls++;
                return MicroReportSendResult.NotSent;
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            ApprovalActionContract.DeclineId));

        Assert.Equal(1, microCalls);
        Assert.Equal(1, automationCalls);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal("approval.decline.control-invoked", evidence.Code);
    }

    [Theory]
    [InlineData(
        "approval.accept",
        "Approve",
        ActionSafetyLevel.HighRisk)]
    [InlineData(
        "approval.decline",
        "Decline",
        ActionSafetyLevel.Routine)]
    [InlineData("turn.steer", "Steer", ActionSafetyLevel.Routine)]
    [InlineData("turn.queue", "Queue", ActionSafetyLevel.Routine)]
    public async Task KnownActionInvokesMappedUiControl(
        string actionIdValue,
        string expectedActionName,
        ActionSafetyLevel safetyLevel)
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
            ActionId.Parse(actionIdValue),
            safetyLevel));

        Assert.Contains(expectedActionName, receivedNames!);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal(
            $"{actionIdValue}.control-invoked",
            evidence.Code);
    }

    [Theory]
    [InlineData(ActionSafetyLevel.Routine)]
    [InlineData(ActionSafetyLevel.ConfirmationRequired)]
    public async Task AcceptRequiresHighRiskConfirmation(
        ActionSafetyLevel safetyLevel)
    {
        var calls = 0;
        var executor = new CodexUiCommandActionExecutor(
            blockReason: null,
            _ =>
            {
                calls++;
                return CodexComposerService.UiAutomationSucceeded();
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            ApprovalActionContract.AcceptId,
            safetyLevel));

        Assert.Equal(0, calls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            "action.high-risk-confirmation-required",
            result.ErrorCode);
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
            ActionId.Parse("approval.pause")));

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal("action.unsupported", result.ErrorCode);
    }

    private static ActionRequest CreateRequest(
        ActionId actionId,
        ActionSafetyLevel safetyLevel = ActionSafetyLevel.Routine)
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
            safetyLevel,
            DateTimeOffset.UtcNow);
    }
}
