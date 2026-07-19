using AgentController.Domain.Actions;

namespace AgentController.Application.Actions;

public static class ComposerActionContract
{
    public static ActionId SubmitId { get; } =
        ActionId.Parse("composer.submit");

    public static ActionId ClearId { get; } =
        ActionId.Parse("composer.clear");
}
