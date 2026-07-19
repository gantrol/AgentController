using AgentController.Platform.Windowing;
using Xunit;

namespace AgentController.Architecture.Tests;

public sealed class PlatformContractShapeTests
{
    [Fact]
    public void ForegroundApplicationContractLeaksNoNativeHandle()
    {
        var contract = typeof(IForegroundApplication);
        var property = Assert.Single(contract.GetProperties());
        var method = Assert.Single(
            contract.GetMethods(),
            candidate => !candidate.IsSpecialName);

        Assert.True(contract.IsInterface);
        Assert.Equal("IsForeground", property.Name);
        Assert.Equal(typeof(bool), property.PropertyType);
        Assert.Equal("TryActivate", method.Name);
        Assert.Equal(typeof(bool), method.ReturnType);
        Assert.Empty(method.GetParameters());
        Assert.DoesNotContain(
            contract.GetMembers(),
            member => member.Name.Contains(
                "Handle",
                StringComparison.OrdinalIgnoreCase));
    }
}
