namespace AgentController.Platform.Permissions;

public enum PlatformPermissionKind
{
    Accessibility,
    InputMonitoring,
    Microphone,
}

public enum PlatformPermissionState
{
    NotRequired,
    NotRequested,
    Granted,
    Denied,
    Unknown,
    Unsupported,
}

public sealed record PlatformPermissionSnapshot(
    PlatformPermissionKind Kind,
    PlatformPermissionState State,
    string Purpose,
    bool CanRequestFromApplication);
