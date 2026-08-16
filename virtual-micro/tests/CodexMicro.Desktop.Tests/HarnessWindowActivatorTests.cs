using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class HarnessWindowActivatorTests
{
    [Fact]
    public void ForegroundFallbackStaysInTheNormalZOrderBand()
    {
        Assert.Equal(
            IntPtr.Zero,
            HarnessWindowActivator.ActivationFallbackInsertAfter);
    }
}
