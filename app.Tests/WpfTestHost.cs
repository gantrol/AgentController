using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace CodexController.Tests;

/// <summary>
/// WPF permits one Application per AppDomain. All visual regression tests run
/// on this single STA dispatcher instead of racing to create their own app.
/// </summary>
internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> HostDispatcher =
        new(StartHost);
    private static App? _application;

    internal static void Run(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? failure = null;
        HostDispatcher.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Dispatcher StartHost()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                App.SuppressCompositionStartupForTests = true;
                _application = new App
                {
                    ShutdownMode =
                        System.Windows.ShutdownMode.OnExplicitShutdown,
                };
                _application.InitializeComponent();
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                ready.Set();
            }

            if (failure is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "AgentController.WpfTestHost",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return dispatcher ?? throw new InvalidOperationException(
            "The WPF test dispatcher did not start.");
    }
}
