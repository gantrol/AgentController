using CodexController.Core.Bridge;

namespace CodexController.Presentation.Feedback;

/// <summary>
/// Formats locale-independent bridge events for the current Simplified
/// Chinese interface. It deliberately tolerates missing parameters so that
/// diagnostic feedback can never interrupt controller input handling.
/// </summary>
public sealed class ChineseBridgeFeedbackFormatter :
    IBridgeFeedbackFormatter
{
    public BridgeFeedbackContent Format(BridgeEvent bridgeEvent)
    {
        ArgumentNullException.ThrowIfNull(bridgeEvent);

        var key = bridgeEvent.Key.Value;
        return key switch
        {
            "app.ready" => Content(
                "Agent Controller 已就绪",
                "Agent Controller",
                "已就绪"),

            "diagnostic.legacy.message" =>
                FormatLegacyMessage(bridgeEvent),

            "controller.device.connected" or
            "controller.connection.restored" =>
                FormatControllerConnection(bridgeEvent, connected: true),

            "controller.device.disconnected" or
            "controller.connection.lost" =>
                FormatControllerConnection(bridgeEvent, connected: false),

            "controller.session.armed" => Content(
                "手柄控制已启用",
                "手柄控制",
                "已启用"),

            "controller.session.locked" => Content(
                "手柄控制已锁定",
                "手柄控制",
                "已锁定"),

            "controller.session.paused" => Content(
                "手柄控制已暂停",
                "手柄控制",
                "已暂停"),

            "controller.session.resumed" => Content(
                "手柄控制已恢复",
                "手柄控制",
                "已恢复"),

            "codex.wake.started" or
            "codex.wake.requested" => Content(
                "正在将 Codex 置于前台",
                "Codex",
                "正在唤醒"),

            "codex.wake.succeeded" => Content(
                "Codex 已置于前台",
                "Codex",
                "已置于前台"),

            "codex.wake.failed" => FormatWakeFailure(bridgeEvent),

            "sidebar.scope.changed" or
            "navigation.scope.changed" => FormatSidebarScope(bridgeEvent),

            "sidebar.focus.changed" or
            "navigation.focus.changed" => FormatSidebarFocus(bridgeEvent),

            "sidebar.entry.opened" => FormatSidebarEntryOpened(bridgeEvent),

            "sidebar.navigation.undone" => Content(
                "已撤回上一次侧边栏跳转",
                "侧边栏",
                "已撤回跳转"),

            "model.selection.changed" => FormatValueChange(
                bridgeEvent,
                "模型",
                ["model", "value", "label"]),

            "model.reasoning-effort.changed" => FormatValueChange(
                bridgeEvent,
                "思考强度",
                ["reasoning", "effort", "value", "label"],
                FormatReasoningValue),

            "model.speed.changed" => FormatValueChange(
                bridgeEvent,
                "速度",
                ["speed", "value", "label"],
                FormatSpeedValue),

            "composer.dictation.started" => Content(
                "语音输入已开始",
                "语音输入",
                "正在聆听"),

            "composer.dictation.stopped" => Content(
                "语音输入已结束",
                "语音输入",
                "已结束"),

            "composer.prompt.sent" => Content(
                "提示词已发送",
                "Codex",
                "已发送"),

            "composer.action.cancelled" => Content(
                "已取消当前操作",
                "Codex",
                "已取消"),

            "automation.action.failed" =>
                FormatAutomationFailure(bridgeEvent),

            _ => FormatFallback(bridgeEvent),
        };
    }

    private static BridgeFeedbackContent FormatControllerConnection(
        BridgeEvent bridgeEvent,
        bool connected)
    {
        var device = FirstParameter(
            bridgeEvent,
            "device",
            "controller",
            "name");
        var restored = BooleanParameter(bridgeEvent, "restored");
        var requiresNeutral = BooleanParameter(
            bridgeEvent,
            "requiresNeutral");
        var autoResume = BooleanParameter(bridgeEvent, "autoResume");
        var status = connected
            ? restored
                ? "已重新连接"
                : "已连接"
            : "连接已断开";
        var instruction = connected && requiresNeutral
            ? restored
                ? "松开按键后自动恢复"
                : "松开按键后启用"
            : !connected && autoResume
                ? "重连后自动恢复"
                : null;
        var logText = device is null
            ? $"手柄{status}"
            : $"手柄{status}：{device}";
        if (instruction is not null)
        {
            logText = $"{logText} · {instruction}";
        }

        return Content(
            logText,
            "手柄连接",
            string.Join(
                " · ",
                new[] { device, status, instruction }
                    .Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static BridgeFeedbackContent FormatLegacyMessage(
        BridgeEvent bridgeEvent)
    {
        var text = FirstParameter(bridgeEvent, "text");
        return Content(
            text ?? "Agent Controller 状态已更新",
            "Agent Controller",
            text ?? "状态已更新");
    }

    private static BridgeFeedbackContent FormatWakeFailure(
        BridgeEvent bridgeEvent)
    {
        var reason = FirstParameter(
            bridgeEvent,
            "reasonCode",
            "reason",
            "error");
        reason = reason switch
        {
            "window-unavailable" => "未找到或无法启动 Codex",
            _ => reason,
        };

        return Content(
            reason is null
                ? "无法将 Codex 置于前台"
                : $"无法将 Codex 置于前台：{reason}",
            "Codex",
            reason ?? "唤醒失败，请重试");
    }

    private static BridgeFeedbackContent FormatSidebarScope(
        BridgeEvent bridgeEvent)
    {
        var rawScope = FirstParameter(
            bridgeEvent,
            "scope",
            "value");
        var scope = rawScope is null
            ? null
            : FormatScopeValue(rawScope);

        return Content(
            scope is null
                ? "侧边栏区域已切换"
                : $"侧边栏区域：{scope}",
            "侧边栏",
            scope ?? "区域已切换");
    }

    private static BridgeFeedbackContent FormatSidebarFocus(
        BridgeEvent bridgeEvent)
    {
        var label = FirstParameter(
            bridgeEvent,
            "label",
            "title",
            "name",
            "value");

        return Content(
            label is null
                ? "侧边栏焦点已移动"
                : $"侧边栏焦点：{label}",
            "侧边栏",
            label ?? "焦点已移动");
    }

    private static BridgeFeedbackContent FormatSidebarEntryOpened(
        BridgeEvent bridgeEvent)
    {
        var label = FirstParameter(
            bridgeEvent,
            "label",
            "title",
            "name",
            "value");

        return Content(
            label is null
                ? "已打开侧边栏项目"
                : $"已打开：{label}",
            "侧边栏",
            label ?? "已打开");
    }

    private static BridgeFeedbackContent FormatValueChange(
        BridgeEvent bridgeEvent,
        string category,
        string[] parameterKeys,
        Func<string, string>? formatValue = null)
    {
        var rawValue = FirstParameter(bridgeEvent, parameterKeys);
        var value = rawValue is null
            ? null
            : (formatValue?.Invoke(rawValue) ?? rawValue);

        return Content(
            value is null
                ? $"{category}已切换"
                : $"{category}：{value}",
            category,
            value ?? "已切换");
    }

    private static BridgeFeedbackContent FormatAutomationFailure(
        BridgeEvent bridgeEvent)
    {
        var action = FirstParameter(
            bridgeEvent,
            "action",
            "operation");
        var reason = FirstParameter(
            bridgeEvent,
            "reason",
            "error");
        var subject = action is null
            ? "自动操作"
            : $"自动操作“{action}”";
        var logText = reason is null
            ? $"{subject}失败"
            : $"{subject}失败：{reason}";

        return Content(
            logText,
            "操作失败",
            reason ?? $"{action ?? "自动操作"}未完成");
    }

    private static BridgeFeedbackContent FormatFallback(
        BridgeEvent bridgeEvent)
    {
        var key = bridgeEvent.Key.Value;
        return Content(
            $"Agent Controller 状态已更新（{key}）",
            "Agent Controller",
            "状态已更新");
    }

    private static string FormatScopeValue(string value)
    {
        return Normalize(value) switch
        {
            "pinnedtasks" or "pinned-tasks" => "置顶任务",
            "pinnedprojects" or "pinned-projects" => "置顶项目",
            "projects" => "普通项目",
            "projecttasks" or "project-tasks" => "项目任务",
            "projectlesstasks" or "projectless-tasks" => "未归项目任务",
            _ => value,
        };
    }

    private static string FormatReasoningValue(string value)
    {
        return Normalize(value) switch
        {
            "minimal" => "最低",
            "low" => "低",
            "medium" => "中",
            "high" => "高",
            "xhigh" or "extra-high" => "极高",
            _ => value,
        };
    }

    private static string FormatSpeedValue(string value)
    {
        return Normalize(value) switch
        {
            "fast" => "Fast",
            "standard" or "normal" => "标准",
            _ => value,
        };
    }

    private static string Normalize(string value)
    {
        return value.Trim().Replace("_", "-", StringComparison.Ordinal)
            .ToLowerInvariant();
    }

    private static string? FirstParameter(
        BridgeEvent bridgeEvent,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (
                bridgeEvent.Parameters.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool BooleanParameter(
        BridgeEvent bridgeEvent,
        string key)
    {
        return
            bridgeEvent.Parameters.TryGetValue(key, out var value) &&
            bool.TryParse(value, out var parsed) &&
            parsed;
    }

    private static BridgeFeedbackContent Content(
        string logText,
        string toastTitle,
        string toastValue)
    {
        return new BridgeFeedbackContent(
            logText,
            logText,
            new BridgeToastText(toastTitle, toastValue));
    }
}
