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

    [Theory]
    [InlineData("Windows录屏软件与脚本方案 — DeepSeek Harness", true)]
    [InlineData("DeepSeek Harness - Google Chrome", false)]
    [InlineData("DeepSeek Harness - Microsoft Edge", false)]
    [InlineData("DeepSeek Harness - Personal - Microsoft Edge", false)]
    public void DeepSeekActivationRejectsNormalBrowserChrome(
        string title,
        bool expected)
    {
        Assert.Equal(
            expected,
            HarnessWindowActivator.IsDedicatedBrowserWindowTitle(title));
    }
}
