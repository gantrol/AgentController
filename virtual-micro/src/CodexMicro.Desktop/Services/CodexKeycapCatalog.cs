namespace CodexMicro.Desktop.Services;

internal sealed record CodexKeycapDefinition(
    string Id,
    string Label,
    string DefaultAction);

internal static class CodexKeycapCatalog
{
    private static readonly IReadOnlyDictionary<string, CodexKeycapDefinition>
        Definitions = new Dictionary<string, CodexKeycapDefinition>(
            StringComparer.Ordinal)
        {
            ["FAST"] = new("FAST", "Fast mode", "composer.toggleFastMode"),
            ["APPR"] = new("APPR", "Approve", "approval.approve"),
            ["REJ"] = new("REJ", "Decline", "approval.decline"),
            ["SPLIT"] = new("SPLIT", "Fork chat", "forkThread"),
            ["MIC"] = new("MIC", "Push to talk", "dictation.pushToTalk"),
            ["CODEX"] = new("CODEX", "Submit to Codex", "composer.submit"),
            ["BUG"] = new("BUG", "Feedback", "feedback"),
            ["OAI"] = new("OAI", "OpenAI developers", "developers.openai.com"),
            ["TERM"] = new("TERM", "Terminal", "toggleTerminal"),
            ["DWN"] = new("DWN", "Copy conversation", "copyConversationMarkdown"),
            ["DEL"] = new("DEL", "Archive chat", "archiveThread"),
            ["NEW"] = new("NEW", "New task", "newTask"),
            ["NAV"] = new("NAV", "Browser", "openBrowserTab"),
            ["MAGIC"] = new("MAGIC", "Pin chat", "toggleThreadPin"),
            ["DIFF"] = new("DIFF", "Review", "toggleReviewTab"),
            ["PLAY"] = new("PLAY", "Environment action", "environmentAction1"),
            ["GIT"] = new("GIT", "Commit", "git.commit"),
            ["BRCH"] = new("BRCH", "Branch", "toggleReviewTab"),
            ["MRG"] = new("MRG", "Merge", "toggleReviewTab"),
            ["PR"] = new("PR", "Create pull request", "git.createPullRequest"),
            ["PAINT"] = new("PAINT", "Add photos", "composer.addPhotos"),
            ["LAB"] = new("LAB", "Settings", "settings"),
            ["PARTY"] = new("PARTY", "Side chat", "openSideChat"),
            ["TIME"] = new("TIME", "Manage tasks", "manageTasks"),
            ["MIND+"] = new("MIND+", "More reasoning", "composer.increaseReasoningEffort"),
            ["MIND-"] = new("MIND-", "Less reasoning", "composer.decreaseReasoningEffort"),
            ["EMPT1"] = new("EMPT1", "Custom shortcut", "unassigned"),
            ["EMPT2"] = new("EMPT2", "Custom shortcut", "unassigned"),
            ["EMPT3"] = new("EMPT3", "Custom shortcut", "unassigned"),
            ["EMPT4"] = new("EMPT4", "Custom shortcut", "unassigned"),
            ["SETUP"] = new("SETUP", "Settings", "settings"),
            ["FOLD"] = new("FOLD", "Open folder", "openFolder"),
            ["UPL"] = new("UPL", "Add files", "composer.addFiles"),
            ["APPS"] = new("APPS", "Skills", "openSkills"),
            ["YOLO"] = new("YOLO", ":yolo:", "custom"),
            ["YEET"] = new("YEET", ":yeet:", "custom"),
            ["EMPT5"] = new("EMPT5", "Custom shortcut", "unassigned"),
        };

    public static bool IsKnown(string id) => Definitions.ContainsKey(id);

    public static IEnumerable<string> KnownIds => Definitions.Keys;

    public static CodexKeycapDefinition Get(string id) =>
        Definitions.TryGetValue(id, out var definition)
            ? definition
            : new CodexKeycapDefinition(id, id, "unknown");
}
