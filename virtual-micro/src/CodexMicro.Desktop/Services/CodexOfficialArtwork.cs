using System.Windows.Media;

namespace CodexMicro.Desktop.Services;

internal static partial class CodexOfficialArtwork
{
    private sealed record Glyph(double Width, double Height, double Scale, double X, double Y, Geometry[] Paths);

    private static Geometry[] Create(params string[] paths) => paths.Select(path =>
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }).ToArray();

    internal static bool Draw(DrawingContext context, string keycap, Brush brush)
    {
        if (!KeycapIcons.TryGetValue(keycap, out var name) || !Icons.TryGetValue(name, out var glyph)) return false;
        var scale = 20 / Math.Max(glyph.Width, glyph.Height);
        context.PushTransform(new TranslateTransform((20 - glyph.Width * scale) / 2, (20 - glyph.Height * scale) / 2));
        context.PushTransform(new ScaleTransform(scale, scale));
        context.PushTransform(new TranslateTransform(glyph.X, glyph.Y));
        context.PushTransform(new ScaleTransform(glyph.Scale, glyph.Scale));
        foreach (var path in glyph.Paths) context.DrawGeometry(brush, null, path);
        context.Pop();
        context.Pop();
        context.Pop();
        context.Pop();
        return true;
    }
}
