using CodexController.Native;

namespace CodexController.Tests;

public sealed class Win32InputTests
{
    [Fact]
    public void MainElectronWindowOutranksOwnedToolWindow()
    {
        var main = new CodexWindowCandidate(
            new nint(1),
            10,
            "Codex",
            "Chrome_WidgetWin_1",
            HasOwner: false,
            IsToolWindow: false,
            Area: 1_800_000);
        var tool = new CodexWindowCandidate(
            new nint(2),
            10,
            "ChatGPT",
            "Chrome_WidgetWin_1",
            HasOwner: true,
            IsToolWindow: true,
            Area: 2_000_000);

        Assert.True(
            CodexWindowActivator.ScoreCandidate(main) >
            CodexWindowActivator.ScoreCandidate(tool));
    }

    [Fact]
    public void ExplicitWindowAdvanceCyclesOnlyMainWindows()
    {
        var first = WindowCandidate(
            handle: 1,
            area: 2_000_000);
        var second = WindowCandidate(
            handle: 2,
            area: 1_800_000);
        var tool = WindowCandidate(
            handle: 3,
            area: 3_000_000,
            hasOwner: true,
            isToolWindow: true);
        var ranked = new[] { first, second, tool };

        Assert.Equal(
            second,
            CodexWindowActivator.SelectCandidate(
                ranked,
                first.Handle,
                advanceCurrentWindow: true));
        Assert.Equal(
            first,
            CodexWindowActivator.SelectCandidate(
                ranked,
                second.Handle,
                advanceCurrentWindow: true));
    }

    [Fact]
    public void OrdinaryActivationNeverCyclesForegroundWindow()
    {
        var first = WindowCandidate(handle: 1, area: 2_000_000);
        var second = WindowCandidate(handle: 2, area: 1_800_000);

        Assert.Equal(
            first,
            CodexWindowActivator.SelectCandidate(
                [first, second],
                first.Handle,
                advanceCurrentWindow: false));
    }

    [Theory]
    [InlineData(0x25)]
    [InlineData(0x26)]
    [InlineData(0x27)]
    [InlineData(0x28)]
    public void ArrowKeysAreInjectedAsExtendedKeys(ushort virtualKey)
    {
        Assert.Equal(
            0x0001u,
            Win32Input.GetKeyboardEventFlags(
                virtualKey,
                keyUp: false));
        Assert.Equal(
            0x0003u,
            Win32Input.GetKeyboardEventFlags(
                virtualKey,
                keyUp: true));
    }

    [Theory]
    [InlineData(0x12)]
    [InlineData(0x41)]
    [InlineData(0x82)]
    public void OrdinaryKeysDoNotGainTheExtendedFlag(ushort virtualKey)
    {
        Assert.Equal(
            0u,
            Win32Input.GetKeyboardEventFlags(
                virtualKey,
                keyUp: false));
        Assert.Equal(
            0x0002u,
            Win32Input.GetKeyboardEventFlags(
                virtualKey,
                keyUp: true));
    }

    private static CodexWindowCandidate WindowCandidate(
        long handle,
        long area,
        bool hasOwner = false,
        bool isToolWindow = false) =>
        new(
            new nint(handle),
            checked((uint)handle),
            "Codex",
            "Chrome_WidgetWin_1",
            hasOwner,
            isToolWindow,
            area);
}
