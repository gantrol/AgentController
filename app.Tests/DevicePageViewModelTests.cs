using System.Collections.ObjectModel;
using CodexController.Controllers;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation.Feedback;
using CodexController.ViewModels;

namespace CodexController.Tests;

public sealed class DevicePageViewModelTests
{
    [Fact]
    public void HoldsExistingCollectionsAndForwardsThinCommands()
    {
        var sidebarEntries = new ObservableCollection<SidebarEntry>();
        var mutableEvents =
            new ObservableCollection<BridgeFeedbackLogRow>();
        var recentEvents =
            new ReadOnlyObservableCollection<BridgeFeedbackLogRow>(
                mutableEvents);
        var refreshCount = 0;
        var selectedScopes = new List<SidebarScope>();
        var viewModel = new DevicePageViewModel(
            sidebarEntries,
            recentEvents,
            () => refreshCount++,
            selectedScopes.Add);

        viewModel.RefreshCommand.Execute(null);
        viewModel.SelectPinnedTasksCommand.Execute(null);
        viewModel.SelectPinnedProjectsCommand.Execute(null);
        viewModel.SelectProjectsCommand.Execute(null);
        viewModel.SelectProjectlessTasksCommand.Execute(null);

        Assert.Same(sidebarEntries, viewModel.SidebarEntries);
        Assert.Same(recentEvents, viewModel.RecentEvents);
        Assert.Equal(1, refreshCount);
        Assert.Equal(
            [
                SidebarScope.PinnedTasks,
                SidebarScope.PinnedProjects,
                SidebarScope.Projects,
                SidebarScope.ProjectlessTasks,
            ],
            selectedScopes);
    }

    [Fact]
    public void ContextUsesActiveAgentAndControllerProfileGlyphs()
    {
        var viewModel = CreateViewModel();
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;

        viewModel.UpdateContext(
            strings,
            "Studio Agent",
            BuiltInControllerProfiles.Ultimate2);

        Assert.Equal("Studio Agent", viewModel.AgentName);
        Assert.Equal(
            BuiltInControllerProfiles.Ultimate2.Id,
            viewModel.ControllerProfileId);
        Assert.Equal("A", viewModel.PrimaryGlyph);
        Assert.Equal("LT", viewModel.VoiceGlyph);
        Assert.Equal("X", viewModel.SendGlyph);
        Assert.Equal("B", viewModel.CancelGlyph);
        Assert.Equal("Y", viewModel.ProjectGlyph);
        Assert.Equal("+", viewModel.WakeGlyph);
        Assert.Equal(
            "↑↓ Move focus · → Enter project · ← Exit project · A Open task · LS changes root",
            viewModel.LeftStickHint);
        Assert.Equal(
            "Simple: ← / → Power, ↑ Standard, ↓ Fast · Advanced: ← / → changes control, ↑ / ↓ changes an available value · click RS opens the picker · hold RS Settings",
            viewModel.RightStickHint);
        Assert.Equal(
            "A · Open task",
            viewModel.PrimaryActionTitle);
        Assert.Equal("LT · Hold to talk", viewModel.VoiceActionTitle);
        Assert.Equal("X · Send", viewModel.SendActionTitle);
        Assert.Equal(
            "Y · Action panel",
            viewModel.ProjectActionTitle);
        Assert.Contains("Studio Agent", viewModel.WakeActionTitle);
        Assert.Equal(
            "Studio Agent sidebar",
            viewModel.SidebarTitle);
        Assert.Equal("LT", viewModel.LeftTriggerGlyph);
        Assert.Equal("RB", viewModel.RightShoulderGlyph);
    }

    [Fact]
    public void ContextRefreshesDynamicTextForNewLanguageAndProfile()
    {
        var viewModel = CreateViewModel();
        var english =
            new LocalizationService(AppLanguage.EnUs).Strings;
        var chinese =
            new LocalizationService(AppLanguage.ZhCn).Strings;

        viewModel.UpdateContext(
            english,
            "Codex",
            BuiltInControllerProfiles.Generic);
        viewModel.UpdateContext(
            chinese,
            "Codex",
            BuiltInControllerProfiles.Xbox);

        Assert.Equal(
            BuiltInControllerProfiles.Xbox.Id,
            viewModel.ControllerProfileId);
        Assert.Equal("☰", viewModel.WakeGlyph);
        Assert.Contains("Codex", viewModel.SidebarTitle);
        Assert.Contains("侧边栏", viewModel.SidebarTitle);
        Assert.Contains("打开任务", viewModel.PrimaryActionTitle);
        Assert.Contains("按住说话", viewModel.VoiceActionTitle);
    }

    [Fact]
    public void ConnectionPresentationTracksStateAndProfile()
    {
        var viewModel = CreateViewModel();
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;
        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Ultimate2);

        viewModel.UpdateControllerState(ControllerState.Disconnected);

        Assert.False(viewModel.IsControllerConnected);
        Assert.Equal(
            "Waiting for controller",
            viewModel.ControllerStatusText);
        Assert.Equal("Idle", viewModel.ControllerLiveBadge);

        viewModel.UpdateControllerState(
            ConnectedState("Windows.Gaming.Input"));

        Assert.True(viewModel.IsControllerConnected);
        Assert.Equal(
            "8BitDo Ultimate 2 · Windows.Gaming.Input",
            viewModel.ControllerStatusText);
        Assert.Equal("Live input", viewModel.ControllerLiveBadge);

        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Xbox);

        Assert.Equal(
            "Xbox Controller · Windows.Gaming.Input",
            viewModel.ControllerStatusText);
    }

    [Fact]
    public void RightModeExposesLocalizedLabelValueAndSelectionFlags()
    {
        var viewModel = CreateViewModel();
        var chinese =
            new LocalizationService(AppLanguage.ZhCn).Strings;
        viewModel.UpdateContext(
            chinese,
            "Codex",
            BuiltInControllerProfiles.Generic);

        Assert.Equal(RightControlMode.Dial, viewModel.RightMode);
        Assert.Equal("简易模式", viewModel.RightModeLabel);

        viewModel.UpdateRightMode(
            RightControlMode.Model,
            "gpt-5.2-codex");

        Assert.False(viewModel.IsReasoningMode);
        Assert.True(viewModel.IsModelMode);
        Assert.False(viewModel.IsSpeedMode);
        Assert.Equal("模型", viewModel.RightModeLabel);
        Assert.Equal("gpt-5.2-codex", viewModel.RightModeValue);

        viewModel.UpdateRightModeValue("正在应用…");

        Assert.Equal("正在应用…", viewModel.RightModeValue);

        viewModel.UpdateRightMode(
            RightControlMode.Speed,
            chinese.SpeedValue("fast"));

        Assert.True(viewModel.IsSpeedMode);
        Assert.Equal("速度", viewModel.RightModeLabel);
        Assert.Equal("Fast", viewModel.RightModeValue);
    }

    [Fact]
    public void DialPromptTracksConnectionWithoutMaskingActualValues()
    {
        var viewModel = CreateViewModel();
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;
        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Generic);

        Assert.Equal(
            "Connect a controller to begin",
            viewModel.RightModeValue);

        viewModel.UpdateControllerState(
            ConnectedState("Windows.Gaming.Input"));

        Assert.Equal(
            "Simple mode · live Power and Speed controls",
            viewModel.RightModeValue);

        viewModel.UpdateControllerState(ControllerState.Disconnected);

        foreach (var (mode, value) in new[]
                 {
                     (RightControlMode.Reasoning, "High"),
                     (RightControlMode.Model, "gpt-5.2-codex"),
                     (RightControlMode.Speed, "Fast"),
                 })
        {
            viewModel.UpdateRightMode(mode, value);
            Assert.Equal(value, viewModel.RightModeValue);
        }

        viewModel.UpdateRightMode(
            RightControlMode.Dial,
            strings.ComposerDialReady);

        Assert.Equal(
            "Connect a controller to begin",
            viewModel.RightModeValue);

        var chinese =
            new LocalizationService(AppLanguage.ZhCn).Strings;
        viewModel.UpdateContext(
            chinese,
            "Codex",
            BuiltInControllerProfiles.Generic);

        Assert.Equal("连接手柄后开始", viewModel.RightModeValue);

        viewModel.UpdateControllerState(
            ConnectedState("Windows.Gaming.Input"));

        Assert.Equal(
            "简易模式 · 实际 Power 与速度控件",
            viewModel.RightModeValue);
    }

    [Fact]
    public void VirtualDialHintTracksNativePickerState()
    {
        var viewModel = CreateViewModel();
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;
        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Ultimate2);

        viewModel.UpdateVirtualDialMenuState(isOpen: true);

        Assert.Equal(
            "Model picker active · ↑ / ↓ Move · A Select · B Close",
            viewModel.RightStickHint);

        viewModel.UpdateVirtualDialMenuState(isOpen: false);

        Assert.Equal(
            "Simple: ← / → Power, ↑ Standard, ↓ Fast · Advanced: ← / → changes control, ↑ / ↓ changes an available value · click RS opens the picker · hold RS Settings",
            viewModel.RightStickHint);
    }

    [Fact]
    public void VirtualDialHintExplainsFullAccessConfirmationButtons()
    {
        var viewModel = CreateViewModel();
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;
        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Ultimate2);

        viewModel.UpdateVirtualDialMenuState(
            isOpen: true,
            requiresConfirmation: true);

        Assert.Equal(
            "Model selection confirmation · A Confirm · B Cancel",
            viewModel.RightStickHint);
    }

    [Fact]
    public void ProjectTasksUsesExplicitRootForHighlight()
    {
        var viewModel = CreateViewModel();
        var strings =
            new LocalizationService(AppLanguage.EnUs).Strings;
        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Generic);

        viewModel.UpdateSidebarScope(
            SidebarScope.ProjectTasks,
            SidebarScope.PinnedProjects,
            "Pinned workspace",
            projectTasksPinnedOnly: true);

        Assert.Equal(
            SidebarScope.ProjectTasks,
            viewModel.CurrentSidebarScope);
        Assert.Equal(
            SidebarScope.PinnedProjects,
            viewModel.ActiveRootScope);
        Assert.False(viewModel.IsPinnedTasksRootActive);
        Assert.True(viewModel.IsPinnedProjectsRootActive);
        Assert.False(viewModel.IsProjectsRootActive);
        Assert.Equal(
            "Pinned workspace › Pinned in this project",
            viewModel.SidebarContextText);
        Assert.True(viewModel.IsProjectDirectory);
        Assert.True(viewModel.IsProjectTasksPinnedOnly);
        Assert.Equal(
            "Pinned workspace",
            viewModel.SidebarProjectName);
        Assert.Equal(
            "Pinned in this project",
            viewModel.SidebarProjectFilterText);
    }

    [Fact]
    public void RootScopeUpdatesHighlightAndLocalizedContext()
    {
        var viewModel = CreateViewModel();
        var strings =
            new LocalizationService(AppLanguage.ZhCn).Strings;
        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Generic);

        viewModel.UpdateSidebarScope(
            SidebarScope.ProjectlessTasks);

        Assert.Equal(
            SidebarScope.ProjectlessTasks,
            viewModel.ActiveRootScope);
        Assert.True(viewModel.IsProjectlessTasksRootActive);
        Assert.Equal(
            strings.SidebarProjectlessTasks,
            viewModel.SidebarContextText);
        Assert.False(viewModel.IsProjectDirectory);
        Assert.False(viewModel.IsProjectTasksPinnedOnly);
    }

    [Fact]
    public void ProjectTasksRejectsMissingOrUnrelatedRootScope()
    {
        var viewModel = CreateViewModel();

        Assert.Throws<ArgumentException>(() =>
            viewModel.UpdateSidebarScope(
                SidebarScope.ProjectTasks));
        Assert.Throws<ArgumentException>(() =>
            viewModel.UpdateSidebarScope(
                SidebarScope.ProjectTasks,
                SidebarScope.PinnedTasks));
    }

    [Fact]
    public void AgentStatusRemainsPresentationOnly()
    {
        var viewModel = CreateViewModel();

        viewModel.UpdateAgentStatus(
            "Codex is armed",
            isActive: true);

        Assert.Equal("Codex is armed", viewModel.AgentStatusText);
        Assert.True(viewModel.IsAgentStatusActive);
    }

    private static DevicePageViewModel CreateViewModel()
    {
        return new DevicePageViewModel(
            new ObservableCollection<SidebarEntry>(),
            new ReadOnlyObservableCollection<BridgeFeedbackLogRow>(
                new ObservableCollection<BridgeFeedbackLogRow>()),
            () => { },
            _ => { });
    }

    private static ControllerState ConnectedState(string backend)
    {
        return new ControllerState(
            true,
            0,
            1,
            backend,
            ControllerButtons.None,
            0,
            0,
            0,
            0,
            0,
            0);
    }
}
