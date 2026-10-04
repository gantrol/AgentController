using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexMicro.Desktop.Services;

internal static class MicroWindowLayout
{
    internal const double DefaultWidth = 442.5;
    internal const double DefaultHeight = 457.5;
    internal const double MinimumScale = 0.8;
    internal const double MaximumScale = 1.4;

    internal static double NormalizeScale(double value) => double.IsFinite(value)
        ? Math.Clamp(value, MinimumScale, MaximumScale) : 1;

    internal static void DragTitle(Window window, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed) return;
        for (var source = e.OriginalSource as DependencyObject; source is not null && source != window;
             source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is ButtonBase or TextBoxBase or Selector or RangeBase or ScrollViewer) return;
        e.Handled = true;
        window.DragMove();
    }

    internal static Rect WorkArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = MonitorFromWindow(handle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return SystemParameters.WorkArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        return new Rect(info.Work.Left / dpi.DpiScaleX, info.Work.Top / dpi.DpiScaleY,
            (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
            (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY);
    }

    internal static void FitDialog(Window window)
    {
        var work = WorkArea(window);
        window.Width = Math.Min(window.Width, Math.Max(1, work.Width - 24));
        window.Height = Math.Min(window.Height, Math.Max(1, work.Height - 24));
        KeepVisible(window, work);
    }

    internal static void SizeKeypad(Window window, double requestedScale)
    {
        var work = WorkArea(window);
        var fittingScale = Math.Min((work.Width - 24) / DefaultWidth, (work.Height - 24) / DefaultHeight);
        var scale = Math.Max(MinimumScale, Math.Min(NormalizeScale(requestedScale), fittingScale));
        window.Width = DefaultWidth * scale;
        window.Height = DefaultHeight * scale;
        KeepVisible(window, work);
    }

    private static void KeepVisible(Window window, Rect work)
    {
        if (double.IsFinite(window.Left))
            window.Left = Math.Clamp(window.Left, work.Left, Math.Max(work.Left, work.Right - window.Width));
        if (double.IsFinite(window.Top))
            window.Top = Math.Clamp(window.Top, work.Top, Math.Max(work.Top, work.Bottom - window.Height));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
