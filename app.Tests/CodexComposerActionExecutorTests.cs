using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexComposerActionExecutorTests
{
    [Fact]
    public async Task MicroSubmitReturnsUnverifiedTransportEvidence()
    {
        var executor = new CodexComposerActionExecutor(
            () => new ComposerAutomationResult(
                true,
                Channel: ComposerAutomationChannel.MicroHid),
            clear: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.SubmitId));

        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal("composer.submit.micro-requested", evidence.Code);
    }

    [Fact]
    public async Task SubmitReturnsUnverifiedTransportEvidence()
    {
        var executor = new CodexComposerActionExecutor(
            () => new ComposerAutomationResult(
                true,
                Channel: ComposerAutomationChannel.KeyboardInput),
            clear: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.SubmitId));

        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal("composer.submit.shortcut-sent", evidence.Code);
    }

    [Fact]
    public async Task ClearReturnsVerifiedUiEvidence()
    {
        var executor = new CodexComposerActionExecutor(
            submit: null,
            () => new ComposerAutomationResult(
                true,
                Channel: ComposerAutomationChannel.UiAutomation,
                StateVerified: true));

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.ClearId,
            ActionSafetyLevel.ConfirmationRequired));

        Assert.Equal(ActionOutcome.Succeeded, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal("composer.clear.verified", evidence.Code);
    }

    [Fact]
    public async Task ClearWithoutConfirmationIsBlockedBeforeExecution()
    {
        var clearCalls = 0;
        var executor = new CodexComposerActionExecutor(
            submit: null,
            () =>
            {
                clearCalls++;
                return new ComposerAutomationResult(true);
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.ClearId));

        Assert.Equal(0, clearCalls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal("action.confirmation-required", result.ErrorCode);
    }

    [Fact]
    public async Task StopReturnsUnverifiedUiEvidence()
    {
        var executor = new CodexComposerActionExecutor(
            submit: null,
            clear: null,
            stop: () => new ComposerAutomationResult(
                true,
                Channel: ComposerAutomationChannel.UiAutomation));

        var result = await executor.ExecuteAsync(CreateRequest(
            TurnActionContract.StopId,
            ActionSafetyLevel.HighRisk));

        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.UiObservation, evidence.Kind);
        Assert.Equal("turn.stop.control-invoked", evidence.Code);
    }

    [Fact]
    public async Task StopWithoutHighRiskConfirmationIsBlocked()
    {
        var stopCalls = 0;
        var executor = new CodexComposerActionExecutor(
            submit: null,
            clear: null,
            stop: () =>
            {
                stopCalls++;
                return new ComposerAutomationResult(true);
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            TurnActionContract.StopId,
            ActionSafetyLevel.ConfirmationRequired));

        Assert.Equal(0, stopCalls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            "action.high-risk-confirmation-required",
            result.ErrorCode);
    }

    [Fact]
    public async Task SuccessfulOperationWithoutChannelFailsClosed()
    {
        var executor = new CodexComposerActionExecutor(
            () => new ComposerAutomationResult(true),
            clear: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.SubmitId));

        Assert.Equal(ActionOutcome.Failed, result.Outcome);
        Assert.Equal("action.evidence.missing", result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Theory]
    [InlineData(
        AgentAutomationErrorCodes.AgentNotForeground,
        ActionOutcome.Blocked)]
    [InlineData(
        AgentAutomationErrorCodes.InputInjectionFailed,
        ActionOutcome.NotSent)]
    [InlineData(
        AgentAutomationErrorCodes.CapabilityUnavailable,
        ActionOutcome.Unsupported)]
    [InlineData(
        AgentAutomationErrorCodes.Unexpected,
        ActionOutcome.Failed)]
    public async Task FailureCodeMapsToTerminalOutcome(
        string errorCode,
        ActionOutcome expected)
    {
        var executor = new CodexComposerActionExecutor(
            () => new ComposerAutomationResult(false, errorCode),
            clear: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.SubmitId));

        Assert.Equal(expected, result.Outcome);
        Assert.Equal(errorCode, result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task MissingConfiguredOperationIsUnsupported()
    {
        var executor = new CodexComposerActionExecutor(null, null);

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.SubmitId));

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal(
            "agent.composer-command.unavailable",
            result.ErrorCode);
    }

    [Fact]
    public async Task DifferentActionIsUnsupportedByProbe()
    {
        var executor = new CodexComposerActionExecutor(
            () => new ComposerAutomationResult(
                true,
                Channel: ComposerAutomationChannel.KeyboardInput),
            clear: null);
        var request = CreateRequest(ActionId.Parse("thread.open"));

        var capability = await executor.ProbeAsync(request);

        Assert.Equal(
            ExecutorCapabilityStatus.Unsupported,
            capability.Status);
        Assert.Equal("action.unsupported", capability.ReasonCode);
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
                ControlId.Parse("controller.face.west")),
            InputContext.Parse("composer.input"),
            $"test-{requestId:N}",
            safetyLevel,
            DateTimeOffset.UtcNow);
    }
}
