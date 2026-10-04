using CodexMicro.Codex;
using System.Diagnostics;
using AgentController.MicroSurface.Wpf;
using AgentController.MicroSurface.Wpf.SoftwareControl;
using CodexMicro.DesktopHost;

namespace CodexMicro.Plugin;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception error)
        {
            SoftwareControlDiagnostics.Write("startup-failed", error);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Contains("--mcp"))
        {
            RunServerAsync().GetAwaiter().GetResult();
            return 0;
        }
        var index = Array.IndexOf(args, "--thread");
        SoftwareControlDiagnostics.Write("starting");
        var thread = index >= 0 && index + 1 < args.Length ? JsonSupport.ThreadId(args[index + 1]) : null;
        if (KeypadWindowHost.TryActivateAsync(thread, CancellationToken.None).GetAwaiter().GetResult()) return 0;
        using var mutex = new Mutex(true, KeypadWindowHost.MutexName, out var acquired);
        if (!acquired) return 0;
        var restartRequested = false;
        int exitCode;
        try
        {
            var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (_, e) => SoftwareControlDiagnostics.Write("dispatcher-failed", e.Exception);
            var settings = new MicroLanguageSettings();
            var localization = settings.CreateLocalization();
            using var surface = MicroSurfaceController.CreateSoftwareControlled(localization);
            SoftwareControlDiagnostics.Write("surface-created");
            using var host = new KeypadWindowHost(surface, app.Dispatcher);
            var exiting = false;
            async void Exit(bool restart)
            {
                if (exiting) return;
                exiting = true;
                restartRequested = restart;
                await surface.ShutdownAsync();
                app.Shutdown();
            }
            using var tray = new MicroTrayIcon(surface, localization,
                new MicroStartupRegistration("CodexMicroPluginKeypad"),
                language => { localization.SetLanguage(language); settings.Save(language); },
                () => Exit(true), () => Exit(false));
            host.Start();
            if (thread is not null) surface.SelectThread(thread);
            app.Startup += (_, _) =>
            {
                SoftwareControlDiagnostics.Write("dispatcher-started");
                if (!args.Contains("--background")) surface.Show();
                SoftwareControlDiagnostics.Write(args.Contains("--background") ? "background-ready" : "surface-shown");
                surface.StartBackgroundServices();
            };
            exitCode = app.Run();
        }
        finally { mutex.ReleaseMutex(); }
        if (restartRequested && Environment.ProcessPath is { } executable)
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })?.Dispose();
        return exitCode;
    }

    private static async Task RunServerAsync()
    {
        await using var controller = new KeypadController();
        await new McpServer(controller).RunAsync();
    }
}
