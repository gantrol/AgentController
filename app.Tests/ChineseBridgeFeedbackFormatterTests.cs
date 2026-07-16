using CodexController.Core.Bridge;
using CodexController.Presentation.Feedback;

namespace CodexController.Tests;

public sealed class ChineseBridgeFeedbackFormatterTests
{
    private readonly ChineseBridgeFeedbackFormatter _formatter = new();

    public static TheoryData<string, string, string> BasicEvents =>
        new()
        {
            { "app.ready", "Agent Controller 已就绪", "已就绪" },
            {
                BridgeEventKeys.ControllerArmed.Value,
                "手柄控制已启用",
                "已启用"
            },
            {
                BridgeEventKeys.ControllerDisconnected.Value,
                "手柄连接已断开",
                "连接已断开"
            },
            {
                "codex.wake.started",
                "正在将 Codex 置于前台",
                "正在唤醒"
            },
            {
                "codex.wake.succeeded",
                "Codex 已置于前台",
                "已置于前台"
            },
            {
                BridgeEventKeys.ComposerPromptSent.Value,
                "提示词已发送",
                "已发送"
            },
        };

    [Theory]
    [MemberData(nameof(BasicEvents))]
    public void FormatsCommonEvents(
        string key,
        string expectedLog,
        string expectedToastValue)
    {
        var content = _formatter.Format(Event(key));

        Assert.Equal(expectedLog, content.LogText);
        Assert.Equal(expectedLog, content.FooterText);
        Assert.Equal(expectedToastValue, content.Toast?.Value);
    }

    [Fact]
    public void FormatsConnectionWithOptionalDeviceName()
    {
        var content = _formatter.Format(Event(
            BridgeEventKeys.ControllerConnected,
            ("device", "8BitDo Ultimate")));

        Assert.Equal(
            "手柄已连接：8BitDo Ultimate",
            content.LogText);
        Assert.Equal(
            "8BitDo Ultimate · 已连接",
            content.Toast?.Value);
    }

    [Fact]
    public void KeepsReconnectAndNeutralInstructions()
    {
        var disconnected = _formatter.Format(Event(
            BridgeEventKeys.ControllerDisconnected,
            ("autoResume", "true")));
        var restored = _formatter.Format(Event(
            BridgeEventKeys.ControllerConnected,
            ("restored", "true"),
            ("requiresNeutral", "true")));

        Assert.Equal(
            "手柄连接已断开 · 重连后自动恢复",
            disconnected.LogText);
        Assert.Equal(
            "手柄已重新连接 · 松开按键后自动恢复",
            restored.LogText);
    }

    [Theory]
    [InlineData("PinnedTasks", "置顶任务")]
    [InlineData("pinned-projects", "置顶项目")]
    [InlineData("projects", "普通项目")]
    [InlineData("project_tasks", "项目任务")]
    [InlineData("projectless-tasks", "未归项目任务")]
    public void LocalizesSidebarScopeValues(
        string scope,
        string expected)
    {
        var content = _formatter.Format(Event(
            BridgeEventKeys.SidebarScopeChanged,
            ("scope", scope)));

        Assert.Equal($"侧边栏区域：{expected}", content.LogText);
        Assert.Equal(expected, content.Toast?.Value);
    }

    [Fact]
    public void FormatsSidebarFocusWithoutRequiringEveryParameter()
    {
        var named = _formatter.Format(Event(
            BridgeEventKeys.SidebarFocusChanged,
            ("title", "整理上下文管理需求")));
        var unnamed = _formatter.Format(Event(
            BridgeEventKeys.SidebarFocusChanged));

        Assert.Equal(
            "侧边栏焦点：整理上下文管理需求",
            named.LogText);
        Assert.Equal("侧边栏焦点已移动", unnamed.LogText);
    }

    [Theory]
    [InlineData("minimal", "最低")]
    [InlineData("medium", "中")]
    [InlineData("extra_high", "极高")]
    public void LocalizesReasoningEffort(
        string effort,
        string expected)
    {
        var content = _formatter.Format(Event(
            BridgeEventKeys.ReasoningEffortChanged,
            ("effort", effort)));

        Assert.Equal($"思考强度：{expected}", content.LogText);
    }

    [Theory]
    [InlineData("fast", "Fast")]
    [InlineData("standard", "标准")]
    public void LocalizesSpeed(string speed, string expected)
    {
        var content = _formatter.Format(Event(
            BridgeEventKeys.SpeedChanged,
            ("speed", speed)));

        Assert.Equal($"速度：{expected}", content.LogText);
    }

    [Fact]
    public void FormatsModelAndWakeFailureDetails()
    {
        var model = _formatter.Format(Event(
            BridgeEventKeys.ModelChanged,
            ("model", "gpt-5.3-codex")));
        var wakeFailure = _formatter.Format(Event(
            "codex.wake.failed",
            ("reasonCode", "window-unavailable")));

        Assert.Equal("模型：gpt-5.3-codex", model.LogText);
        Assert.Equal(
            "无法将 Codex 置于前台：未找到或无法启动 Codex",
            wakeFailure.LogText);
    }

    [Fact]
    public void UnknownEventUsesReadableNonThrowingFallback()
    {
        var content = _formatter.Format(Event(
            "future.feature.changed",
            ("value", "opaque-value")));

        Assert.Equal(
            "Agent Controller 状态已更新（future.feature.changed）",
            content.LogText);
        Assert.Equal("Agent Controller", content.Toast?.Title);
        Assert.Equal("状态已更新", content.Toast?.Value);
    }

    [Fact]
    public void PreservesLegacyLogTextDuringIncrementalMigration()
    {
        var content = _formatter.Format(Event(
            BridgeEventKeys.LegacyMessage,
            ("text", "已刷新 Codex 本机任务")));

        Assert.Equal("已刷新 Codex 本机任务", content.LogText);
    }

    [Fact]
    public void FormatRejectsNullEvent()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _formatter.Format(null!));
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
