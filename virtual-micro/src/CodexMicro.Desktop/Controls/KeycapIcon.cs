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

    // The default Codex Micro command glyphs are authored in a 24 x 24
    // viewBox. Keep their exact filled paths instead of approximating them
    // with 20 x 20 strokes; the outer KeycapIcon size already follows the
    // 18.1277 / 62.44 ratio exported by Paper.
    private static readonly IReadOnlyList<Geometry> PaperFastGeometry =
        CreateGeometry(
            "M15.24 3.486v.002l-1.056 4.517h5.823c1.726 0 2.596 2.021 1.518 3.3l-.002.003-9.308 10.965-.002.002c-1.397 1.656-3.922.223-3.454-1.76v-.003l1.057-4.517H3.993c-1.726 0-2.596-2.021-1.519-3.3l.003-.003 9.308-10.965.002-.002c1.397-1.656 3.922-.223 3.454 1.76Zm-1.95-.444-1.34 5.735a.998.998 0 0 0 .973 1.226h7.074a.04.04 0 0 1 .003.01.045.045 0 0 1-.004.005l-9.287 10.94 1.341-5.735a.998.998 0 0 0-.973-1.226H4.003a.041.041 0 0 1-.003-.01l.004-.005 9.287-10.94Z");

    private static readonly IReadOnlyList<Geometry> PaperApproveGeometry =
        CreateGeometry(
            "M12 4C7.582 4 4 7.582 4 12C4 16.418 7.582 20 12 20C16.418 20 20 16.418 20 12C20 7.582 16.418 4 12 4ZM2 12C2 6.477 6.477 2 12 2C17.523 2 22 6.477 22 12C22 17.523 17.523 22 12 22C6.477 22 2 17.523 2 12ZM16.076 7.932C16.527 8.25 16.636 8.874 16.318 9.325L11.568 16.076C11.393 16.324 11.115 16.479 10.812 16.498C10.509 16.517 10.214 16.397 10.01 16.173L7.51 13.423C7.139 13.014 7.169 12.382 7.577 12.01C7.986 11.639 8.618 11.669 8.99 12.077L10.65 13.904L14.682 8.175C15 7.723 15.624 7.614 16.076 7.932Z");

    private static readonly IReadOnlyList<Geometry> PaperRejectGeometry =
        CreateGeometry(
            "M10.207 8.793a1 1 0 0 0-1.414 1.414L10.586 12l-1.793 1.793a1 1 0 1 0 1.414 1.414L12 13.414l1.793 1.793a1 1 0 0 0 1.414-1.414L13.414 12l1.793-1.793a1 1 0 0 0-1.414-1.414L12 10.586l-1.793-1.793Z",
            "M12 2C6.477 2 2 6.477 2 12s4.477 10 10 10 10-4.477 10-10S17.523 2 12 2ZM4 12a8 8 0 1 1 16 0 8 8 0 0 1-16 0Z");

    private static readonly IReadOnlyList<Geometry> PaperForkGeometry =
        CreateGeometry(
            "M7.75 18C7.75 17.31 7.19 16.75 6.5 16.75C5.81 16.75 5.25 17.31 5.25 18C5.25 18.69 5.81 19.25 6.5 19.25C7.19 19.25 7.75 18.69 7.75 18ZM7.75 6C7.75 5.31 7.19 4.75 6.5 4.75C5.81 4.75 5.25 5.31 5.25 6C5.25 6.69 5.81 7.25 6.5 7.25C7.19 7.25 7.75 6.69 7.75 6ZM18.75 6C18.75 5.31 18.19 4.75 17.5 4.75C16.81 4.75 16.25 5.31 16.25 6C16.25 6.69 16.81 7.25 17.5 7.25C18.19 7.25 18.75 6.69 18.75 6ZM20.75 6C20.75 7.446 19.805 8.67 18.5 9.092V10C18.5 11.657 17.157 13 15.5 13H8.5C7.948 13 7.5 13.448 7.5 14V14.907C8.806 15.329 9.75 16.554 9.75 18C9.75 19.795 8.295 21.25 6.5 21.25C4.705 21.25 3.25 19.795 3.25 18C3.25 16.554 4.194 15.329 5.5 14.907V9.092C4.195 8.67 3.25 7.446 3.25 6C3.25 4.205 4.705 2.75 6.5 2.75C8.295 2.75 9.75 4.205 9.75 6C9.75 7.446 8.805 8.67 7.5 9.092V11.174C7.813 11.063 8.149 11 8.5 11H15.5C16.052 11 16.5 10.552 16.5 10V9.092C15.195 8.67 14.25 7.446 14.25 6C14.25 4.205 15.705 2.75 17.5 2.75C19.295 2.75 20.75 4.205 20.75 6Z");

    private static readonly IReadOnlyList<Geometry> PaperMicrophoneGeometry =
        CreateGeometry(
            "M18.995 11.541C19.525 11.699 19.826 12.256 19.669 12.785C18.777 15.78 16.179 18.042 13 18.438V19.5H14.5C15.052 19.5 15.5 19.948 15.5 20.5C15.5 21.052 15.052 21.5 14.5 21.5H9.5C8.948 21.5 8.5 21.052 8.5 20.5C8.5 19.948 8.948 19.5 9.5 19.5H11V18.438C7.821 18.042 5.223 15.78 4.331 12.785C4.174 12.256 4.475 11.699 5.005 11.541C5.534 11.384 6.091 11.685 6.248 12.215C6.986 14.694 9.283 16.5 12 16.5C14.716 16.5 17.014 14.694 17.752 12.215C17.909 11.685 18.466 11.384 18.995 11.541Z",
            "M14.5 10.5V7C14.5 5.619 13.381 4.5 12 4.5C10.619 4.5 9.5 5.619 9.5 7V10.5C9.5 11.881 10.619 13 12 13C13.381 13 14.5 11.881 14.5 10.5ZM12 2.5C9.515 2.5 7.5 4.515 7.5 7V10.5C7.5 12.985 9.515 15 12 15C14.485 15 16.5 12.985 16.5 10.5V7C16.5 4.515 14.485 2.5 12 2.5Z");

    private static readonly IReadOnlyList<Geometry> CodexGeometry = CreateGeometry(
        "M13.333 11.418C13.7002 11.418 13.9978 11.7159 13.998 12.083C13.998 12.4503 13.7003 12.748 13.333 12.748H10.833C10.4657 12.748 10.168 12.4503 10.168 12.083C10.1682 11.7159 10.4659 11.418 10.833 11.418H13.333Z",
        "M6.74121 7.34668C7.0561 7.15796 7.46442 7.26036 7.65332 7.5752L8.90332 9.6582C9.02949 9.86874 9.02961 10.1323 8.90332 10.3428L7.65332 12.4258C7.46441 12.7403 7.05597 12.8427 6.74121 12.6543C6.42637 12.4654 6.32396 12.0561 6.5127 11.7412L7.55664 10L6.5127 8.25879C6.324 7.94395 6.4265 7.53562 6.74121 7.34668Z",
        "M9.00195 1.75C10.1157 1.75021 11.1362 2.15467 11.9238 2.82227C12.1849 2.77516 12.455 2.74903 12.7295 2.74902C15.2262 2.74978 17.2507 4.77449 17.251 7.27148C17.2509 7.54581 17.2238 7.81473 17.1768 8.0752C17.8448 8.86317 18.2499 9.88479 18.25 10.999C18.2496 12.9609 16.9996 14.6284 15.2549 15.2549C14.6285 16.9998 12.9608 18.2497 10.999 18.25C9.88486 18.25 8.86411 17.8448 8.07617 17.1768C7.8155 17.2239 7.54592 17.2509 7.27148 17.251C4.77445 17.2507 2.7504 15.2257 2.75 12.7285C2.75003 12.4539 2.77608 12.1848 2.82324 11.9238C2.20237 11.1913 1.80895 10.2574 1.75684 9.23438L1.75 9.00098C1.75022 7.03932 2.99952 5.36992 4.74414 4.74316C5.37104 2.99851 7.04034 1.75002 9.00195 1.75ZM9.00195 3.07812C7.52474 3.07814 6.27967 4.08156 5.91504 5.44531C5.85362 5.67419 5.67418 5.85363 5.44531 5.91504C4.08208 6.27984 3.07836 7.52408 3.07812 9.00098C3.07826 9.88321 3.43594 10.682 4.01465 11.2607C4.1816 11.4283 4.24663 11.6728 4.18555 11.9014C4.11505 12.1653 4.07719 12.4429 4.07715 12.7285C4.07755 14.4925 5.50753 15.9225 7.27148 15.9229C7.55712 15.9228 7.83548 15.886 8.09961 15.8154L8.18652 15.7979C8.38833 15.7722 8.59297 15.8403 8.73926 15.9863C9.31801 16.5649 10.1168 16.9218 10.999 16.9219C12.4759 16.9216 13.7203 15.9183 14.085 14.5547L14.1133 14.4707C14.1918 14.2821 14.3542 14.1386 14.5547 14.085C15.9181 13.7203 16.9225 12.4758 16.9229 10.999C16.9228 10.1168 16.5648 9.31802 15.9863 8.73926C15.819 8.57175 15.7544 8.3274 15.8154 8.09863C15.886 7.83454 15.9238 7.5568 15.9238 7.27148C15.9235 5.50751 14.4924 4.07762 12.7285 4.07715C12.4424 4.0772 12.164 4.11412 11.9004 4.18457C11.672 4.24541 11.4282 4.18048 11.2607 4.01367C10.7183 3.47141 9.98306 3.12271 9.16699 3.08105L9.00195 3.07812Z");

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
                DrawPaperGeometry(drawingContext, brush, PaperFastGeometry);
                break;
            case "APPR":
                DrawPaperGeometry(drawingContext, brush, PaperApproveGeometry);
                break;
            case "REJ":
                DrawPaperGeometry(drawingContext, brush, PaperRejectGeometry);
                break;
            case "SPLIT":
            case "BRCH":
                DrawPaperGeometry(drawingContext, brush, PaperForkGeometry);
                break;
            case "MIC":
                DrawPaperGeometry(
                    drawingContext,
                    brush,
                    PaperMicrophoneGeometry);
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

    private static void DrawPaperGeometry(
        DrawingContext context,
        Brush brush,
        IEnumerable<Geometry> geometry)
    {
        context.PushTransform(new ScaleTransform(20.0 / 24.0, 20.0 / 24.0));
        DrawGeometry(context, brush, geometry);
        context.Pop();
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
