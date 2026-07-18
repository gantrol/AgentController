using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class JoystickGeometryTests
{
    [Theory]
    [InlineData(24, 0, 0.00, "right", 13, 0)]
    [InlineData(0, 24, 0.25, "down", 0, 13)]
    [InlineData(-24, 0, 0.50, "left", -13, 0)]
    [InlineData(0, -24, 0.75, "up", 0, -13)]
    public void ResolveDelta_MapsCardinalDirectionsToCodexAngles(
        double deltaX,
        double deltaY,
        double expectedAngle,
        string expectedDirection,
        double expectedVisualX,
        double expectedVisualY)
    {
        var result = JoystickGeometry.ResolveDelta(deltaX, deltaY);

        Assert.Equal(expectedAngle, result.Angle, 6);
        Assert.Equal(1, result.Distance, 6);
        Assert.Equal(expectedDirection, result.Direction);
        Assert.Equal(expectedVisualX, result.VisualX, 6);
        Assert.Equal(expectedVisualY, result.VisualY, 6);
    }

    [Fact]
    public void ResolveDelta_ClampsDistanceAndVisualTravel()
    {
        var result = JoystickGeometry.ResolveDelta(56, 0);

        Assert.Equal(1, result.Distance, 6);
        Assert.Equal(13, result.VisualX, 6);
        Assert.Equal(0, result.VisualY, 6);
    }

    [Fact]
    public void ResolveDelta_CentersWithoutChoosingADirection()
    {
        var result = JoystickGeometry.ResolveDelta(0, 0);

        Assert.Equal(0, result.Distance);
        Assert.Null(result.Direction);
        Assert.Equal(0, result.VisualX);
        Assert.Equal(0, result.VisualY);
    }
}
