namespace CodexController.Models;

/// <summary>
/// One visible row in the controller-owned sidebar wheel.
/// </summary>
public sealed record SidebarNavigationWheelItem(
    string Title,
    SidebarScope Scope,
    string ScopeLabel,
    bool CrossesSectionBoundary);

/// <summary>
/// Lightweight three-row projection of the frozen sidebar directory.
/// </summary>
public sealed record SidebarNavigationWheelState(
    SidebarNavigationWheelItem? Previous,
    SidebarNavigationWheelItem Current,
    SidebarNavigationWheelItem? Next,
    int Position,
    int Count);
