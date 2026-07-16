using System.Collections.ObjectModel;

namespace CodexController.Controllers;

/// <summary>
/// Presentation and raw-input metadata for a controller family.
/// </summary>
public sealed record ControllerProfile
{
    public ControllerProfile(
        string id,
        string displayName,
        ControllerVisual visual,
        IReadOnlyDictionary<LogicalInput, string> glyphs,
        RawMapping? rawMapping = null,
        StickTuning? tuning = null,
        Uri? vendorTool = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(glyphs);

        var normalizedId = id.Trim();
        if (
            normalizedId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_')))
        {
            throw new ArgumentException(
                "Profile IDs may contain only ASCII letters, digits, '-' and '_'.",
                nameof(id));
        }

        if (
            glyphs.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Value)))
        {
            throw new ArgumentException(
                "Profile glyphs cannot be empty.",
                nameof(glyphs));
        }

        if (vendorTool is { IsAbsoluteUri: false })
        {
            throw new ArgumentException(
                "The vendor tool URI must be absolute.",
                nameof(vendorTool));
        }

        Id = normalizedId.ToLowerInvariant();
        DisplayName = displayName.Trim();
        Visual = visual;
        Glyphs =
            new ReadOnlyDictionary<LogicalInput, string>(
                glyphs.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Trim()));
        RawMapping = rawMapping;
        Tuning = tuning;
        VendorTool = vendorTool;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public ControllerVisual Visual { get; }

    public IReadOnlyDictionary<LogicalInput, string> Glyphs { get; }

    public RawMapping? RawMapping { get; }

    public StickTuning? Tuning { get; }

    public Uri? VendorTool { get; }

    public string GetGlyph(LogicalInput input) =>
        Glyphs.TryGetValue(input, out var glyph)
            ? glyph
            : input.ToString();
}
