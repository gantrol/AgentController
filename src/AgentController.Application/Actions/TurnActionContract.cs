using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class TurnActionContract
{
    public static ActionId SteerId { get; } =
        ActionId.Parse("turn.steer");

    public static ActionId QueueId { get; } =
        ActionId.Parse("turn.queue");

    public static ActionId StopId { get; } =
        ActionId.Parse("turn.stop");
}
