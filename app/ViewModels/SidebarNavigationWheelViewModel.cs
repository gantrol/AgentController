using CodexController.Models;

namespace CodexController.ViewModels;

public sealed class SidebarNavigationWheelViewModel : ObservableObject
{
    private string _previousTitle = "—";
    private string _previousBoundary = string.Empty;
    private string _currentTitle = string.Empty;
    private string _currentScope = string.Empty;
    private string _nextTitle = "—";
    private string _nextBoundary = string.Empty;
    private string _position = string.Empty;

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

    public void Update(SidebarNavigationWheelState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        PreviousTitle = state.Previous?.Title ?? "—";
        PreviousBoundary = BoundaryText(state.Previous, "↑");
        CurrentTitle = state.Current.Title;
        CurrentScope = state.Current.ScopeLabel;
        NextTitle = state.Next?.Title ?? "—";
        NextBoundary = BoundaryText(state.Next, "↓");
        Position = $"{state.Position} / {state.Count}";
    }

    private static string BoundaryText(
        SidebarNavigationWheelItem? item,
        string arrow) =>
        item is { CrossesSectionBoundary: true }
            ? $"{arrow} {item.ScopeLabel}"
            : string.Empty;
}
