namespace CodexController.Models;

public enum SidebarLayer
{
    Pinned,
    Projects,
    Tasks,
}

public enum SidebarScope
{
    PinnedTasks,
    PinnedProjects,
    Projects,
    ProjectTasks,
    ProjectlessTasks,
}

public enum RightControlMode
{
    Dial,
    Reasoning,
    Model,
    Speed,
}

public sealed record CodexThread(
    string Id,
    string Title,
    DateTimeOffset UpdatedAt,
    string? ProjectPath,
    bool IsPinned,
    string? NativeTitle = null,
    ThreadStatus Status = ThreadStatus.Unknown);

public sealed record CodexProject(
    string Path,
    string Name,
    bool IsPinned,
    IReadOnlyList<CodexThread> Threads);

public sealed record SidebarEntry(
    string Id,
    string Title,
    string Subtitle,
    SidebarLayer Layer,
    string? ThreadId = null,
    string? ProjectPath = null,
    string? NativeTitle = null,
    int? NativeListIndex = null,
    bool IsPinned = false,
    bool ProjectIsPinned = false,
    string PinBadge = "",
    string ActionHint = "",
    SidebarScope NavigationScope = SidebarScope.Projects)
{
    public bool IsProject => Layer == SidebarLayer.Projects;

    public string KindGlyph => IsProject ? "▱" : "·";
}

public sealed class CodexSnapshot
{
    public IReadOnlyList<CodexThread> Threads { get; init; } = [];
    public IReadOnlyList<CodexThread> PinnedThreads { get; init; } = [];
    public IReadOnlyList<CodexThread> ProjectlessThreads { get; init; } = [];
    public IReadOnlyList<CodexProject> Projects { get; init; } = [];
    public int ArchivedThreadCount { get; init; }
    public int UnavailableThreadCount { get; init; }
}
