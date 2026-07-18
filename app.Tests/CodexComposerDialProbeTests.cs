using System.Windows.Automation;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexComposerDialProbeTests
{
    [Fact]
    public void PopupKindAcceptsOnlyDialSurfaceControlTypes()
    {
        Assert.Equal(
            ComposerDialPopupElementKind.Menu,
            CodexComposerDialProbe.DialPopupKind(ControlType.Menu));
        Assert.Equal(
            ComposerDialPopupElementKind.ListBox,
            CodexComposerDialProbe.DialPopupKind(ControlType.List));
        Assert.Equal(
            ComposerDialPopupElementKind.OptionItem,
            CodexComposerDialProbe.DialPopupKind(ControlType.ListItem));
        Assert.Equal(
            ComposerDialPopupElementKind.Edit,
            CodexComposerDialProbe.DialPopupKind(ControlType.Edit));
        Assert.Null(CodexComposerDialProbe.DialPopupKind(
            ControlType.Button));
    }
}
