using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using AgentController.MicroSurface.Wpf;
using AgentController.MicroBroker;
using CodexMicro.Desktop.Services;

namespace CodexMicro.DesktopHost;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName =
        "Local\\CodexMicro.Keypad.1C01985F-1A5E-47DB-8E70-240EBA2F4D76";
    private const string RelaunchAfterArgument = "--relaunch-after";
    private const string RestartedArgument = "--restarted";
    private static readonly TimeSpan RelaunchWaitTimeout =
        TimeSpan.FromSeconds(30);

    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private MicroSurfaceController? _surface;
    private MicroTrayIcon? _trayIcon;
    private MicroLanguageSettings? _languageSettings;
    private MicroLocalization? _localization;
    private MicroStartupRegistration? _startupRegistration;
    private MicroKeypadControlServer? _controlServer;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private bool _exiting;
    private bool _restartQueued;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (MicroBrokerHost.IsBrokerArgument(e.Args))
        {
            var exitCode = MicroBrokerHost.RunFromCommandLine();
            Shutdown(exitCode);
            return;
        }

#if DEBUG
        var e2eCommand = ResolveE2eControlCommand(e.Args);
#endif

        if (!WaitForPreviousInstance(e.Args))
        {
            Shutdown(2);
            return;
        }

        _singleInstance = new Mutex(
            initiallyOwned: true,
            SingleInstanceName,
            out var isFirstInstance);
        _ownsSingleInstance = isFirstInstance;
        if (!isFirstInstance)
        {
#if DEBUG
            if (e2eCommand is { } command)
            {
                var response = MicroKeypadControlClient.TrySendAsync(
                        command,
                        TimeSpan.FromSeconds(60))
                    .GetAwaiter()
                    .GetResult();
                Shutdown(response is { Accepted: true } ? 0 : 3);
                return;
            }
#endif
            if (!e.Args.Contains(
                    "--background",
                    StringComparer.OrdinalIgnoreCase))
            {
                _ = MicroKeypadControlClient.TrySendAsync(
                        MicroKeypadControlCommand.Show,
                        TimeSpan.FromSeconds(2))
                    .GetAwaiter()
                    .GetResult();
            }

            Shutdown();
            return;
        }

        base.OnStartup(e);
        _languageSettings = new MicroLanguageSettings();
        _localization = _languageSettings.CreateLocalization();
        _startupRegistration = new MicroStartupRegistration();
        _surface = new MicroSurfaceController(
            _localization);
        _surface.StartBackgroundServices();
        _trayIcon = new MicroTrayIcon(
            _surface,
            _localization,
            _startupRegistration,
            language =>
            {
                _localization.SetLanguage(language);
                _languageSettings.Save(language);
            },
            RestartApplication,
            ExitApplication);
        if (!e.Args.Contains(
                "--background",
                StringComparer.OrdinalIgnoreCase))
        {
            _surface.Show();
        }

        _controlServer = new MicroKeypadControlServer(
            HandleControlCommandAsync,
            AfterControlResponseAsync);
        _controlServer.Start();

        if (e.Args.Contains(
                RestartedArgument,
                StringComparer.OrdinalIgnoreCase))
        {
            _trayIcon.ShowRestarted();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_controlServer is not null)
        {
            _ = _controlServer.DisposeAsync();
            _controlServer = null;
        }
        _surface?.Dispose();
        _surface = null;
        _localization = null;
        _languageSettings = null;
        _startupRegistration = null;
        if (_ownsSingleInstance)
        {
            _singleInstance?.ReleaseMutex();
            _ownsSingleInstance = false;
        }

        _singleInstance?.Dispose();
        _singleInstance = null;
        base.OnExit(e);
    }

    private async void ExitApplication()
    {
        await StopApplicationAsync(restart: false);
    }

    private async void RestartApplication()
    {
        _restartQueued = true;
        await StopApplicationAsync(restart: true);
    }

    private async Task StopApplicationAsync(bool restart)
    {
        if (_exiting)
        {
            return;
        }

        if (restart && !TryStartSuccessor())
        {
            _restartQueued = false;
            return;
        }

        _exiting = true;
        try
        {
            if (_surface is not null)
            {
                await _surface.ShutdownAsync();
                _surface = null;
            }

            if (_controlServer is not null)
            {
                await _controlServer.DisposeAsync();
                _controlServer = null;
            }

            _trayIcon?.Dispose();
            _trayIcon = null;
            Shutdown();
        }
        catch (Exception exception)
        {
            _exiting = false;
            _restartQueued = false;
            _trayIcon?.ShowRestartFailed(exception.Message);
        }
    }

    private Task<MicroKeypadControlResponse> HandleControlCommandAsync(
        MicroKeypadControlCommand command,
        CancellationToken cancellationToken)
    {
#if DEBUG
        if (command is
            MicroKeypadControlCommand.E2eNewTask or
            MicroKeypadControlCommand.E2eToggleQuickModel)
        {
            return HandleE2eControlCommandAsync(command, cancellationToken);
        }
#endif
        _ = cancellationToken;
        return Dispatcher
            .InvokeAsync(() => HandleControlCommand(command))
            .Task;
    }

#if DEBUG
    private async Task<MicroKeypadControlResponse>
        HandleE2eControlCommandAsync(
            MicroKeypadControlCommand command,
            CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (_exiting || _restartQueued || _surface is null)
        {
            return ControlResponse(
                accepted: false,
                MicroKeypadControlState.Busy,
                "The keypad is not ready for an E2E action.");
        }

        try
        {
            if (command == MicroKeypadControlCommand.E2eNewTask)
            {
                await _surface.RunE2eNewTaskAsync();
            }
            else
            {
                await _surface.RunE2eToggleQuickModelAsync();
            }

            return ControlResponse(
                accepted: true,
                MicroKeypadControlState.Ready);
        }
        catch (Exception exception)
        {
            return ControlResponse(
                accepted: false,
                MicroKeypadControlState.Rejected,
                exception.Message);
        }
    }

    private static MicroKeypadControlCommand? ResolveE2eControlCommand(
        string[] arguments)
    {
        if (arguments.Contains(
                "--e2e-new-task",
                StringComparer.OrdinalIgnoreCase))
        {
            return MicroKeypadControlCommand.E2eNewTask;
        }

        return arguments.Contains(
                "--e2e-toggle-quick-model",
                StringComparer.OrdinalIgnoreCase)
            ? MicroKeypadControlCommand.E2eToggleQuickModel
            : null;
    }
#endif

    private MicroKeypadControlResponse HandleControlCommand(
        MicroKeypadControlCommand command)
    {
        if (command == MicroKeypadControlCommand.Ping)
        {
            return ControlResponse(
                accepted: true,
                _exiting || _restartQueued
                    ? MicroKeypadControlState.Restarting
                    : MicroKeypadControlState.Ready);
        }

        if (_exiting || _restartQueued || _surface is null)
        {
            return ControlResponse(
                accepted: false,
                MicroKeypadControlState.Busy,
                "The keypad is already shutting down or restarting.");
        }

        if (command == MicroKeypadControlCommand.Show)
        {
            _surface.Show();
            return ControlResponse(
                accepted: true,
                MicroKeypadControlState.Ready);
        }

        if (command == MicroKeypadControlCommand.Restart)
        {
            _restartQueued = true;
            return ControlResponse(
                accepted: true,
                MicroKeypadControlState.Restarting);
        }

        return ControlResponse(
            accepted: false,
            MicroKeypadControlState.Rejected,
            "Unsupported keypad control command.");
    }

    private MicroKeypadControlResponse ControlResponse(
        bool accepted,
        MicroKeypadControlState state,
        string? detail = null) =>
        new(
            MicroKeypadControlClient.ProtocolVersion,
            accepted,
            state,
            _instanceId,
            detail);

    private Task AfterControlResponseAsync(
        MicroKeypadControlCommand command,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (command == MicroKeypadControlCommand.Restart)
        {
            _ = Dispatcher.BeginInvoke(
                new Action(RestartApplication));
        }

        return Task.CompletedTask;
    }

    private bool TryStartSuccessor()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) ||
            !File.Exists(executable))
        {
            _trayIcon?.ShowRestartFailed(
                _localization?.IsEnglish == true
                    ? "The running executable could not be located."
                    : "无法定位当前正在运行的程序文件。");
            return false;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(RelaunchAfterArgument);
            start.ArgumentList.Add(
                Environment.ProcessId.ToString(
                    CultureInfo.InvariantCulture));
            start.ArgumentList.Add(RestartedArgument);
            if (_surface is not { IsVisible: true })
            {
                start.ArgumentList.Add("--background");
            }

            var successor = Process.Start(start);
            if (successor is null)
            {
                _trayIcon?.ShowRestartFailed(
                    _localization?.IsEnglish == true
                        ? "Windows did not create the replacement process."
                        : "Windows 未能创建接班进程。");
                return false;
            }

            successor.Dispose();
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception)
        {
            _trayIcon?.ShowRestartFailed(exception.Message);
            return false;
        }
    }

    private static bool WaitForPreviousInstance(string[] arguments)
    {
        var index = Array.FindIndex(
            arguments,
            argument => argument.Equals(
                RelaunchAfterArgument,
                StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return true;
        }

        if (index + 1 >= arguments.Length ||
            !int.TryParse(
                arguments[index + 1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var processId) ||
            processId <= 0 ||
            processId == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var previous = Process.GetProcessById(processId);
            return previous.WaitForExit(
                checked((int)RelaunchWaitTimeout.TotalMilliseconds));
        }
        catch (ArgumentException)
        {
            // The previous process exited before the successor opened it.
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception)
        {
            return false;
        }
    }
}
