using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Input;
using Avalonia.Threading;
using AgentController.Platform.Capabilities;
using AgentController.Platform.Controllers;
using AgentController.Platform.MacOS;
using AgentController.Platform.Permissions;

namespace AgentController.Desktop.ViewModels;

public sealed class FoundationViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MacFoundationRuntime _runtime;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private string _platformSummary = "Checking platform…";
    private string _codexSummary = "Checking Codex CLI…";
    private string _lastUpdated = string.Empty;
    private string _controllerSummary = "No controller detected";
    private string _controllerEmptyState =
        "Connect a controller supported by Apple Game Controller.";
    private string _statusMessage =
        "Read-only preview: no Codex action has been sent.";
    private IReadOnlyList<ControllerRowViewModel> _controllers = [];
    private IReadOnlyList<CapabilityRowViewModel> _capabilities = [];
    private IReadOnlyList<PermissionRowViewModel> _permissions = [];

    public FoundationViewModel(MacFoundationRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        RefreshCommand = new RelayCommand(Refresh);
        OpenPrivacySettingsCommand = new RelayCommand(OpenPrivacySettings);
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120),
        };
        _timer.Tick += Timer_Tick;
        Refresh();
        _timer.Start();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand OpenPrivacySettingsCommand { get; }

    public string PlatformSummary
    {
        get => _platformSummary;
        private set => SetField(ref _platformSummary, value);
    }

    public string CodexSummary
    {
        get => _codexSummary;
        private set => SetField(ref _codexSummary, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public string ControllerSummary
    {
        get => _controllerSummary;
        private set => SetField(ref _controllerSummary, value);
    }

    public string ControllerEmptyState
    {
        get => _controllerEmptyState;
        private set => SetField(ref _controllerEmptyState, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public IReadOnlyList<ControllerRowViewModel> Controllers
    {
        get => _controllers;
        private set => SetField(ref _controllers, value);
    }

    public IReadOnlyList<CapabilityRowViewModel> Capabilities
    {
        get => _capabilities;
        private set => SetField(ref _capabilities, value);
    }

    public IReadOnlyList<PermissionRowViewModel> Permissions
    {
        get => _permissions;
        private set => SetField(ref _permissions, value);
    }

    public void Refresh()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            Apply(_runtime.Capture());
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ExternalException)
        {
            StatusMessage = $"Platform refresh failed: {exception.Message}";
        }
    }

    public void OpenPrivacySettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StatusMessage = _runtime.TryOpenPrivacySettings()
            ? "Opened macOS Privacy & Security settings."
            : "Privacy settings are available only when running on macOS.";
    }

    public void ShowAbout()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StatusMessage =
            "Agent Controller 0.1 Foundation Preview · no virtual Micro · no semantic actions yet.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _runtime.Dispose();
    }

    private void Timer_Tick(object? sender, EventArgs e) => Refresh();

    private void Apply(MacFoundationSnapshot snapshot)
    {
        PlatformSummary = snapshot.Platform switch
        {
            MacPlatformAvailability.Supported =>
                $"{MacPlatformSupport.MinimumVersionDisplayName}+ · native services ready",
            MacPlatformAvailability.UnsupportedVersion =>
                $"Unsupported · requires {MacPlatformSupport.MinimumVersionDisplayName}+",
            _ => "Cross-build host · run this bundle on macOS",
        };
        CodexSummary = snapshot.CodexExecutable.IsFound
            ? $"Codex CLI found · {snapshot.CodexExecutable.ExecutablePath}"
            : "Codex CLI not found · App Server actions remain unavailable";
        LastUpdated = $"Last checked {snapshot.CapturedAt:HH:mm:ss}";

        Controllers = snapshot.Controllers
            .Select(ControllerRowViewModel.From)
            .ToArray();
        ControllerSummary = Controllers.Count switch
        {
            0 => "No controller detected",
            1 => "1 controller detected",
            _ => $"{Controllers.Count} controllers detected",
        };
        ControllerEmptyState = Controllers.Count == 0
            ? "Connect a controller supported by Apple Game Controller."
            : "Standard profile input is active; non-standard back buttons are not claimed.";

        Capabilities = snapshot.Capabilities
            .Select(CapabilityRowViewModel.From)
            .ToArray();
        Permissions = snapshot.Permissions
            .Select(PermissionRowViewModel.From)
            .ToArray();
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed record ControllerRowViewModel(
    string DisplayName,
    string Identity,
    string Sticks,
    string Input,
    string CurrentState,
    string Battery)
{
    internal static ControllerRowViewModel From(
        ControllerInputSnapshot controller) =>
        new(
            controller.DisplayName,
            $"{controller.ProductCategory} · {controller.Id}",
            $"L {Axis(controller.LeftStick.X)}, {Axis(controller.LeftStick.Y)} · " +
            $"R {Axis(controller.RightStick.X)}, {Axis(controller.RightStick.Y)}",
            $"{controller.Buttons} · LT {controller.LeftTrigger:0.00} · RT {controller.RightTrigger:0.00}",
            controller.IsCurrent ? "CURRENT" : "CONNECTED",
            controller.BatteryLevel is { } level
                ? $"Battery {level:P0}"
                : "Battery n/a");

    private static string Axis(float value) =>
        value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
}

public sealed record CapabilityRowViewModel(
    string Name,
    string State,
    string Detail)
{
    internal static CapabilityRowViewModel From(
        PlatformCapability capability) =>
        new(
            capability.DisplayName,
            capability.State.ToString(),
            capability.Detail);
}

public sealed record PermissionRowViewModel(
    string Name,
    string State,
    string Purpose)
{
    internal static PermissionRowViewModel From(
        PlatformPermissionSnapshot permission) =>
        new(
            PermissionName(permission.Kind),
            permission.State.ToString(),
            permission.Purpose);

    private static string PermissionName(PlatformPermissionKind kind) =>
        kind switch
        {
            PlatformPermissionKind.Accessibility => "Accessibility",
            PlatformPermissionKind.InputMonitoring => "Input Monitoring",
            PlatformPermissionKind.Microphone => "Microphone",
            _ => kind.ToString(),
        };
}
