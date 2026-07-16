namespace CodexController.Models;

public sealed record RadialMenuItemState
{
    public RadialMenuItemState(
        string id,
        RadialMenuSlotPosition position,
        string inputGlyph,
        string title,
        string? subtitle = null,
        bool isEnabled = true,
        bool isHighlighted = false,
        double confirmationProgress = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputGlyph);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Id = id;
        Position = position;
        InputGlyph = inputGlyph.Trim();
        Title = title.Trim();
        Subtitle = subtitle?.Trim() ?? string.Empty;
        IsEnabled = isEnabled;
        IsHighlighted = isHighlighted;
        ConfirmationProgress = Math.Clamp(
            confirmationProgress,
            0,
            1);
    }

    public string Id { get; }

    public RadialMenuSlotPosition Position { get; }

    public string InputGlyph { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public bool IsEnabled { get; }

    public bool IsHighlighted { get; }

    public double ConfirmationProgress { get; }

    public bool HasConfirmationProgress => ConfirmationProgress > 0;
}
