using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexMicroReadbackObserverTests
{
    [Fact]
    public void MenuReadbackRequiresAConcreteSelection()
    {
        var readback = new CodexMicroReadback(
            CodexMicroSurfaceKind.Menu,
            "Model",
            string.Empty,
            0,
            6,
            SelectionVerified: false);

        Assert.True(readback.IsMenuOpen);
        Assert.False(readback.SelectionVerified);
        Assert.Equal("Model", readback.DisplayText);
    }

    [Fact]
    public void VerifiedReadbackDisplaysVisualPositionAndItem()
    {
        var readback = new CodexMicroReadback(
            CodexMicroSurfaceKind.Approval,
            "Approval mode",
            "Approve for me",
            2,
            3,
            SelectionVerified: true);

        Assert.True(readback.IsMenuOpen);
        Assert.Equal(
            "2 / 3 · Approve for me",
            readback.DisplayText);
    }

    [Fact]
    public void ClosedReadbackIsVerifiedAndNotAMenu()
    {
        Assert.True(CodexMicroReadback.Closed.SelectionVerified);
        Assert.False(CodexMicroReadback.Closed.IsMenuOpen);
    }

    [Theory]
    [InlineData("Approve for me", "Approve for me")]
    [InlineData("Approve for me Only for unsafe actions", "Approve for me")]
    [InlineData("完全访问", "Full access")]
    [InlineData("请求批准", "Ask for approval")]
    public void ApprovalLabelsNormalizeAcrossDescriptionsAndLanguages(
        string value,
        string expected)
    {
        Assert.Equal(
            expected,
            CodexMicroReadbackObserver.CanonicalApprovalName(value));
    }
}
