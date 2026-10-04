using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace CodexController.Views;

public sealed class ConnectedTabOutline : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ConnectedTabOutline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ConnectedTabOutline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (RenderSize.Width <= 1 || RenderSize.Height <= 1)
            return;

        const double strokeWidth = 1;
        var inset = strokeWidth / 2;
        var left = inset;
        var right = RenderSize.Width - inset;
        var top = inset;
        var bottom = RenderSize.Height - inset;
        var shoulder = Math.Min(4, Math.Min((right - left) / 4, (bottom - top) / 2));
        var bodyLeft = left + shoulder;
        var bodyRight = right - shoulder;
        var radius = Math.Min(8, Math.Min((bodyRight - bodyLeft) / 2, bottom - top - shoulder));

        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(left, bottom), isFilled: true, isClosed: false);
            path.QuadraticBezierTo(new Point(bodyLeft, bottom),
                new Point(bodyLeft, bottom - shoulder), isStroked: true, isSmoothJoin: true);
            path.LineTo(new Point(bodyLeft, top + radius), isStroked: true, isSmoothJoin: true);
            path.QuadraticBezierTo(new Point(bodyLeft, top),
                new Point(bodyLeft + radius, top), isStroked: true, isSmoothJoin: true);
            path.LineTo(new Point(bodyRight - radius, top), isStroked: true, isSmoothJoin: true);
            path.QuadraticBezierTo(new Point(bodyRight, top),
                new Point(bodyRight, top + radius), isStroked: true, isSmoothJoin: true);
            path.LineTo(new Point(bodyRight, bottom - shoulder), isStroked: true, isSmoothJoin: true);
            path.QuadraticBezierTo(new Point(bodyRight, bottom),
                new Point(right, bottom), isStroked: true, isSmoothJoin: true);

            // Cover the panel's top stroke without drawing a bottom edge on the tab.
            path.LineTo(new Point(right, RenderSize.Height), isStroked: false, isSmoothJoin: false);
            path.LineTo(new Point(left, RenderSize.Height), isStroked: false, isSmoothJoin: false);
            path.LineTo(new Point(left, bottom), isStroked: false, isSmoothJoin: false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(Fill, Stroke is null ? null : new Pen(Stroke, strokeWidth), geometry);
    }
}
