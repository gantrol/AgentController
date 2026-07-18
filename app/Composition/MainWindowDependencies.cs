using AgentController.Application.Actions;
using AgentController.Application.Navigation;
using AgentController.Platform.Windowing;
using CodexController.Agents;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Services;
using CodexController.Services.Micro;

namespace CodexController.Composition;

internal sealed record MainWindowDependencies(
    BridgeEventHub BridgeEvents,
    LocalizationService Localization,
    ControllerProfileRegistry ControllerProfiles,
    IAgentTarget ActiveAgent,
    IForegroundApplication ForegroundApplication,
    SettingsService Settings,
    AppSettings CurrentSettings,
    MicroInputService MicroInput,
    XInputService Controller,
    ControllerInteractionCoordinator ControllerInteraction,
    ControllerHoldCoordinator ControllerHolds,
    RadialLayerCoordinator RadialLayers,
    ActionDispatcher ActionDispatcher,
    ThreadNavigationCoordinator ThreadNavigation);
