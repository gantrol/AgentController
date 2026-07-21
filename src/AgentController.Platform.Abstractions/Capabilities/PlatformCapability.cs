namespace AgentController.Platform.Capabilities;

public enum PlatformCapabilityState
{
    Available,
    Limited,
    NeedsPermission,
    Unavailable,
    Unsupported,
}

public sealed record PlatformCapability(
    string Id,
    string DisplayName,
    PlatformCapabilityState State,
    string Detail);
