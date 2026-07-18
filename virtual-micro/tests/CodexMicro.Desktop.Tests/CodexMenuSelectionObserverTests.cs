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
    public void FormatShowsFocusedPermissionDialogButton()
    {
        var selection = new CodexMenuSelection(
            "Full access confirmation",
            "Confirm",
            3,
            3,
            CodexSelectionSurface.Dialog);

        Assert.Equal("3 / 3  ·  Confirm", selection.DisplayText);
    }
}
