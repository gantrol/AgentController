namespace CodexController.Models;

public static class RadialMenuModes
{
    public const string Always = "always";
    public const string Learning = "learning";
    public const string Off = "off";

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            Always => Always,
            Off => Off,
            Learning => Learning,
            _ => Learning,
        };
    }
}
