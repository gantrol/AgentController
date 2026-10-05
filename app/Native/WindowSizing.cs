using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexController.Native;

internal static partial class WindowSizing
{
    private const int GwlStyle = -16;
    private const int WsMaximizeBox = 0x00010000;
    private const int WmSysCommand = 0x0112;
    private const int ScMaximize = 0xF030;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    public static void DisableMaximize(HwndSource source)
    {
        // Keep the sizing frame and minimize command while removing the
        // maximize capability advertised to the Windows shell.
        var style = GetWindowLong(source.Handle, GwlStyle);
        if (SetWindowLong(source.Handle, GwlStyle, style & ~WsMaximizeBox) == 0 &&
            Marshal.GetLastPInvokeError() is var error && error != 0)
        {
            throw new Win32Exception(error);
        }

        if (!SetWindowPos(source.Handle, nint.Zero, 0, 0, 0, 0,
                SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        source.AddHook(BlockMaximizeCommand);
    }

    private static nint BlockMaximizeCommand(
        nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmSysCommand && (wParam.ToInt64() & 0xFFF0) == ScMaximize)
        {
            handled = true;
        }

        return nint.Zero;
    }

    // GWL_STYLE is a 32-bit value on both 32-bit and 64-bit Windows.
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static partial int SetWindowLong(nint window, int index, int value);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(
        nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
}
