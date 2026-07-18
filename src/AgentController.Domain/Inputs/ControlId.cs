using AgentController.Domain.Identifiers;

namespace AgentController.Domain.Inputs;

public readonly record struct ControlId
{
    private ControlId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public bool IsDefined => !string.IsNullOrEmpty(Value);

    public static ControlId Parse(string value) =>
        new(IdentifierRules.Normalize(value, nameof(value)));

    public static bool TryParse(string? value, out ControlId controlId)
    {
        try
        {
            controlId = Parse(value!);
            return true;
        }
        catch (ArgumentException)
        {
            controlId = default;
            return false;
        }
    }

    public override string ToString() => Value ?? string.Empty;
}
