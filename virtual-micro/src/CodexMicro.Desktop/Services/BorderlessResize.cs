using System.Windows;

namespace CodexMicro.Desktop.Services;

internal static class BorderlessResize
{
    internal const int WmNcHitTest = 0x0084;
    internal const int HitLeft = 10;
    internal const int HitRight = 11;
    internal const int HitTop = 12;
    internal const int HitTopLeft = 13;
    internal const int HitTopRight = 14;
    internal const int HitBottom = 15;
    internal const int HitBottomLeft = 16;
    internal const int HitBottomRight = 17;

    internal static int HitTest(
        Size clientSize,
        Point clientPoint,
        double requestedBorderThickness)
    {
        if (
            clientSize.Width <= 0 ||
            clientSize.Height <= 0 ||
            requestedBorderThickness <= 0)
        {
            return 0;
        }

        var border = Math.Min(
            requestedBorderThickness,
            Math.Min(clientSize.Width, clientSize.Height) / 2);
        var left = clientPoint.X >= 0 && clientPoint.X < border;
        var right = clientPoint.X <= clientSize.Width &&
            clientPoint.X >= clientSize.Width - border;
        var top = clientPoint.Y >= 0 && clientPoint.Y < border;
        var bottom = clientPoint.Y <= clientSize.Height &&
            clientPoint.Y >= clientSize.Height - border;

        if (top && left)
        {
            return HitTopLeft;
        }

        if (top && right)
        {
            return HitTopRight;
        }

        if (bottom && left)
        {
            return HitBottomLeft;
        }

        if (bottom && right)
        {
            return HitBottomRight;
        }

        if (left)
        {
            return HitLeft;
        }

        if (right)
        {
            return HitRight;
        }

        if (top)
        {
            return HitTop;
        }

        return bottom ? HitBottom : 0;
    }
}
