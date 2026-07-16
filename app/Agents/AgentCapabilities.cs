namespace CodexController.Agents;

/// <summary>
/// Discoverable feature surface exposed by an agent target.
/// </summary>
[Flags]
public enum AgentCapabilities
{
    None = 0,
    Presence = 1 << 0,
    Shortcuts = 1 << 1,
    Workspace = 1 << 2,
    Sidebar = 1 << 3,
    Composer = 1 << 4,
    DeepLinks = 1 << 5,
    Keybindings = 1 << 6,
}
