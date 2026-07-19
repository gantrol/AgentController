using AgentController.Domain.Identifiers;

namespace AgentController.Domain.Inputs;

public readonly record struct InputContext
{
    private InputContext(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsDefined => !string.IsNullOrEmpty(Value);

    public static InputContext Global { get; } = Parse("global");

    public static InputContext Parse(string value) =>
        new(IdentifierRules.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;
}
