using AgentController.Domain.Identifiers;
using AgentController.Domain.Inputs;

namespace AgentController.Domain.Actions;

public sealed class BindingRule
{
    public BindingRule(
        string id,
        Gesture gesture,
        InputContext context,
        ActionId actionId,
        int priority = 0,
        bool isEnabled = true)
    {
        Id = IdentifierRules.Normalize(id, nameof(id));
        Gesture = gesture ?? throw new ArgumentNullException(nameof(gesture));
        if (!context.IsDefined)
        {
            throw new ArgumentException(
                "Input context must be defined.",
                nameof(context));
        }

        if (!actionId.IsDefined)
        {
            throw new ArgumentException(
                "Action identifier must be defined.",
                nameof(actionId));
        }

        Context = context;
        ActionId = actionId;
        Priority = priority;
        IsEnabled = isEnabled;
    }

    public string Id { get; }

    public Gesture Gesture { get; }

    public InputContext Context { get; }

    public ActionId ActionId { get; }

    public int Priority { get; }

    public bool IsEnabled { get; }
}
