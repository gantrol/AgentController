using AgentController.MicroSurface.Wpf;
using AgentController.MicroBroker;
using CodexMicro.Desktop.Services;

namespace CodexMicro.DesktopHost;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName =
        "Local\\CodexMicro.Keypad.1C01985F-1A5E-47DB-8E70-240EBA2F4D76";

    private Mutex? _singleInstance;
    private bool _ownsSingleInstance;
    private MicroSurfaceController? _surface;
    private MicroTrayIcon? _trayIcon;
    private MicroLanguageSettings? _languageSettings;
    private MicroLocalization? _localization;
    private MicroStartupRegistration? _startupRegistration;
    private bool _exiting;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        if (MicroBrokerHost.IsBrokerArgument(e.Args))
        {
            var exitCode = MicroBrokerHost.RunFromCommandLine();
            Shutdown(exitCode);
            return;
        }

        _singleInstance = new Mutex(
            initiallyOwned: true,
            SingleInstanceName,
            out var isFirstInstance);
        _ownsSingleInstance = isFirstInstance;
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _languageSettings = new MicroLanguageSettings();
        _localization = _languageSettings.CreateLocalization();
        _startupRegistration = new MicroStartupRegistration();
        _surface = new MicroSurfaceController(
            _localization);
        _trayIcon = new MicroTrayIcon(
            _surface,
            _localization,
            _startupRegistration,
            language =>
            {
                _localization.SetLanguage(language);
                _languageSettings.Save(language);
            },
            ExitApplication);
        if (!e.Args.Contains(
                "--background",
                StringComparer.OrdinalIgnoreCase))
        {
            _surface.Show();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
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

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _surface?.Dispose();
        _surface = null;
        Shutdown();
    }
}
