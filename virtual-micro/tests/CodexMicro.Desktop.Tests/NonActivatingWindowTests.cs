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
}
