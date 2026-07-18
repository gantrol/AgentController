using AgentController.Application.Actions;
using AgentController.Application.Navigation;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Services;
using CodexController.Services.Micro;

namespace CodexController.Composition;

internal sealed class AppComposition : IDisposable
{
    private bool _disposed;

    private AppComposition(MainWindowDependencies desktop)
    {
        Desktop = desktop ??
            throw new ArgumentNullException(nameof(desktop));
    }

    internal MainWindowDependencies Desktop { get; }

    internal LocalizationService Localization => Desktop.Localization;

    internal static AppComposition CreateDefault()
    {
        var startupRegistration = new StartupRegistrationService();
        var settings = new SettingsService(startupRegistration);
        var currentSettings = settings.Load();
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
            currentSettings.ActiveAgentId);
        var foregroundApplication =
            new AgentForegroundApplication(activeAgent.Presence);

        Func<string?> codexActionBlockReason = () =>
            !currentSettings.BridgeEnabled
                ? AgentAutomationErrorCodes.BridgeSafePreview
                : currentSettings.OnlyWhenCodexForeground &&
                  !codexCommand.IsCodexForeground
                    ? AgentAutomationErrorCodes.AgentNotForeground
                    : null;
        var actionRouter = new ActionRouter(
        [
            new CodexForkThreadActionExecutor(
                codexActionBlockReason,
                microInput.TryForkThread,
                () => codexCommand.ExecuteShortcut(
                    currentSettings.ForkShortcut,
                    currentSettings),
                actionNames => codexComposer.InvokeComposerAction(
                    currentSettings,
                    actionNames)),
            new CodexNavigationUndoActionExecutor(
                codexActionBlockReason,
                () => codexSidebar.GoBack(currentSettings)),
            new CodexShellActionExecutor(
                codexActionBlockReason,
                shortcut => codexCommand.ExecuteShortcut(
                    shortcut,
                    currentSettings)),
            new CodexConversationActionExecutor(
                codexActionBlockReason,
                (boundary, cancellationToken) =>
                    codexComposer.ScrollConversationAsync(
                        boundary,
                        currentSettings,
                        cancellationToken)),
            new CodexUiCommandActionExecutor(
                codexActionBlockReason,
                actionNames => codexComposer.InvokeComposerAction(
                    currentSettings,
                    actionNames)),
            new CodexComposerActionExecutor(
                () => codexComposer.SubmitComposer(currentSettings),
                () => codexComposer.ClearComposer(currentSettings),
                () => codexComposer.StopCurrentTurn(currentSettings)),
            new CodexCreateThreadActionExecutor(
                actionNames => codexComposer.InvokeComposerAction(
                    currentSettings,
                    actionNames),
                shortcut => codexCommand.ExecuteShortcut(
                    shortcut,
                    currentSettings)),
            new CodexOpenThreadActionExecutor(
                CodexCommandService.OpenThread),
        ]);
        var actionDispatcher = new ActionDispatcher(actionRouter);
        var workspace = activeAgent.WorkspaceOrEmpty();
        var sidebar = activeAgent.SidebarOrUnavailable();
        var navigationContext = new AgentThreadNavigationContext(
            currentSettings,
            workspace,
            sidebar);
        var threadNavigation = new ThreadNavigationCoordinator(
            actionDispatcher,
            navigationContext,
            foregroundApplication,
            new ThreadNavigationOptions(
                TimeSpan.FromMilliseconds(
                    BridgeTimings.NavigationConfirmTimeoutMs),
                TimeSpan.FromMilliseconds(
                    BridgeTimings.NavigationConfirmPollMs),
                BridgeTimings.NavigationUndoWindow));
        var desktop = new MainWindowDependencies(
            new BridgeEventHub(),
            localization,
            controllerProfiles,
            activeAgent,
            foregroundApplication,
            settings,
            currentSettings,
            microInput,
            new XInputService(controllerProfiles),
            new ControllerInteractionCoordinator(),
            new ControllerHoldCoordinator(),
            new RadialLayerCoordinator(),
            actionDispatcher,
            threadNavigation);
        return new AppComposition(desktop);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Desktop.Controller.Dispose();
        Desktop.ControllerHolds.Dispose();
        Desktop.RadialLayers.Dispose();
        Desktop.ThreadNavigation.Dispose();
        Desktop.MicroInput.Dispose();
        Desktop.BridgeEvents.Dispose();
        _disposed = true;
    }
}
