using AgentController.Domain.Identifiers;

namespace AgentController.Domain.Actions;

public readonly record struct ActionId
{
    private ActionId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsDefined => !string.IsNullOrEmpty(Value);

    public static ActionId Parse(string value) =>
        new(IdentifierRules.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;
}
