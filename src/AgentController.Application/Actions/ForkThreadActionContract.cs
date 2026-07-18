using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class ForkThreadActionContract
{
    public static ActionId Id { get; } = ActionId.Parse("thread.fork");
}
