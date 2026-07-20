using CodexController.Agents;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Presentation.Feedback;

namespace CodexController.Tests;

public sealed class LocalizedBridgeFeedbackFormatterTests
{
    [Fact]
    public void SameFormatterFollowsRuntimeLanguageSwitch()
    {
        var localization = new LocalizationService(AppLanguage.EnUs);
        var formatter = new LocalizedBridgeFeedbackFormatter(
            localization.Strings);
        var bridgeEvent = Event(
            BridgeEventKeys.SidebarScopeChanged,
            ("scope", "pinned_tasks"));

        var english = formatter.Format(bridgeEvent);
        localization.SetLanguage(AppLanguage.ZhCn);
        var chinese = formatter.Format(bridgeEvent);

        Assert.Equal(
            "Sidebar scope: Pinned tasks",
            english.LogText);
        Assert.Equal(
            "侧边栏区域：置顶任务",
            chinese.LogText);
        Assert.NotEqual(english.LogText, chinese.LogText);
    }

    [Fact]
    public void SidebarScopeToastIncludesCurrentProjectWhenAvailable()
    {
        var localization = new LocalizationService(AppLanguage.ZhCn);
        var formatter = new LocalizedBridgeFeedbackFormatter(
            localization.Strings);

        var content = formatter.Format(Event(
            BridgeEventKeys.SidebarScopeChanged,
            ("scope", "PinnedProjects"),
            ("project", "AgentController")));

        Assert.Equal(
            "侧边栏区域：置顶项目 · 项目 › AgentController",
            content.LogText);
        Assert.Equal("项目 › AgentController", content.Toast?.Title);
        Assert.Equal("置顶项目", content.Toast?.Value);
    }

    [Fact]
    public void LocalizesAgentAndProductSpecificEvents()
    {
        var localization = new LocalizationService(AppLanguage.EnUs);
        var formatter = new LocalizedBridgeFeedbackFormatter(
            localization.Strings,
            "Control Deck",
            "Studio Agent");

        var ready = formatter.Format(Event(BridgeEventKeys.AppReady));
        var wake = formatter.Format(Event(
            BridgeEventKeys.CodexWakeSucceeded));

        Assert.Equal("Control Deck is ready", ready.LogText);
        Assert.Equal("Control Deck", ready.Toast?.Title);
        Assert.Equal(
            "Studio Agent is now in the foreground",
            wake.LogText);
        Assert.Equal("Studio Agent", wake.Toast?.Title);
    }

    [Fact]
    public void FormatsControllerReconnectFromExistingLocalizedTerms()
    {
        var localization = new LocalizationService(AppLanguage.EnUs);
        var formatter = new LocalizedBridgeFeedbackFormatter(
            localization.Strings);

        var disconnected = formatter.Format(Event(
            BridgeEventKeys.ControllerDisconnected,
            ("autoResume", "true")));
        var restored = formatter.Format(Event(
            BridgeEventKeys.ControllerConnected,
            ("device", "8BitDo Ultimate"),
            ("restored", "true"),
            ("requiresNeutral", "true")));

        Assert.Equal(
            "Controller disconnected · Reconnecting…",
            disconnected.LogText);
        Assert.Equal(
            "Connected · 8BitDo Ultimate · Controller input locked",
            restored.LogText);
        Assert.Equal(
            "8BitDo Ultimate · Controller input locked",
            restored.Toast?.Value);
    }

    [Theory]
    [InlineData(
        "model.reasoning-effort.changed",
        "effort",
        "extra_high",
        "Reasoning effort: Extra high")]
    [InlineData(
        "model.speed.changed",
        "speed",
        "normal",
        "Speed: Standard")]
    [InlineData(
        "model.selection.changed",
        "model",
        "gpt-5.3-codex",
        "Model: gpt-5.3-codex")]
    public void LocalizesComposerSelectionValues(
        string key,
        string parameter,
        string value,
        string expected)
    {
        var formatter = EnglishFormatter();

        var content = formatter.Format(Event(
            key,
            (parameter, value)));

        Assert.Equal(expected, content.LogText);
    }

    [Theory]
    [InlineData("controller.session.armed")]
    [InlineData("controller.session.locked")]
    [InlineData("controller.session.paused")]
    [InlineData("controller.session.resumed")]
    [InlineData("codex.wake.requested")]
    [InlineData("codex.wake.failed")]
    [InlineData("sidebar.focus.changed")]
    [InlineData("sidebar.entry.opened")]
    [InlineData("sidebar.navigation.undone")]
    [InlineData("composer.dictation.started")]
    [InlineData("composer.dictation.stopped")]
    [InlineData("composer.prompt.sent")]
    [InlineData("composer.action.cancelled")]
    [InlineData("automation.action.failed")]
    [InlineData("future.feature.changed")]
    public void MajorEventsRemainReadableWithMissingParameters(
        string key)
    {
        var content = EnglishFormatter().Format(Event(key));

        Assert.False(string.IsNullOrWhiteSpace(content.LogText));
        Assert.Equal(content.LogText, content.FooterText);
        Assert.False(string.IsNullOrWhiteSpace(content.Toast?.Title));
        Assert.False(string.IsNullOrWhiteSpace(content.Toast?.Value));
    }

    [Fact]
    public void PreservesLegacyTextAcrossLanguageChanges()
    {
        var localization = new LocalizationService(AppLanguage.EnUs);
        var formatter = new LocalizedBridgeFeedbackFormatter(
            localization.Strings);
        var bridgeEvent = Event(
            BridgeEventKeys.LegacyMessage,
            ("text", "Existing diagnostic detail"));

        var before = formatter.Format(bridgeEvent);
        localization.SetLanguage(AppLanguage.ZhCn);
        var after = formatter.Format(bridgeEvent);

        Assert.Equal("Existing diagnostic detail", before.LogText);
        Assert.Equal(before.LogText, after.LogText);
    }

    [Fact]
    public void LocalizesStableWakeFailureCode()
    {
        var content = EnglishFormatter().Format(Event(
            BridgeEventKeys.CodexWakeFailed,
            (
                "reasonCode",
                AgentAutomationErrorCodes.AgentWindowNotFound)));

        Assert.Equal(
            "Could not bring Codex to the foreground: The Agent window could not be found",
            content.LogText);
        Assert.Equal(
            "The Agent window could not be found",
            content.Toast?.Value);
    }

    [Fact]
    public void LocalizesAutomationErrorCodeAndKeepsDiagnosticInLog()
    {
        var content = EnglishFormatter().Format(Event(
            BridgeEventKeys.AutomationFailed,
            ("action", "submit"),
            (
                "errorCode",
                AgentAutomationErrorCodes.InputInjectionFailed),
            ("errorDetail", "SendInput returned 0")));

        Assert.Equal(
            "Operation failed: submit · Key input failed · SendInput returned 0",
            content.LogText);
        Assert.Equal(
            "Key input failed",
            content.Toast?.Value);
    }

    [Fact]
    public void UnknownEventUsesLocalizedNonThrowingFallback()
    {
        var localization = new LocalizationService(AppLanguage.EnUs);
        var formatter = new LocalizedBridgeFeedbackFormatter(
            localization.Strings);
        var bridgeEvent = Event("future.feature.changed");

        var english = formatter.Format(bridgeEvent);
        localization.SetLanguage(AppLanguage.ZhCn);
        var chinese = formatter.Format(bridgeEvent);

        Assert.Equal(
            "Agent Controller: Status updated (future.feature.changed)",
            english.LogText);
        Assert.Equal(
            "Agent Controller：状态已更新 (future.feature.changed)",
            chinese.LogText);
    }

    [Fact]
    public void FormatRejectsNullEvent()
    {
        Assert.Throws<ArgumentNullException>(() =>
            EnglishFormatter().Format(null!));
    }

    private static LocalizedBridgeFeedbackFormatter EnglishFormatter()
    {
        return new LocalizedBridgeFeedbackFormatter(
            new LocalizationService(AppLanguage.EnUs).Strings);
    }

    private static BridgeEvent Event(
        BridgeEventKey key,
        params (string Key, string Value)[] parameters)
    {
        var values = parameters.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal);

        return new BridgeEvent(
            key,
            DateTimeOffset.UnixEpoch,
            BridgeEventSeverity.Info,
            values);
    }
}
