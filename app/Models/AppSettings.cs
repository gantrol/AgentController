namespace CodexController.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 3;
    public string Language { get; set; } = "auto";
    public string TextSize { get; set; } = UiTextSizes.Medium;
    public string ActiveAgentId { get; set; } = "codex";
    public bool BridgeEnabled { get; set; } = true;
    public bool OnlyWhenCodexForeground { get; set; } = true;
    public bool HapticFeedback { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public string RadialMenuMode { get; set; } =
        RadialMenuModes.Learning;
    public string ComposerDialMode { get; set; } =
        ComposerDialModes.Simple;
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public double DeadZone { get; set; } = 0.58;
    public int RepeatDelayMs { get; set; } = 360;
    public int RepeatIntervalMs { get; set; } = 220;
    public string ReasoningDownShortcut { get; set; } = "F17";
    public string ReasoningUpShortcut { get; set; } = "F18";
    public string PlanToggleShortcut { get; set; } = "F19";
    public string ModelPickerShortcut { get; set; } = "Ctrl+Shift+M";
    public string FastToggleShortcut { get; set; } = "F20";
    public string ForkShortcut { get; set; } = "F21";
    public string DictationShortcut { get; set; } = "Ctrl+Shift+D";
    public string CancelShortcut { get; set; } = "Escape";
}

public static class UiTextSizes
{
    public const string Small = "small";
    public const string Medium = "medium";
    public const string Large = "large";
    public const string ExtraLarge = "extra-large";

    public static string Normalize(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            Small => Small,
            Large => Large,
            ExtraLarge => ExtraLarge,
            _ => Medium,
        };

    public static double Scale(string? value) =>
        Normalize(value) switch
        {
            Small => 10d / 14d,
            Large => 18d / 14d,
            ExtraLarge => 23d / 14d,
            _ => 1d,
        };
}
