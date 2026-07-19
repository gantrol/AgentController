using CodexController.Controllers;
using CodexController.Localization;
using CodexController.Models;

namespace CodexController.Presentation;

internal sealed record ControllerLayerItemPresentation(
    string Id,
    RadialMenuSlotPosition Position,
    LogicalInput Input,
    string Title,
    string? Description = null);

internal sealed record ControllerCommandPresentationOptions(
    string FastLabel,
    string DictationLabel,
    string DispatchLabel,
    string DispatchDescription,
    string ApproveGlyph,
    bool IsApproveConfirmationPending = false);

internal sealed record ControllerActionPresentationOptions(
    string ClearGlyph,
    bool IsClearConfirmationPending = false);

/// <summary>
/// Produces the user-facing definition of each controller layer. The runtime
/// overlay and the dashboard tutorial both project these definitions, so a
/// button cannot silently acquire two different labels in the two surfaces.
/// </summary>
internal static class ControllerLayerPresentationFactory
{
    internal static IReadOnlyList<ControllerLayerItemPresentation>
        Command(
            AppLanguage language,
            ControllerCommandPresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return
        [
            new(
                "command-fast",
                RadialMenuSlotPosition.Top,
                LogicalInput.FaceNorth,
                options.FastLabel),
            new(
                "command-decline",
                RadialMenuSlotPosition.Right,
                LogicalInput.FaceEast,
                Text(language, "拒绝更改", "Decline changes")),
            new(
                "command-approve",
                RadialMenuSlotPosition.Bottom,
                LogicalInput.FaceSouth,
                Text(language, "接受更改", "Approve changes"),
                options.IsApproveConfirmationPending
                    ? Text(
                        language,
                        $"再次按 {options.ApproveGlyph} 确认",
                        $"Press {options.ApproveGlyph} again to confirm")
                    : Text(
                        language,
                        "需要二次确认",
                        "Requires confirmation")),
            new(
                "command-fork",
                RadialMenuSlotPosition.Left,
                LogicalInput.FaceWest,
                Text(language, "分支任务", "Fork task")),
            new(
                "command-ptt",
                RadialMenuSlotPosition.CenterLeft,
                LogicalInput.View,
                options.DictationLabel,
                Text(language, "按住说话", "Hold to talk")),
            new(
                "command-dispatch",
                RadialMenuSlotPosition.CenterRight,
                LogicalInput.Menu,
                options.DispatchLabel,
                options.DispatchDescription),
        ];
    }

    internal static IReadOnlyList<ControllerLayerItemPresentation>
        Turn(AppLanguage language)
    {
        var contextual = Text(
            language,
            "仅在 Codex 显示对应操作时可用",
            "Available only when Codex shows the matching action");
        return
        [
            new(
                "turn-queue",
                RadialMenuSlotPosition.Top,
                LogicalInput.FaceNorth,
                Text(language, "排到下一轮", "Queue next turn"),
                contextual),
            new(
                "turn-stop",
                RadialMenuSlotPosition.Right,
                LogicalInput.FaceEast,
                Text(language, "停止", "Stop"),
                Text(language, "长按 3 秒", "Hold for 3 seconds")),
            new(
                "turn-fork",
                RadialMenuSlotPosition.Bottom,
                LogicalInput.FaceSouth,
                Text(language, "分支任务", "Fork task")),
            new(
                "turn-steer",
                RadialMenuSlotPosition.Left,
                LogicalInput.FaceWest,
                Text(language, "加入当前运行", "Steer current turn"),
                contextual),
        ];
    }

    internal static IReadOnlyList<ControllerLayerItemPresentation>
        Action(
            AppLanguage language,
            ControllerActionPresentationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return
        [
            new(
                "action-new-task",
                RadialMenuSlotPosition.Top,
                LogicalInput.DPadUp,
                Text(language, "新建任务", "New task")),
            new(
                "action-forward",
                RadialMenuSlotPosition.Right,
                LogicalInput.DPadRight,
                Text(language, "前进", "Forward")),
            new(
                "action-sidebar",
                RadialMenuSlotPosition.Bottom,
                LogicalInput.DPadDown,
                Text(language, "切换侧边栏", "Toggle sidebar")),
            new(
                "action-back",
                RadialMenuSlotPosition.Left,
                LogicalInput.DPadLeft,
                Text(language, "后退", "Back")),
            new(
                "action-clear",
                RadialMenuSlotPosition.CenterLeft,
                LogicalInput.FaceSouth,
                Text(language, "清空当前输入", "Clear current input"),
                options.IsClearConfirmationPending
                    ? Text(
                        language,
                        $"再次按 {options.ClearGlyph} 确认",
                        $"Press {options.ClearGlyph} again to confirm")
                    : Text(
                        language,
                        "需要二次确认",
                        "Requires confirmation")),
            new(
                "action-project",
                RadialMenuSlotPosition.CenterRight,
                LogicalInput.FaceWest,
                Text(language, "项目上下文", "Project context")),
        ];
    }

    internal static string Text(
        AppLanguage language,
        string zhCn,
        string enUs) =>
        language == AppLanguage.ZhCn ? zhCn : enUs;
}
