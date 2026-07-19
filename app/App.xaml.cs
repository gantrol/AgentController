using System.Threading;
using System.Windows;
using AgentController.MicroBroker;
using CodexController.Composition;
using CodexController.Localization;

namespace CodexController;

public partial class App : System.Windows.Application
{
    internal static bool SuppressCompositionStartupForTests { get; set; }

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private AppComposition? _composition;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (SuppressCompositionStartupForTests)
        {
            base.OnStartup(e);
            return;
        }

        if (MicroBrokerHost.IsBrokerArgument(e.Args))
        {
            var exitCode = MicroBrokerHost.RunFromCommandLine();
            Shutdown(exitCode);
            return;
        }

        var isDevelopmentInstance = e.Args.Contains(
            "--dev-instance",
            StringComparer.OrdinalIgnoreCase);
        if (!isDevelopmentInstance)
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\CodexController.SingleInstance",
                createdNew: out var createdNew);
            _ownsSingleInstanceMutex = createdNew;
            if (!createdNew)
            {
                Shutdown();
                return;
            }
        }

        base.OnStartup(e);
        _composition = AppComposition.CreateDefault();
        LocalizationHost.Use(_composition.Localization);
        var window = new MainWindow(_composition.Desktop);
        MainWindow = window;
        if (e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            window.WindowState = WindowState.Minimized;
            window.ShowInTaskbar = false;
            window.Show();
            window.Hide();
        }
        else
        {
            window.Show();
        }

        if (isDevelopmentInstance)
        {
            window.Title = "Agent Controller Preview";
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _composition?.Dispose();
        _composition = null;
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
