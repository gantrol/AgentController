using System.Threading;
using System.Windows;
using CodexController.Composition;
using CodexController.Localization;
using CodexController.Presentation;

namespace CodexController;

public partial class App : System.Windows.Application
{
    static App()
    {
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            System.Windows.Controls.ToolTipService.ToolTipOpeningEvent,
            new System.Windows.Controls.ToolTipEventHandler((_, e) => e.Handled = true));
    }

    internal static bool SuppressCompositionStartupForTests { get; set; }
    internal bool IsDevelopmentMode { get; private set; }

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

        IsDevelopmentMode = e.Args.Contains("--dev-instance", StringComparer.OrdinalIgnoreCase);
        if (e.Args.Contains("--component-gallery", StringComparer.OrdinalIgnoreCase))
        {
            if (!IsDevelopmentMode)
            {
                Console.Error.WriteLine("Component gallery requires --dev-instance.");
                Shutdown(2);
                return;
            }

            Console.WriteLine("Starting component gallery...");
            base.OnStartup(e);
            var gallerySettings = new Services.SettingsService().Load();
            UiTypography.Apply(gallerySettings.TextSize, resizeOpenWindows: false);
            LocalizationHost.Use(new LocalizationService(AppLanguageParser.Parse(gallerySettings.Language)));
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new Views.ComponentGalleryWindow();
            MainWindow.ContentRendered += (_, _) => Console.WriteLine("Component gallery ready.");
            MainWindow.Show();
            return;
        }

        if (!IsDevelopmentMode)
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
        UiTypography.Apply(
            _composition.Desktop.CurrentSettings.TextSize,
            resizeOpenWindows: false);
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

        if (IsDevelopmentMode)
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
