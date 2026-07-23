using AgentController.Platform.Capabilities;
using AgentController.Platform.Controllers;
using AgentController.Platform.MacOS.Codex;
using AgentController.Platform.MacOS.Controllers;
using AgentController.Platform.MacOS.Permissions;
using AgentController.Platform.Permissions;

namespace AgentController.Platform.MacOS;

public sealed record MacFoundationSnapshot(
    MacPlatformAvailability Platform,
    IReadOnlyList<ControllerInputSnapshot> Controllers,
    IReadOnlyList<MacControllerLifecycleChange> ControllerChanges,
    string? CurrentControllerId,
    long ControllerTopologyRevision,
    CodexExecutableProbe CodexExecutable,
    IReadOnlyList<PlatformCapability> Capabilities,
    IReadOnlyList<PlatformPermissionSnapshot> Permissions,
    DateTimeOffset CapturedAt);

public sealed class MacFoundationRuntime : IDisposable
{
    private readonly MacGameControllerBackend _controllers = new();
    private readonly MacControllerTopologyTracker _controllerTopology = new();
    private bool _disposed;

    public MacFoundationSnapshot Capture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var platform = MacPlatformSupport.Current;
        var controllerTopology = _controllerTopology.Update(
            _controllers.Poll());
        var codex = MacCodexExecutableLocator.Locate();
        return new MacFoundationSnapshot(
            platform,
            controllerTopology.Controllers,
            controllerTopology.Changes,
            controllerTopology.CurrentControllerId,
            controllerTopology.Revision,
            codex,
            BuildCapabilities(platform, codex),
            MacPermissionProbe.Capture(),
            DateTimeOffset.Now);
    }

    public bool TryOpenPrivacySettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return MacSystemSettings.TryOpenPrivacySettings();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controllers.Dispose();
    }

    private IReadOnlyList<PlatformCapability> BuildCapabilities(
        MacPlatformAvailability platform,
        CodexExecutableProbe codex)
    {
        if (platform != MacPlatformAvailability.Supported)
        {
            var state = platform ==
                MacPlatformAvailability.DifferentOperatingSystem
                    ? PlatformCapabilityState.Unsupported
                    : PlatformCapabilityState.Unsupported;
            return
            [
                new(
                    "platform",
                    MacPlatformSupport.MinimumVersionDisplayName,
                    state,
                    platform == MacPlatformAvailability.DifferentOperatingSystem
                        ? "This build can be cross-compiled here; run it on a supported Mac."
                        : $"Requires {MacPlatformSupport.MinimumVersionDisplayName} or later."),
            ];
        }

        return
        [
            new(
                "controller.standard",
                "Apple Game Controller",
                _controllers.IsAvailable
                    ? PlatformCapabilityState.Available
                    : PlatformCapabilityState.Unavailable,
                _controllers.IsAvailable
                    ? "Extended profiles, session-stable controller identities, battery, haptics, light, and current-controller state are polled through GameController.framework."
                    : _controllers.LastError ?? "The native framework could not be loaded."),
            new(
                "controller.background",
                "Background controller events",
                _controllers.SupportsBackgroundEvents
                    ? PlatformCapabilityState.Limited
                    : PlatformCapabilityState.Unavailable,
                _controllers.SupportsBackgroundEvents
                    ? "Enabled and read back from Apple Game Controller; sleep/wake and hardware matrices still require a physical Mac validation run."
                    : "Apple Game Controller did not confirm background monitoring."),
            new(
                "codex.app-server",
                "Codex App Server",
                codex.IsFound
                    ? PlatformCapabilityState.Limited
                    : PlatformCapabilityState.Unavailable,
                codex.IsFound
                    ? $"Codex CLI detected at {codex.ExecutablePath}; the semantic Thread/Turn client remains a later vertical slice."
                    : "Codex CLI was not found in PATH or standard Homebrew/user locations."),
            new(
                "shell.native-menu",
                "Native menu bar",
                PlatformCapabilityState.Available,
                "Provided by Avalonia NativeMenu on macOS."),
            new(
                "lifecycle.single-instance",
                "Single instance",
                PlatformCapabilityState.Available,
                "A process-wide named mutex prevents duplicate preview instances."),
            new(
                "lifecycle.sleep",
                "Sleep/wake recovery",
                PlatformCapabilityState.Limited,
                "The read-only polling backend resumes without held input; active-action recovery still requires Mac hardware validation."),
            new(
                "voice",
                "Push to talk",
                PlatformCapabilityState.Unavailable,
                "Microphone access is not requested in this Foundation Preview."),
            new(
                "micro.virtual",
                "Virtual Micro",
                PlatformCapabilityState.Unavailable,
                "No CoreHID entitlement or HIDDriverKit evidence is claimed; Windows VHF is not reused on macOS."),
        ];
    }
}
