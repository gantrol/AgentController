using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CodexMicro.Desktop.Services;

internal readonly record struct DeepSeekSurfaceLaunchResult(
    bool Started,
    int? ProcessId,
    string? Error);

/// <summary>
/// Opens the DeepSeek web UI as a dedicated native Windows app-mode window.
/// The Harness host can run inside WSL, but WSL interop is not guaranteed to
/// be registered or enabled. The Windows Micro host is therefore the
/// authoritative launcher for a physical DeepSeek-key gesture.
/// </summary>
internal static class DeepSeekSurfaceLauncher
{
    private const string SurfaceQuery = "codexMicroSurface=1";

    internal static DeepSeekSurfaceLaunchResult TryLaunch(
        MicroHarnessDefinition harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        if (!OperatingSystem.IsWindows())
        {
            return new(false, null, "Dedicated DeepSeek launch requires Windows.");
        }

        var browser = FindAppBrowser();
        if (browser is null)
        {
            return new(
                false,
                null,
                "Microsoft Edge or Google Chrome app mode is unavailable.");
        }

        try
        {
            using var process = Process.Start(CreateStartInfo(
                browser,
                BuildSurfaceUri(harness)));
            return process is null
                ? new(false, null, "The dedicated DeepSeek window did not start.")
                : new(true, process.Id, null);
        }
        catch (Exception exception) when (
            exception is Win32Exception or
                InvalidOperationException or
                IOException or
                UnauthorizedAccessException)
        {
            return new(
                false,
                null,
                $"The dedicated DeepSeek window could not start ({exception.GetType().Name}).");
        }
    }

    internal static Uri BuildSurfaceUri(MicroHarnessDefinition harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        var webUri = Uri.TryCreate(
                harness.ControlUri,
                UriKind.Absolute,
                out var controlUri) &&
            controlUri.Scheme == Uri.UriSchemeHttp &&
            controlUri.IsLoopback &&
            string.IsNullOrEmpty(controlUri.UserInfo)
                ? controlUri
                : new Uri(MicroHarnessRegistry.DeepSeekOfficialWebUri);
        return new UriBuilder(webUri)
        {
            Path = "/",
            Query = SurfaceQuery,
            Fragment = string.Empty,
        }.Uri;
    }

    internal static ProcessStartInfo CreateStartInfo(
        string browser,
        Uri surfaceUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(browser);
        ArgumentNullException.ThrowIfNull(surfaceUri);
        var startInfo = new ProcessStartInfo
        {
            FileName = browser,
            UseShellExecute = false,
            CreateNoWindow = false,
            WindowStyle = ProcessWindowStyle.Normal,
        };
        startInfo.ArgumentList.Add($"--app={surfaceUri.AbsoluteUri}");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--no-default-browser-check");
        return startInfo;
    }

    private static string? FindAppBrowser()
    {
        var programFilesX86 = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(
            Environment.SpecialFolder.ProgramFiles);
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        // Preserve the product contract: Edge is the dedicated DeepSeek host;
        // Chrome is only a compatibility fallback when Edge is unavailable.
        var candidates = new[]
        {
            Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
            Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
            Combine(localAppData, "Microsoft", "Edge", "Application", "msedge.exe"),
            Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
            Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
            Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe"),
        };
        return candidates
            .Where(path => path is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    private static string? Combine(string root, params string[] parts) =>
        string.IsNullOrWhiteSpace(root)
            ? null
            : Path.Combine([root, .. parts]);
}
