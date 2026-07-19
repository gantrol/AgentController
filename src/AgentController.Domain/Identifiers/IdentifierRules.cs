namespace AgentController.Domain.Identifiers;

internal static class IdentifierRules
{
    private const int MaximumLength = 128;

    public static string Normalize(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Identifier must not be empty.",
                parameterName);
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"Identifier must be at most {MaximumLength} characters.",
                parameterName);
        }

        if (!IsLetterOrDigit(normalized[0]) ||
            normalized.Any(character => !IsAllowed(character)))
        {
            throw new ArgumentException(
                "Identifier must start with a letter or digit and contain only " +
                "letters, digits, '.', '_', '-', ':' or '/'.",
                parameterName);
        }

        return normalized;
    }

    private static bool IsAllowed(char character) =>
        IsLetterOrDigit(character) || character is '.' or '_' or '-' or ':' or '/';

    private static bool IsLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= '0' and <= '9';
}
