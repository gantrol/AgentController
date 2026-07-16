using CodexController.Models;

namespace CodexController.Services;

/// <summary>
/// Owns independent navigation wheels for the unified root directory and for
/// every project task directory. Filtered project views do not mutate the
/// full project wheel.
/// </summary>
public sealed class SidebarNavigationDirectory
{
    private readonly Dictionary<string, ProjectNavigationStates> _projects =
        new(StringComparer.OrdinalIgnoreCase);

    public SidebarNavigationState Root { get; } = new();

    public SidebarNavigationState Resolve(
        SidebarScope scope,
        string? projectPath,
        bool pinnedOnly)
    {
        if (
            scope != SidebarScope.ProjectTasks ||
            string.IsNullOrWhiteSpace(projectPath))
        {
            return Root;
        }

        if (!_projects.TryGetValue(projectPath, out var states))
        {
            states = new ProjectNavigationStates();
            _projects[projectPath] = states;
        }

        return pinnedOnly
            ? states.PinnedOnly
            : states.AllTasks;
    }

    private sealed class ProjectNavigationStates
    {
        public SidebarNavigationState AllTasks { get; } = new();

        public SidebarNavigationState PinnedOnly { get; } = new();
    }
}
