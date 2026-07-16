using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class SidebarNavigationIntentResolverTests
{
    [Fact]
    public void RightEntersOnlyAFocusedProjectDirectory()
    {
        Assert.Equal(
            SidebarNavigationIntent.EnterProjectDirectory,
            SidebarNavigationIntentResolver.ResolveHorizontal(
                SidebarScope.Projects,
                Project(),
                direction: 1));
        Assert.Equal(
            SidebarNavigationIntent.NoChildDirectory,
            SidebarNavigationIntentResolver.ResolveHorizontal(
                SidebarScope.PinnedTasks,
                Task(),
                direction: 1));
    }

    [Fact]
    public void LeftExitsOnlyFromAProjectDirectory()
    {
        Assert.Equal(
            SidebarNavigationIntent.ExitProjectDirectory,
            SidebarNavigationIntentResolver.ResolveHorizontal(
                SidebarScope.ProjectTasks,
                Task(),
                direction: -1));
        Assert.Equal(
            SidebarNavigationIntent.AlreadyAtRoot,
            SidebarNavigationIntentResolver.ResolveHorizontal(
                SidebarScope.Projects,
                Project(),
                direction: -1));
    }

    [Fact]
    public void PrimaryOpensTasksButNeverProjects()
    {
        Assert.Equal(
            SidebarNavigationIntent.OpenTask,
            SidebarNavigationIntentResolver.ResolvePrimary(Task()));
        Assert.Equal(
            SidebarNavigationIntent.ProjectRequiresHorizontalEntry,
            SidebarNavigationIntentResolver.ResolvePrimary(Project()));
    }

    private static SidebarEntry Project() =>
        new(
            "project",
            "Project",
            "2 tasks",
            SidebarLayer.Projects,
            ProjectPath: @"D:\projects\one",
            NavigationScope: SidebarScope.Projects);

    private static SidebarEntry Task() =>
        new(
            "task",
            "Task",
            string.Empty,
            SidebarLayer.Tasks,
            ThreadId: "task",
            ProjectPath: @"D:\projects\one",
            NavigationScope: SidebarScope.ProjectTasks);
}
