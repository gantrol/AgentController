using AgentController.Platform.MacOS.Controllers;

namespace AgentController.Platform.MacOS.Tests;

public sealed class MacControllerIdentityMapTests
{
    [Fact]
    public void KeepsIdentityWhenTheNativeControllerArrayReorders()
    {
        var identities = new MacControllerIdentityMap();
        var first = (nint)101;
        var second = (nint)202;

        var firstId = identities.GetOrAdd(first);
        var secondId = identities.GetOrAdd(second);
        identities.RetainOnly([second, first]);

        Assert.Equal(firstId, identities.GetOrAdd(first));
        Assert.Equal(secondId, identities.GetOrAdd(second));
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public void AssignsANewIdentityAfterDisconnect()
    {
        var identities = new MacControllerIdentityMap();
        var nativeController = (nint)101;
        var beforeDisconnect = identities.GetOrAdd(nativeController);

        identities.RetainOnly(Array.Empty<nint>());
        var afterReconnect = identities.GetOrAdd(nativeController);

        Assert.NotEqual(beforeDisconnect, afterReconnect);
    }
}
