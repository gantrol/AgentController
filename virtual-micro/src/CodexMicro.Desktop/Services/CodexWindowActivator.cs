using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexMicro.Desktop.Services;

internal static class CodexWindowActivator
{
    private const int SwRestore = 9;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const uint GwOwner = 4;

    public static bool TryActivate(string? packageRoot)
    {
        var candidates = FindCandidates(packageRoot);
        var candidate = candidates
            .OrderByDescending(ScoreCandidate)
            .FirstOrDefault();
        if (candidate.Handle == IntPtr.Zero)
        {
            return false;
        }

        if (
            GetForegroundWindow() == candidate.Handle &&
            IsWindowVisible(candidate.Handle) &&
            !IsIconic(candidate.Handle))
        {
            return true;
        }

        // A Codex build can own both the real application window (currently
        // titled "ChatGPT") and a small "Codex" tool window.  Always raise the
        // selected main window in the normal, non-topmost Z-order band.
        _ = AllowSetForegroundWindow(candidate.ProcessId);
        if (IsIconic(candidate.Handle))
        {
            _ = ShowWindow(candidate.Handle, SwRestore);
        }

        var currentThread = GetCurrentThreadId();
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(
            candidate.Handle,
            out _);

        var attachedForeground = AttachIfNeeded(
            currentThread,
            foregroundThread);
        var attachedTarget = AttachIfNeeded(currentThread, targetThread);
        try
        {
            _ = BringWindowToTop(candidate.Handle);
            _ = SetForegroundWindow(candidate.Handle);
            _ = SetActiveWindow(candidate.Handle);
            _ = SetFocus(candidate.Handle);
        }
        finally
        {
            DetachIfNeeded(currentThread, targetThread, attachedTarget);
            DetachIfNeeded(
                currentThread,
                foregroundThread,
                attachedForeground);
        }

        if (GetForegroundWindow() != candidate.Handle)
        {
            // Keep Electron's existing maximize/fullscreen state intact. The
            // FALSE form raises the window without emulating an Alt+Tab.
            SwitchToThisWindow(candidate.Handle, false);
        }

        return IsWindowVisible(candidate.Handle) &&
            !IsIconic(candidate.Handle) &&
            GetForegroundWindow() == candidate.Handle;
    }

    private static List<WindowCandidate> FindCandidates(string? packageRoot)
    {
        var candidates = new List<WindowCandidate>();
        _ = EnumWindows((handle, state) =>
        {
            _ = state;
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || !IsCodexProcess(processId, packageRoot))
            {
                return true;
            }

            var title = new StringBuilder(
                Math.Max(GetWindowTextLength(handle) + 1, 2));
            _ = GetWindowText(handle, title, title.Capacity);

            var className = new StringBuilder(256);
            _ = GetClassName(handle, className, className.Capacity);

            _ = GetWindowRect(handle, out var rect);
            var width = Math.Max(0L, (long)rect.Right - rect.Left);
            var height = Math.Max(0L, (long)rect.Bottom - rect.Top);
            var exStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            candidates.Add(new WindowCandidate(
                handle,
                processId,
                title.ToString(),
                className.ToString(),
                GetWindow(handle, GwOwner) != IntPtr.Zero,
                (exStyle & WsExToolWindow) != 0,
                width * height));
            return true;
        }, IntPtr.Zero);
        return candidates;
    }

    private static long ScoreCandidate(WindowCandidate candidate)
    {
        var score = Math.Min(candidate.Area, 9_999_999_999L);
        if (!candidate.IsToolWindow)
        {
            score += 1_000_000_000_000L;
        }

        if (!candidate.HasOwner)
        {
            score += 100_000_000_000L;
        }

        if (candidate.ClassName.Equals(
            "Chrome_WidgetWin_1",
            StringComparison.Ordinal))
        {
            score += 10_000_000_000L;
        }

        if (candidate.Title.Equals(
            "ChatGPT",
            StringComparison.OrdinalIgnoreCase))
        {
            score += 1_000_000_000L;
        }

        return score;
    }

    private static bool AttachIfNeeded(uint currentThread, uint otherThread) =>
        otherThread != 0 &&
        otherThread != currentThread &&
        AttachThreadInput(currentThread, otherThread, true);

    private static void DetachIfNeeded(
        uint currentThread,
        uint otherThread,
        bool attached)
    {
        if (attached)
        {
            _ = AttachThreadInput(currentThread, otherThread, false);
        }
    }

    private static bool IsCodexProcess(uint processId, string? packageRoot)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var path = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(packageRoot) &&
                path.StartsWith(
                    packageRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return path.Contains(
                @"\WindowsApps\OpenAI.Codex_",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private readonly record struct WindowCandidate(
        IntPtr Handle,
        uint ProcessId,
        string Title,
        string ClassName,
        bool HasOwner,
        bool IsToolWindow,
        long Area);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        IntPtr windowHandle,
        StringBuilder text,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out WindowRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(
        IntPtr windowHandle,
        int index);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(
        IntPtr windowHandle,
        uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(
        uint attachThread,
        uint attachToThread,
        [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(
        IntPtr windowHandle,
        [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
