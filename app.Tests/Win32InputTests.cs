using CodexController.Native;

namespace CodexController.Tests;

public sealed class Win32InputTests
{
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
}
