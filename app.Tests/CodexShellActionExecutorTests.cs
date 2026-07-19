using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexShellActionExecutorTests
{
    [Theory]
    [InlineData("navigation.back", "Ctrl+[")]
    [InlineData("navigation.forward", "Ctrl+]")]
    [InlineData("conversation.previous-user-message", "Alt+Up")]
    [InlineData("conversation.next-user-message", "Alt+Down")]
    [InlineData("sidebar.toggle", "Ctrl+B")]
    public async Task KnownActionSendsMappedShortcut(
        string actionIdValue,
        string expectedShortcut)
    {
        string? sentShortcut = null;
        var executor = new CodexShellActionExecutor(
            blockReason: null,
            shortcut =>
            {
                sentShortcut = shortcut;
                return true;
            });
        var actionId = ActionId.Parse(actionIdValue);

        var result = await executor.ExecuteAsync(CreateRequest(actionId));

        Assert.Equal(expectedShortcut, sentShortcut);
        Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
        var evidence = Assert.Single(result.Evidence);
        Assert.Equal(ActionEvidenceKind.Transport, evidence.Kind);
        Assert.Equal($"{actionIdValue}.shortcut-sent", evidence.Code);
    }

    [Fact]
    public async Task BlockedCapabilityDoesNotSendShortcut()
    {
        var calls = 0;
        var executor = new CodexShellActionExecutor(
            () => AgentAutomationErrorCodes.AgentNotForeground,
            _ =>
            {
                calls++;
                return true;
            });

        var result = await executor.ExecuteAsync(CreateRequest(
            NavigationActionContract.BackId));

        Assert.Equal(0, calls);
        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.AgentNotForeground,
            result.ErrorCode);
    }

    [Fact]
    public async Task InjectionFailureIsNotSent()
    {
        var executor = new CodexShellActionExecutor(
            blockReason: null,
            _ => false);

        var result = await executor.ExecuteAsync(CreateRequest(
            SidebarActionContract.ToggleId));

        Assert.Equal(ActionOutcome.NotSent, result.Outcome);
        Assert.Equal(
            AgentAutomationErrorCodes.InputInjectionFailed,
            result.ErrorCode);
        Assert.Empty(result.Evidence);
    }

    [Fact]
    public async Task MissingShortcutTransportIsUnsupported()
    {
        var executor = new CodexShellActionExecutor(
            blockReason: null,
            executeShortcut: null);

        var result = await executor.ExecuteAsync(CreateRequest(
            NavigationActionContract.ForwardId));

        Assert.Equal(ActionOutcome.Unsupported, result.Outcome);
        Assert.Equal(
            "agent.shell-shortcut.unavailable",
            result.ErrorCode);
    }

    [Fact]
    public async Task DifferentActionIsUnsupported()
    {
        var executor = new CodexShellActionExecutor(
            blockReason: null,
            _ => true);

        var result = await executor.ExecuteAsync(CreateRequest(
            ActionId.Parse("thread.open")));

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
                ControlId.Parse("controller.radial.action-panel")),
            InputContext.Parse("radial.action-panel"),
            $"test-{requestId:N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);
    }
}
