using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgentController.Desktop.ViewModels;
using AgentController.Platform.MacOS;

namespace AgentController.Desktop;

public partial class App : Avalonia.Application
{
    private MacFoundationRuntime? _runtime;
    private FoundationViewModel? _viewModel;
    private MainWindow? _mainWindow;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime
            desktop)
        {
            _runtime = new MacFoundationRuntime();
            _viewModel = new FoundationViewModel(_runtime);
            _mainWindow = new MainWindow
            {
                DataContext = _viewModel,
            };
            desktop.MainWindow = _mainWindow;
            desktop.Exit += Desktop_Exit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void About_OnClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
        _viewModel?.ShowAbout();
    }

    private void Refresh_OnClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
        _viewModel?.Refresh();
    }

    private void Privacy_OnClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
        _viewModel?.OpenPrivacySettings();
    }

    private void ShowMainWindow_OnClick(object? sender, EventArgs e) =>
        ShowMainWindow();

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    private void Desktop_Exit(
        object? sender,
        ControlledApplicationLifetimeExitEventArgs e)
    {
        _viewModel?.Dispose();
        _viewModel = null;
        _runtime = null;
        _mainWindow = null;
    }
}
