using System.Collections.ObjectModel;

namespace CodexController.Models;

public sealed class RadialMenuState
{
    private readonly IReadOnlyDictionary<
        RadialMenuSlotPosition,
        RadialMenuItemState> _itemsByPosition;

    public RadialMenuState(
        RadialMenuLayerKind layer,
        string title,
        string modifierGlyph,
        IEnumerable<RadialMenuItemState> items,
        RadialMenuDisplayMode displayMode =
            RadialMenuDisplayMode.Learning,
        bool isLayerEngaged = true,
        bool isLearningCueReady = false,
        string? subtitle = null,
        RadialMenuInteractionPhase interactionPhase =
            RadialMenuInteractionPhase.AwaitingInput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(modifierGlyph);
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToArray();
        if (itemList.Length > 6)
        {
            throw new ArgumentException(
                "A radial menu supports at most six fixed physical slots.",
                nameof(items));
        }

        var duplicatePosition = itemList
            .GroupBy(item => item.Position)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePosition is not null)
        {
            throw new ArgumentException(
                $"Slot position '{duplicatePosition.Key}' is duplicated.",
                nameof(items));
        }

        Layer = layer;
        Title = title.Trim();
        Subtitle = subtitle?.Trim() ?? string.Empty;
        ModifierGlyph = modifierGlyph.Trim();
        DisplayMode = displayMode;
        IsLayerEngaged = isLayerEngaged;
        IsLearningCueReady = isLearningCueReady;
        InteractionPhase = interactionPhase;
        Items = new ReadOnlyCollection<RadialMenuItemState>(itemList);
        _itemsByPosition = new ReadOnlyDictionary<
            RadialMenuSlotPosition,
            RadialMenuItemState>(
            itemList.ToDictionary(item => item.Position));
    }

    public RadialMenuLayerKind Layer { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string ModifierGlyph { get; }

    public RadialMenuDisplayMode DisplayMode { get; }

    public bool IsLayerEngaged { get; }

    public bool IsLearningCueReady { get; }

    public RadialMenuInteractionPhase InteractionPhase { get; }

    public IReadOnlyList<RadialMenuItemState> Items { get; }

    public bool IsVisible =>
        IsLayerEngaged &&
        InteractionPhase !=
            RadialMenuInteractionPhase.WaitingForResponse &&
        DisplayMode switch
        {
            RadialMenuDisplayMode.Always => true,
            RadialMenuDisplayMode.Learning => IsLearningCueReady,
            RadialMenuDisplayMode.Off => false,
            _ => false,
        };

    public RadialMenuItemState? GetItem(
        RadialMenuSlotPosition position)
    {
        return _itemsByPosition.GetValueOrDefault(position);
    }
}
