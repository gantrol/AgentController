using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexMicro.Desktop.Controls;

/// <summary>
/// Code-native keycap artwork mirroring the Codex Micro settings catalog.
/// CODEX uses the current Codex settings glyph geometry; OAI is loaded from
/// the installed Codex package asset so no copied bitmap is shipped here.
/// </summary>
public sealed class KeycapIcon : FrameworkElement
{
    public static readonly DependencyProperty KeycapIdProperty =
        DependencyProperty.Register(
            nameof(KeycapId),
            typeof(string),
            typeof(KeycapIcon),
            new FrameworkPropertyMetadata(
                "CODEX",
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IconBrushProperty =
        DependencyProperty.Register(
            nameof(IconBrush),
            typeof(Brush),
            typeof(KeycapIcon),
            new FrameworkPropertyMetadata(
                Brushes.Black,
                FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly ConcurrentDictionary<string, BitmapSource?>
        LogoCache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<Geometry> CodexGeometry = CreateGeometry(
        "M13.333 11.418C13.7002 11.418 13.9978 11.7159 13.998 12.083C13.998 12.4503 13.7003 12.748 13.333 12.748H10.833C10.4657 12.748 10.168 12.4503 10.168 12.083C10.1682 11.7159 10.4659 11.418 10.833 11.418H13.333Z",
        "M6.74121 7.34668C7.0561 7.15796 7.46442 7.26036 7.65332 7.5752L8.90332 9.6582C9.02949 9.86874 9.02961 10.1323 8.90332 10.3428L7.65332 12.4258C7.46441 12.7403 7.05597 12.8427 6.74121 12.6543C6.42637 12.4654 6.32396 12.0561 6.5127 11.7412L7.55664 10L6.5127 8.25879C6.324 7.94395 6.4265 7.53562 6.74121 7.34668Z",
        "M9.00195 1.75C10.1157 1.75021 11.1362 2.15467 11.9238 2.82227C12.1849 2.77516 12.455 2.74903 12.7295 2.74902C15.2262 2.74978 17.2507 4.77449 17.251 7.27148C17.2509 7.54581 17.2238 7.81473 17.1768 8.0752C17.8448 8.86317 18.2499 9.88479 18.25 10.999C18.2496 12.9609 16.9996 14.6284 15.2549 15.2549C14.6285 16.9998 12.9608 18.2497 10.999 18.25C9.88486 18.25 8.86411 17.8448 8.07617 17.1768C7.8155 17.2239 7.54592 17.2509 7.27148 17.251C4.77445 17.2507 2.7504 15.2257 2.75 12.7285C2.75003 12.4539 2.77608 12.1848 2.82324 11.9238C2.20237 11.1913 1.80895 10.2574 1.75684 9.23438L1.75 9.00098C1.75022 7.03932 2.99952 5.36992 4.74414 4.74316C5.37104 2.99851 7.04034 1.75002 9.00195 1.75ZM9.00195 3.07812C7.52474 3.07814 6.27967 4.08156 5.91504 5.44531C5.85362 5.67419 5.67418 5.85363 5.44531 5.91504C4.08208 6.27984 3.07836 7.52408 3.07812 9.00098C3.07826 9.88321 3.43594 10.682 4.01465 11.2607C4.1816 11.4283 4.24663 11.6728 4.18555 11.9014C4.11505 12.1653 4.07719 12.4429 4.07715 12.7285C4.07755 14.4925 5.50753 15.9225 7.27148 15.9229C7.55712 15.9228 7.83548 15.886 8.09961 15.8154L8.18652 15.7979C8.38833 15.7722 8.59297 15.8403 8.73926 15.9863C9.31801 16.5649 10.1168 16.9218 10.999 16.9219C12.4759 16.9216 13.7203 15.9183 14.085 14.5547L14.1133 14.4707C14.1918 14.2821 14.3542 14.1386 14.5547 14.085C15.9181 13.7203 16.9225 12.4758 16.9229 10.999C16.9228 10.1168 16.5648 9.31802 15.9863 8.73926C15.819 8.57175 15.7544 8.3274 15.8154 8.09863C15.886 7.83454 15.9238 7.5568 15.9238 7.27148C15.9235 5.50751 14.4924 4.07762 12.7285 4.07715C12.4424 4.0772 12.164 4.11412 11.9004 4.18457C11.672 4.24541 11.4282 4.18048 11.2607 4.01367C10.7183 3.47141 9.98306 3.12271 9.16699 3.08105L9.00195 3.07812Z");

    private static readonly IReadOnlyList<Geometry> ApproveGeometry = CreateGeometry(
        "M12.1599 7.13617C12.3713 6.83596 12.7863 6.76372 13.0866 6.97504C13.3867 7.18642 13.4589 7.60153 13.2477 7.90179L9.28876 13.5268C9.17264 13.6917 8.98808 13.7954 8.7868 13.808C8.61044 13.819 8.43764 13.7592 8.30634 13.644L8.25262 13.5912L6.16962 11.2993L6.08954 11.1918C5.93136 10.9259 5.97666 10.5761 6.21454 10.3598C6.45225 10.1439 6.80379 10.1326 7.05341 10.3149L7.15399 10.4047L8.67841 12.0815L12.1599 7.13617Z",
        "M9.99506 2.31226C14.3664 2.31226 17.9101 5.85596 17.9101 10.2273C17.9101 14.5986 14.3664 18.1423 9.99506 18.1423C5.62372 18.1423 2.08002 14.5986 2.08002 10.2273C2.08002 5.85596 5.62372 2.31226 9.99506 2.31226ZM9.99506 3.64233C6.35826 3.64233 3.4101 6.5905 3.4101 10.2273C3.4101 13.8641 6.35826 16.8123 9.99506 16.8123C13.6319 16.8123 16.58 13.8641 16.58 10.2273C16.58 6.5905 13.6319 3.64233 9.99506 3.64233Z");

    private static readonly IReadOnlyList<Geometry> RejectGeometry = CreateGeometry(
        "M7.231 7.231A.665.665 0 0 1 8.171 7.231L10 9.06L11.828 7.231L11.932 7.146A.666.666 0 0 1 12.853 8.068L12.769 8.172L10.94 10L12.769 11.828A.665.665 0 0 1 11.829 12.768L10 10.94L8.172 12.77A.665.665 0 0 1 7.232 11.83L9.06 10L7.23 8.172A.665.665 0 0 1 7.231 7.231Z",
        "M10 2.085A7.915 7.915 0 1 1 10 17.915A7.915 7.915 0 0 1 10 2.085ZM10 3.415A6.585 6.585 0 1 0 10 16.585A6.585 6.585 0 0 0 10 3.415Z");

    private string? _packageRoot;

    public string KeycapId
    {
        get => (string)GetValue(KeycapIdProperty);
        set => SetValue(KeycapIdProperty, value);
    }

    public Brush IconBrush
    {
        get => (Brush)GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public string? PackageRoot
    {
        get => _packageRoot;
        set
        {
            if (_packageRoot == value)
            {
                return;
            }

            _packageRoot = value;
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 32 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 32 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Min(ActualWidth, ActualHeight) / 20.0;
        var x = (ActualWidth - (20 * scale)) / 2;
        var y = (ActualHeight - (20 * scale)) / 2;
        drawingContext.PushTransform(new TranslateTransform(x, y));
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        var brush = IconBrush;
        var pen = CreatePen(brush, 1.35);
        switch (KeycapId)
        {
            case "FAST":
                DrawLightning(drawingContext, pen);
                break;
            case "APPR":
                DrawGeometry(drawingContext, brush, ApproveGeometry);
                break;
            case "REJ":
                DrawGeometry(drawingContext, brush, RejectGeometry);
                break;
            case "SPLIT":
            case "BRCH":
                DrawBranch(drawingContext, pen, merge: false);
                break;
            case "MIC":
                DrawMicrophone(drawingContext, pen);
                break;
            case "CODEX":
                DrawGeometry(drawingContext, brush, CodexGeometry);
                break;
            case "OAI":
                if (!DrawOpenAiLogo(drawingContext))
                {
                    DrawText(drawingContext, "OAI", brush, 4.2);
                }
                break;
            case "BUG":
                DrawBug(drawingContext, pen);
                break;
            case "TERM":
                DrawTerminal(drawingContext, pen);
                break;
            case "DWN":
                DrawDownload(drawingContext, pen);
                break;
            case "DEL":
                DrawTrash(drawingContext, pen);
                break;
            case "NEW":
                DrawCompose(drawingContext, pen);
                break;
            case "NAV":
                DrawPointer(drawingContext, brush, pen);
                break;
            case "MAGIC":
                DrawStar(drawingContext, pen);
                break;
            case "DIFF":
            case "GIT":
                DrawDiff(drawingContext, pen);
                break;
            case "PLAY":
                DrawPlay(drawingContext, brush, pen);
                break;
            case "MRG":
                DrawBranch(drawingContext, pen, merge: true);
                break;
            case "PR":
                DrawPullRequest(drawingContext, pen);
                break;
            case "PAINT":
                DrawPalette(drawingContext, pen, brush);
                break;
            case "LAB":
                DrawFlask(drawingContext, pen);
                break;
            case "PARTY":
                DrawConfetti(drawingContext, pen, brush);
                break;
            case "TIME":
                DrawClock(drawingContext, pen);
                break;
            case "MIND+":
                DrawMind(drawingContext, pen, plus: true);
                break;
            case "MIND-":
                DrawMind(drawingContext, pen, plus: false);
                break;
            case "SETUP":
                DrawSettings(drawingContext, pen);
                break;
            case "FOLD":
                DrawFolderPlus(drawingContext, pen);
                break;
            case "UPL":
                DrawCloudUpload(drawingContext, pen);
                break;
            case "APPS":
                DrawApps(drawingContext, pen);
                break;
            case "YOLO":
                DrawText(drawingContext, ":yolo:", brush, 3.1);
                break;
            case "YEET":
                DrawText(drawingContext, ":yeet:", brush, 3.1);
                break;
            case "EMPT1":
            case "EMPT2":
            case "EMPT3":
            case "EMPT4":
            case "EMPT5":
                drawingContext.DrawRoundedRectangle(
                    null,
                    pen,
                    new Rect(6, 6, 8, 8),
                    1.5,
                    1.5);
                break;
            default:
                DrawText(drawingContext, "?", brush, 8);
                break;
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        return pen;
    }

    private static IReadOnlyList<Geometry> CreateGeometry(params string[] data) =>
        data.Select(value =>
        {
            var geometry = Geometry.Parse(value);
            geometry.Freeze();
            return geometry;
        }).ToArray();

    private static void DrawGeometry(
        DrawingContext context,
        Brush brush,
        IEnumerable<Geometry> geometry)
    {
        foreach (var shape in geometry)
        {
            context.DrawGeometry(brush, null, shape);
        }
    }

    private bool DrawOpenAiLogo(DrawingContext context)
    {
        if (string.IsNullOrWhiteSpace(PackageRoot))
        {
            return false;
        }

        var path = Path.Combine(
            PackageRoot,
            "assets",
            "Square44x44Logo.targetsize-256_altform-lightunplated.png");
        if (!LogoCache.TryGetValue(path, out var image))
        {
            image = LoadBitmap(path);
            if (image is not null)
            {
                _ = LogoCache.TryAdd(path, image);
            }
        }

        if (image is null)
        {
            return false;
        }

        context.DrawImage(image, new Rect(1.7, 1.7, 16.6, 16.6));
        return true;
    }

    private static BitmapSource? LoadBitmap(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DrawLightning(DrawingContext context, Pen pen)
    {
        var geometry = Geometry.Parse("M11.6 2.4 L4.6 11.1 L9.2 11.1 L8.4 17.6 L15.5 8.9 L10.8 8.9 Z");
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawBranch(DrawingContext context, Pen pen, bool merge)
    {
        context.DrawEllipse(null, pen, new Point(5.3, 4.2), 1.8, 1.8);
        context.DrawEllipse(null, pen, new Point(5.3, 15.8), 1.8, 1.8);
        context.DrawEllipse(null, pen, new Point(14.7, merge ? 15.8 : 4.2), 1.8, 1.8);
        context.DrawLine(pen, new Point(5.3, 6), new Point(5.3, 14));
        var path = merge
            ? "M14.7 14 C14.7 9.3 5.3 10.7 5.3 6"
            : "M5.3 10 C5.3 7 14.7 9 14.7 6";
        context.DrawGeometry(null, pen, Geometry.Parse(path));
    }

    private static void DrawMicrophone(DrawingContext context, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(7.2, 2.1, 5.6, 9.6), 2.8, 2.8);
        context.DrawGeometry(null, pen, Geometry.Parse("M4.8 9.8 C4.8 13.1 7.1 15.1 10 15.1 C12.9 15.1 15.2 13.1 15.2 9.8 M10 15.1 V18 M7.7 18 H12.3"));
    }

    private static void DrawBug(DrawingContext context, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(6.2, 5.2, 7.6, 10.2), 3.5, 3.5);
        context.DrawLine(pen, new Point(8, 5.2), new Point(7, 3.5));
        context.DrawLine(pen, new Point(12, 5.2), new Point(13, 3.5));
        context.DrawLine(pen, new Point(6.2, 8), new Point(3.8, 6.8));
        context.DrawLine(pen, new Point(6.2, 11), new Point(3.5, 11));
        context.DrawLine(pen, new Point(6.5, 14), new Point(4.2, 15.4));
        context.DrawLine(pen, new Point(13.8, 8), new Point(16.2, 6.8));
        context.DrawLine(pen, new Point(13.8, 11), new Point(16.5, 11));
        context.DrawLine(pen, new Point(13.5, 14), new Point(15.8, 15.4));
    }

    private static void DrawTerminal(DrawingContext context, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(2.6, 3.2, 14.8, 13.6), 2.2, 2.2);
        context.DrawGeometry(null, pen, Geometry.Parse("M6 7 L9 10 L6 13 M10.8 13 H14"));
    }

    private static void DrawDownload(DrawingContext context, Pen pen)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M10 2.8 V12.2 M6.8 9.2 L10 12.4 L13.2 9.2 M3 12.8 V15 C3 16.1 3.9 17 5 17 H15 C16.1 17 17 16.1 17 15 V12.8"));
    }

    private static void DrawTrash(DrawingContext context, Pen pen)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M4 5 H16 M7 5 V3.2 H13 V5 M5.2 5 L6 17 H14 L14.8 5 M8.2 8 V14 M11.8 8 V14"));
    }

    private static void DrawCompose(DrawingContext context, Pen pen)
    {
        context.DrawRoundedRectangle(null, pen, new Rect(3, 3, 12.5, 14), 2, 2);
        context.DrawGeometry(null, pen, Geometry.Parse("M7 13 L7.7 10.4 L14.1 4 C14.8 3.3 15.8 3.3 16.5 4 C17.2 4.7 17.2 5.7 16.5 6.4 L10.1 12.8 Z"));
    }

    private static void DrawPointer(DrawingContext context, Brush brush, Pen pen)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M4 2.8 L15.7 10.2 L10.4 11.4 L8.2 17.2 Z"));
        context.DrawLine(pen, new Point(10.4, 11.4), new Point(14.8, 15.8));
        context.DrawEllipse(brush, null, new Point(4, 2.8), .5, .5);
    }

    private static void DrawStar(DrawingContext context, Pen pen)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M10 2.5 L12.2 7.1 L17.3 7.8 L13.6 11.4 L14.5 16.6 L10 14.2 L5.5 16.6 L6.4 11.4 L2.7 7.8 L7.8 7.1 Z"));
    }

    private static void DrawDiff(DrawingContext context, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(5, 4), 1.6, 1.6);
        context.DrawEllipse(null, pen, new Point(5, 16), 1.6, 1.6);
        context.DrawLine(pen, new Point(5, 5.6), new Point(5, 14.4));
        context.DrawLine(pen, new Point(11, 6), new Point(17, 6));
        context.DrawLine(pen, new Point(14, 3), new Point(14, 9));
        context.DrawLine(pen, new Point(11, 14), new Point(17, 14));
    }

    private static void DrawPlay(DrawingContext context, Brush brush, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(10, 10), 7.5, 7.5);
        context.DrawGeometry(brush, null, Geometry.Parse("M8 6.5 L14 10 L8 13.5 Z"));
    }

    private static void DrawPullRequest(DrawingContext context, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(5, 4), 1.7, 1.7);
        context.DrawEllipse(null, pen, new Point(5, 16), 1.7, 1.7);
        context.DrawEllipse(null, pen, new Point(15, 16), 1.7, 1.7);
        context.DrawLine(pen, new Point(5, 5.7), new Point(5, 14.3));
        context.DrawGeometry(null, pen, Geometry.Parse("M11 4 H13 C14.1 4 15 4.9 15 6 V14 M11 4 L13 2 M11 4 L13 6"));
    }

    private static void DrawPalette(DrawingContext context, Pen pen, Brush brush)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M10 2.5 C5.6 2.5 2.5 5.6 2.5 9.6 C2.5 13.8 5.8 17.2 10.1 17.2 H11.4 C12.3 17.2 12.8 16.2 12.3 15.5 C11.7 14.6 12.3 13.3 13.4 13.3 H15 C16.6 13.3 17.5 12.2 17.5 10.4 C17.5 5.9 14.2 2.5 10 2.5 Z"));
        foreach (var point in new[] { new Point(6.2, 7), new Point(9.8, 5.6), new Point(13.5, 7.3) })
        {
            context.DrawEllipse(brush, null, point, .8, .8);
        }
    }

    private static void DrawFlask(DrawingContext context, Pen pen)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M7 2.8 H13 M8 2.8 V8 L3.8 14.2 C3 15.4 3.9 17.2 5.4 17.2 H14.6 C16.1 17.2 17 15.4 16.2 14.2 L12 8 V2.8 M5.8 12.2 C8.2 11.5 9.4 13.2 14.4 11.9"));
    }

    private static void DrawConfetti(DrawingContext context, Pen pen, Brush brush)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M4 16 L7.2 7.2 L12.8 12.8 Z M8 8 C11 5 13.5 7.5 16.2 4.4"));
        context.DrawLine(pen, new Point(11.3, 3), new Point(11.8, 5));
        context.DrawLine(pen, new Point(15.5, 8.5), new Point(17.3, 9.3));
        context.DrawEllipse(brush, null, new Point(16.6, 3.1), .8, .8);
    }

    private static void DrawClock(DrawingContext context, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(10, 10), 7.4, 7.4);
        context.DrawGeometry(null, pen, Geometry.Parse("M10 5.5 V10 L13.2 12"));
    }

    private static void DrawMind(DrawingContext context, Pen pen, bool plus)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M8 4 C5.8 2.8 3.7 4.6 4.2 6.8 C2.3 7.6 2.5 10.5 4.4 11.2 C3.6 13.6 5.6 15.4 7.7 14.8 C8.5 17.1 11.8 17.2 12.6 14.8 C15 15.3 16.7 13.1 15.6 11.2 C17.5 10 17.1 7.4 15.2 6.8 C15.6 4.4 12.7 3.1 11.2 4.7 C10.4 3.1 8.8 3 8 4 Z"));
        context.DrawLine(pen, new Point(8.5, 10), new Point(13.5, 10));
        if (plus)
        {
            context.DrawLine(pen, new Point(11, 7.5), new Point(11, 12.5));
        }
    }

    private static void DrawSettings(DrawingContext context, Pen pen)
    {
        context.DrawEllipse(null, pen, new Point(10, 10), 3, 3);
        context.DrawEllipse(null, pen, new Point(10, 10), 7.2, 7.2);
        for (var index = 0; index < 8; index++)
        {
            var angle = index * Math.PI / 4;
            var inner = new Point(10 + (7.2 * Math.Cos(angle)), 10 + (7.2 * Math.Sin(angle)));
            var outer = new Point(10 + (8.5 * Math.Cos(angle)), 10 + (8.5 * Math.Sin(angle)));
            context.DrawLine(pen, inner, outer);
        }
    }

    private static void DrawFolderPlus(DrawingContext context, Pen pen)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M2.5 5.5 H8 L9.7 7.3 H17.5 V16.5 H2.5 Z M10 11.8 H15 M12.5 9.3 V14.3"));
    }

    private static void DrawCloudUpload(DrawingContext context, Pen pen)
    {
        context.DrawGeometry(null, pen, Geometry.Parse("M5.2 15.6 H4.8 C2.8 15.6 1.9 13.5 2.8 11.9 C2.1 9.5 4.4 7.5 6.5 8.2 C7.4 4.4 12.8 4.1 14.1 7.8 C17.8 7.6 19 12.6 15.8 14.2 M10 16.8 V9.8 M7.2 12.6 L10 9.8 L12.8 12.6"));
    }

    private static void DrawApps(DrawingContext context, Pen pen)
    {
        foreach (var rect in new[]
        {
            new Rect(3, 3, 5.4, 5.4),
            new Rect(11.6, 3, 5.4, 5.4),
            new Rect(3, 11.6, 5.4, 5.4),
            new Rect(11.6, 11.6, 5.4, 5.4),
        })
        {
            context.DrawRoundedRectangle(null, pen, rect, 1.2, 1.2);
        }
    }

    private void DrawText(
        DrawingContext context,
        string text,
        Brush brush,
        double fontSize)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Cascadia Mono"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            fontSize,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(
            formatted,
            new Point((20 - formatted.Width) / 2, (20 - formatted.Height) / 2));
    }
}
