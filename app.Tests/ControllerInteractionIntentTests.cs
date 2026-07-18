using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class ControllerInteractionIntentTests
{
    [Fact]
    public void NeutralFramesReuseTheEmptyIntentSet()
    {
        var coordinator = new ControllerInteractionCoordinator();
        var edges = coordinator.PhysicalEdges(ControllerButtons.None);

        var first = coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            edges,
            dialContextActive: false);
        var second = coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            edges,
            dialContextActive: false);

        Assert.Empty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void PreservesLegacyIntentOrderForSimultaneousPresses()
    {
        var coordinator = new ControllerInteractionCoordinator();
        var pressed =
            ControllerButtons.LeftThumb |
            ControllerButtons.RightThumb |
            ControllerButtons.DPadUp |
            ControllerButtons.DPadLeft |
            ControllerButtons.DPadRight |
            ControllerButtons.Y |
            ControllerButtons.A |
            ControllerButtons.X |
            ControllerButtons.B;

        var intents = coordinator.ResolveBaseIntents(
            pressed,
            coordinator.PhysicalEdges(pressed),
            dialContextActive: false);

        Assert.Collection(
            intents,
            intent => Assert.IsType<
                ControllerInteractionIntent.CycleRootSidebarScope>(intent),
            intent => Assert.IsType<
                ControllerInteractionIntent.BeginVirtualDialPress>(intent),
            intent => Assert.Equal(
                ConversationTurnInputAction.PreviousUserMessage,
                Assert.IsType<
                    ControllerInteractionIntent.NavigateConversationTurn>(
                        intent).Action),
            intent => Assert.Equal(
                -1,
                Assert.IsType<
                    ControllerInteractionIntent.NavigateSidebarHorizontal>(
                        intent).Direction),
            intent => Assert.Equal(
                1,
                Assert.IsType<
                    ControllerInteractionIntent.NavigateSidebarHorizontal>(
                        intent).Direction),
            intent => Assert.IsType<
                ControllerInteractionIntent.OpenActionPanel>(intent),
            intent => Assert.IsType<
                ControllerInteractionIntent.OpenSelectedSidebarTask>(intent),
            intent => Assert.IsType<
                ControllerInteractionIntent.SendPrompt>(intent),
            intent => Assert.IsType<
                ControllerInteractionIntent.BeginBaseCancelPress>(intent));
    }

    [Fact]
    public void DialContextChangesFaceSouthIntentWithoutChangingEdges()
    {
        var coordinator = new ControllerInteractionCoordinator();
        var pressed = ControllerButtons.A;

        var intents = coordinator.ResolveBaseIntents(
            pressed,
            coordinator.PhysicalEdges(pressed),
            dialContextActive: true);

        Assert.IsType<
            ControllerInteractionIntent.SelectVirtualDialOption>(
                Assert.Single(intents));
    }

    [Fact]
    public void HeldBaseButtonsDoNotEmitRepeatedIntents()
    {
        var coordinator = new ControllerInteractionCoordinator();
        var pressed =
            ControllerButtons.A |
            ControllerButtons.X |
            ControllerButtons.B;
        coordinator.CommitButtonHistory(pressed, pressed);

        var intents = coordinator.ResolveBaseIntents(
            pressed,
            coordinator.PhysicalEdges(pressed),
            dialContextActive: false);

        Assert.Empty(intents);
    }

    [Fact]
    public void RightThumbReleaseSurvivesBaseLayerSuppression()
    {
        var coordinator = new ControllerInteractionCoordinator();
        coordinator.CommitButtonHistory(
            ControllerButtons.RightThumb,
            ControllerButtons.RightThumb);

        var intents = coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            coordinator.PhysicalEdges(ControllerButtons.None),
            dialContextActive: false);

        Assert.IsType<
            ControllerInteractionIntent.EndVirtualDialPress>(
                Assert.Single(intents));
    }

    [Fact]
    public void RightThumbPressRequiresBaseLayerEligibility()
    {
        var coordinator = new ControllerInteractionCoordinator();

        var intents = coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            coordinator.PhysicalEdges(ControllerButtons.RightThumb),
            dialContextActive: false);

        Assert.Empty(intents);
    }

    [Fact]
    public void PhysicalDPadReleaseEndsConversationHold()
    {
        var coordinator = new ControllerInteractionCoordinator();
        coordinator.CommitButtonHistory(
            ControllerButtons.None,
            ControllerButtons.DPadDown);

        var intents = coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            coordinator.PhysicalEdges(ControllerButtons.None),
            dialContextActive: false);
        var release = Assert.IsType<
            ControllerInteractionIntent.EndConversationBoundaryHold>(
                Assert.Single(intents));

        Assert.Equal(
            ControllerButtons.DPadDown,
            release.ReleasedButtons);
    }

    [Fact]
    public void BaseCancelReleaseEmitsEndIntent()
    {
        var coordinator = new ControllerInteractionCoordinator();
        coordinator.CommitButtonHistory(
            ControllerButtons.B,
            ControllerButtons.B);

        var intents = coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            coordinator.PhysicalEdges(ControllerButtons.None),
            dialContextActive: false);

        Assert.IsType<
            ControllerInteractionIntent.EndBaseCancelPress>(
                Assert.Single(intents));
    }
}
