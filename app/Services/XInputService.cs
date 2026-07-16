using CodexController.Models;
using CodexController.Native;
using Windows.Gaming.Input;

namespace CodexController.Services;

public sealed class XInputService : IDisposable
{
    private const int DisconnectDebounceMs = 320;

    private readonly object _sync = new();
    private System.Threading.Timer? _timer;
    private ControllerState _lastState = ControllerState.Disconnected;
    private uint? _activeUserIndex;
    private Gamepad? _activeGamepad;
    private RawGameController? _activeRawController;
    private long _disconnectObservedAt;
    private int _polling;
    private bool _disposed;

    public event EventHandler<ControllerState>? StateChanged;

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
                Math.Abs(next.RightTrigger - _lastState.RightTrigger) > 0.008;
            _lastState = next;
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
                _activeGamepad = Gamepad.Gamepads.FirstOrDefault();
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
                _activeRawController = RawGameController.RawGameControllers
                    .FirstOrDefault(controller =>
                        controller.HardwareVendorId == 0x2DC8) ??
                    RawGameController.RawGameControllers.FirstOrDefault();
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

            var mappedButtons = ControllerButtons.None;
            if (buttons.ElementAtOrDefault(0))
                mappedButtons |= ControllerButtons.A;
            if (buttons.ElementAtOrDefault(1))
                mappedButtons |= ControllerButtons.B;
            if (buttons.ElementAtOrDefault(2))
                mappedButtons |= ControllerButtons.X;
            if (buttons.ElementAtOrDefault(3))
                mappedButtons |= ControllerButtons.Y;
            if (buttons.ElementAtOrDefault(8))
                mappedButtons |= ControllerButtons.Back;
            if (buttons.ElementAtOrDefault(9))
                mappedButtons |= ControllerButtons.Start;
            if (buttons.ElementAtOrDefault(10))
                mappedButtons |= ControllerButtons.LeftThumb;
            if (buttons.ElementAtOrDefault(11))
                mappedButtons |= ControllerButtons.RightThumb;

            state = new ControllerState(
                true,
                0,
                (uint)(timestamp & uint.MaxValue),
                $"Raw HID 0x{_activeRawController.HardwareVendorId:X4}",
                mappedButtons,
                axesAtRawZero ? 0 : ReadRawAxis(axes, 0),
                axesAtRawZero ? 0 : -ReadRawAxis(axes, 1),
                axesAtRawZero ? 0 : ReadRawAxis(axes, 2),
                axesAtRawZero ? 0 : -ReadRawAxis(axes, 3),
                ReadRawTrigger(axes, 4),
                ReadRawTrigger(axes, 5));
            return true;
        }
        catch
        {
            _activeRawController = null;
            state = ControllerState.Disconnected;
            return false;
        }
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

    private static double NormalizeRawAxis(double value)
    {
        return Math.Clamp((value - 0.5) * 2.0, -1, 1);
    }

    private static double ReadRawAxis(double[] axes, int index)
    {
        return index < axes.Length ? NormalizeRawAxis(axes[index]) : 0;
    }

    private static double ReadRawTrigger(double[] axes, int index)
    {
        return index < axes.Length ? Math.Clamp(axes[index], 0, 1) : 0;
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
