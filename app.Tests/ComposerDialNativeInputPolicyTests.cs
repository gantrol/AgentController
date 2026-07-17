using CodexController.Services;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class ComposerDialNativeInputPolicyTests
{
    [Theory]
    [InlineData(ComposerDialNavigation.Left, 0x25)]
    [InlineData(ComposerDialNavigation.Up, 0x26)]
    [InlineData(ComposerDialNavigation.Right, 0x27)]
    [InlineData(ComposerDialNavigation.Down, 0x28)]
    public void MapsDialNavigationToNativeArrowKeys(
        ComposerDialNavigation navigation,
        ushort expected)
    {
        Assert.True(
            ComposerDialNativeInputPolicy.TryGetNavigationKey(
                navigation,
                out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void InitialFocusNudgesAwayAndBackToCurrentItem()
    {
        Assert.Equal(
            [0x28, 0x26],
            ComposerDialNativeInputPolicy
                .BuildFocusNudgeSequence(1, 3));
        Assert.Equal(
            [0x26, 0x28],
            ComposerDialNativeInputPolicy
                .BuildFocusNudgeSequence(2, 3));
    }

    [Theory]
    [InlineData(-1, 3)]
    [InlineData(3, 3)]
    [InlineData(0, 0)]
    public void InitialFocusRejectsInvalidNavigationCounts(
        int index,
        int count)
    {
        Assert.Empty(
            ComposerDialNativeInputPolicy
                .BuildFocusNudgeSequence(index, count));
    }

    [Fact]
    public void DialNavigationAlwaysReportsElapsedTime()
    {
        var result = new CodexComposerService().DialNavigate(
            ComposerDialNavigation.Down,
            new AppSettings
            {
                BridgeEnabled = false,
            });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.ElapsedMilliseconds);
        Assert.True(result.ElapsedMilliseconds >= 0);
    }

    [Theory]
    [InlineData("Power")]
    [InlineData("Reasoning effort")]
    [InlineData("推理强度")]
    public void DetectsControlsThatNeedMicroSpecificSemantics(string name)
    {
        Assert.True(
            ComposerDialNativeInputPolicy
                .RequiresMicroSemanticNavigation(name));
    }

    [Theory]
    [InlineData("Model")]
    [InlineData("Full access")]
    [InlineData("Project")]
    public void OrdinaryMenusCanUseNativeNavigation(string name)
    {
        Assert.False(
            ComposerDialNativeInputPolicy
                .RequiresMicroSemanticNavigation(name));
    }
}
