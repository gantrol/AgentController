using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class TurnActionContract
{
    public static ActionId StopId { get; } =
        ActionId.Parse("turn.stop");
}
