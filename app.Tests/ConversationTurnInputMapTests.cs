using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class ConversationTurnInputMapTests
{
    [Theory]
    [InlineData(
        ControllerButtons.DPadUp,
        ConversationTurnInputAction.PreviousUserMessage,
        "Alt+Up")]
    [InlineData(
        ControllerButtons.DPadDown,
        ConversationTurnInputAction.NextUserMessage,
        "Alt+Down")]
    public void MapsBaseDPadToCodexUserMessageNavigation(
        ControllerButtons downEdges,
        ConversationTurnInputAction expectedAction,
        string expectedShortcut)
    {
        var action = ConversationTurnInputMap.Resolve(downEdges);

        Assert.Equal(expectedAction, action);
        Assert.Equal(
            expectedShortcut,
            ConversationTurnInputMap.ShortcutFor(action));
    }

    [Fact]
    public void IgnoresButtonsOutsideConversationNavigation()
    {
        var action = ConversationTurnInputMap.Resolve(
            ControllerButtons.DPadLeft |
            ControllerButtons.DPadRight |
            ControllerButtons.A);

        Assert.Equal(ConversationTurnInputAction.None, action);
        Assert.Null(ConversationTurnInputMap.ShortcutFor(action));
    }
}
