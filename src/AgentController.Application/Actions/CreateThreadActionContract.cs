using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class CreateThreadActionContract
{
    public static ActionId Id { get; } = ActionId.Parse("thread.create");
}
