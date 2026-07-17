namespace CodexController.Services;

internal enum ComposerDialActionKind
{
    Picker,
    BaseAction,
    ExplicitTurnAction,
    DestructiveAction,
}

internal static class ComposerDialActionPolicy
{
    private static readonly string[] BaseActionPrefixes =
    [
        "addfiles",
        "cancel",
        "dictate",
        "pushtotalk",
        "secondaryaction",
        "send",
        "stop",
        "submit",
        "transcribeandsend",
        "undo",
        "review",
        "添加文件",
        "取消",
        "听写",
        "开始听写",
        "发送",
        "停止",
        "提交",
        "撤销",
        "撤回",
        "审查",
        "查看更改",
    ];

    private static readonly string[] ExplicitTurnPrefixes =
    [
        "steer",
        "queue",
        "addtocurrentturn",
        "queuenextturn",
        "加入当前运行",
        "加入当前轮次",
        "排到下一轮",
        "排入下一轮",
    ];

    private static readonly string[] DestructivePrefixes =
    [
        "delete",
        "remove",
        "discard",
        "archive",
        "clear",
        "删除",
        "移除",
        "丢弃",
        "捨棄",
        "归档",
        "封存",
        "清空",
    ];

    public static bool IsPickerControl(string? name) =>
        Classify(name) == ComposerDialActionKind.Picker;

    public static string? BlockReason(string? focusedName)
    {
        return Classify(focusedName) switch
        {
            ComposerDialActionKind.Picker => null,
            ComposerDialActionKind.ExplicitTurnAction =>
                "dial-explicit-turn-action",
            ComposerDialActionKind.DestructiveAction =>
                "dial-destructive-action-blocked",
            ComposerDialActionKind.BaseAction =>
                string.IsNullOrWhiteSpace(focusedName)
                    ? "dial-selection-unverified"
                    : "dial-base-action-blocked",
            _ => "dial-selection-unverified",
        };
    }

    public static ComposerDialActionKind Classify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ComposerDialActionKind.BaseAction;
        }

        var normalized = Normalize(name);
        if (DestructivePrefixes.Any(prefix =>
                normalized.StartsWith(
                    Normalize(prefix),
                    StringComparison.Ordinal)))
        {
            return ComposerDialActionKind.DestructiveAction;
        }

        if (ExplicitTurnPrefixes.Any(prefix =>
                normalized.StartsWith(
                    Normalize(prefix),
                    StringComparison.Ordinal)))
        {
            return ComposerDialActionKind.ExplicitTurnAction;
        }

        if (BaseActionPrefixes.Any(prefix =>
                normalized.StartsWith(
                    Normalize(prefix),
                    StringComparison.Ordinal)))
        {
            return ComposerDialActionKind.BaseAction;
        }

        return ComposerDialActionKind.Picker;
    }

    public static int PickerControlPriority(
        bool supportsExpandCollapse,
        bool allowInvoke)
    {
        if (supportsExpandCollapse)
        {
            return 0;
        }

        return allowInvoke ? 1 : 2;
    }

    private static string Normalize(string value)
    {
        return new string(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }
}
