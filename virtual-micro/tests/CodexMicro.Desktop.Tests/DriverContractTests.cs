using CodexMicro.Desktop.Driver;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class DriverContractTests
{
    [Fact]
    public void RestrictedKeyboardInputEncodesShiftTab()
    {
        var input = new byte[24];

        DriverContract.WriteKeyboardInput(
            input,
            VhfKeyboardKey.Tab,
            shift: true,
            sequence: 42);

        Assert.Equal(DriverContract.Magic, BitConverter.ToUInt32(input, 0));
        Assert.Equal(DriverContract.Version, BitConverter.ToUInt16(input, 4));
        Assert.Equal(24, BitConverter.ToUInt16(input, 6));
        Assert.Equal(42UL, BitConverter.ToUInt64(input, 8));
        Assert.Equal(0x2B, input[16]);
        Assert.Equal(0x02, input[17]);
        Assert.All(input[18..], value => Assert.Equal(0, value));
    }
}
