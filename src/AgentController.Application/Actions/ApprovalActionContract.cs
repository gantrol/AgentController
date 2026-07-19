using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class ApprovalActionContract
{
    public static ActionId AcceptId { get; } =
        ActionId.Parse("approval.accept");

    public static ActionId DeclineId { get; } =
        ActionId.Parse("approval.decline");
}
