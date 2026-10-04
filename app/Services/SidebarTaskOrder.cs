using CodexController.Models;

namespace CodexController.Services;

public static class SidebarTaskOrder
{
    public static IReadOnlyList<SidebarEntry> Flatten(IReadOnlyList<SidebarEntry> roots,
        Func<string, IReadOnlyList<SidebarEntry>> projectTasks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return roots.SelectMany(entry => entry.IsProject && entry.ProjectPath is not null
                ? projectTasks(entry.ProjectPath) : new[] { entry })
            .Where(entry => entry.ThreadId is not null && seen.Add(entry.ThreadId)).ToArray();
    }

    public static SidebarEntry? NextSection(IReadOnlyList<SidebarEntry> roots, string? selectedId)
    {
        var sections = roots.GroupBy(entry => entry.SectionKey).ToArray();
        if (sections.Length == 0) return null;
        var current = Array.FindIndex(sections, group => group.Any(entry => entry.Id == selectedId));
        return sections[(current + 1) % sections.Length].First();
    }
}
