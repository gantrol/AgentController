using CodexMicro.Codex;
using System.Text.Json.Nodes;
using AgentController.MicroSurface.Wpf.SoftwareControl;

namespace CodexMicro.Plugin;

internal static class KeypadCapabilities
{
    internal static JsonNode Read() => JsonSupport.Node(new
    {
        surface = "Original Codex Micro WPF keypad, core controls only",
        removed = new[] { "DeepSeek and external Harnesses", "Qwen3 ASR and local voice services", "additional keypad windows" },
        transport = "Codex desktop IPC + app-server + Windows UI Automation",
        requiresDriver = false,
        implemented = new[]
        {
            "control/monitor panel switching",
            "six agent keys using the displayed roster",
            "new draft", "fork chat", "Fast service toggle",
            "single command/file approval or decline",
            "reasoning dial in reasoning mode", "configured quick models A/B",
            "MCP: explicit message submission and exact-turn interruption",
            "foreground composer submission with UI observation",
            "composer control focus and selection", "conversation scrolling",
            "Plan/sidebar/back/forward joystick navigation",
            "configured skill mention insertion with name/path verification",
            "custom key/joystick bindings for the implemented actions"
        },
        unavailable = new[]
        {
            "native push-to-talk and realtime voice",
            "blank-draft model and reasoning changes",
            "arbitrary Codex UI commands",
            "hardware lighting and device settings"
        },
        limitation = "UI controls require the foreground Codex window and an identifiable target. Unobserved changes return OutcomeUnknown; unsupported controls return NotSent. No HID fallback or automatic retry is used.",
        verification = "Partial regression coverage. Implementation and transport acknowledgements do not establish complete live control acceptance."
    });
}
