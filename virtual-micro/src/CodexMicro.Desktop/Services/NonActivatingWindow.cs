using System.Runtime.InteropServices;

namespace CodexMicro.Desktop.Services;

internal static class NonActivatingWindow
{
    internal const int WmMouseActivate = 0x0021;
    internal const int MaNoActivate = 3;

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
}
