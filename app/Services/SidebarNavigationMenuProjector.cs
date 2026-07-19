using CodexController.Models;

namespace CodexController.Services;

/// <summary>
/// Projects the controller's frozen navigation directories into an adjacent
/// parent/child menu without changing navigation state.
/// </summary>
public static class SidebarNavigationMenuProjector
{
    public static readonly IReadOnlyList<SidebarScope> RootScopes =
    [
        SidebarScope.PinnedTasks,
        SidebarScope.PinnedProjects,
        SidebarScope.Projects,
        SidebarScope.ProjectlessTasks,
    ];

    public static SidebarNavigationMenuState Project(
        IReadOnlyList<SidebarEntry> rootEntries,
        string? selectedRootId,
        Func<SidebarScope, string> scopeLabel,
        IReadOnlyList<SidebarEntry>? childEntries = null,
        string? selectedChildId = null,
        string? childTitle = null,
        bool childIsActive = false)
    {
        ArgumentNullException.ThrowIfNull(rootEntries);
        ArgumentNullException.ThrowIfNull(scopeLabel);

        var selectedRoot = FindSelected(rootEntries, selectedRootId);
        var rootSections = RootScopes
            .Select(scope => new SidebarNavigationMenuSection(
                scope,
                scopeLabel(scope),
                rootEntries
                    .Where(entry => entry.NavigationScope == scope)
                    .Select(entry => CreateItem(
                        entry,
                        scopeLabel(scope),
                        ReferenceEquals(entry, selectedRoot) ||
                        Equals(entry, selectedRoot)))
                    .ToArray()))
            .ToArray();

        var hasDisclosedProject =
            selectedRoot is { IsProject: true } &&
            childEntries is not null;
        var root = new SidebarNavigationMenuPanel(
            "Sidebar",
            rootSections,
            IsActive: !hasDisclosedProject || !childIsActive);

        if (!hasDisclosedProject)
        {
            return new SidebarNavigationMenuState(root);
        }

        var selectedChild = FindSelected(childEntries!, selectedChildId);
        var childScopeLabel = scopeLabel(SidebarScope.ProjectTasks);
        var child = new SidebarNavigationMenuPanel(
            string.IsNullOrWhiteSpace(childTitle)
                ? selectedRoot!.Title
                : childTitle,
            [
                new SidebarNavigationMenuSection(
                    SidebarScope.ProjectTasks,
                    string.Empty,
                    childEntries!
                        .Select(entry => CreateItem(
                            entry,
                            childScopeLabel,
                            ReferenceEquals(entry, selectedChild) ||
                            Equals(entry, selectedChild)))
                        .ToArray()),
            ],
            IsActive: childIsActive);

        return new SidebarNavigationMenuState(root, child);
    }

    private static SidebarEntry? FindSelected(
        IReadOnlyList<SidebarEntry> entries,
        string? selectedId)
    {
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var match = entries.FirstOrDefault(entry =>
                string.Equals(
                    entry.Id,
                    selectedId,
                    StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return entries.FirstOrDefault();
    }

    private static SidebarNavigationMenuItem CreateItem(
        SidebarEntry entry,
        string scopeLabel,
        bool isSelected) =>
        new(
            entry.Id,
            entry.Title,
            entry.Subtitle,
            entry.NavigationScope,
            scopeLabel,
            entry.IsProject
                ? SidebarNavigationMenuItemKind.Project
                : SidebarNavigationMenuItemKind.Task,
            isSelected,
            entry.IsPinned || entry.ProjectIsPinned);
}
