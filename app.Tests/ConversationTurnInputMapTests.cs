using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class ConversationTurnInputMapTests
{
    [Theory]
    [InlineData(
        ControllerButtons.DPadUp,
        ConversationTurnInputAction.PreviousUserMessage,
        "conversation.previous-user-message")]
    [InlineData(
        ControllerButtons.DPadDown,
        ConversationTurnInputAction.NextUserMessage,
        "conversation.next-user-message")]
    public void MapsBaseDPadToCodexUserMessageNavigation(
        ControllerButtons downEdges,
        ConversationTurnInputAction expectedAction,
        string expectedActionId)
    {
        var action = ConversationTurnInputMap.Resolve(downEdges);

        Assert.Equal(expectedAction, action);
        Assert.Equal(
            expectedActionId,
            ConversationTurnInputMap.ActionIdFor(action)?.Value);
    }

    [Fact]
    public void IgnoresButtonsOutsideConversationNavigation()
    {
        var action = ConversationTurnInputMap.Resolve(
            ControllerButtons.DPadLeft |
            ControllerButtons.DPadRight |
            ControllerButtons.A);

        Assert.Equal(ConversationTurnInputAction.None, action);
        Assert.Null(ConversationTurnInputMap.ActionIdFor(action));
    }
}
