namespace CodexController.Services;

internal static class ComposerPickerViewPolicy
{
    internal static ComposerPickerView ResolveEntryView(
        bool usesAdvancedMode) =>
        usesAdvancedMode
            ? ComposerPickerView.Advanced
            : ComposerPickerView.Model;

    private static readonly string[] PowerNames =
        ["power", "功率", "强度"];

    private static readonly string[] ModelNames =
        ["model", "模型"];

    private static readonly string[] EffortNames =
        ["effort", "reasoning effort", "思考强度", "推理强度"];

    internal static ComposerPickerView Detect(
        IEnumerable<string> names,
        bool hasPowerRange = false)
    {
        ArgumentNullException.ThrowIfNull(names);
        var values = names
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        var hasSimpleMarkers =
            hasPowerRange ||
            values.Any(IsPowerItem) ||
            values.Any(IsShowAdvancedToggle);
        var hasAdvancedMarkers =
            (
                values.Any(IsModelItem) &&
                values.Any(IsEffortItem)
            ) ||
            values.Any(IsShowCompactToggle);
        if (hasSimpleMarkers == hasAdvancedMarkers)
        {
            return ComposerPickerView.Unknown;
        }

        if (hasSimpleMarkers)
        {
            return ComposerPickerView.Simple;
        }

        if (hasAdvancedMarkers)
        {
            return ComposerPickerView.Advanced;
        }

        return ComposerPickerView.Unknown;
    }

    internal static bool IsPowerItem(string? name) =>
        StartsWithAny(name, PowerNames);

    internal static bool IsModelItem(string? name) =>
        StartsWithAny(name, ModelNames);

    internal static bool IsEffortItem(string? name) =>
        StartsWithAny(name, EffortNames);

    internal static bool IsShowAdvancedToggle(string? name) =>
        StartsWithAny(name, ["advanced", "高级"]) ||
        ContainsAny(
            name,
            "show advanced",
            "advanced options",
            "显示高级",
            "展开高级");

    internal static bool IsShowCompactToggle(string? name) =>
        StartsWithAny(
            name,
            [
                "simple",
                "basic",
                "compact",
                "简易",
                "基础",
                "紧凑",
            ]) ||
        ContainsAny(
            name,
            "show compact",
            "compact options",
            "显示简易",
            "显示紧凑",
            "收起高级");

    internal static bool IsViewToggleToward(
        string? name,
        ComposerPickerView desired) =>
        desired switch
        {
            ComposerPickerView.Advanced =>
                IsShowAdvancedToggle(name) ||
                MatchesAny(name, ["advanced", "高级"]),
            ComposerPickerView.Simple =>
                IsShowCompactToggle(name) ||
                StartsWithAny(
                    name,
                    [
                        "simple",
                        "basic",
                        "compact",
                        "简易",
                        "基础",
                        "紧凑",
                    ]),
            _ => false,
        };

    internal static bool IsFastToggle(string? descriptor) =>
        ContainsAny(
            descriptor,
            "fast mode",
            "toggle fast",
            "speed mode",
            "priority mode",
            "quick mode",
            "快速模式",
            "切换快速",
            "速度模式");

    internal static bool IsEnableFastAction(string? name) =>
        ContainsAny(
            name,
            "enable fast",
            "turn on fast",
            "启用快速",
            "开启快速");

    internal static bool IsEnableStandardAction(string? name) =>
        ContainsAny(
            name,
            "enable standard",
            "turn off fast",
            "turn fast mode off",
            "disable fast",
            "启用标准",
            "关闭快速");

    private static bool MatchesAny(
        string? value,
        IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return candidates.Any(candidate =>
            string.Equals(
                value.Trim(),
                candidate,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool StartsWithAny(
        string? value,
        IReadOnlyList<string> candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return candidates.Any(candidate =>
            value.Trim().StartsWith(
                candidate,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsAny(
        string? value,
        params string[] candidates)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return candidates.Any(candidate =>
            value.Contains(
                candidate,
                StringComparison.OrdinalIgnoreCase));
    }
}
