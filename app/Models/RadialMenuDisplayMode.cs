namespace CodexController.Models;

public enum RadialMenuDisplayMode
{
    Always,
    Learning,
    Off,
}

public static class RadialMenuDisplayModeParser
{
    public static RadialMenuDisplayMode ParseOrDefault(
        string? value,
        RadialMenuDisplayMode fallback = RadialMenuDisplayMode.Learning)
    {
        return TryParse(value, out var mode) ? mode : fallback;
    }

    public static bool TryParse(
        string? value,
        out RadialMenuDisplayMode mode)
    {
        value = value?.Trim();
        if (string.Equals(
                value,
                RadialMenuModes.Always,
                StringComparison.OrdinalIgnoreCase))
        {
            mode = RadialMenuDisplayMode.Always;
            return true;
        }

        if (string.Equals(
                value,
                RadialMenuModes.Learning,
                StringComparison.OrdinalIgnoreCase))
        {
            mode = RadialMenuDisplayMode.Learning;
            return true;
        }

        if (string.Equals(
                value,
                RadialMenuModes.Off,
                StringComparison.OrdinalIgnoreCase))
        {
            mode = RadialMenuDisplayMode.Off;
            return true;
        }

        mode = default;
        return false;
    }
}
