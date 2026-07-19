using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class OpenThreadActionContract
{
    public static ActionId Id { get; } = ActionId.Parse("thread.open");

    public const string ThreadIdParameter = "threadId";
}
