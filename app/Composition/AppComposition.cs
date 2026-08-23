using AgentController.Application.Actions;
using AgentController.Application.Navigation;
using CodexController.Agents;
using CodexController.Agents.Codex;
using CodexController.Agents.DeepSeek;
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
            new VhfMicroReportTransport());
        var codexComposer = new CodexComposerService(microInput);
        var codexSidebar = new CodexSidebarService();
        var controllerProfiles = ControllerProfileRegistry.BuiltIn;
        var codexAgent = new CodexAgentTarget(
            codexCommand,
            codexData,
            codexSidebar,
            codexComposer,
            codexKeybindings);
        var deepSeekAgent = new DeepSeekAgentTarget(
            new DeepSeekHarnessClient());
        var agentTargets = new AgentTargetRegistry(
            [codexAgent, deepSeekAgent],
            CodexAgentTarget.CodexId);
        var agentSelection = new AgentTargetSelection(
            agentTargets,
            currentSettings.ActiveAgentId);
        var foregroundApplication =
            new AgentForegroundApplication(
                () => agentSelection.Active.Presence);

        Func<string?> codexActionBlockReason = () =>
            !currentSettings.BridgeEnabled
                ? AgentAutomationErrorCodes.BridgeSafePreview
                : currentSettings.OnlyWhenCodexForeground &&
                  !codexCommand.IsCodexForeground
                    ? AgentAutomationErrorCodes.AgentNotForeground
                    : null;
        Func<string?> deepSeekActionBlockReason = () =>
            !currentSettings.BridgeEnabled
                ? AgentAutomationErrorCodes.BridgeSafePreview
                : currentSettings.OnlyWhenCodexForeground &&
                  !deepSeekAgent.Presence.IsForeground
                    ? AgentAutomationErrorCodes.AgentNotForeground
                    : null;
        IActionExecutor[] codexExecutors =
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
                    actionNames),
                tryMicro: actionId => actionId ==
                    ApprovalActionContract.AcceptId
                        ? microInput.SendApprove()
                        : actionId == ApprovalActionContract.DeclineId
                            ? microInput.SendDecline()
                            : MicroReportSendResult.NotSent),
            new CodexComposerActionExecutor(
                () =>
                {
                    if (codexActionBlockReason() is null)
                    {
                        var micro = microInput.SendSubmit();
                        if (micro is
                            MicroReportSendResult.Accepted or
                            MicroReportSendResult.OutcomeUnknown)
                        {
                            return new ComposerAutomationResult(
                                true,
                                Channel:
                                    ComposerAutomationChannel.MicroHid);
                        }

                        if (micro == MicroReportSendResult.Rejected)
                        {
                            return new ComposerAutomationResult(
                                false,
                                AgentAutomationErrorCodes.Unexpected,
                                "micro.input-rejected");
                        }
                    }

                    return codexComposer.SubmitComposer(currentSettings);
                },
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
        ];
        var actionRouter = new ActionRouter(
            codexExecutors
                .Select(executor => new AgentScopedActionExecutor(
                    executor,
                    agentSelection,
                    CodexAgentTarget.CodexId))
                .Append(new AgentScopedActionExecutor(
                    new DeepSeekHarnessActionExecutor(
                        deepSeekAgent,
                        deepSeekActionBlockReason),
                    agentSelection,
                    DeepSeekAgentTarget.DeepSeekId)));
        var actionDispatcher = new ActionDispatcher(actionRouter);
        var navigationContext = new AgentThreadNavigationContext(
            currentSettings,
            () => agentSelection.Active);
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
            agentSelection,
            foregroundApplication,
            settings,
            currentSettings,
            microInput,
            new XInputService(controllerProfiles),
            new ControllerInteractionCoordinator(),
            new ControllerHoldCoordinator(),
            new RadialLayerCoordinator(),
            actionDispatcher,
            threadNavigation,
            new CodexRateLimitResetService());
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
