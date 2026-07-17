using CodexController.Models;

namespace CodexController.Controllers;

public enum ConversationTurnInputAction
{
    None,
    PreviousUserMessage,
    NextUserMessage,
}

public static class ConversationTurnInputMap
{
    public const string PreviousShortcut = "Alt+Up";
    public const string NextShortcut = "Alt+Down";

    public static ConversationTurnInputAction Resolve(
        ControllerButtons downEdges)
    {
        if (downEdges.HasFlag(ControllerButtons.DPadUp))
        {
            return ConversationTurnInputAction.PreviousUserMessage;
        }

        if (downEdges.HasFlag(ControllerButtons.DPadDown))
        {
            return ConversationTurnInputAction.NextUserMessage;
        }

        return ConversationTurnInputAction.None;
    }

    public static string? ShortcutFor(ConversationTurnInputAction action)
    {
        return action switch
        {
            ConversationTurnInputAction.PreviousUserMessage =>
                PreviousShortcut,
            ConversationTurnInputAction.NextUserMessage =>
                NextShortcut,
            _ => null,
        };
    }
}
