using CodexController.Controllers;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class VirtualDialInputPolicyTests
{
    [Theory]
    [InlineData(0.58, 0.24, 0.30)]
    [InlineData(0.58, 0.36, 0.36)]
    [InlineData(0.35, null, 0.35)]
    [InlineData(0.25, 0.40, 0.25)]
    public void ResolvesAResponsiveDiscreteDeadZone(
        double userDeadZone,
        double? profileDeadZone,
        double expected)
    {
        Assert.Equal(
            expected,
            VirtualDialInputPolicy.ResolveDeadZone(
                userDeadZone,
                profileDeadZone),
            precision: 6);
    }

    [Theory]
    [InlineData(-1, ComposerDialNavigation.Left)]
    [InlineData(1, ComposerDialNavigation.Right)]
    public void HorizontalMotionKeepsItsLiteralScreenDirection(
        int direction,
        ComposerDialNavigation expected)
    {
        Assert.Equal(
            expected,
            VirtualDialInputPolicy.ResolveHorizontalNavigation(direction));
    }

    [Fact]
    public void NeutralHorizontalMotionDoesNotNavigate()
    {
        Assert.Null(
            VirtualDialInputPolicy.ResolveHorizontalNavigation(0));
    }

    [Theory]
    [InlineData(-1, ComposerDialNavigation.Down)]
    [InlineData(1, ComposerDialNavigation.Up)]
    public void VerticalMotionTraversesAnOpenComposerSurface(
        int direction,
        ComposerDialNavigation expected)
    {
        Assert.Equal(
            expected,
            VirtualDialInputPolicy.ResolveVerticalNavigation(
                direction,
                isMenuActive: true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void VerticalMotionIsInertWithoutAnOpenComposerSurface(
        int direction)
    {
        Assert.Null(
            VirtualDialInputPolicy.ResolveVerticalNavigation(
                direction,
                isMenuActive: false));
    }
}
