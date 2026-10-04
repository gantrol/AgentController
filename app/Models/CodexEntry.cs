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
    SidebarScope NavigationScope = SidebarScope.Projects,
    string? SectionId = null,
    string? SectionName = null)
{
    public bool IsProject => Layer == SidebarLayer.Projects;

    public string SectionKey => SectionId ?? NavigationScope.ToString();
    public string? SectionHeader { get; init; }

    public string KindGlyph => IsProject ? "▱" : "·";
}

public sealed class CodexSnapshot
{
    public IReadOnlyList<CodexThread> Threads { get; init; } = [];
    public IReadOnlyList<CodexThread> PinnedThreads { get; init; } = [];
    public IReadOnlyList<CodexThread> ProjectlessThreads { get; init; } = [];
    public IReadOnlyList<CodexProject> Projects { get; init; } = [];
    public CodexSidebarLayout? SidebarLayout { get; init; }
    public int ArchivedThreadCount { get; init; }
    public int UnavailableThreadCount { get; init; }
}

public sealed record CodexSidebarSection(string Id, string Name, IReadOnlyList<string> ItemIds);

public sealed record CodexSidebarLayout(
    IReadOnlyList<CodexSidebarSection> Sections,
    IReadOnlyList<string> SectionOrder,
    IReadOnlyList<string> PinnedOrder,
    IReadOnlyList<string> ProjectOrder,
    IReadOnlyList<string> ChatOrder,
    bool ManualChatOrder,
    bool ManualProjectThreadOrder)
{
    public IReadOnlyList<string> OrderedSectionIds
    {
        get
        {
            var order = new[] { "pinned" }.Concat(Sections.Select(section => section.Id))
                .Concat(["threads", "chats"]).ToList();
            var requested = SectionOrder.Where(order.Contains).Distinct().ToArray();
            var slots = order.Select((id, index) => (id, index))
                .Where(item => requested.Contains(item.id)).Select(item => item.index).ToArray();
            for (var index = 0; index < slots.Length; index++)
                order[slots[index]] = requested[index];
            return order;
        }
    }
}

public sealed record SidebarSectionTab(
    string Id,
    string Name,
    SidebarScope Scope,
    string? EntryId,
    bool IsSelected)
{
    public bool CanNavigate => EntryId is not null;
}
