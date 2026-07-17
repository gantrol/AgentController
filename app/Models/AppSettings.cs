namespace CodexController.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string Language { get; set; } = "auto";
    public string ActiveAgentId { get; set; } = "codex";
    public bool BridgeEnabled { get; set; } = true;
    public bool OnlyWhenCodexForeground { get; set; } = true;
    public bool HapticFeedback { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public string RadialMenuMode { get; set; } =
        RadialMenuModes.Learning;
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
    public string DictationShortcut { get; set; } = "Ctrl+Shift+D";
    public string SubmitShortcut { get; set; } = "F22";
    public string CancelShortcut { get; set; } = "Escape";
}
