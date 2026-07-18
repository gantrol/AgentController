using System.Runtime.InteropServices;
using System.Windows;

namespace CodexMicro.Desktop.Services;

internal enum RoutedDialPointerAction
{
    Pressed,
    Moved,
    Released,
}

internal readonly record struct RoutedDialPointerInput(
    RoutedDialPointerAction Action,
    Point ScreenPoint);

internal sealed class InactiveDialInputRouter : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;

    private readonly Func<Point, int, bool> _routeWheel;
    private readonly Func<RoutedDialPointerInput, bool> _routePointer;
    private readonly HookProcedure _hookProcedure;
    private IntPtr _hook;
    private bool _pointerIntercepted;
    private bool _disposed;

    public bool IsStarted => _hook != IntPtr.Zero;

    public int LastError { get; private set; }

    public InactiveDialInputRouter(
        Func<Point, int, bool> routeWheel,
        Func<RoutedDialPointerInput, bool> routePointer)
    {
        ArgumentNullException.ThrowIfNull(routeWheel);
        ArgumentNullException.ThrowIfNull(routePointer);
        _routeWheel = routeWheel;
        _routePointer = routePointer;
        _hookProcedure = HookCallback;
    }

    public bool Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_hook != IntPtr.Zero)
        {
            return true;
        }

        _hook = SetWindowsHookEx(
            WhMouseLl,
            _hookProcedure,
            GetModuleHandle(null),
            0);
        LastError = _hook == IntPtr.Zero
            ? Marshal.GetLastWin32Error()
            : 0;
        return _hook != IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pointerIntercepted = false;
        if (_hook != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(
        int code,
        IntPtr wordParameter,
        IntPtr longParameter)
    {
        try
        {
            if (code >= 0)
            {
                var message = wordParameter.ToInt32();
                var data = Marshal.PtrToStructure<LowLevelMouseInput>(longParameter);
                var point = new Point(data.Point.X, data.Point.Y);

                if (message == WmMouseWheel)
                {
                    var delta = unchecked((short)(data.MouseData >> 16));
                    if (delta != 0 && _routeWheel(point, delta))
                    {
                        return new IntPtr(1);
                    }
                }
                else if (message == WmLeftButtonDown)
                {
                    if (_routePointer(new RoutedDialPointerInput(
                        RoutedDialPointerAction.Pressed,
                        point)))
                    {
                        // The physical click must not reach another HWND. An
                        // open Codex popup treats it as an outside click and
                        // closes before the official ENC event is processed.
                        _pointerIntercepted = true;
                        return new IntPtr(1);
                    }
                }
                else if (_pointerIntercepted && message == WmMouseMove)
                {
                    _ = _routePointer(new RoutedDialPointerInput(
                        RoutedDialPointerAction.Moved,
                        point));
                }
                else if (_pointerIntercepted && message == WmLeftButtonUp)
                {
                    _pointerIntercepted = false;
                    _ = _routePointer(new RoutedDialPointerInput(
                        RoutedDialPointerAction.Released,
                        point));
                    return new IntPtr(1);
                }
            }
        }
        catch (Exception)
        {
            // Native input hooks must never take down the UI thread. If the
            // simulator cannot route this packet, let Windows deliver it to
            // the next hook/window instead.
            _pointerIntercepted = false;
        }

        return CallNextHookEx(
            _hook,
            code,
            wordParameter,
            longParameter);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LowLevelMouseInput
    {
        public readonly NativePoint Point;
        public readonly uint MouseData;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly UIntPtr ExtraInfo;
    }

    private delegate IntPtr HookProcedure(
        int code,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookProcedure hookProcedure,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wordParameter,
        IntPtr longParameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
