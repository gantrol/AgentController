using AgentController.Application.Actions;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Services.Micro;

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
        BridgeEventHub bridgeEvents,
        LocalizationService localization,
        ControllerProfileRegistry controllerProfiles,
        AgentTargetRegistry agentTargets,
        IAgentTarget activeAgent,
        StartupRegistrationService startupRegistration,
        SettingsService settings,
        CodexDataService codexData,
        CodexCommandService codexCommand,
        CodexKeybindingService codexKeybindings,
        MicroInputService microInput,
        CodexComposerService codexComposer,
        CodexSidebarService codexSidebar,
        XInputService controller,
        ControllerInteractionCoordinator controllerInteraction,
        ActionRouter actionRouter)
    {
        BridgeEvents = bridgeEvents;
        Localization = localization;
        ControllerProfiles = controllerProfiles;
        AgentTargets = agentTargets;
        ActiveAgent = activeAgent;
        StartupRegistration = startupRegistration;
        Settings = settings;
        CodexData = codexData;
        CodexCommand = codexCommand;
        CodexKeybindings = codexKeybindings;
        MicroInput = microInput;
        CodexComposer = codexComposer;
        CodexSidebar = codexSidebar;
        Controller = controller;
        ControllerInteraction = controllerInteraction;
        ActionRouter = actionRouter;
    }

    public BridgeEventHub BridgeEvents { get; }
    public LocalizationService Localization { get; }
    public ControllerProfileRegistry ControllerProfiles { get; }
    public AgentTargetRegistry AgentTargets { get; }
    public IAgentTarget ActiveAgent { get; }
    public StartupRegistrationService StartupRegistration { get; }
    public SettingsService Settings { get; }
    public CodexDataService CodexData { get; }
    public CodexCommandService CodexCommand { get; }
    public CodexKeybindingService CodexKeybindings { get; }
    public MicroInputService MicroInput { get; }
    public CodexComposerService CodexComposer { get; }
    public CodexSidebarService CodexSidebar { get; }
    public XInputService Controller { get; }
    public ControllerInteractionCoordinator ControllerInteraction { get; }
    public ActionRouter ActionRouter { get; }

    public static AppServices CreateDefault()
    {
        var startupRegistration = new StartupRegistrationService();
        var settings = new SettingsService(startupRegistration);
        var localization = new LocalizationService();
        var codexData = new CodexDataService(localization);
        var codexCommand = new CodexCommandService();
        var codexKeybindings = new CodexKeybindingService();
        var microInput = new MicroInputService(
            new NamedPipeMicroReportTransport());
        var codexComposer = new CodexComposerService(microInput);
        var codexSidebar = new CodexSidebarService();
        var controllerProfiles = ControllerProfileRegistry.BuiltIn;
        var codexAgent = new CodexAgentTarget(
            codexCommand,
            codexData,
            codexSidebar,
            codexComposer,
            codexKeybindings);
        var agentTargets = new AgentTargetRegistry(
            [codexAgent],
            CodexAgentTarget.CodexId);
        var activeAgent = agentTargets.Resolve(
            settings.Load().ActiveAgentId);
        var actionRouter = new ActionRouter(
        [
            new CodexCreateThreadActionExecutor(
                actionNames => codexComposer.InvokeComposerAction(
                    settings.Load(),
                    actionNames),
                shortcut => codexCommand.ExecuteShortcut(
                    shortcut,
                    settings.Load())),
            new CodexOpenThreadActionExecutor(
                CodexCommandService.OpenThread),
        ]);
        return new AppServices(
            new BridgeEventHub(),
            localization,
            controllerProfiles,
            agentTargets,
            activeAgent,
            startupRegistration,
            settings,
            codexData,
            codexCommand,
            codexKeybindings,
            microInput,
            codexComposer,
            codexSidebar,
            new XInputService(controllerProfiles),
            new ControllerInteractionCoordinator(),
            actionRouter);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Controller.Dispose();
        MicroInput.Dispose();
        BridgeEvents.Dispose();
        _disposed = true;
    }
}
