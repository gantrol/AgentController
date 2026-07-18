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

        Assert.Equal(
            [
                ControllerInteractionIntentKind.CycleRootSidebarScope,
                ControllerInteractionIntentKind.BeginVirtualDialPress,
                ControllerInteractionIntentKind.NavigateConversationTurn,
                ControllerInteractionIntentKind.NavigateSidebarHorizontal,
                ControllerInteractionIntentKind.NavigateSidebarHorizontal,
                ControllerInteractionIntentKind.OpenActionPanel,
                ControllerInteractionIntentKind.OpenSelectedSidebarTask,
                ControllerInteractionIntentKind.SendPrompt,
                ControllerInteractionIntentKind.BeginBaseCancelPress,
            ],
            intents.Select(intent => intent.Kind));
        Assert.Equal(
            ConversationTurnInputAction.PreviousUserMessage,
            intents[2].ConversationAction);
        Assert.Equal(-1, intents[3].Direction);
        Assert.Equal(1, intents[4].Direction);
    }

    [Fact]
    public void DialContextChangesFaceSouthIntentWithoutChangingEdges()
    {
        var coordinator = new ControllerInteractionCoordinator();
        var pressed = ControllerButtons.A;

        var intent = Assert.Single(coordinator.ResolveBaseIntents(
            pressed,
            coordinator.PhysicalEdges(pressed),
            dialContextActive: true));

        Assert.Equal(
            ControllerInteractionIntentKind.SelectVirtualDialOption,
            intent.Kind);
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

        var intent = Assert.Single(coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            coordinator.PhysicalEdges(ControllerButtons.None),
            dialContextActive: false));

        Assert.Equal(
            ControllerInteractionIntentKind.EndVirtualDialPress,
            intent.Kind);
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

        var intent = Assert.Single(coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            coordinator.PhysicalEdges(ControllerButtons.None),
            dialContextActive: false));

        Assert.Equal(
            ControllerInteractionIntentKind.EndConversationBoundaryHold,
            intent.Kind);
        Assert.Equal(ControllerButtons.DPadDown, intent.ReleasedButtons);
    }

    [Fact]
    public void BaseCancelReleaseEmitsEndIntent()
    {
        var coordinator = new ControllerInteractionCoordinator();
        coordinator.CommitButtonHistory(
            ControllerButtons.B,
            ControllerButtons.B);

        var intent = Assert.Single(coordinator.ResolveBaseIntents(
            ControllerButtons.None,
            coordinator.PhysicalEdges(ControllerButtons.None),
            dialContextActive: false));

        Assert.Equal(
            ControllerInteractionIntentKind.EndBaseCancelPress,
            intent.Kind);
    }
}
