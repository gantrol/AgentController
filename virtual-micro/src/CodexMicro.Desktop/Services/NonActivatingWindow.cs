using System.Runtime.InteropServices;

namespace CodexMicro.Desktop.Services;

internal static class NonActivatingWindow
{
    internal const int WmMouseActivate = 0x0021;
    internal const int MaNoActivate = 3;
    internal const long WsExNoActivate = 0x08000000L;

    private const int GwlExStyle = -20;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly IntPtr HwndTopmost = new(-1);

    public static bool TryHandleMessage(
        int message,
        ref bool handled,
        out IntPtr result)
    {
        if (message != WmMouseActivate)
        {
            result = IntPtr.Zero;
            return false;
        }

        handled = true;
        result = new IntPtr(MaNoActivate);
        return true;
    }

    public static void ApplyNoActivateStyle(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var current = GetWindowLongPtr(windowHandle, GwlExStyle);
        var updated = AddNoActivateStyle(current.ToInt64());
        if (updated != current.ToInt64())
        {
            _ = SetWindowLongPtr(
                windowHandle,
                GwlExStyle,
                new IntPtr(updated));
        }
    }

    internal static long AddNoActivateStyle(long extendedStyle) =>
        extendedStyle | WsExNoActivate;

    public static void ShowWithoutActivation(IntPtr windowHandle, bool topmost)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        _ = SetWindowPos(
            windowHandle,
            topmost ? HwndTopmost : HwndTop,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private static IntPtr GetWindowLongPtr(IntPtr windowHandle, int index) =>
        IntPtr.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new IntPtr(GetWindowLong32(windowHandle, index));

    private static IntPtr SetWindowLongPtr(
        IntPtr windowHandle,
        int index,
        IntPtr value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new IntPtr(SetWindowLong32(
                windowHandle,
                index,
                value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(
        IntPtr windowHandle,
        int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(
        IntPtr windowHandle,
        int index,
        int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(
        IntPtr windowHandle,
        int index,
        IntPtr value);
}
