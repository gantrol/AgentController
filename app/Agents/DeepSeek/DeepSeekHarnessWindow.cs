using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexController.Agents.DeepSeek;

/// <summary>
/// Finds, launches, and raises only the dedicated DeepSeek Harness browser
/// surface. Ordinary DeepSeek tabs are deliberately excluded.
/// </summary>
internal static class DeepSeekHarnessWindow
{
    private const int SwRestore = 9;

    internal static bool IsForeground()
    {
        var foreground = GetForegroundWindow();
        return foreground != nint.Zero &&
            FindCandidates().Any(candidate =>
                candidate.Handle == foreground);
    }

    internal static bool TryActivate(int? preferredProcessId = null)
    {
        var candidate = FindCandidates()
            .OrderByDescending(item =>
                (preferredProcessId is > 0 &&
                    item.ProcessId == preferredProcessId.Value
                        ? 10_000_000_000_000L
                        : 0) +
                item.Area)
            .FirstOrDefault();
        if (candidate.Handle == nint.Zero)
        {
            return false;
        }

        if (GetForegroundWindow() == candidate.Handle &&
            !IsIconic(candidate.Handle))
        {
            return true;
        }

        _ = AllowSetForegroundWindow((uint)candidate.ProcessId);
        if (IsIconic(candidate.Handle))
        {
            _ = ShowWindow(candidate.Handle, SwRestore);
        }

        _ = BringWindowToTop(candidate.Handle);
        _ = SetForegroundWindow(candidate.Handle);
        if (GetForegroundWindow() != candidate.Handle)
        {
            SwitchToThisWindow(candidate.Handle, false);
        }

        return GetForegroundWindow() == candidate.Handle &&
            IsWindowVisible(candidate.Handle) &&
            !IsIconic(candidate.Handle);
    }

    internal static int? TryLaunch(Uri controlEndpoint)
    {
        ArgumentNullException.ThrowIfNull(controlEndpoint);
        var browser = FindAppBrowser();
        if (browser is null)
        {
            return null;
        }

        var surface = new UriBuilder(controlEndpoint)
        {
            Path = "/",
            Query =
                "agentControllerSurface=1&codexMicroSurface=1",
            Fragment = string.Empty,
        }.Uri;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = browser,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal,
                ArgumentList =
                {
                    $"--app={surface.AbsoluteUri}",
                    "--no-first-run",
                    "--no-default-browser-check",
                },
            });
            return process?.Id;
        }
        catch (Exception exception) when (
            exception is Win32Exception or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<WindowCandidate> FindCandidates()
    {
        var candidates = new List<WindowCandidate>();
        _ = EnumWindows((handle, state) =>
        {
            _ = state;
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            _ = GetWindowThreadProcessId(handle, out var processId);
            if (!IsSupportedBrowser(processId))
            {
                return true;
            }

            var title = new StringBuilder(
                Math.Max(GetWindowTextLength(handle) + 1, 2));
            _ = GetWindowText(handle, title, title.Capacity);
            var text = title.ToString();
            if (!IsDedicatedTitle(text) ||
                !text.Contains("DeepSeek", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            _ = GetWindowRect(handle, out var rectangle);
            var area = Math.Max(
                    0L,
                    (long)rectangle.Right - rectangle.Left) *
                Math.Max(
                    0L,
                    (long)rectangle.Bottom - rectangle.Top);
            candidates.Add(new(handle, checked((int)processId), area));
            return true;
        }, nint.Zero);
        return candidates;
    }

    internal static bool IsDedicatedTitle(string title) =>
        !title.Contains(" - Google Chrome", StringComparison.OrdinalIgnoreCase) &&
        !title.Contains(" - Microsoft Edge", StringComparison.OrdinalIgnoreCase) &&
        !title.Contains(" - Brave", StringComparison.OrdinalIgnoreCase) &&
        !title.Contains(" - Vivaldi", StringComparison.OrdinalIgnoreCase) &&
        !title.Contains(" — Mozilla Firefox", StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedBrowser(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName is
                "chrome" or "msedge" or "brave" or
                "firefox" or "vivaldi" or "arc";
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                Win32Exception or
                OverflowException)
        {
            return false;
        }
    }

    private static string? FindAppBrowser()
    {
        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return new[]
            {
                Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe"),
                Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
            }
            .Where(path => path is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static string? Combine(string root, params string[] parts) =>
        string.IsNullOrWhiteSpace(root)
            ? null
            : Path.Combine([root, .. parts]);

    private readonly record struct WindowCandidate(
        nint Handle,
        int ProcessId,
        long Area);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
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

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint windowHandle,
        out WindowRectangle rectangle);

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
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(
        nint windowHandle,
        [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
