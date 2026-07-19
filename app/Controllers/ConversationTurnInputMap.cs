using AgentController.Application.Actions;
using AgentController.Domain.Actions;
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

    public static ActionId? ActionIdFor(ConversationTurnInputAction action)
    {
        return action switch
        {
            ConversationTurnInputAction.PreviousUserMessage =>
                ConversationActionContract.PreviousUserMessageId,
            ConversationTurnInputAction.NextUserMessage =>
                ConversationActionContract.NextUserMessageId,
            _ => null,
        };
    }
}
