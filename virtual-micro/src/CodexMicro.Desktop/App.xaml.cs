using System.Threading;
using System.Windows;
using AgentController.MicroBroker;

namespace CodexMicro.Desktop;

public partial class App : Application
{
    private const string SingleInstanceMutexName =
        @"Local\CodexMicroSimulator.303A8360";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (MicroBrokerHost.IsBrokerArgument(e.Args))
        {
            var exitCode = MicroBrokerHost.RunFromCommandLine();
            Shutdown(exitCode);
            return;
        }

        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out _ownsSingleInstanceMutex);
        if (!_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _ownsSingleInstanceMutex = false;
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        base.OnExit(e);
    }
}
