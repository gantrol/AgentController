using CodexController.Models;

namespace CodexController.Services;

public enum SidebarNavigationIntent
{
    None,
    EnterProjectDirectory,
    ExitProjectDirectory,
    OpenTask,
    AlreadyAtRoot,
    NoChildDirectory,
    ProjectRequiresHorizontalEntry,
    EntryUnavailable,
}

/// <summary>
/// Keeps directory navigation separate from task activation so a face button
/// can never accidentally open a project as though it were a conversation.
/// </summary>
public static class SidebarNavigationIntentResolver
{
    public static SidebarNavigationIntent ResolveHorizontal(
        SidebarScope scope,
        SidebarEntry? entry,
        int direction)
    {
        if (direction == 0)
        {
            return SidebarNavigationIntent.None;
        }

        if (direction < 0)
        {
            return scope == SidebarScope.ProjectTasks
                ? SidebarNavigationIntent.ExitProjectDirectory
                : SidebarNavigationIntent.AlreadyAtRoot;
        }

        return entry is { IsProject: true } &&
               !string.IsNullOrWhiteSpace(entry.ProjectPath)
            ? SidebarNavigationIntent.EnterProjectDirectory
            : SidebarNavigationIntent.NoChildDirectory;
    }

    public static SidebarNavigationIntent ResolvePrimary(
        SidebarEntry? entry)
    {
        if (entry is null)
        {
            return SidebarNavigationIntent.EntryUnavailable;
        }

        if (entry.IsProject)
        {
            return SidebarNavigationIntent.ProjectRequiresHorizontalEntry;
        }

        return string.IsNullOrWhiteSpace(entry.ThreadId)
            ? SidebarNavigationIntent.EntryUnavailable
            : SidebarNavigationIntent.OpenTask;
    }
}
