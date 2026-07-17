namespace CodexController.Services;

internal static class PlanModeAutomationPolicy
{
    internal const string StateUnavailableDetail = "plan-mode-state";
    internal const string StateUnchangedDetail =
        "plan-mode-state-unchanged";
    internal const string RunningUnavailableDetail =
        "plan-mode-unavailable-while-running";
    internal const string CommandUnavailableDetail =
        "plan-mode-command";
    internal const string CommandInvokeDetail =
        "plan-mode-command-invoke";
    internal const string DraftUnavailableDetail =
        "plan-mode-draft";
    internal const string DraftRestoreDetail =
        "plan-mode-draft-restore";
    internal const string SlashCommandQuery = "/p";

    internal static IReadOnlyCollection<string> IndicatorNames { get; } =
        ["Plan", "计划", "規劃", "計劃"];

    internal static IReadOnlyCollection<string> SlashCommandNames { get; } =
        ["Plan mode", "计划模式", "規劃模式", "計劃模式"];

    internal static IReadOnlyCollection<string> RunningActionNames { get; } =
    [
        "Stop",
        "Cancel request",
        "停止",
        "停止生成",
        "停止当前运行",
        "停止当前轮次",
    ];

    internal static bool DidStateChange(bool before, bool after) =>
        before != after;

    internal static bool IsSlashCommand(string? accessibleName)
    {
        var name = accessibleName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return SlashCommandNames.Any(commandName =>
            name.Equals(
                commandName,
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                $"{commandName} ",
                StringComparison.OrdinalIgnoreCase));
    }
}
