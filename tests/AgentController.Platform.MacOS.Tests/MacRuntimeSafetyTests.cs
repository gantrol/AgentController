using AgentController.Platform.Capabilities;
using AgentController.Platform.MacOS.Controllers;
using AgentController.Platform.MacOS.Permissions;
using AgentController.Platform.Permissions;

namespace AgentController.Platform.MacOS.Tests;

public sealed class MacRuntimeSafetyTests
{
    [Fact]
    public void NativeControllerBackendDoesNotLoadOnAnotherOperatingSystem()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        using var backend = new MacGameControllerBackend();

        Assert.False(backend.IsAvailable);
        Assert.False(backend.SupportsBackgroundEvents);
        Assert.Empty(backend.Poll());
        Assert.Contains("macOS", backend.LastError);
    }

    [Fact]
    public void PermissionsRemainUnrequestedOnAnotherOperatingSystem()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        var permissions = MacPermissionProbe.Capture();

        Assert.Equal(3, permissions.Count);
        Assert.All(
            permissions,
            permission => Assert.Equal(
                PlatformPermissionState.Unsupported,
                permission.State));
    }

    [Fact]
    public void FoundationSnapshotNeverClaimsMacFeaturesOnWindows()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        using var runtime = new MacFoundationRuntime();
        var snapshot = runtime.Capture();

        Assert.Equal(
            MacPlatformAvailability.DifferentOperatingSystem,
            snapshot.Platform);
        Assert.Empty(snapshot.Controllers);
        var platform = Assert.Single(snapshot.Capabilities);
        Assert.Equal(PlatformCapabilityState.Unsupported, platform.State);
        Assert.DoesNotContain(
            snapshot.Capabilities,
            capability =>
                capability.Id == "micro.virtual" &&
                capability.State == PlatformCapabilityState.Available);
    }
}
