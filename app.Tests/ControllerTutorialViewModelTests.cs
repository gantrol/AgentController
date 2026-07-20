using CodexController.Controllers;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation.Dispatch;
using CodexController.ViewModels;

namespace CodexController.Tests;

public sealed class ControllerTutorialViewModelTests
{
    [Fact]
    public void ExposesInteractiveLayersAndExplicitStickPressHelp()
    {
        var viewModel = Create(
            AppLanguage.EnUs,
            BuiltInControllerProfiles.Xbox);

        Assert.Equal(ControllerTutorialMode.Overview, viewModel.Mode);
        Assert.Equal("Tap Y", viewModel.ActionTabLabel);
        Assert.Equal("Hold LB", viewModel.AgentTabLabel);
        Assert.Equal("Hold RT", viewModel.TurnTabLabel);
        Assert.Equal("Hold RB", viewModel.CommandTabLabel);
        Assert.Contains("L3", viewModel.StickPressGuideTitle);
        Assert.Contains("press", viewModel.StickPressGuideTitle);
        Assert.Contains("LS / L3", viewModel.LeftStickPressGuide);
        Assert.Contains("RS / R3", viewModel.RightStickPressGuide);
        Assert.Contains(
            viewModel.Items,
            item =>
                item.Glyph == "⧉" &&
                item.Title == "View: reserved" &&
                item.Description.Contains(
                    "switch the controlled Agent",
                    StringComparison.Ordinal));
        var chinese = Create(
            AppLanguage.ZhCn,
            BuiltInControllerProfiles.Xbox);
        Assert.Contains(
            chinese.Items,
            item =>
                item.Title == "View：保留键" &&
                item.Description.Contains(
                    "后续可能用于切换控制不同 Agent",
                    StringComparison.Ordinal));

        viewModel.SelectStickPressCommand.Execute(null);

        Assert.True(viewModel.IsStickPressMode);
        Assert.Equal(2, viewModel.Items.Count);
        Assert.Contains("vertically", viewModel.ModeDescription);
        Assert.Contains(
            viewModel.Items,
            item => item.Glyph.Contains("L3", StringComparison.Ordinal));
        Assert.Contains(
            viewModel.Items,
            item => item.Glyph.Contains("R3", StringComparison.Ordinal));
    }

    [Fact]
    public void UsesLiveProfileGlyphsForViewAndMenu()
    {
        var viewModel = Create(
            AppLanguage.EnUs,
            BuiltInControllerProfiles.Ultimate2);

        viewModel.SelectAgentCommand.Execute(null);

        Assert.Contains(
            viewModel.Items,
            item => item.Glyph == "−" && item.Title == "Agent 5");
        Assert.Contains(
            viewModel.Items,
            item => item.Glyph == "+" && item.Title == "Agent 6");

        viewModel.SelectCommandCommand.Execute(null);

        Assert.Contains(viewModel.Items, item => item.Glyph == "−");
        Assert.Contains(viewModel.Items, item => item.Glyph == "+");
    }

    [Fact]
    public void RuntimeLayerStateSelectsTutorialOnlyAfterEngagement()
    {
        var viewModel = Create(
            AppLanguage.EnUs,
            BuiltInControllerProfiles.Xbox);

        viewModel.ObserveStickPresses(
            State(ControllerButtons.RightShoulder));
        viewModel.UpdateActiveLayer(
            RadialMenuLayerKind.Command,
            isEngaged: false,
            isCancelled: false);
        Assert.Equal(ControllerTutorialMode.Overview, viewModel.Mode);

        viewModel.UpdateActiveLayer(
            RadialMenuLayerKind.Command,
            isEngaged: true,
            isCancelled: false);
        Assert.Equal(ControllerTutorialMode.Command, viewModel.Mode);

        viewModel.UpdateActiveLayer(
            RadialMenuLayerKind.Agent,
            isEngaged: true,
            isCancelled: true);
        Assert.Equal(ControllerTutorialMode.Command, viewModel.Mode);

        viewModel.UpdateActiveLayer(
            RadialMenuLayerKind.Turn,
            isEngaged: true,
            isCancelled: false);
        Assert.Equal(ControllerTutorialMode.Turn, viewModel.Mode);

        viewModel.UpdateActiveLayer(
            RadialMenuLayerKind.Action,
            isEngaged: true,
            isCancelled: false);
        Assert.Equal(ControllerTutorialMode.Action, viewModel.Mode);
    }

    [Fact]
    public void StickButtonEdgeSelectsExplicitPressLesson()
    {
        var viewModel = Create(
            AppLanguage.EnUs,
            BuiltInControllerProfiles.Xbox);

        viewModel.ObserveStickPresses(State(ControllerButtons.RightThumb));

        Assert.Equal(ControllerTutorialMode.StickPress, viewModel.Mode);
    }

    [Fact]
    public void CommandLessonAcceptsTheLiveDispatchPresentation()
    {
        var viewModel = Create(
            AppLanguage.EnUs,
            BuiltInControllerProfiles.Xbox);
        viewModel.SelectCommandCommand.Execute(null);

        viewModel.UpdateDispatchPresentation(new DispatchDisplay(
            DispatchDisplayKind.Queue,
            "Queue next turn",
            "Runs after the current turn"));

        Assert.Contains(
            viewModel.Items,
            item =>
                item.Glyph == "☰" &&
                item.Title == "Queue next turn" &&
                item.Description == "Runs after the current turn");
    }

    [Fact]
    public void ManualCommandLessonRefreshesDispatchBeforeRendering()
    {
        ControllerTutorialViewModel? viewModel = null;
        viewModel = new ControllerTutorialViewModel(() =>
            viewModel!.UpdateDispatchPresentation(new DispatchDisplay(
                DispatchDisplayKind.Steer,
                "Steer current turn",
                "Adds input to the running turn")));
        var strings = new LocalizationService(AppLanguage.EnUs).Strings;
        viewModel.UpdateContext(
            strings,
            BuiltInControllerProfiles.Xbox,
            strings.ControlLeftStickHint("LS", "A"),
            strings.ControlRightStickHint("RS", "B", "A"));

        viewModel.SelectCommandCommand.Execute(null);

        Assert.Contains(
            viewModel.Items,
            item =>
                item.Glyph == "☰" &&
                item.Title == "Steer current turn" &&
                item.Description == "Adds input to the running turn");
    }

    [Fact]
    public void CoordinatorQuickTapAndEngagementDriveDifferentLessons()
    {
        var viewModel = Create(
            AppLanguage.EnUs,
            BuiltInControllerProfiles.Xbox);
        using var coordinator = new RadialLayerCoordinator();

        coordinator.ProcessFrame(
            State(ControllerButtons.LeftShoulder),
            new ControllerButtonEdges(
                ControllerButtons.LeftShoulder,
                ControllerButtons.None),
            now: 1000);
        SyncTutorial(viewModel, coordinator);
        Assert.Equal(ControllerTutorialMode.Overview, viewModel.Mode);

        coordinator.ProcessFrame(
            State(),
            new ControllerButtonEdges(
                ControllerButtons.None,
                ControllerButtons.LeftShoulder),
            now: 1100);
        SyncTutorial(viewModel, coordinator);
        Assert.Equal(ControllerTutorialMode.Overview, viewModel.Mode);

        coordinator.ProcessFrame(
            State(ControllerButtons.RightShoulder),
            new ControllerButtonEdges(
                ControllerButtons.RightShoulder,
                ControllerButtons.None),
            now: 2000);
        coordinator.PromoteLearningCue(
            ControllerButtons.RightShoulder);
        SyncTutorial(viewModel, coordinator);

        Assert.Equal(ControllerTutorialMode.Command, viewModel.Mode);
    }

    [Fact]
    public void LayerItemsKeepRuntimeActionIdsAndDescriptions()
    {
        var viewModel = Create(
            AppLanguage.ZhCn,
            BuiltInControllerProfiles.Xbox);

        viewModel.SelectActionCommand.Execute(null);
        Assert.Equal(6, viewModel.Items.Count);
        Assert.Contains(viewModel.Items, item => item.Glyph == "↑");
        Assert.Contains(viewModel.Items, item => item.Glyph == "A");

        viewModel.SelectTurnCommand.Execute(null);
        Assert.Equal(4, viewModel.Items.Count);
        Assert.Contains(
            viewModel.Items,
            item => item.Glyph == "B" && item.Description.Contains("3 秒"));

        viewModel.SelectCommandCommand.Execute(null);
        Assert.Equal(6, viewModel.Items.Count);
        Assert.Contains(
            viewModel.Items,
            item => item.Glyph == "A" && item.Description.Contains("二次确认"));
    }

    private static ControllerTutorialViewModel Create(
        AppLanguage language,
        ControllerProfile profile)
    {
        var strings = new LocalizationService(language).Strings;
        var viewModel = new ControllerTutorialViewModel();
        viewModel.UpdateContext(
            strings,
            profile,
            strings.ControlLeftStickHint("LS", "A"),
            strings.ControlRightStickHint(
                "RS",
                "B",
                "A",
                menuOpen: false,
                requiresConfirmation: false));
        return viewModel;
    }

    private static void SyncTutorial(
        ControllerTutorialViewModel viewModel,
        RadialLayerCoordinator coordinator) =>
        viewModel.UpdateActiveLayer(
            coordinator.Layer,
            coordinator.IsEngaged,
            coordinator.IsCancelled);

    private static ControllerState State(
        ControllerButtons buttons = ControllerButtons.None,
        double rightTrigger = 0) =>
        new(
            IsConnected: true,
            UserIndex: 0,
            PacketNumber: 1,
            Backend: "Tests",
            Buttons: buttons,
            LeftX: 0,
            LeftY: 0,
            RightX: 0,
            RightY: 0,
            LeftTrigger: 0,
            RightTrigger: rightTrigger);
}
