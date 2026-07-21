using System.Runtime.InteropServices;
using AgentController.Platform.Permissions;

namespace AgentController.Platform.MacOS.Permissions;

public static class MacPermissionProbe
{
    private const string ApplicationServicesFramework =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    public static IReadOnlyList<PlatformPermissionSnapshot> Capture()
    {
        if (MacPlatformSupport.Current != MacPlatformAvailability.Supported)
        {
            return Enum.GetValues<PlatformPermissionKind>()
                .Select(kind => new PlatformPermissionSnapshot(
                    kind,
                    PlatformPermissionState.Unsupported,
                    Purpose(kind),
                    CanRequestFromApplication: false))
                .ToArray();
        }

        return
        [
            new(
                PlatformPermissionKind.Accessibility,
                IsAccessibilityTrusted()
                    ? PlatformPermissionState.Granted
                    : PlatformPermissionState.NotRequested,
                Purpose(PlatformPermissionKind.Accessibility),
                CanRequestFromApplication: false),
            new(
                PlatformPermissionKind.InputMonitoring,
                PlatformPermissionState.NotRequired,
                Purpose(PlatformPermissionKind.InputMonitoring),
                CanRequestFromApplication: false),
            new(
                PlatformPermissionKind.Microphone,
                PlatformPermissionState.NotRequested,
                Purpose(PlatformPermissionKind.Microphone),
                CanRequestFromApplication: false),
        ];
    }

    private static bool IsAccessibilityTrusted()
    {
        try
        {
            return AXIsProcessTrusted();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static string Purpose(PlatformPermissionKind kind) => kind switch
    {
        PlatformPermissionKind.Accessibility =>
            "Only for optional Companion window discovery and activation; " +
            "not required by the App Server mode.",
        PlatformPermissionKind.InputMonitoring =>
            "Not used by the standard Apple Game Controller backend.",
        PlatformPermissionKind.Microphone =>
            "Reserved for a future push-to-talk slice; the preview does not " +
            "request it.",
        _ => string.Empty,
    };

    [DllImport(ApplicationServicesFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();
}
