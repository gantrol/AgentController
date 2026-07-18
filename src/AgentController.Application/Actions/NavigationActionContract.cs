using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class NavigationActionContract
{
    public static ActionId BackId { get; } =
        ActionId.Parse("navigation.back");

    public static ActionId ForwardId { get; } =
        ActionId.Parse("navigation.forward");
}
