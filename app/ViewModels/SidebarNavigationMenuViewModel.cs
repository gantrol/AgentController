using CodexController.Models;

namespace CodexController.ViewModels;

public sealed class SidebarNavigationMenuViewModel : ObservableObject
{
    private string _previousTitle = "—";
    private string _previousBoundary = string.Empty;
    private string _currentTitle = string.Empty;
    private string _currentScope = string.Empty;
    private string _nextTitle = "—";
    private string _nextBoundary = string.Empty;
    private string _position = string.Empty;
    private string _title = "Sidebar";
    private string _navigateGlyph = "LS";
    private string _navigateHint = "Move";
    private string _cycleScopeGlyph = "L3";
    private string _cycleScopeHint = "Region";
    private string _openGlyph = "A";
    private string _openHint = "Open";

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string PreviousTitle
    {
        get => _previousTitle;
        private set => SetProperty(ref _previousTitle, value);
    }

    public string PreviousBoundary
    {
        get => _previousBoundary;
        private set => SetProperty(ref _previousBoundary, value);
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => SetProperty(ref _currentTitle, value);
    }

    public string CurrentScope
    {
        get => _currentScope;
        private set => SetProperty(ref _currentScope, value);
    }

    public string NextTitle
    {
        get => _nextTitle;
        private set => SetProperty(ref _nextTitle, value);
    }

    public string NextBoundary
    {
        get => _nextBoundary;
        private set => SetProperty(ref _nextBoundary, value);
    }

    public string Position
    {
        get => _position;
        private set => SetProperty(ref _position, value);
    }

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

        PreviousTitle = state.Previous?.Title ?? "—";
        PreviousBoundary = BoundaryText(state.Previous);
        CurrentTitle = state.Current.Title;
        CurrentScope = state.Current.ScopeLabel;
        NextTitle = state.Next?.Title ?? "—";
        NextBoundary = BoundaryText(state.Next);
        Position = $"{state.Position} / {state.Count}";
        Title = state.Title;
        NavigateGlyph = state.NavigateGlyph;
        NavigateHint = state.NavigateHint;
        CycleScopeGlyph = state.CycleScopeGlyph;
        CycleScopeHint = state.CycleScopeHint;
        OpenGlyph = state.OpenGlyph;
        OpenHint = state.OpenHint;
    }

    private static string BoundaryText(
        SidebarNavigationMenuItem? item) =>
        item is { CrossesSectionBoundary: true }
            ? item.ScopeLabel
            : string.Empty;
}
