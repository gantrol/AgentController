namespace CodexController.Models;

public static class ComposerDialModes
{
    public const string Simple = "simple";
    public const string Advanced = "advanced";

    public static string Normalize(string? value)
    {
        if (string.Equals(
                value?.Trim(),
                Advanced,
                StringComparison.OrdinalIgnoreCase))
        {
            return Advanced;
        }

        return Simple;
    }

    public static bool IsAdvanced(string? value)
    {
        return string.Equals(
            Normalize(value),
            Advanced,
            StringComparison.Ordinal);
    }
}
