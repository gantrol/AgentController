using CodexController.Controllers;
using CodexController.Models;
using CodexController.Native;
using Windows.Gaming.Input;

namespace CodexController.Services;

public sealed class XInputService : IDisposable
{
    private const int DisconnectDebounceMs = 320;
    private const int IdentityRetryDelayMs = 750;
    private const int IdentityResolveAttemptLimit = 4;

    private static readonly DeviceIdentity DisconnectedIdentity = new(
        null,
        null,
        null,
        "None");

    private readonly ControllerProfileRegistry _controllerProfiles;
    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private ControllerState _lastState = ControllerState.Disconnected;
    private DeviceIdentity _lastIdentity = DisconnectedIdentity;
    private uint? _activeUserIndex;
    private Gamepad? _activeGamepad;
    private RawGameController? _activeRawController;
    private bool _identityRefreshPending;
    private bool _identityResolved;
    private int _identityResolveAttempts;
    private long _nextIdentityResolveAt;
    private long _disconnectObservedAt;
    private int _polling;
    private bool _disposed;

    public event EventHandler<ControllerState>? StateChanged;

    public XInputService(
        ControllerProfileRegistry? controllerProfiles = null)
    {
        _controllerProfiles =
            controllerProfiles ?? ControllerProfileRegistry.BuiltIn;
    }

    public ControllerState LastState
    {
        get
        {
            lock (_sync)
            {
                return _lastState;
            }
        }
    }

    public DeviceIdentity LastIdentity
    {
        get
        {
            lock (_sync)
            {
                return _lastIdentity;
            }
        }
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer ??= new System.Threading.Timer(
            Poll,
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(16));
    }

    public void Pulse(double strength = 0.22, int durationMs = 45)
    {
        var index = _activeUserIndex;
        if (index is null)
        {
            if (_activeGamepad is not null)
            {
                var previous = _activeGamepad.Vibration;
                _activeGamepad.Vibration = new GamepadVibration
                {
                    LeftMotor = Math.Clamp(strength, 0, 1),
                    RightMotor = Math.Clamp(strength * 0.72, 0, 1),
                };
                _ = Task.Run(async () =>
                {
                    await Task.Delay(durationMs).ConfigureAwait(false);
                    if (_activeGamepad is not null)
                    {
                        _activeGamepad.Vibration = previous;
                    }
                });
            }

            return;
        }

        XInputNative.SetVibration(index.Value, strength, strength * 0.72);
        _ = Task.Run(async () =>
        {
            await Task.Delay(durationMs).ConfigureAwait(false);
            XInputNative.SetVibration(index.Value, 0, 0);
        });
    }

    private void Poll(object? state)
    {
        if (Interlocked.Exchange(ref _polling, 1) != 0)
        {
            return;
        }

        try
        {
            PollCore();
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    private void PollCore()
    {
        ControllerState next = ControllerState.Disconnected;
        var found = false;
        var publishStableDisconnect = false;

        if (_activeUserIndex is uint active)
        {
            if (XInputNative.TryGetState(active, out next))
            {
                found = true;
                _disconnectObservedAt = 0;
            }
            else if (LastState.IsConnected)
            {
                var now = Environment.TickCount64;
                if (_disconnectObservedAt == 0)
                {
                    _disconnectObservedAt = now;
                }

                if (
                    now - _disconnectObservedAt <
                    DisconnectDebounceMs)
                {
                    return;
                }

                _activeUserIndex = null;
                _activeGamepad = null;
                _activeRawController = null;
                publishStableDisconnect = true;
            }
            else
            {
                _activeUserIndex = null;
            }
        }

        if (!found && !publishStableDisconnect)
        {
            for (uint index = 0; index < 4; index++)
            {
                if (!XInputNative.TryGetState(index, out next))
                {
                    continue;
                }

                _activeUserIndex = index;
                _activeGamepad = null;
                _activeRawController = null;
                BeginIdentityCycle();
                found = true;
                _disconnectObservedAt = 0;
                break;
            }
        }

        if (!found && !publishStableDisconnect)
        {
            _activeUserIndex = null;
            found = TryGetWindowsGamepad(out next);
        }

        if (!found && !publishStableDisconnect)
        {
            found = TryGetRawController(out next);
        }

        if (!found)
        {
            if (!publishStableDisconnect && LastState.IsConnected)
            {
                var now = Environment.TickCount64;
                if (_disconnectObservedAt == 0)
                {
                    _disconnectObservedAt = now;
                }

                if (
                    now - _disconnectObservedAt <
                    DisconnectDebounceMs)
                {
                    return;
                }
            }

            _activeGamepad = null;
            _activeRawController = null;
            next = ControllerState.Disconnected;
        }
        else
        {
            _disconnectObservedAt = 0;
        }

        var identityUpdate = found
            ? TryRefreshIdentity(next.Backend)
            : EndIdentityCycle();

        bool changed;
        lock (_sync)
        {
            changed =
                next.IsConnected != _lastState.IsConnected ||
                next.Backend != _lastState.Backend ||
                next.Buttons != _lastState.Buttons ||
                Math.Abs(next.LeftX - _lastState.LeftX) > 0.008 ||
                Math.Abs(next.LeftY - _lastState.LeftY) > 0.008 ||
                Math.Abs(next.RightX - _lastState.RightX) > 0.008 ||
                Math.Abs(next.RightY - _lastState.RightY) > 0.008 ||
                Math.Abs(next.LeftTrigger - _lastState.LeftTrigger) > 0.008 ||
                Math.Abs(next.RightTrigger - _lastState.RightTrigger) > 0.008 ||
                (
                    identityUpdate is not null &&
                    identityUpdate != _lastIdentity
                );
            _lastState = next;
            if (identityUpdate is not null)
            {
                _lastIdentity = identityUpdate;
            }
        }

        var hasActiveAnalogInput =
            Math.Max(Math.Abs(next.LeftX), Math.Abs(next.LeftY)) > 0.02 ||
            Math.Max(Math.Abs(next.RightX), Math.Abs(next.RightY)) > 0.02 ||
            next.LeftTrigger > 0.02 ||
            next.RightTrigger > 0.02;

        if (changed || hasActiveAnalogInput)
        {
            StateChanged?.Invoke(this, next);
        }
    }

    private bool TryGetWindowsGamepad(out ControllerState state)
    {
        try
        {
            if (
                _activeGamepad is null ||
                !Gamepad.Gamepads.Contains(_activeGamepad))
            {
                var selected = Gamepad.Gamepads.FirstOrDefault();
                if (!ReferenceEquals(selected, _activeGamepad))
                {
                    _activeGamepad = selected;
                    if (selected is not null)
                    {
                        BeginIdentityCycle();
                    }
                }
            }

            if (_activeGamepad is null)
            {
                state = ControllerState.Disconnected;
                return false;
            }

            _activeRawController = null;
            var reading = _activeGamepad.GetCurrentReading();
            state = new ControllerState(
                true,
                0,
                (uint)(reading.Timestamp & uint.MaxValue),
                "Windows Gaming Input",
                MapButtons(reading.Buttons),
                reading.LeftThumbstickX,
                reading.LeftThumbstickY,
                reading.RightThumbstickX,
                reading.RightThumbstickY,
                reading.LeftTrigger,
                reading.RightTrigger);
            return true;
        }
        catch
        {
            _activeGamepad = null;
            state = ControllerState.Disconnected;
            return false;
        }
    }

    private bool TryGetRawController(out ControllerState state)
    {
        try
        {
            if (
                _activeRawController is null ||
                !RawGameController.RawGameControllers.Contains(
                    _activeRawController))
            {
                var controllers =
                    RawGameController.RawGameControllers;
                var selected = controllers.FirstOrDefault(controller =>
                    _controllerProfiles.TryResolveKnown(
                        CreateIdentity(controller, "Raw HID"),
                        out _)) ??
                    controllers.FirstOrDefault();
                if (!ReferenceEquals(selected, _activeRawController))
                {
                    _activeRawController = selected;
                    if (selected is not null)
                    {
                        BeginIdentityCycle();
                    }
                }
            }

            if (_activeRawController is null)
            {
                state = ControllerState.Disconnected;
                return false;
            }

            _activeGamepad = null;
            var buttons = new bool[_activeRawController.ButtonCount];
            var switches = new GameControllerSwitchPosition[
                _activeRawController.SwitchCount];
            var axes = new double[_activeRawController.AxisCount];
            var timestamp = _activeRawController.GetCurrentReading(
                buttons,
                switches,
                axes);
            var axesAtRawZero =
                axes.Take(Math.Min(4, axes.Length))
                    .All(value => Math.Abs(value) < 0.01);
            var identity = CreateIdentity(
                _activeRawController,
                "Raw HID");
            var profile = _controllerProfiles.Resolve(identity);
            var mapping =
                profile.RawMapping ??
                _controllerProfiles.Fallback.RawMapping ??
                throw new InvalidOperationException(
                    "The fallback controller profile must define a raw mapping.");
            var mappedButtons = MapRawButtons(buttons, mapping);

            state = new ControllerState(
                true,
                0,
                (uint)(timestamp & uint.MaxValue),
                $"Raw HID 0x{_activeRawController.HardwareVendorId:X4}",
                mappedButtons,
                axesAtRawZero
                    ? 0
                    : ReadRawAxis(axes, mapping.LeftXIndex),
                axesAtRawZero
                    ? 0
                    : -ReadRawAxis(axes, mapping.LeftYIndex),
                axesAtRawZero
                    ? 0
                    : ReadRawAxis(axes, mapping.RightXIndex),
                axesAtRawZero
                    ? 0
                    : -ReadRawAxis(axes, mapping.RightYIndex),
                ReadRawTrigger(axes, mapping.LeftTriggerIndex),
                ReadRawTrigger(axes, mapping.RightTriggerIndex));
            return true;
        }
        catch
        {
            _activeRawController = null;
            state = ControllerState.Disconnected;
            return false;
        }
    }

    private void BeginIdentityCycle()
    {
        _identityRefreshPending = true;
        _identityResolved = false;
        _identityResolveAttempts = 0;
        _nextIdentityResolveAt = 0;
    }

    private DeviceIdentity? TryRefreshIdentity(string backend)
    {
        var now = Environment.TickCount64;
        if (
            !_identityRefreshPending &&
            (
                _identityResolved ||
                _identityResolveAttempts >= IdentityResolveAttemptLimit ||
                now < _nextIdentityResolveAt
            ))
        {
            return null;
        }

        var identity = ResolveIdentity(backend);
        _identityRefreshPending = false;
        _identityResolved = HasHardwareIdentity(identity);
        _identityResolveAttempts++;
        _nextIdentityResolveAt = now + IdentityRetryDelayMs;
        return identity;
    }

    private DeviceIdentity EndIdentityCycle()
    {
        _identityRefreshPending = false;
        _identityResolved = false;
        _identityResolveAttempts = 0;
        _nextIdentityResolveAt = 0;
        return DisconnectedIdentity;
    }

    private DeviceIdentity ResolveIdentity(string backend)
    {
        try
        {
            if (_activeUserIndex is not null)
            {
                return ResolveXInputIdentity(backend);
            }

            if (_activeGamepad is not null)
            {
                var raw = RawGameController.FromGameController(
                    _activeGamepad);
                return raw is null
                    ? UnknownIdentity(backend)
                    : CreateIdentity(raw, backend);
            }

            return _activeRawController is null
                ? UnknownIdentity(backend)
                : CreateIdentity(_activeRawController, backend);
        }
        catch
        {
            return UnknownIdentity(backend);
        }
    }

    private static DeviceIdentity ResolveXInputIdentity(string backend)
    {
        // XInput does not expose VID/PID. Only correlate identity when both
        // sides are unambiguous; otherwise retain an unknown XInput identity.
        var connectedXInputCount = 0;
        for (uint index = 0; index < 4; index++)
        {
            if (XInputNative.TryGetState(index, out _))
            {
                connectedXInputCount++;
            }
        }

        if (connectedXInputCount != 1)
        {
            return UnknownIdentity(backend);
        }

        var gamepads = Gamepad.Gamepads;
        if (gamepads.Count == 1)
        {
            var raw = RawGameController.FromGameController(gamepads[0]);
            if (raw is not null)
            {
                return CreateIdentity(raw, backend);
            }
        }
        else if (gamepads.Count > 1)
        {
            return UnknownIdentity(backend);
        }

        var rawControllers = RawGameController.RawGameControllers;
        return rawControllers.Count == 1
            ? CreateIdentity(rawControllers[0], backend)
            : UnknownIdentity(backend);
    }

    private static DeviceIdentity CreateIdentity(
        RawGameController controller,
        string backend)
    {
        return new DeviceIdentity(
            KnownId(controller.HardwareVendorId),
            KnownId(controller.HardwareProductId),
            controller.DisplayName,
            backend);
    }

    private static DeviceIdentity UnknownIdentity(string backend)
    {
        return new DeviceIdentity(null, null, null, backend);
    }

    private static ushort? KnownId(ushort value)
    {
        return value == 0 ? null : value;
    }

    private static bool HasHardwareIdentity(DeviceIdentity identity)
    {
        return
            identity.Vid is not null ||
            identity.Pid is not null ||
            !string.IsNullOrWhiteSpace(identity.RawName);
    }

    private static ControllerButtons MapButtons(GamepadButtons buttons)
    {
        var result = ControllerButtons.None;
        if (buttons.HasFlag(GamepadButtons.A))
            result |= ControllerButtons.A;
        if (buttons.HasFlag(GamepadButtons.B))
            result |= ControllerButtons.B;
        if (buttons.HasFlag(GamepadButtons.X))
            result |= ControllerButtons.X;
        if (buttons.HasFlag(GamepadButtons.Y))
            result |= ControllerButtons.Y;
        if (buttons.HasFlag(GamepadButtons.Menu))
            result |= ControllerButtons.Start;
        if (buttons.HasFlag(GamepadButtons.View))
            result |= ControllerButtons.Back;
        if (buttons.HasFlag(GamepadButtons.LeftThumbstick))
            result |= ControllerButtons.LeftThumb;
        if (buttons.HasFlag(GamepadButtons.RightThumbstick))
            result |= ControllerButtons.RightThumb;
        if (buttons.HasFlag(GamepadButtons.LeftShoulder))
            result |= ControllerButtons.LeftShoulder;
        if (buttons.HasFlag(GamepadButtons.RightShoulder))
            result |= ControllerButtons.RightShoulder;
        if (buttons.HasFlag(GamepadButtons.DPadUp))
            result |= ControllerButtons.DPadUp;
        if (buttons.HasFlag(GamepadButtons.DPadDown))
            result |= ControllerButtons.DPadDown;
        if (buttons.HasFlag(GamepadButtons.DPadLeft))
            result |= ControllerButtons.DPadLeft;
        if (buttons.HasFlag(GamepadButtons.DPadRight))
            result |= ControllerButtons.DPadRight;
        return result;
    }

    private static ControllerButtons MapRawButtons(
        bool[] buttons,
        RawMapping mapping)
    {
        var result = ControllerButtons.None;
        foreach (var pair in mapping.ButtonIndices)
        {
            if (!buttons.ElementAtOrDefault(pair.Key))
            {
                continue;
            }

            result |= pair.Value switch
            {
                LogicalInput.FaceSouth => ControllerButtons.A,
                LogicalInput.FaceEast => ControllerButtons.B,
                LogicalInput.FaceWest => ControllerButtons.X,
                LogicalInput.FaceNorth => ControllerButtons.Y,
                LogicalInput.View => ControllerButtons.Back,
                LogicalInput.Menu => ControllerButtons.Start,
                LogicalInput.LeftStickPress =>
                    ControllerButtons.LeftThumb,
                LogicalInput.RightStickPress =>
                    ControllerButtons.RightThumb,
                LogicalInput.LeftShoulder =>
                    ControllerButtons.LeftShoulder,
                LogicalInput.RightShoulder =>
                    ControllerButtons.RightShoulder,
                LogicalInput.DPadUp => ControllerButtons.DPadUp,
                LogicalInput.DPadDown => ControllerButtons.DPadDown,
                LogicalInput.DPadLeft => ControllerButtons.DPadLeft,
                LogicalInput.DPadRight => ControllerButtons.DPadRight,
                _ => ControllerButtons.None,
            };
        }

        return result;
    }

    private static double NormalizeRawAxis(double value)
    {
        return Math.Clamp((value - 0.5) * 2.0, -1, 1);
    }

    private static double ReadRawAxis(double[] axes, int index)
    {
        return index < axes.Length ? NormalizeRawAxis(axes[index]) : 0;
    }

    private static double ReadRawTrigger(
        double[] axes,
        int? index)
    {
        return index is int value && value < axes.Length
            ? Math.Clamp(axes[value], 0, 1)
            : 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        if (_activeUserIndex is uint index)
        {
            XInputNative.SetVibration(index, 0, 0);
        }

        if (_activeGamepad is not null)
        {
            _activeGamepad.Vibration = new GamepadVibration();
        }
    }
}
