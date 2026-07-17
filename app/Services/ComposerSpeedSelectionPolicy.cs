namespace CodexController.Services;

internal static class ComposerSpeedSelectionPolicy
{
    internal static string TargetLabel(bool fast) =>
        fast ? "Fast" : "Standard";

    internal static bool MatchesOption(
        string? optionName,
        bool fast) =>
        StartsWithChoice(optionName, TargetLabel(fast));

    internal static bool MatchesCategory(
        string? categoryName,
        bool fast)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        const string category = "Speed";
        var value = categoryName.Trim();
        if (!value.StartsWith(
                category,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return StartsWithChoice(
            value[category.Length..].Trim(' ', ':', '-', '·'),
            TargetLabel(fast));
    }

    /// <summary>
    /// Reads the current speed from a composer button name such as
    /// "5.1 High · Fast". Returns null when the name carries no
    /// recognizable trailing speed token, so callers fall back to the
    /// live picker instead of guessing.
    /// </summary>
    internal static bool? TryParseSpeedSuffix(string? buttonName)
    {
        var normalized = Normalize(buttonName);
        if (normalized.EndsWith("standard", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.EndsWith("fast", StringComparison.Ordinal))
        {
            return true;
        }

        return null;
    }

    internal static bool OptionMatchesCurrentValue(
        string? optionName,
        string? currentValue)
    {
        var option = Normalize(optionName);
        var current = Normalize(currentValue);
        return current.Length > 0 &&
            (
                option.Equals(current, StringComparison.Ordinal) ||
                option.StartsWith(current, StringComparison.Ordinal)
            );
    }

    private static bool StartsWithChoice(
        string? value,
        string choice)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith(
                choice,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmed.Length == choice.Length ||
            !char.IsLetterOrDigit(trimmed[choice.Length]);
    }

    private static string Normalize(string? value) =>
        string.Concat(
            (value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant));
}
