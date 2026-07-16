namespace CodexController.Agents;

/// <summary>
/// Stable, locale-independent identity for an agent integration.
/// </summary>
public readonly record struct AgentId
{
    private const int MaxLength = 64;

    public AgentId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength || !IsValid(value))
        {
            throw new ArgumentException(
                "Agent identifiers must be lowercase slugs containing " +
                "letters, digits, and single hyphens.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator AgentId(string value) => new(value);

    private static bool IsValid(string value)
    {
        var segmentStart = true;

        foreach (var character in value)
        {
            if (character == '-')
            {
                if (segmentStart)
                {
                    return false;
                }

                segmentStart = true;
                continue;
            }

            if (segmentStart)
            {
                if (character is < 'a' or > 'z')
                {
                    return false;
                }

                segmentStart = false;
                continue;
            }

            if (
                character is not (>= 'a' and <= 'z') &&
                character is not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        return !segmentStart;
    }
}
