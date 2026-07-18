using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class RadialLayerCoordinatorTests
{
    [Fact]
    public void ShoulderTapMovesToAdjacentTaskBeforeLearningDelay()
    {
        using var coordinator = new RadialLayerCoordinator();

        var begin = coordinator.ProcessFrame(
            State(ControllerButtons.RightShoulder),
            new ControllerButtonEdges(
                ControllerButtons.RightShoulder,
                ControllerButtons.None),
            now: 1000);
        var layerAfterBegin = coordinator.Layer;
        var release = coordinator.ProcessFrame(
            State(ControllerButtons.None),
            new ControllerButtonEdges(
                ControllerButtons.None,
                ControllerButtons.RightShoulder),
            now: 1100);

        Assert.Equal(RadialMenuLayerKind.Command, layerAfterBegin);
        Assert.True(begin.Effects.HasFlag(
            RadialLayerEffect.StartLearningTimer));
        Assert.True(release.Effects.HasFlag(
            RadialLayerEffect.OpenNextTask));
        Assert.Null(coordinator.Layer);
    }

    [Fact]
    public void LearningCuePromotionPreventsShoulderTapNavigation()
    {
        using var coordinator = new RadialLayerCoordinator();
        coordinator.ProcessFrame(
            State(ControllerButtons.LeftShoulder),
            new ControllerButtonEdges(
                ControllerButtons.LeftShoulder,
                ControllerButtons.None),
            now: 1000);

        var promoted = coordinator.PromoteLearningCue(
            ControllerButtons.LeftShoulder);
        var engagedAfterPromotion = coordinator.IsEngaged;
        var release = coordinator.ProcessFrame(
            State(ControllerButtons.None),
            new ControllerButtonEdges(
                ControllerButtons.None,
                ControllerButtons.LeftShoulder),
            now: 1100);

        Assert.True(engagedAfterPromotion);
        Assert.True(promoted.Effects.HasFlag(
            RadialLayerEffect.RefreshMenu));
        Assert.False(release.Effects.HasFlag(
            RadialLayerEffect.OpenPreviousTask));
        Assert.Null(coordinator.Layer);
    }

    [Fact]
    public void PartialRightTriggerFreezesThenSuppressesHeldFaceButton()
    {
        using var coordinator = new RadialLayerCoordinator();

        var candidate = coordinator.ProcessFrame(
            State(ControllerButtons.X, rightTrigger: 0.3),
            new ControllerButtonEdges(
                ControllerButtons.X,
                ControllerButtons.None),
            now: 1000);
        var released = coordinator.ProcessFrame(
            State(ControllerButtons.X, rightTrigger: 0),
            new ControllerButtonEdges(
                ControllerButtons.None,
                ControllerButtons.None),
            now: 1010);

        Assert.Equal(
            RadialInputMap.FrozenTurnCandidateButtons,
            candidate.FrozenButtons);
        Assert.Equal(
            RadialInputMap.FrozenTurnCandidateButtons,
            released.FrozenButtons);
        Assert.False(coordinator.IsRightTriggerCandidate);
        Assert.True(coordinator.SuppressedButtons.HasFlag(
            ControllerButtons.X));
    }

    [Fact]
    public void TurnLayerUsesHysteresisAndHighlightsTurnFork()
    {
        using var coordinator = new RadialLayerCoordinator();
        coordinator.ProcessFrame(
            State(rightTrigger: 0.3),
            default,
            now: 1000);

        var engaged = coordinator.ProcessFrame(
            State(rightTrigger: 0.7),
            default,
            now: 1010);
        var fork = coordinator.ProcessFrame(
            State(ControllerButtons.A, rightTrigger: 0.7),
            new ControllerButtonEdges(
                ControllerButtons.A,
                ControllerButtons.None),
            now: 1020);
        var highlightedAfterFork = coordinator.HighlightedItemId;
        var released = coordinator.ProcessFrame(
            State(rightTrigger: 0.2),
            default,
            now: 1030);

        Assert.True(engaged.Effects.HasFlag(
            RadialLayerEffect.RefreshMenu));
        Assert.Equal(RadialInputAction.Fork, fork.Action);
        Assert.True(fork.Effects.HasFlag(
            RadialLayerEffect.AcknowledgeAction));
        Assert.Equal("turn-fork", highlightedAfterFork);
        Assert.True(released.Effects.HasFlag(
            RadialLayerEffect.StopLearningTimer));
        Assert.False(released.Effects.HasFlag(
            RadialLayerEffect.HideMenu));
        Assert.Null(coordinator.Layer);
    }

    [Fact]
    public void ActionPanelCancelSuppressesClosingButton()
    {
        using var coordinator = new RadialLayerCoordinator();
        var opened = coordinator.ToggleActionPanel(
            ControllerButtons.Y,
            now: 1000);

        var closed = coordinator.ProcessFrame(
            State(ControllerButtons.Y),
            new ControllerButtonEdges(
                ControllerButtons.Y,
                ControllerButtons.None),
            now: 1010);

        Assert.True(opened.Effects.HasFlag(
            RadialLayerEffect.ActionPanelOpened));
        Assert.True(closed.Effects.HasFlag(
            RadialLayerEffect.ActionPanelClosed));
        Assert.True(closed.Effects.HasFlag(
            RadialLayerEffect.HideMenu));
        Assert.True(coordinator.SuppressedButtons.HasFlag(
            ControllerButtons.Y));
        Assert.Null(coordinator.Layer);
    }

    [Fact]
    public void ConfirmationFirstPressStaysOpenAndSecondIsAcknowledged()
    {
        using var coordinator = new RadialLayerCoordinator();
        BeginCommandLayer(coordinator);

        var first = coordinator.ProcessFrame(
            State(
                ControllerButtons.RightShoulder |
                ControllerButtons.A),
            new ControllerButtonEdges(
                ControllerButtons.A,
                ControllerButtons.None),
            now: 1010);
        var firstConfirmed = coordinator.TryConfirmAction(
            RadialInputAction.Approve,
            "command-approve",
            TimeSpan.FromSeconds(10),
            () => { });
        var second = coordinator.ProcessFrame(
            State(
                ControllerButtons.RightShoulder |
                ControllerButtons.A),
            new ControllerButtonEdges(
                ControllerButtons.A,
                ControllerButtons.None),
            now: 1020);
        var secondConfirmed = coordinator.TryConfirmAction(
            RadialInputAction.Approve,
            "command-approve",
            TimeSpan.FromSeconds(10),
            () => { });

        Assert.True(first.Effects.HasFlag(
            RadialLayerEffect.ExecuteFollowUpAction));
        Assert.False(firstConfirmed);
        Assert.True(second.Effects.HasFlag(
            RadialLayerEffect.AcknowledgeAction));
        Assert.True(secondConfirmed);
        Assert.False(coordinator.IsConfirmationPending(
            RadialInputAction.Approve));
    }

    [Fact]
    public void PushToTalkReleaseProducesPhysicalStopEffect()
    {
        using var coordinator = new RadialLayerCoordinator();
        BeginCommandLayer(coordinator);
        var start = coordinator.ProcessFrame(
            State(
                ControllerButtons.RightShoulder |
                ControllerButtons.Back),
            new ControllerButtonEdges(
                ControllerButtons.Back,
                ControllerButtons.None),
            now: 1010);

        Assert.True(start.Effects.HasFlag(
            RadialLayerEffect.ExecuteFollowUpAction));
        Assert.True(coordinator.TryStartPushToTalk());

        var release = coordinator.ProcessFrame(
            State(ControllerButtons.RightShoulder),
            new ControllerButtonEdges(
                ControllerButtons.None,
                ControllerButtons.Back),
            now: 1020);

        Assert.True(release.Effects.HasFlag(
            RadialLayerEffect.StopDictationPhysical));
        Assert.False(coordinator.IsPushToTalkActive);
    }

    [Fact]
    public void WaitingLayerAcceptsOnlyOneTerminalAction()
    {
        using var coordinator = new RadialLayerCoordinator();
        BeginCommandLayer(coordinator);

        var first = coordinator.ProcessFrame(
            State(
                ControllerButtons.RightShoulder |
                ControllerButtons.Y),
            new ControllerButtonEdges(
                ControllerButtons.Y,
                ControllerButtons.None),
            now: 1010);
        var second = coordinator.ProcessFrame(
            State(
                ControllerButtons.RightShoulder |
                ControllerButtons.B),
            new ControllerButtonEdges(
                ControllerButtons.B,
                ControllerButtons.None),
            now: 1020);

        Assert.True(first.Effects.HasFlag(
            RadialLayerEffect.AcknowledgeAction));
        Assert.False(second.Effects.HasFlag(
            RadialLayerEffect.AcknowledgeAction));
        Assert.Equal(
            RadialMenuInteractionPhase.WaitingForResponse,
            coordinator.InteractionPhase);
    }

    private static void BeginCommandLayer(
        RadialLayerCoordinator coordinator)
    {
        coordinator.ProcessFrame(
            State(ControllerButtons.RightShoulder),
            new ControllerButtonEdges(
                ControllerButtons.RightShoulder,
                ControllerButtons.None),
            now: 1000);
    }

    private static ControllerState State(
        ControllerButtons buttons = ControllerButtons.None,
        double rightTrigger = 0) =>
        new(
            IsConnected: true,
            UserIndex: 0,
            PacketNumber: 1,
            Backend: "test",
            Buttons: buttons,
            LeftX: 0,
            LeftY: 0,
            RightX: 0,
            RightY: 0,
            LeftTrigger: 0,
            RightTrigger: rightTrigger);
}
