using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Services;
using AgentController.MicroSurface.Wpf.SoftwareControl;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += async (_, _) =>
        {
            var window = new MicroSurfaceWindow(new(MicroLanguage.ZhCn),
                profileSettings: MicroProfileSettings.CreateTransient(), transport: new SoftwareMicroTransport())
                { ShowActivated = false, ShowInTaskbar = false, Topmost = false, Left = -10000, Top = -10000, Opacity = 0 };
            var exit = 0;
            try
            {
                window.Show();
                await Task.Delay(800);
                for (var round = 0; round < 2; round++)
                {
                    await Switch(window.MonitorPageButton, window.MonitorGrid, window.ControlGrid);
                    if (window.MonitorGrid.Children.OfType<Button>().Count() != 14) throw new Exception("Missing monitor slots");
                    await Switch(window.ControlPageButton, window.ControlGrid, window.MonitorGrid);
                }
                Console.WriteLine("PASS: two control/monitor round trips; 14 task keys; knob and submit remain visible");
            }
            catch (Exception error) { Console.WriteLine(error); exit = 1; }
            finally { await window.CloseForApplicationExitAsync(); app.Shutdown(exit); }

            async Task Switch(RadioButton selector, Grid target, Grid previous)
            {
                selector.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (!selector.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(25);
                if (target.Visibility != Visibility.Visible || previous.Visibility != Visibility.Collapsed ||
                    !window.ModelKnob.IsVisible || !window.ActionKey12.IsVisible || !selector.IsEnabled)
                    throw new Exception("Panel switch or shared controls failed");
            }
        };
        return app.Run();
    }
}
