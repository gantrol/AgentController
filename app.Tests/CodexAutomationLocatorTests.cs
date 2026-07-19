using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexAutomationLocatorTests
{
    [Fact]
    public void NearComposerRequiresHorizontalOverlapAndVerticalProximity()
    {
        var composer = new System.Windows.Rect(80, 100, 200, 40);

        Assert.True(CodexAutomationLocator.IsNearComposer(
            new System.Windows.Rect(100, 90, 50, 30),
            composer));
        Assert.False(CodexAutomationLocator.IsNearComposer(
            new System.Windows.Rect(400, 90, 50, 30),
            composer));
        Assert.False(CodexAutomationLocator.IsNearComposer(
            new System.Windows.Rect(100, 300, 50, 30),
            composer));
    }
}
