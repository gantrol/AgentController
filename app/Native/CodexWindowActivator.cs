using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexController.Native;

internal readonly record struct CodexWindowCandidate(
    nint Handle,
    uint ProcessId,
    string Title,
    string ClassName,
    bool HasOwner,
    bool IsToolWindow,
    long Area);

/// <summary>
/// Finds and raises the real Codex application window. A Codex process can
/// own both its large Electron window and small helper/tool windows, so
/// Process.MainWindowHandle is not a sufficient selection rule.
/// </summary>
internal static class CodexWindowActivator
{
    private const int SwRestore = 9;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const uint GwOwner = 4;

    internal static bool IsForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        return processId != 0 && IsCodexProcess(processId);
    }

    internal static bool TryFindMainWindow(
        out CodexWindowCandidate candidate)
    {
        candidate = RankCandidates(FindCandidates())
            .FirstOrDefault();
        return candidate.Handle != nint.Zero;
    }

    internal static bool TryActivate() =>
        TryActivateCore(advanceCurrentWindow: false);

    internal static bool TryActivateNext() =>
        TryActivateCore(advanceCurrentWindow: true);

    private static bool TryActivateCore(bool advanceCurrentWindow)
    {
        var candidates = RankCandidates(FindCandidates());
        if (candidates.Count == 0)
        {
            return false;
        }

        var foreground = GetForegroundWindow();
        var candidate = SelectCandidate(
            candidates,
            foreground,
            advanceCurrentWindow);
        return ActivateCandidate(candidate, foreground);
    }

    internal static CodexWindowCandidate SelectCandidate(
        IReadOnlyList<CodexWindowCandidate> rankedCandidates,
        nint foreground,
        bool advanceCurrentWindow)
    {
        if (rankedCandidates.Count == 0)
        {
            return default;
        }

        var primary = rankedCandidates[0];
        if (!advanceCurrentWindow)
        {
            return primary;
        }

        var cycle = rankedCandidates
            .Where(candidate =>
                !candidate.IsToolWindow &&
                !candidate.HasOwner)
            .ToArray();
        if (cycle.Length < 2)
        {
            return primary;
        }

        var currentIndex = Array.FindIndex(
            cycle,
            candidate => candidate.Handle == foreground);
        return currentIndex < 0
            ? primary
            : cycle[(currentIndex + 1) % cycle.Length];
    }

    private static IReadOnlyList<CodexWindowCandidate> RankCandidates(
        IEnumerable<CodexWindowCandidate> candidates) =>
        candidates
            .OrderByDescending(ScoreCandidate)
            .ToArray();

    private static bool ActivateCandidate(
        CodexWindowCandidate candidate,
        nint foreground)
    {
        if (foreground == candidate.Handle)
        {
            return true;
        }

        // Ported from the proven virtual-micro activator. The call can arrive
        // on a worker thread, so ensure that thread owns a Win32 message queue
        // before attaching input queues.
        _ = PeekMessage(
            out _,
            nint.Zero,
            0,
            0,
            0);
        _ = AllowSetForegroundWindow(candidate.ProcessId);
        if (IsIconic(candidate.Handle))
        {
            _ = ShowWindow(candidate.Handle, SwRestore);
        }

        var currentThread = GetCurrentThreadId();
        foreground = GetForegroundWindow();
        var foregroundThread = foreground == nint.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(
            candidate.Handle,
            out _);
        if (targetThread == 0)
        {
            return false;
        }

        var attachedForeground = AttachIfNeeded(
            currentThread,
            foregroundThread);
        var attachedTarget = targetThread != foregroundThread &&
            AttachIfNeeded(currentThread, targetThread);
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
            // Keep Electron's maximize/fullscreen state intact. FALSE raises
            // the existing window without emulating Alt+Tab.
            SwitchToThisWindow(candidate.Handle, false);
        }

        return IsWindowVisible(candidate.Handle) &&
            !IsIconic(candidate.Handle) &&
            GetForegroundWindow() == candidate.Handle;
    }

    internal static long ScoreCandidate(CodexWindowCandidate candidate)
    {
        var score = Math.Clamp(candidate.Area, 0, 9_999_999_999L);
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

    private static List<CodexWindowCandidate> FindCandidates()
    {
        var candidates = new List<CodexWindowCandidate>();
        _ = EnumWindows((handle, state) =>
        {
            _ = state;
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);
            if (processId == 0 || !IsCodexProcess(processId))
            {
                return true;
            }

            var title = new StringBuilder(
                Math.Max(GetWindowTextLength(handle) + 1, 2));
            _ = GetWindowText(handle, title, title.Capacity);

            var className = new StringBuilder(256);
            _ = GetClassName(
                handle,
                className,
                className.Capacity);

            _ = GetWindowRect(handle, out var rectangle);
            var width = Math.Max(
                0L,
                (long)rectangle.Right - rectangle.Left);
            var height = Math.Max(
                0L,
                (long)rectangle.Bottom - rectangle.Top);
            var area = width == 0 || height == 0
                ? 0
                : width > long.MaxValue / height
                    ? long.MaxValue
                    : width * height;
            var extendedStyle = GetWindowLongPtr(
                    handle,
                    GwlExStyle)
                .ToInt64();
            candidates.Add(new(
                handle,
                processId,
                title.ToString(),
                className.ToString(),
                GetWindow(handle, GwOwner) != nint.Zero,
                (extendedStyle & WsExToolWindow) != 0,
                area));
            return true;
        }, nint.Zero);
        return candidates;
    }

    private static bool IsCodexProcess(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(
                checked((int)processId));
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path.Contains(
                        @"\WindowsApps\OpenAI.Codex_",
                        StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Preserve the old process-name fallback when Windows denies
                // package-path inspection.
            }

            return process.ProcessName.Equals(
                "ChatGPT",
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                OverflowException or
                System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool AttachIfNeeded(
        uint currentThread,
        uint otherThread) =>
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

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nuint WordParameter;
        public nint LongParameter;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    private delegate bool EnumWindowsCallback(nint handle, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(
        EnumWindowsCallback callback,
        nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(
        nint windowHandle,
        StringBuilder text,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        nint windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint windowHandle,
        out WindowRect rectangle);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(
        nint windowHandle,
        int index);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(
        nint windowHandle,
        uint command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

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
        nint windowHandle,
        [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        nint windowHandle,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);
}
