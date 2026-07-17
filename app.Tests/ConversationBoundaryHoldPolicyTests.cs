using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class ConversationBoundaryHoldPolicyTests
{
    [Theory]
    [InlineData(
        ConversationTurnInputAction.PreviousUserMessage,
        ConversationBoundary.Top)]
    [InlineData(
        ConversationTurnInputAction.NextUserMessage,
        ConversationBoundary.Bottom)]
    public void MapsTurnDirectionToBoundary(
        ConversationTurnInputAction action,
        ConversationBoundary expected)
    {
        Assert.Equal(
            expected,
            ConversationBoundaryHoldPolicy.ResolveBoundary(action));
    }

    [Theory]
    [InlineData(ConversationBoundary.Top, 4000)]
    [InlineData(ConversationBoundary.Bottom, 3000)]
    public void UsesAsymmetricHoldDurations(
        ConversationBoundary boundary,
        int expectedMilliseconds)
    {
        Assert.Equal(
            expectedMilliseconds,
            ConversationBoundaryHoldPolicy.ResolveHoldMilliseconds(
                boundary,
                topHoldMs: 4000,
                bottomHoldMs: 3000));
    }

    [Theory]
    [InlineData(ConversationBoundary.Top, ControllerButtons.DPadUp)]
    [InlineData(ConversationBoundary.Bottom, ControllerButtons.DPadDown)]
    public void KeepsTrackingTheOriginatingButton(
        ConversationBoundary boundary,
        ControllerButtons expected)
    {
        Assert.Equal(
            expected,
            ConversationBoundaryHoldPolicy.ResolveButton(boundary));
    }
}
