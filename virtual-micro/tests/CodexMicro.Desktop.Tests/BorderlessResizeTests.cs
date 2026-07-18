using System.Windows;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class BorderlessResizeTests
{
    private static readonly Size WindowSize = new(590, 610);

    [Theory]
    [InlineData(1, 1, BorderlessResize.HitTopLeft)]
    [InlineData(589, 1, BorderlessResize.HitTopRight)]
    [InlineData(1, 609, BorderlessResize.HitBottomLeft)]
    [InlineData(589, 609, BorderlessResize.HitBottomRight)]
    [InlineData(1, 305, BorderlessResize.HitLeft)]
    [InlineData(589, 305, BorderlessResize.HitRight)]
    [InlineData(295, 1, BorderlessResize.HitTop)]
    [InlineData(295, 609, BorderlessResize.HitBottom)]
    [InlineData(295, 305, 0)]
    public void HitTestReturnsSymmetricResizeRegions(
        double x,
        double y,
        int expected)
    {
        var result = BorderlessResize.HitTest(
            WindowSize,
            new Point(x, y),
            10);

        Assert.Equal(expected, result);
    }
}
