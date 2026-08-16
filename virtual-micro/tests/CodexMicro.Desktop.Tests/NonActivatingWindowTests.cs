using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class NonActivatingWindowTests
{
    [Fact]
    public void MouseActivationIsRejectedWithoutHandlingOtherMessages()
    {
        var handled = false;

        var accepted = NonActivatingWindow.TryHandleMessage(
            NonActivatingWindow.WmMouseActivate,
            ref handled,
            out var result);

        Assert.True(accepted);
        Assert.True(handled);
        Assert.Equal(NonActivatingWindow.MaNoActivate, result.ToInt32());

        handled = false;
        accepted = NonActivatingWindow.TryHandleMessage(
            BorderlessResize.WmNcHitTest,
            ref handled,
            out result);

        Assert.False(accepted);
        Assert.False(handled);
        Assert.Equal(IntPtr.Zero, result);
    }

    [Fact]
    public void ExtendedStyleAlwaysIncludesNoActivate()
    {
        const long existingStyle = 0x00040000L;

        var updated = NonActivatingWindow.AddNoActivateStyle(existingStyle);

        Assert.Equal(
            existingStyle | NonActivatingWindow.WsExNoActivate,
            updated);
    }

    [Fact]
    public void TopmostContinuityOnlyRepairsAgainstAnotherNormalApplication()
    {
        Assert.True(NonActivatingWindow.ShouldReassertTopmost(
            requestedTopmost: true,
            targetVisible: true,
            sameWindow: false,
            sameProcess: false,
            foregroundTopmost: false));
        Assert.False(NonActivatingWindow.ShouldReassertTopmost(
            requestedTopmost: false,
            targetVisible: true,
            sameWindow: false,
            sameProcess: false,
            foregroundTopmost: false));
        Assert.False(NonActivatingWindow.ShouldReassertTopmost(
            requestedTopmost: true,
            targetVisible: false,
            sameWindow: false,
            sameProcess: false,
            foregroundTopmost: false));
        Assert.False(NonActivatingWindow.ShouldReassertTopmost(
            requestedTopmost: true,
            targetVisible: true,
            sameWindow: true,
            sameProcess: false,
            foregroundTopmost: false));
        Assert.False(NonActivatingWindow.ShouldReassertTopmost(
            requestedTopmost: true,
            targetVisible: true,
            sameWindow: false,
            sameProcess: true,
            foregroundTopmost: false));
        Assert.False(NonActivatingWindow.ShouldReassertTopmost(
            requestedTopmost: true,
            targetVisible: true,
            sameWindow: false,
            sameProcess: false,
            foregroundTopmost: true));
    }

    [Fact]
    public void TopmostExtendedStyleIsRecognized()
    {
        Assert.True(NonActivatingWindow.HasTopmostStyle(
            NonActivatingWindow.WsExTopmost | 0x00040000L));
        Assert.False(NonActivatingWindow.HasTopmostStyle(0x00040000L));
    }
}
