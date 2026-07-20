using CodexController.Models;

namespace CodexController.ViewModels;

public sealed class SidebarNavigationMenuViewModel : ObservableObject
{
    public const double SinglePanelWidth = 448;
    public const double TwoPanelWidth = 888;
    public const double MenuHeight = 430;

    private SidebarNavigationMenuPanelViewModel _rootPanel =
        SidebarNavigationMenuPanelViewModel.Empty("Sidebar");
    private SidebarNavigationMenuPanelViewModel? _childPanel;
    private double _viewWidth = SinglePanelWidth;
    private string _navigateGlyph = "LS";
    private string _navigateHint = "Move";
    private string _cycleScopeGlyph = "L3";
    private string _cycleScopeHint = "Region";
    private string _openGlyph = "A";
    private string _openHint = "Open";

    public SidebarNavigationMenuPanelViewModel RootPanel
    {
        get => _rootPanel;
        private set => SetProperty(ref _rootPanel, value);
    }

    public SidebarNavigationMenuPanelViewModel? ChildPanel
    {
        get => _childPanel;
        private set
        {
            if (!SetProperty(ref _childPanel, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasChildPanel));
        }
    }

    public bool HasChildPanel => ChildPanel is not null;

    public double ViewWidth
    {
        get => _viewWidth;
        private set => SetProperty(ref _viewWidth, value);
    }

    public double ViewHeight => MenuHeight;

    public string NavigateGlyph
    {
        get => _navigateGlyph;
        private set => SetProperty(ref _navigateGlyph, value);
    }

    public string NavigateHint
    {
        get => _navigateHint;
        private set => SetProperty(ref _navigateHint, value);
    }

    public string CycleScopeGlyph
    {
        get => _cycleScopeGlyph;
        private set => SetProperty(ref _cycleScopeGlyph, value);
    }

    public string CycleScopeHint
    {
        get => _cycleScopeHint;
        private set => SetProperty(ref _cycleScopeHint, value);
    }

    public string OpenGlyph
    {
        get => _openGlyph;
        private set => SetProperty(ref _openGlyph, value);
    }

    public string OpenHint
    {
        get => _openHint;
        private set => SetProperty(ref _openHint, value);
    }

    public void Update(SidebarNavigationMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        RootPanel = CreatePanel(
            state.Root,
            string.IsNullOrWhiteSpace(state.Title)
                ? state.Root.Title
                : state.Title);
        ChildPanel = state.Child is null
            ? null
            : CreatePanel(state.Child, state.Child.Title);
        ViewWidth = ChildPanel is null
            ? SinglePanelWidth
            : TwoPanelWidth;
        NavigateGlyph = state.NavigateGlyph;
        NavigateHint = state.NavigateHint;
        CycleScopeGlyph = state.CycleScopeGlyph;
        CycleScopeHint = state.CycleScopeHint;
        OpenGlyph = state.OpenGlyph;
        OpenHint = state.OpenHint;
    }

    private static SidebarNavigationMenuPanelViewModel CreatePanel(
        SidebarNavigationMenuPanel panel,
        string title)
    {
        var sections = panel.Sections
            .Select(section => new SidebarNavigationMenuSectionViewModel(
                section.Scope,
                section.Title,
                section.Items
                    .Select(item => new SidebarNavigationMenuItemViewModel(
                        item.Id,
                        item.Title,
                        item.Subtitle,
                        item.ScopeLabel,
                        item.HasChildren,
                        item.IsSelected,
                        item.IsSelected && panel.IsActive,
                        item.IsPinned))
                    .ToArray()))
            .ToArray();

        return new SidebarNavigationMenuPanelViewModel(
            title,
            sections,
            panel.IsActive,
            panel.SelectedPosition,
            panel.Items.Count);
    }
}

public sealed record SidebarNavigationMenuPanelViewModel(
    string Title,
    IReadOnlyList<SidebarNavigationMenuSectionViewModel> Sections,
    bool IsActive,
    int SelectedPosition,
    int ItemCount)
{
    public string PositionText =>
        ItemCount == 0
            ? "0 / 0"
            : $"{SelectedPosition} / {ItemCount}";

    public static SidebarNavigationMenuPanelViewModel Empty(string title) =>
        new(title, [], IsActive: true, SelectedPosition: 0, ItemCount: 0);
}

public sealed record SidebarNavigationMenuSectionViewModel(
    SidebarScope Scope,
    string Title,
    IReadOnlyList<SidebarNavigationMenuItemViewModel> Items)
{
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
}

public sealed record SidebarNavigationMenuItemViewModel(
    string Id,
    string Title,
    string Subtitle,
    string ScopeLabel,
    bool HasChildren,
    bool IsSelected,
    bool IsActiveSelection,
    bool IsPinned);
