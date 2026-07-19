using CodexController.Models;
using CodexController.ViewModels;
using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class RadialMenuViewModelTests
{
    [Fact]
    public void UpdateProjectsStateIntoStablePhysicalSlots()
    {
        var viewModel = new RadialMenuViewModel();
        var state = new RadialMenuState(
            RadialMenuLayerKind.Turn,
            "Running turn",
            "RT",
            [
                new RadialMenuItemState(
                    "queue",
                    RadialMenuSlotPosition.Top,
                    "Y",
                    "Queue",
                    "Send after the current turn",
                    logicalInput: LogicalInput.FaceNorth),
                new RadialMenuItemState(
                    "stop",
                    RadialMenuSlotPosition.Right,
                    "B",
                    "Stop",
                    "Unavailable",
                    isEnabled: false),
                new RadialMenuItemState(
                    "steer",
                    RadialMenuSlotPosition.Left,
                    "X",
                    "Steer",
                    isHighlighted: true,
                    confirmationProgress: 0.4),
            ],
            RadialMenuDisplayMode.Always,
            subtitle: "Choose a follow-up action");

        viewModel.Update(state);

        Assert.True(viewModel.IsVisible);
        Assert.Equal(RadialMenuLayerKind.Turn, viewModel.Layer);
        Assert.Equal("RT", viewModel.ModifierGlyph);
        Assert.Equal("Queue", viewModel.Top.Title);
        Assert.Equal(
            LogicalInput.FaceNorth,
            viewModel.Top.LogicalInput);
        Assert.False(viewModel.Right.IsActionEnabled);
        Assert.True(viewModel.Left.IsHighlighted);
        Assert.True(viewModel.Left.HasConfirmationProgress);
        Assert.Equal(0.4, viewModel.Left.ConfirmationProgress);
        Assert.False(viewModel.Bottom.IsPresent);
        Assert.False(viewModel.CenterLeft.IsPresent);
        Assert.False(viewModel.CenterRight.IsPresent);
    }

    [Fact]
    public void UpdateClearsSlotsMissingFromNextLayer()
    {
        var viewModel = new RadialMenuViewModel();
        viewModel.Update(
            StateWith(
                RadialMenuSlotPosition.CenterLeft,
                "View",
                "Agent five"));

        viewModel.Update(
            StateWith(
                RadialMenuSlotPosition.Bottom,
                "A",
                "Approve"));

        Assert.False(viewModel.CenterLeft.IsPresent);
        Assert.Equal(string.Empty, viewModel.CenterLeft.Title);
        Assert.Null(viewModel.CenterLeft.LogicalInput);
        Assert.True(viewModel.Bottom.IsPresent);
        Assert.Equal("Approve", viewModel.Bottom.Title);
    }

    [Fact]
    public void AgentLayerProjectsKeypadPresentationAndStatus()
    {
        var presentation = new AgentKeypadPresentation(
            "Press a mapped key",
            "B",
            "Cancel",
            "Selected key pulses",
            "Idle",
            "Working",
            "Complete unread",
            "Needs response",
            "Error",
            "Unassigned");
        var viewModel = new RadialMenuViewModel();
        viewModel.Update(
            new RadialMenuState(
                RadialMenuLayerKind.Agent,
                "Agent tasks",
                "LB",
                [
                    new RadialMenuItemState(
                        "agent-slot-1",
                        RadialMenuSlotPosition.Top,
                        "Up",
                        "Task one",
                        status: ThreadStatus.Thinking),
                ],
                RadialMenuDisplayMode.Always,
                agentKeypad: presentation));

        Assert.True(viewModel.IsAgentLayer);
        Assert.Same(presentation, viewModel.AgentKeypad);
        Assert.Equal(ThreadStatus.Thinking, viewModel.Top.Status);
        Assert.Equal(
            ThreadStatus.Unassigned,
            viewModel.Right.Status);
    }

    [Fact]
    public void HideDoesNotDiscardPreparedMenu()
    {
        var viewModel = new RadialMenuViewModel();
        viewModel.Update(
            StateWith(
                RadialMenuSlotPosition.Left,
                "X",
                "Steer"));

        viewModel.Hide();

        Assert.False(viewModel.IsVisible);
        Assert.Equal("Steer", viewModel.Left.Title);
    }

    [Fact]
    public void InputAcknowledgementIsLocalAndThenEntersWaiting()
    {
        var viewModel = new RadialMenuViewModel();
        viewModel.Update(
            new RadialMenuState(
                RadialMenuLayerKind.Command,
                "Commands",
                "RB",
                [
                    new RadialMenuItemState(
                        "command-dispatch",
                        RadialMenuSlotPosition.CenterRight,
                        "Menu",
                        "Send"),
                ],
                RadialMenuDisplayMode.Learning,
                isLearningCueReady: false));

        Assert.False(viewModel.IsVisible);
        Assert.True(
            viewModel.TryAcceptInput(
                "command-dispatch",
                out var title));
        Assert.Equal("Send", title);
        Assert.True(viewModel.IsVisible);
        Assert.True(viewModel.CenterRight.IsHighlighted);
        Assert.Equal(
            RadialMenuInteractionPhase.InputAccepted,
            viewModel.InteractionPhase);

        viewModel.EnterWaitingForResponse();

        Assert.False(viewModel.IsVisible);
        Assert.True(viewModel.IsWaitingForResponse);
    }

    [Fact]
    public void InputAcknowledgementHonorsOffDisplayPolicy()
    {
        var viewModel = new RadialMenuViewModel();
        viewModel.Update(
            new RadialMenuState(
                RadialMenuLayerKind.Agent,
                "Agents",
                "LB",
                [
                    new RadialMenuItemState(
                        "agent-slot-1",
                        RadialMenuSlotPosition.Top,
                        "Up",
                        "Task one"),
                ],
                RadialMenuDisplayMode.Off));

        Assert.True(
            viewModel.TryAcceptInput(
                "agent-slot-1",
                out _));
        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public void DisabledAccessibleNameExplainsAvailability()
    {
        var viewModel = new RadialMenuViewModel();
        viewModel.Update(
            new RadialMenuState(
                RadialMenuLayerKind.Command,
                "Commands",
                "RB",
                [
                    new RadialMenuItemState(
                        "approve",
                        RadialMenuSlotPosition.Bottom,
                        "A",
                        "Approve",
                        "No approval request",
                        isEnabled: false),
                ],
                RadialMenuDisplayMode.Always));

        Assert.Contains(
            "No approval request",
            viewModel.Bottom.AccessibleName);
        Assert.StartsWith(
            "A,",
            viewModel.Bottom.AccessibleName);
        Assert.Contains(
            "unavailable",
            viewModel.Bottom.AccessibleName);
    }

    private static RadialMenuState StateWith(
        RadialMenuSlotPosition position,
        string glyph,
        string title)
    {
        return new RadialMenuState(
            RadialMenuLayerKind.Command,
            "Commands",
            "RB",
            [
                new RadialMenuItemState(
                    title,
                    position,
                    glyph,
                    title),
            ],
            RadialMenuDisplayMode.Always);
    }
}
