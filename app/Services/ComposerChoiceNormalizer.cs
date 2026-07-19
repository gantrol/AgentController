namespace CodexController.Services;

internal static class ComposerChoiceNormalizer
{
    internal static string Normalize(string value) =>
        new(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
}
