using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class ConversationActionContract
{
    public static ActionId PreviousUserMessageId { get; } =
        ActionId.Parse("conversation.previous-user-message");

    public static ActionId NextUserMessageId { get; } =
        ActionId.Parse("conversation.next-user-message");
}
