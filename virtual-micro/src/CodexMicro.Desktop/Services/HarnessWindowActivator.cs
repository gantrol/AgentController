using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Raises a dedicated browser window owned by a Harness. This uses the
/// operating-system window API; it does not synthesize keyboard or pointer
/// input and deliberately ignores unrelated application windows.
/// </summary>
internal static class HarnessWindowActivator
{
    private const int SwRestore = 9;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    internal static IntPtr ActivationFallbackInsertAfter => HwndTop;

    internal static bool IsForeground(
        MicroHarnessDefinition harness,
        int? preferredProcessId = null)
    {
        ArgumentNullException.ThrowIfNull(harness);
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero ||
            !IsWindowVisible(foreground) ||
            IsIconic(foreground))
        {
            return false;
        }

        return FindCandidates(
                TitleFragments(harness),
                preferredProcessId,
                RequiresDedicatedAppWindow(harness))
            .Any(candidate => candidate.Handle == foreground);
    }

    internal static bool TryActivate(
        MicroHarnessDefinition harness,
        int? preferredProcessId = null)
    {
        var candidate = FindCandidates(
                TitleFragments(harness),
                preferredProcessId,
                RequiresDedicatedAppWindow(harness))
            .OrderByDescending(item => item.Score)
            .FirstOrDefault();
        if (candidate.Handle == IntPtr.Zero)
        {
            return false;
        }

        if (GetForegroundWindow() == candidate.Handle &&
            !IsIconic(candidate.Handle))
        {
            return true;
        }

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
        var targetThread = GetWindowThreadProcessId(candidate.Handle, out _);
        var attachedForeground = AttachIfNeeded(currentThread, foregroundThread);
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
            DetachIfNeeded(currentThread, foregroundThread, attachedForeground);
        }

        if (GetForegroundWindow() != candidate.Handle)
        {
            // Chromium may report the short-lived launcher PID instead of the
            // process that owns its real top-level window. Keep the guarded
            // Harness window in the normal Z-order band while bringing it to
            // the top of that band. Temporarily promoting it to HWND_TOPMOST
            // can cover the Micro surface if the matching demotion races or
            // fails. Do not pass SWP_NOACTIVATE here: this branch exists
            // specifically because SetForegroundWindow did not change the
            // real foreground HWND.
            _ = ShowWindowAsync(candidate.Handle, SwRestore);
            _ = SetWindowPos(
                candidate.Handle,
                ActivationFallbackInsertAfter,
                0,
                0,
                0,
                0,
                SwpNoSize | SwpNoMove);
            _ = BringWindowToTop(candidate.Handle);
            _ = SetForegroundWindow(candidate.Handle);
        }

        if (GetForegroundWindow() != candidate.Handle)
        {
            SwitchToThisWindow(candidate.Handle, false);
        }

        return GetForegroundWindow() == candidate.Handle &&
            IsWindowVisible(candidate.Handle) &&
            !IsIconic(candidate.Handle);
    }

    private static IReadOnlyList<string> TitleFragments(
        MicroHarnessDefinition harness) =>
        harness.Id.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
            ? ["DeepSeek Harness", "DeepSeek"]
            : [harness.DisplayName];

    private static IEnumerable<WindowCandidate> FindCandidates(
        IReadOnlyList<string> titleFragments,
        int? preferredProcessId,
        bool dedicatedAppOnly)
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
            if (!IsSupportedBrowser(processId))
            {
                return true;
            }

            var title = new StringBuilder(
                Math.Max(GetWindowTextLength(handle) + 1, 2));
            _ = GetWindowText(handle, title, title.Capacity);
            var text = title.ToString();
            if (dedicatedAppOnly && !IsDedicatedBrowserWindowTitle(text))
            {
                return true;
            }
            var isPreferredProcess = preferredProcessId is > 0 &&
                processId == (uint)preferredProcessId.Value;
            var hasPrimaryTitle = titleFragments.Count > 0 &&
                text.Contains(
                    titleFragments[0],
                    StringComparison.OrdinalIgnoreCase);
            var match = titleFragments
                .Where(fragment => !string.IsNullOrWhiteSpace(fragment))
                .Select(fragment => new
                {
                    Fragment = fragment,
                    Index = text.IndexOf(
                        fragment,
                        StringComparison.OrdinalIgnoreCase),
                })
                .Where(item => item.Index >= 0)
                .OrderByDescending(item => item.Fragment.Length)
                .FirstOrDefault();
            if (!isPreferredProcess && match is null)
            {
                return true;
            }

            _ = GetWindowRect(handle, out var rect);
            var area = Math.Max(0L, (long)rect.Right - rect.Left) *
                Math.Max(0L, (long)rect.Bottom - rect.Top);
            var score = Math.Min(area, 9_999_999_999L) +
                (isPreferredProcess ? 10_000_000_000_000L : 0) +
                (hasPrimaryTitle ? 5_000_000_000_000L : 0) +
                (match?.Index == 0 ? 100_000_000_000L : 0) +
                (match is not null &&
                    text.Equals(match.Fragment, StringComparison.OrdinalIgnoreCase)
                    ? 1_000_000_000_000L
                    : 0);
            candidates.Add(new(handle, processId, score));
            return true;
        }, IntPtr.Zero);
        return candidates;
    }

    private static bool RequiresDedicatedAppWindow(
        MicroHarnessDefinition harness) =>
        harness.Id.Contains("deepseek", StringComparison.OrdinalIgnoreCase);

    internal static bool IsDedicatedBrowserWindowTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return !title.Contains(" - Google Chrome", StringComparison.OrdinalIgnoreCase) &&
            !title.Contains(" - Microsoft Edge", StringComparison.OrdinalIgnoreCase) &&
            !title.Contains(" - Brave", StringComparison.OrdinalIgnoreCase) &&
            !title.Contains(" - Vivaldi", StringComparison.OrdinalIgnoreCase) &&
            !title.Contains(" — Mozilla Firefox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedBrowser(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName is "chrome" or
                "msedge" or
                "brave" or
                "firefox" or
                "vivaldi" or
                "arc";
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                Win32Exception)
        {
            return false;
        }
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

    private readonly record struct WindowCandidate(
        IntPtr Handle,
        uint ProcessId,
        long Score);

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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        IntPtr windowHandle,
        out WindowRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(
        IntPtr windowHandle,
        int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

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
