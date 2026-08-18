using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class InputRoutingPolicyTests
{
    [Theory]
    [InlineData("AG00")]
    [InlineData("AG01")]
    [InlineData("AG02")]
    [InlineData("AG03")]
    [InlineData("AG04")]
    [InlineData("AG05")]
    [InlineData("ACT12")]
    public void AgentAndCodexKeysBringCodexToTheForeground(string key)
    {
        Assert.True(MicroSurfaceWindow.ShouldActivateCodexForKey(key));
    }

    [Theory]
    [InlineData("ACT06")]
    [InlineData("ACT10")]
    [InlineData("ENC")]
    [InlineData("AG06")]
    public void OtherKeysDoNotForceForegroundActivation(string key)
    {
        Assert.False(MicroSurfaceWindow.ShouldActivateCodexForKey(key));
    }

    [Fact]
    public void CodexKeyActivatesFirstAndOnlySendsWhenAlreadyForeground()
    {
        Assert.False(MicroSurfaceWindow.ShouldSendCodexHidForKey(
            "ACT12",
            codexIsForeground: false));
        Assert.True(MicroSurfaceWindow.ShouldSendCodexHidForKey(
            "ACT12",
            codexIsForeground: true));
        Assert.True(MicroSurfaceWindow.ShouldSendCodexHidForKey(
            "ACT06",
            codexIsForeground: false));
    }

    [Fact]
    public void CodexLaunchUsesTheRegisteredDesktopApplication()
    {
        var startInfo = CodexWindowActivator.CreateLaunchStartInfo();

        Assert.Equal("explorer.exe", startInfo.FileName);
        Assert.Equal(
            @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App",
            startInfo.Arguments);
        Assert.True(startInfo.UseShellExecute);
    }
}
