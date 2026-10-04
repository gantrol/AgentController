namespace CodexMicro.Desktop.Services;

internal sealed record CodexActionDefinition(string Id, string Label, string IconId, string? LabelZh = null)
{
    public bool SoftwareSupported => CodexActionCatalog.SoftwareRoute(Id) is not null;
}

internal static partial class CodexActionCatalog
{
    internal static IEnumerable<CodexActionDefinition> All =>
        Official.Append(new("turn.cancel", "Stop", "REJ"));

    // Matches SoftwareMicroTransport's dispatch routes. Availability still depends
    // on the selected chat, current approval/turn, and the native UI adapter.
    internal static string? SoftwareRoute(string id) => id switch
    {
        "newTask" or "forkThread" or "composer.toggleFastMode" or
        "composer.togglePlanMode" or "toggleReviewTab" or
        "approval.approve" or "approval.decline" or
        "composer.increaseReasoningEffort" or "composer.decreaseReasoningEffort" or
        "turn.cancel" => "ipc",
        "composer.submit" or "toggleSidebar" or "navigateBack" or "navigateForward" => "native-ui",
        _ => null,
    };
}
