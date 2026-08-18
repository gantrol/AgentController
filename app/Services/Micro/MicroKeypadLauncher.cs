using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AgentController.MicroBroker;

namespace CodexController.Services.Micro;

internal enum MicroKeypadActionOutcome
{
    Shown,
    Started,
    Restarted,
    Busy,
    DownloadOpened,
    Failed,
}

internal sealed record MicroKeypadActionResult(
    MicroKeypadActionOutcome Outcome,
    string? Detail = null);

/// <summary>
/// Controls the optional standalone keypad without loading its UI assembly
/// into Agent Controller. A same-user named pipe handles an existing keypad;
/// process launch is used only when no ready instance answers.
/// </summary>
internal sealed class MicroKeypadLauncher
{
    private const string DownloadPage =
        "https://github.com/gantrol/AgentController/releases";
    private static readonly TimeSpan CommandTimeout =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadyTimeout =
        TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReadyPollInterval =
        TimeSpan.FromMilliseconds(150);
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    internal Task<MicroKeypadActionResult> ShowAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteExclusiveAsync(
            restart: false,
            cancellationToken);

    internal Task<MicroKeypadActionResult> RestartAsync(
        CancellationToken cancellationToken = default) =>
        ExecuteExclusiveAsync(
            restart: true,
            cancellationToken);

    private async Task<MicroKeypadActionResult> ExecuteExclusiveAsync(
        bool restart,
        CancellationToken cancellationToken)
    {
        if (!await _operationGate.WaitAsync(0, cancellationToken))
        {
            return new(MicroKeypadActionOutcome.Busy);
        }

        try
        {
            var executable = CandidatePaths()
                .FirstOrDefault(File.Exists);
            return restart
                ? await RestartCoreAsync(executable, cancellationToken)
                : await ShowCoreAsync(executable, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static async Task<MicroKeypadActionResult> ShowCoreAsync(
        string? executable,
        CancellationToken cancellationToken)
    {
        var response = await MicroKeypadControlClient.TrySendAsync(
            MicroKeypadControlCommand.Show,
            CommandTimeout,
            cancellationToken);
        if (response is
            {
                Accepted: true,
                State: MicroKeypadControlState.Ready,
            })
        {
            return new(MicroKeypadActionOutcome.Shown);
        }

        if (response is not null)
        {
            return response.State is
                MicroKeypadControlState.Busy or
                MicroKeypadControlState.Restarting
                    ? new(
                        MicroKeypadActionOutcome.Busy,
                        response.Detail)
                    : new(
                        MicroKeypadActionOutcome.Failed,
                        response.Detail);
        }

        if (executable is null)
        {
            return OpenDownloadPage();
        }

        var launch = TryLaunch(executable);
        if (launch is not null)
        {
            return launch;
        }

        var ready = await WaitForReadyAsync(
            previousInstanceId: null,
            cancellationToken);
        return ready is not null
            ? new(MicroKeypadActionOutcome.Started)
            : new(
                MicroKeypadActionOutcome.Failed,
                "Codex Micro did not become ready after it was launched.");
    }

    private static async Task<MicroKeypadActionResult> RestartCoreAsync(
        string? executable,
        CancellationToken cancellationToken)
    {
        var current = await MicroKeypadControlClient.TrySendAsync(
            MicroKeypadControlCommand.Ping,
            CommandTimeout,
            cancellationToken);
        if (current is null)
        {
            if (executable is null)
            {
                return OpenDownloadPage();
            }

            var launch = TryLaunch(executable);
            if (launch is not null)
            {
                return launch;
            }

            var started = await WaitForReadyAsync(
                previousInstanceId: null,
                cancellationToken);
            return started is not null
                ? new(MicroKeypadActionOutcome.Started)
                : new(
                    MicroKeypadActionOutcome.Failed,
                    "Codex Micro did not become ready after it was launched.");
        }

        if (current.State != MicroKeypadControlState.Ready)
        {
            return new(
                MicroKeypadActionOutcome.Busy,
                current.Detail);
        }

        var accepted = await MicroKeypadControlClient.TrySendAsync(
            MicroKeypadControlCommand.Restart,
            CommandTimeout,
            cancellationToken);
        if (accepted is not { Accepted: true })
        {
            return accepted?.State == MicroKeypadControlState.Busy
                ? new(MicroKeypadActionOutcome.Busy, accepted.Detail)
                : new(
                    MicroKeypadActionOutcome.Failed,
                    accepted?.Detail ??
                    "The running keypad did not accept the restart request.");
        }

        var successor = await WaitForReadyAsync(
            current.InstanceId,
            cancellationToken);
        return successor is not null
            ? new(MicroKeypadActionOutcome.Restarted)
            : new(
                MicroKeypadActionOutcome.Failed,
                "The replacement Codex Micro instance did not become ready.");
    }

    private static async Task<MicroKeypadControlResponse?> WaitForReadyAsync(
        string? previousInstanceId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ReadyTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await MicroKeypadControlClient.TrySendAsync(
                MicroKeypadControlCommand.Ping,
                CommandTimeout,
                cancellationToken);
            if (response is
                {
                    Accepted: true,
                    State: MicroKeypadControlState.Ready,
                } &&
                (string.IsNullOrWhiteSpace(previousInstanceId) ||
                 !string.Equals(
                     response.InstanceId,
                     previousInstanceId,
                     StringComparison.Ordinal)))
            {
                return response;
            }

            await Task.Delay(ReadyPollInterval, cancellationToken);
        }

        return null;
    }

    private static MicroKeypadActionResult? TryLaunch(string executable)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                WorkingDirectory =
                    Path.GetDirectoryName(executable) ?? string.Empty,
            });
            if (process is null)
            {
                return new(
                    MicroKeypadActionOutcome.Failed,
                    "Windows did not create the Codex Micro process.");
            }

            process.Dispose();
            return null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception)
        {
            return new(
                MicroKeypadActionOutcome.Failed,
                exception.Message);
        }
    }

    private static MicroKeypadActionResult OpenDownloadPage()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = DownloadPage,
                UseShellExecute = true,
            });
            if (process is null)
            {
                return new(
                    MicroKeypadActionOutcome.Failed,
                    "Windows did not open the Codex Micro download page.");
            }

            process.Dispose();
            return new(MicroKeypadActionOutcome.DownloadOpened);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception)
        {
            return new(
                MicroKeypadActionOutcome.Failed,
                exception.Message);
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
