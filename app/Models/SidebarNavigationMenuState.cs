namespace CodexController.Models;

/// <summary>
/// One visible row in the controller-owned sidebar wheel.
/// </summary>
public sealed record SidebarNavigationMenuItem(
    string Title,
    SidebarScope Scope,
    string ScopeLabel,
    bool CrossesSectionBoundary);

/// <summary>
/// Lightweight three-row projection of the frozen sidebar directory.
/// </summary>
public sealed record SidebarNavigationMenuState(
    SidebarNavigationMenuItem? Previous,
    SidebarNavigationMenuItem Current,
    SidebarNavigationMenuItem? Next,
    int Position,
    int Count)
{
    public string Title { get; init; } = "Sidebar";

    public string NavigateGlyph { get; init; } = "LS";

    public string NavigateHint { get; init; } = "Move";

    public string CycleScopeGlyph { get; init; } = "L3";

    public string CycleScopeHint { get; init; } = "Region";

    public string OpenGlyph { get; init; } = "A";

    public string OpenHint { get; init; } = "Open";
}
