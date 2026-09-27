using System.Windows;
using System.Windows.Media;

namespace CodexMicro.Desktop.Controls;

internal sealed class SevenSegmentReadout : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(SevenSegmentReadout),
        new FrameworkPropertyMetadata("—",
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private const double GlyphHeight = 14;
    private const double GlyphGap = 1.5;
    private static readonly int[] DigitSegments =
        [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F];
    private static readonly Geometry[] Segments =
    [
        Segment("M 1.5,0 L 6,0 6.7,0.7 6,1.4 1.5,1.4 0.8,0.7 Z"),
        Segment("M 6.8,1.1 L 7.5,1.8 7.5,5.8 6.8,6.5 6.1,5.8 6.1,1.8 Z"),
        Segment("M 6.8,7.5 L 7.5,8.2 7.5,12.2 6.8,12.9 6.1,12.2 6.1,8.2 Z"),
        Segment("M 1.5,12.6 L 6,12.6 6.7,13.3 6,14 1.5,14 0.8,13.3 Z"),
        Segment("M 0.7,7.5 L 1.4,8.2 1.4,12.2 0.7,12.9 0,12.2 0,8.2 Z"),
        Segment("M 0.7,1.1 L 1.4,1.8 1.4,5.8 0.7,6.5 0,5.8 0,1.8 Z"),
        Segment("M 1.5,6.3 L 6,6.3 6.7,7 6,7.7 1.5,7.7 0.8,7 Z"),
    ];
    private static readonly Brush SegmentBrush = CreateBrush();
    private static readonly Pen PercentPen = CreatePercentPen();

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0d;
        foreach (var character in Text)
        {
            width += GlyphWidth(character) + GlyphGap;
        }
        return new Size(Math.Max(0, width - GlyphGap), GlyphHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var offset = 0d;
        foreach (var character in Text)
        {
            drawingContext.PushTransform(new TranslateTransform(offset, 0));
            if (character == '%')
            {
                drawingContext.DrawRoundedRectangle(null, PercentPen, new Rect(0.5, 3, 2, 2), 0.3, 0.3);
                drawingContext.DrawLine(PercentPen, new Point(0.7, 11), new Point(5.8, 3));
                drawingContext.DrawRoundedRectangle(null, PercentPen, new Rect(4, 9, 2, 2), 0.3, 0.3);
            }
            else
            {
                var mask = character is >= '0' and <= '9' ? DigitSegments[character - '0'] : 0x40;
                for (var segment = 0; segment < Segments.Length; segment++)
                {
                    if ((mask & (1 << segment)) != 0)
                    {
                        drawingContext.DrawGeometry(SegmentBrush, null, Segments[segment]);
                    }
                }
            }
            drawingContext.Pop();
            offset += GlyphWidth(character) + GlyphGap;
        }
    }

    private static double GlyphWidth(char character) => character == '%' ? 6.5 : 7.5;

    private static Geometry Segment(string path)
    {
        var geometry = Geometry.Parse(path);
        geometry.Freeze();
        return geometry;
    }

    private static Brush CreateBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFF));
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePercentPen()
    {
        var pen = new Pen(SegmentBrush, 0.9);
        pen.Freeze();
        return pen;
    }
}
