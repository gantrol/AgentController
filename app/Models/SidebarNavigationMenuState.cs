namespace CodexController.Models;

public enum SidebarNavigationMenuItemKind
{
    Task,
    Project,
}

/// <summary>
/// One row in a sidebar hierarchy panel.
/// </summary>
public sealed record SidebarNavigationMenuItem(
    string Id,
    string Title,
    string Subtitle,
    SidebarScope Scope,
    string ScopeLabel,
    SidebarNavigationMenuItemKind Kind,
    bool IsSelected,
    bool IsPinned = false)
{
    public bool HasChildren => Kind == SidebarNavigationMenuItemKind.Project;
}

/// <summary>
/// A stable root-directory section. Empty sections remain visible so the four
/// controller regions do not appear to move when data changes.
/// </summary>
public sealed record SidebarNavigationMenuSection(
    SidebarScope Scope,
    string Title,
    IReadOnlyList<SidebarNavigationMenuItem> Items);

/// <summary>
/// One level in the adjacent parent/child sidebar menu.
/// </summary>
public sealed record SidebarNavigationMenuPanel(
    string Title,
    IReadOnlyList<SidebarNavigationMenuSection> Sections,
    bool IsActive)
{
    public IReadOnlyList<SidebarNavigationMenuItem> Items =>
        Sections.SelectMany(section => section.Items).ToArray();

    public SidebarNavigationMenuItem? SelectedItem =>
        Items.FirstOrDefault(item => item.IsSelected);

    public int SelectedPosition
    {
        get
        {
            var items = Items;
            var selected = SelectedItem;
            if (selected is null)
            {
                return 0;
            }

            for (var index = 0; index < items.Count; index++)
            {
                if (ReferenceEquals(items[index], selected) ||
                    Equals(items[index], selected))
                {
                    return index + 1;
                }
            }

            return 0;
        }
    }
}

/// <summary>
/// Adjacent hierarchy projection. Root is always the complete four-section
/// directory. Child is present only while a project row is disclosed.
/// </summary>
public sealed record SidebarNavigationMenuState
{
    public SidebarNavigationMenuState(
        SidebarNavigationMenuPanel root,
        SidebarNavigationMenuPanel? child = null)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Child = child;
        Position = ActivePanel.SelectedPosition;
        Count = ActivePanel.Items.Count;
    }

    public SidebarNavigationMenuPanel Root { get; }

    public SidebarNavigationMenuPanel? Child { get; }

    public SidebarNavigationMenuPanel ActivePanel =>
        Child is { IsActive: true }
            ? Child
            : Root;

    public SidebarNavigationMenuItem? ActiveItem =>
        ActivePanel.SelectedItem;

    public bool HasChild => Child is not null;

    public int Position { get; }

    public int Count { get; }

    public string Title { get; init; } = "Sidebar";

    public string NavigateGlyph { get; init; } = "LS";

    public string NavigateHint { get; init; } = "Move";

    public string CycleScopeGlyph { get; init; } = "L3";

    public string CycleScopeHint { get; init; } = "Region";

    public string OpenGlyph { get; init; } = "A";

    public string OpenHint { get; init; } = "Open";
}
