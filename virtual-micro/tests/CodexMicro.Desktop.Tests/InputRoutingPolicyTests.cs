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
}
