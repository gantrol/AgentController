using System.Runtime.InteropServices;
using AgentController.Platform.Controllers;

namespace AgentController.Platform.MacOS.Controllers;

/// <summary>
/// Polling Apple Game Controller backend. Polling is intentional for the
/// Foundation Preview: it observes connection, disconnection, current-device
/// changes, and input without keeping native callback blocks alive across the
/// managed boundary.
/// </summary>
public sealed class MacGameControllerBackend : IControllerInputSource
{
    private MacGameControllerInterop? _interop;
    private bool _disposed;

    public MacGameControllerBackend()
    {
        if (MacPlatformSupport.Current != MacPlatformAvailability.Supported)
        {
            LastError = OperatingSystem.IsMacOS()
                ? $"Requires {MacPlatformSupport.MinimumVersionDisplayName} or later."
                : "Apple Game Controller is available only on macOS.";
            return;
        }

        try
        {
            _interop = new MacGameControllerInterop();
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or
                EntryPointNotFoundException or
                InvalidOperationException)
        {
            LastError = exception.Message;
        }
    }

    public string BackendName => "Apple Game Controller";

    public bool IsAvailable => !_disposed && _interop is not null;

    public bool SupportsBackgroundEvents =>
        IsAvailable && _interop?.MonitorsBackgroundEvents == true;

    public string? LastError { get; private set; }

    public IReadOnlyList<ControllerInputSnapshot> Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_interop is null)
        {
            return [];
        }

        try
        {
            var controllers = _interop.Poll();
            LastError = null;
            return controllers;
        }
        catch (Exception exception) when (
            exception is ExternalException or InvalidOperationException)
        {
            LastError = exception.Message;
            return [];
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _interop?.Dispose();
        _interop = null;
    }
}

internal sealed class MacGameControllerInterop : IDisposable
{
    private const string GameControllerFramework =
        "/System/Library/Frameworks/GameController.framework/GameController";
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private readonly nint _frameworkHandle;
    private readonly nint _controllerClass;
    private readonly MacControllerIdentityMap _identities = new();
    private bool _disposed;

    internal MacGameControllerInterop()
    {
        _frameworkHandle = NativeLibrary.Load(GameControllerFramework);
        _controllerClass = objc_getClass("GCController");
        if (_controllerClass == 0)
        {
            NativeLibrary.Free(_frameworkHandle);
            throw new InvalidOperationException(
                "GameController.framework did not expose GCController.");
        }

        if (RespondsTo(_controllerClass, Selectors.SetBackgroundEvents) &&
            RespondsTo(_controllerClass, Selectors.BackgroundEvents))
        {
            SendVoidBool(
                _controllerClass,
                Selectors.SetBackgroundEvents,
                value: true);
            MonitorsBackgroundEvents = SendBool(
                _controllerClass,
                Selectors.BackgroundEvents);
        }
    }

    internal bool MonitorsBackgroundEvents { get; }

    internal IReadOnlyList<ControllerInputSnapshot> Poll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pool = objc_autoreleasePoolPush();
        try
        {
            var array = SendIntPtr(_controllerClass, Selectors.Controllers);
            if (array == 0)
            {
                _identities.RetainOnly(Array.Empty<nint>());
                return [];
            }

            var count = checked((int)SendNUInt(array, Selectors.Count));
            if (count == 0)
            {
                _identities.RetainOnly(Array.Empty<nint>());
                return [];
            }

            var current = GetOptional(
                _controllerClass,
                Selectors.Current);
            var snapshots = new List<ControllerInputSnapshot>(count);
            var connectedControllers = new HashSet<nint>();
            for (var index = 0; index < count; index++)
            {
                var controller = SendIntPtrNUInt(
                    array,
                    Selectors.ObjectAtIndex,
                    (nuint)index);
                if (controller == 0)
                {
                    continue;
                }

                connectedControllers.Add(controller);
                var profile = GetOptional(
                    controller,
                    Selectors.ExtendedGamepad);
                if (profile == 0)
                {
                    continue;
                }

                snapshots.Add(ReadController(
                    _identities.GetOrAdd(controller),
                    controller,
                    profile,
                    current));
            }

            _identities.RetainOnly(connectedControllers);
            return snapshots;
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    public void Dispose()
    {
        // GameController.framework is a process framework. Unloading it while
        // Objective-C may retain class metadata can invalidate later calls, so
        // the handle intentionally remains loaded until process exit.
        _disposed = true;
    }

    private static ControllerInputSnapshot ReadController(
        string identity,
        nint controller,
        nint profile,
        nint current)
    {
        var vendorName = ReadString(
            GetOptional(controller, Selectors.VendorName));
        var productCategory = ReadString(
            GetOptional(controller, Selectors.ProductCategory));
        var displayName = FirstNonEmpty(
            vendorName,
            productCategory,
            identity);

        var features =
            ControllerFeatures.ExtendedGamepad |
            ControllerFeatures.BackgroundEvents |
            ControllerFeatures.SystemRemapping;
        float? batteryLevel = null;
        var battery = GetOptional(controller, Selectors.Battery);
        if (battery != 0)
        {
            var rawBattery = SendFloat(battery, Selectors.BatteryLevel);
            if (float.IsFinite(rawBattery) && rawBattery >= 0)
            {
                batteryLevel = Math.Clamp(rawBattery, 0f, 1f);
                features |= ControllerFeatures.Battery;
            }
        }

        if (GetOptional(controller, Selectors.Haptics) != 0)
        {
            features |= ControllerFeatures.Haptics;
        }

        if (GetOptional(controller, Selectors.Light) != 0)
        {
            features |= ControllerFeatures.Light;
        }

        var leftStick = ReadStick(
            GetOptional(profile, Selectors.LeftThumbstick));
        var rightStick = ReadStick(
            GetOptional(profile, Selectors.RightThumbstick));
        var dpad = ReadStick(GetOptional(profile, Selectors.Dpad));
        var buttons = ControllerButtons.None;
        AddButton(
            ref buttons,
            profile,
            Selectors.ButtonA,
            ControllerButtons.South);
        AddButton(
            ref buttons,
            profile,
            Selectors.ButtonB,
            ControllerButtons.East);
        AddButton(
            ref buttons,
            profile,
            Selectors.ButtonX,
            ControllerButtons.West);
        AddButton(
            ref buttons,
            profile,
            Selectors.ButtonY,
            ControllerButtons.North);
        AddButton(
            ref buttons,
            profile,
            Selectors.LeftShoulder,
            ControllerButtons.LeftShoulder);
        AddButton(
            ref buttons,
            profile,
            Selectors.RightShoulder,
            ControllerButtons.RightShoulder);
        AddButton(
            ref buttons,
            profile,
            Selectors.ButtonMenu,
            ControllerButtons.Menu);
        AddButton(
            ref buttons,
            profile,
            Selectors.ButtonOptions,
            ControllerButtons.Options);
        AddButton(
            ref buttons,
            profile,
            Selectors.ButtonHome,
            ControllerButtons.Home);
        AddButton(
            ref buttons,
            profile,
            Selectors.LeftThumbstickButton,
            ControllerButtons.LeftStick);
        AddButton(
            ref buttons,
            profile,
            Selectors.RightThumbstickButton,
            ControllerButtons.RightStick);

        if (dpad.Y > 0.5f)
        {
            buttons |= ControllerButtons.DpadUp;
        }

        if (dpad.X > 0.5f)
        {
            buttons |= ControllerButtons.DpadRight;
        }

        if (dpad.Y < -0.5f)
        {
            buttons |= ControllerButtons.DpadDown;
        }

        if (dpad.X < -0.5f)
        {
            buttons |= ControllerButtons.DpadLeft;
        }

        return new ControllerInputSnapshot(
            identity,
            displayName,
            productCategory ?? "Extended Gamepad",
            buttons,
            leftStick,
            rightStick,
            ReadButtonValue(profile, Selectors.LeftTrigger),
            ReadButtonValue(profile, Selectors.RightTrigger),
            batteryLevel,
            features,
            controller == current,
            IsIdentityStable: false);
    }

    private static StickPosition ReadStick(nint directionPad)
    {
        if (directionPad == 0)
        {
            return default;
        }

        var xAxis = GetOptional(directionPad, Selectors.XAxis);
        var yAxis = GetOptional(directionPad, Selectors.YAxis);
        return new StickPosition(
                xAxis == 0 ? 0 : SendFloat(xAxis, Selectors.Value),
                yAxis == 0 ? 0 : SendFloat(yAxis, Selectors.Value))
            .Clamped();
    }

    private static void AddButton(
        ref ControllerButtons buttons,
        nint profile,
        nint selector,
        ControllerButtons flag)
    {
        var button = GetOptional(profile, selector);
        if (button != 0 && SendBool(button, Selectors.IsPressed))
        {
            buttons |= flag;
        }
    }

    private static float ReadButtonValue(nint profile, nint selector)
    {
        var button = GetOptional(profile, selector);
        return button == 0
            ? 0
            : Math.Clamp(SendFloat(button, Selectors.Value), 0f, 1f);
    }

    private static nint GetOptional(nint receiver, nint selector) =>
        receiver != 0 && RespondsTo(receiver, selector)
            ? SendIntPtr(receiver, selector)
            : 0;

    private static bool RespondsTo(nint receiver, nint selector) =>
        receiver != 0 &&
        SendBoolIntPtr(
            receiver,
            Selectors.RespondsToSelector,
            selector);

    private static string? ReadString(nint value)
    {
        if (value == 0)
        {
            return null;
        }

        var bytes = SendIntPtr(value, Selectors.Utf8String);
        return bytes == 0
            ? null
            : Marshal.PtrToStringUTF8(bytes);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!;

    private static class Selectors
    {
        internal static readonly nint Controllers = Sel("controllers");
        internal static readonly nint Current = Sel("current");
        internal static readonly nint SetBackgroundEvents =
            Sel("setShouldMonitorBackgroundEvents:");
        internal static readonly nint BackgroundEvents =
            Sel("shouldMonitorBackgroundEvents");
        internal static readonly nint Count = Sel("count");
        internal static readonly nint ObjectAtIndex = Sel("objectAtIndex:");
        internal static readonly nint RespondsToSelector =
            Sel("respondsToSelector:");
        internal static readonly nint Utf8String = Sel("UTF8String");
        internal static readonly nint VendorName = Sel("vendorName");
        internal static readonly nint ProductCategory = Sel("productCategory");
        internal static readonly nint ExtendedGamepad = Sel("extendedGamepad");
        internal static readonly nint Battery = Sel("battery");
        internal static readonly nint BatteryLevel = Sel("batteryLevel");
        internal static readonly nint Haptics = Sel("haptics");
        internal static readonly nint Light = Sel("light");
        internal static readonly nint LeftThumbstick = Sel("leftThumbstick");
        internal static readonly nint RightThumbstick = Sel("rightThumbstick");
        internal static readonly nint Dpad = Sel("dpad");
        internal static readonly nint XAxis = Sel("xAxis");
        internal static readonly nint YAxis = Sel("yAxis");
        internal static readonly nint Value = Sel("value");
        internal static readonly nint IsPressed = Sel("isPressed");
        internal static readonly nint ButtonA = Sel("buttonA");
        internal static readonly nint ButtonB = Sel("buttonB");
        internal static readonly nint ButtonX = Sel("buttonX");
        internal static readonly nint ButtonY = Sel("buttonY");
        internal static readonly nint LeftShoulder = Sel("leftShoulder");
        internal static readonly nint RightShoulder = Sel("rightShoulder");
        internal static readonly nint LeftTrigger = Sel("leftTrigger");
        internal static readonly nint RightTrigger = Sel("rightTrigger");
        internal static readonly nint ButtonMenu = Sel("buttonMenu");
        internal static readonly nint ButtonOptions = Sel("buttonOptions");
        internal static readonly nint ButtonHome = Sel("buttonHome");
        internal static readonly nint LeftThumbstickButton =
            Sel("leftThumbstickButton");
        internal static readonly nint RightThumbstickButton =
            Sel("rightThumbstickButton");

        private static nint Sel(string name) => sel_registerName(name);
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_autoreleasePoolPush();

    [DllImport(ObjectiveCLibrary)]
    private static extern void objc_autoreleasePoolPop(nint pool);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIntPtr(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint SendIntPtrNUInt(
        nint receiver,
        nint selector,
        nuint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nuint SendNUInt(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern float SendFloat(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBool(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SendBoolIntPtr(
        nint receiver,
        nint selector,
        nint value);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidBool(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.I1)] bool value);
}
