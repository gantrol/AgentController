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
    private RadialMenuInteractionPhase _interactionPhase =
        RadialMenuInteractionPhase.AwaitingInput;
    private bool _isVisible;
    private AgentKeypadPresentation _agentKeypad =
        AgentKeypadPresentation.Empty;

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
        private set
        {
            if (SetProperty(ref _layer, value))
            {
                OnPropertyChanged(nameof(IsAgentLayer));
            }
        }
    }

    public bool IsAgentLayer => Layer == RadialMenuLayerKind.Agent;

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

    public RadialMenuInteractionPhase InteractionPhase
    {
        get => _interactionPhase;
        private set
        {
            if (SetProperty(ref _interactionPhase, value))
            {
                OnPropertyChanged(nameof(IsWaitingForResponse));
            }
        }
    }

    public bool IsWaitingForResponse =>
        InteractionPhase ==
        RadialMenuInteractionPhase.WaitingForResponse;

    public AgentKeypadPresentation AgentKeypad
    {
        get => _agentKeypad;
        private set => SetProperty(ref _agentKeypad, value);
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
        InteractionPhase = state.InteractionPhase;
        AgentKeypad = state.AgentKeypad;
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

    public bool TryAcceptInput(
        string actionId,
        out string actionTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        actionTitle = string.Empty;
        if (
            InteractionPhase !=
            RadialMenuInteractionPhase.AwaitingInput)
        {
            return false;
        }

        var slots = AllSlots().ToArray();
        var selected = slots.FirstOrDefault(slot =>
            slot.IsPresent &&
            string.Equals(
                slot.Id,
                actionId,
                StringComparison.Ordinal));
        if (selected is null)
        {
            return false;
        }

        foreach (var slot in slots)
        {
            slot.SetHighlighted(ReferenceEquals(slot, selected));
        }

        actionTitle = selected.Title;
        InteractionPhase =
            RadialMenuInteractionPhase.InputAccepted;
        IsVisible = DisplayMode != RadialMenuDisplayMode.Off;
        return true;
    }

    public void EnterWaitingForResponse()
    {
        InteractionPhase =
            RadialMenuInteractionPhase.WaitingForResponse;
        IsVisible = false;
    }

    public void Hide()
    {
        IsVisible = false;
    }

    private IEnumerable<RadialMenuSlotViewModel> AllSlots()
    {
        yield return Top;
        yield return Right;
        yield return Bottom;
        yield return Left;
        yield return CenterLeft;
        yield return CenterRight;
    }
}
