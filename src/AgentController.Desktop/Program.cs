using Avalonia;

namespace AgentController.Desktop;

internal static class Program
{
    private const string SingleInstanceName =
        "AgentController.FoundationPreview.SingleInstance";

    [STAThread]
    public static int Main(string[] args)
    {
        using var singleInstance = new Mutex(
            initiallyOwned: true,
            SingleInstanceName,
            out var isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
