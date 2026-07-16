using CodexController.Models;

namespace CodexController.ViewModels;

public sealed class RadialMenuViewModel : ObservableObject
{
    private RadialMenuLayerKind _layer;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _modifierGlyph = string.Empty;
    private RadialMenuDisplayMode _displayMode =
        RadialMenuDisplayMode.Learning;
    private bool _isVisible;

    public RadialMenuViewModel()
    {
        Top = new RadialMenuSlotViewModel(
            RadialMenuSlotPosition.Top);
        Right = new RadialMenuSlotViewModel(
            RadialMenuSlotPosition.Right);
        Bottom = new RadialMenuSlotViewModel(
            RadialMenuSlotPosition.Bottom);
        Left = new RadialMenuSlotViewModel(
            RadialMenuSlotPosition.Left);
        CenterLeft = new RadialMenuSlotViewModel(
            RadialMenuSlotPosition.CenterLeft);
        CenterRight = new RadialMenuSlotViewModel(
            RadialMenuSlotPosition.CenterRight);
    }

    public RadialMenuLayerKind Layer
    {
        get => _layer;
        private set => SetProperty(ref _layer, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set
        {
            if (SetProperty(ref _subtitle, value))
            {
                OnPropertyChanged(nameof(HasSubtitle));
            }
        }
    }

    public bool HasSubtitle => !string.IsNullOrWhiteSpace(Subtitle);

    public string ModifierGlyph
    {
        get => _modifierGlyph;
        private set => SetProperty(ref _modifierGlyph, value);
    }

    public RadialMenuDisplayMode DisplayMode
    {
        get => _displayMode;
        private set => SetProperty(ref _displayMode, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetProperty(ref _isVisible, value);
    }

    public RadialMenuSlotViewModel Top { get; }

    public RadialMenuSlotViewModel Right { get; }

    public RadialMenuSlotViewModel Bottom { get; }

    public RadialMenuSlotViewModel Left { get; }

    public RadialMenuSlotViewModel CenterLeft { get; }

    public RadialMenuSlotViewModel CenterRight { get; }

    public void Update(RadialMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        Layer = state.Layer;
        Title = state.Title;
        Subtitle = state.Subtitle;
        ModifierGlyph = state.ModifierGlyph;
        DisplayMode = state.DisplayMode;
        IsVisible = state.IsVisible;

        Top.Update(state.GetItem(RadialMenuSlotPosition.Top));
        Right.Update(state.GetItem(RadialMenuSlotPosition.Right));
        Bottom.Update(state.GetItem(RadialMenuSlotPosition.Bottom));
        Left.Update(state.GetItem(RadialMenuSlotPosition.Left));
        CenterLeft.Update(
            state.GetItem(RadialMenuSlotPosition.CenterLeft));
        CenterRight.Update(
            state.GetItem(RadialMenuSlotPosition.CenterRight));
    }

    public void Hide()
    {
        IsVisible = false;
    }
}
