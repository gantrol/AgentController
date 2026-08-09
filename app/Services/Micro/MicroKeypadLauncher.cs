using System.Diagnostics;
using System.IO;

namespace CodexController.Services.Micro;

/// <summary>
/// Launches the optional standalone keypad without loading its UI assembly
/// into Agent Controller. The two products share only the broker protocol.
/// </summary>
internal sealed class MicroKeypadLauncher
{
    private const string DownloadPage =
        "https://github.com/gantrol/AgentController/releases";

    internal bool LaunchOrOpenDownloadPage()
    {
        var executable = CandidatePaths()
            .FirstOrDefault(File.Exists);
        var target = executable ?? DownloadPage;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                WorkingDirectory = executable is null
                    ? string.Empty
                    : Path.GetDirectoryName(executable) ?? string.Empty,
            })?.Dispose();
            return executable is not null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    internal static IReadOnlyList<string> CandidatePaths()
    {
        var localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return
        [
            Path.Combine(AppContext.BaseDirectory, "CodexMicro.exe"),
            Path.Combine(
                AppContext.BaseDirectory,
                "CodexMicro",
                "CodexMicro.exe"),
            Path.Combine(
                localApplicationData,
                "CodexMicro",
                "CodexMicro.exe"),
        ];
    }
}
