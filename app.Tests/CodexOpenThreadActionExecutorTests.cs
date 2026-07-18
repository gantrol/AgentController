using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents.Codex;

namespace CodexController.Tests;

public sealed class CodexOpenThreadActionExecutorTests
{
    [Fact]
    public async Task AcceptedDeepLinkReturnsTransportEvidence()
    {
        var now = DateTimeOffset.Parse("2026-07-18T12:00:00Z");
        string? openedThreadId = null;
        var executor = new CodexOpenThreadActionExecutor(
            threadId =>
            {
                openedThreadId = threadId;
                return true;
            },
            () => now);
        var request = CreateRequest("thread-42");

        var result = await executor.ExecuteAsync(request);

        Assert.Equal("thread-42", openedThreadId);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal(CodexOpenThreadActionExecutor.ExecutorId, evidence.Source);
        Assert.Equal("thread.open.requested", evidence.Code);
        Assert.Equal(now, evidence.ObservedAt);
    }

    [Fact]
    public async Task RejectedDeepLinkReturnsNotSent()
    {
        var executor = new CodexOpenThreadActionExecutor(
            _ => false);

        var result = await executor.ExecuteAsync(CreateRequest("thread-1"));

        Assert.Equal(ActionOutcome.NotSent, result.Outcome);
        Assert.Empty(result.Evidence);
        Assert.Equal("thread.open.not-sent", result.ErrorCode);
    }

    [Fact]
    public async Task MissingThreadIdIsBlockedWithoutCallingDeepLink()
    {
        var callCount = 0;
        var executor = new CodexOpenThreadActionExecutor(_ =>
        {
            callCount++;
            return true;
        });
        var request = CreateRequest(threadId: null);

        var result = await executor.ExecuteAsync(request);

        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(0, callCount);
        Assert.Equal("thread.id.missing", result.ErrorCode);
    }

    [Fact]
    public async Task DifferentActionIsUnsupported()
    {
        var executor = new CodexOpenThreadActionExecutor(
            _ => true);
        var request = CreateRequest(
            "thread-1",
            ActionId.Parse("composer.send"));

        var capability = await executor.ProbeAsync(request);

        Assert.Equal(
            ExecutorCapabilityStatus.Unsupported,
            capability.Status);
        Assert.Equal("action.unsupported", capability.ReasonCode);
    }

    private static ActionRequest CreateRequest(
        string? threadId,
        ActionId? actionId = null)
    {
        var parameters = new Dictionary<string, string>();
        if (threadId is not null)
        {
            parameters[OpenThreadActionContract.ThreadIdParameter] = threadId;
        }

        return new ActionRequest(
            Guid.NewGuid(),
            actionId ?? OpenThreadActionContract.Id,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.face.south")),
            InputContext.Parse("sidebar.task"),
            $"test-{Guid.NewGuid():N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow,
            parameters);
    }
}
