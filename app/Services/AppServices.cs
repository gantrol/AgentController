namespace CodexController.Services;

/// <summary>
/// Owns the application-scoped service graph. App can create one instance and
/// pass the individual services to windows or view models without constructing
/// infrastructure dependencies in the presentation layer.
/// </summary>
public sealed class AppServices : IDisposable
{
    private bool _disposed;

    private AppServices(
        StartupRegistrationService startupRegistration,
        SettingsService settings,
        CodexDataService codexData,
        CodexCommandService codexCommand,
        CodexKeybindingService codexKeybindings,
        CodexComposerService codexComposer,
        CodexSidebarService codexSidebar,
        XInputService controller,
        AxisRepeater axisRepeater,
        StickGestureRouter leftStickRouter,
        StickGestureRouter rightStickRouter)
    {
        StartupRegistration = startupRegistration;
        Settings = settings;
        CodexData = codexData;
        CodexCommand = codexCommand;
        CodexKeybindings = codexKeybindings;
        CodexComposer = codexComposer;
        CodexSidebar = codexSidebar;
        Controller = controller;
        AxisRepeater = axisRepeater;
        LeftStickRouter = leftStickRouter;
        RightStickRouter = rightStickRouter;
    }

    public StartupRegistrationService StartupRegistration { get; }
    public SettingsService Settings { get; }
    public CodexDataService CodexData { get; }
    public CodexCommandService CodexCommand { get; }
    public CodexKeybindingService CodexKeybindings { get; }
    public CodexComposerService CodexComposer { get; }
    public CodexSidebarService CodexSidebar { get; }
    public XInputService Controller { get; }
    public AxisRepeater AxisRepeater { get; }
    public StickGestureRouter LeftStickRouter { get; }
    public StickGestureRouter RightStickRouter { get; }

    public static AppServices CreateDefault()
    {
        var startupRegistration = new StartupRegistrationService();
        return new AppServices(
            startupRegistration,
            new SettingsService(startupRegistration),
            new CodexDataService(),
            new CodexCommandService(),
            new CodexKeybindingService(),
            new CodexComposerService(),
            new CodexSidebarService(),
            new XInputService(),
            new AxisRepeater(),
            new StickGestureRouter(),
            new StickGestureRouter());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Controller.Dispose();
        _disposed = true;
    }
}
