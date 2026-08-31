using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class CodexMenuSelectionObserverTests
{
    [Fact]
    public void FormatShowsPositionCountAndFocusedItem()
    {
        var selection = new CodexMenuSelection(
            "5.6 Sol Max",
            "Effort Max",
            2,
            4);

        Assert.Equal("2 / 4  ·  Effort Max", selection.DisplayText);
    }

    [Fact]
    public void FormatPromptsForRotationBeforeMenuItemHasFocus()
    {
        var selection = new CodexMenuSelection(
            "5.6 Sol Max",
            string.Empty,
            0,
            4);

        Assert.Equal("转动选择  ·  5.6 Sol Max", selection.DisplayText);
    }

    [Fact]
    public void FormatMarksPermissionDialogAsUnsupported()
    {
        var selection = new CodexMenuSelection(
            "Full access confirmation",
            "Confirm",
            3,
            3,
            CodexSelectionSurface.Dialog);

        Assert.Equal(
            "确认框暂不支持旋钮 · 请在 Codex 操作",
            selection.DisplayText);
    }

    [Fact]
    public void FormatMarksUnfocusedPermissionDialogAsUnsupported()
    {
        var selection = new CodexMenuSelection(
            "Full access confirmation",
            string.Empty,
            0,
            3,
            CodexSelectionSurface.Dialog);

        Assert.Equal(
            "确认框暂不支持旋钮 · 请在 Codex 操作",
            selection.DisplayText);
    }

    [Theory]
    [InlineData("Ask for approval", "Ask for approval")]
    [InlineData(
        "Approve for me Only ask for actions detected as potentially unsafe",
        "Approve for me")]
    [InlineData(
        "Full access Unrestricted access to the internet and any file on your computer",
        "Full access")]
    public void MatchApprovalOptionAcceptsCodexAccessibleDescriptions(
        string accessibleName,
        string expected)
    {
        Assert.Equal(
            expected,
            CodexMenuSelectionObserver.MatchApprovalOption(accessibleName));
    }

    [Fact]
    public void MatchApprovalOptionRejectsComposerControls()
    {
        Assert.Null(CodexMenuSelectionObserver.MatchApprovalOption("Model 5.6 Sol"));
    }

}
