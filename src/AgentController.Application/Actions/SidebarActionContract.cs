using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class SidebarActionContract
{
    public static ActionId ToggleId { get; } =
        ActionId.Parse("sidebar.toggle");
}
