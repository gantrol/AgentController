using System.Windows;
using AgentController.MicroSurface.Wpf.SoftwareControl;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var profile = MicroProfileSettings.CreateTransient();
        var window = new MicroSurfaceWindow(new(MicroLanguage.ZhCn),
            profileSettings: profile, transport: new SoftwareMicroTransport())
        {
            // Make this test host discoverable by desktop automation; use the product's window and controls.
            ShowInTaskbar = true,
            Title = "Micro Live Smoke",
        };
        if (args.Length > 0)
        {
            var draft = args.Length is 3 or 5 && args[0] == "--draft" && Guid.TryParse(args[1], out _);
            var lights = args.Length == 4 && args[0] == "--lights" &&
                Guid.TryParse(args[1], out _) && Guid.TryParse(args[2], out _);
            if (!draft && !lights && (args.Length != 4 || args.Take(3).Any(value => !Guid.TryParse(value, out _))))
                throw new ArgumentException("Expected: idle-thread-id second-idle-thread-id restore-thread-id report-path");
            EventHandler? run = null;
            run = async (_, _) =>
            {
                window.ContentRendered -= run;
                var exit = lights ? await LightVerification.RunAsync(window, args[1], args[2], args[3]) : draft
                    ? await DraftVerification.RunAsync(window, profile, args[1], args[2],
                        args.Length == 5 ? args[3] : null, args.Length == 5 ? args[4] : null)
                    : await ReversibleVerification.RunAsync(window, args[0], args[1], args[2], args[3]);
                app.Shutdown(exit);
            };
            window.ContentRendered += run;
        }
        else window.Closed += (_, _) => app.Shutdown();
        window.Show();
        return app.Run();
    }
}
