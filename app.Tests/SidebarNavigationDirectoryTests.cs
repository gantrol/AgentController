using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class SidebarNavigationDirectoryTests
{
    [Fact]
    public void RootAndProjectWheelsKeepIndependentFrozenOrders()
    {
        var directory = new SidebarNavigationDirectory();
        var root = directory.Resolve(
            SidebarScope.Projects,
            projectPath: null,
            pinnedOnly: false);
        var project = directory.Resolve(
            SidebarScope.ProjectTasks,
            @"D:\projects\one",
            pinnedOnly: false);

        root.Synchronize(
        [
            Entry("root-a", SidebarScope.Projects),
            Entry("root-b", SidebarScope.Projects),
        ],
        preferredId: "root-b");
        project.Synchronize(
        [
            Entry("task-a", SidebarScope.ProjectTasks),
            Entry("task-b", SidebarScope.ProjectTasks),
        ],
        preferredId: "task-a");

        project.Synchronize(
        [
            Entry("task-b", SidebarScope.ProjectTasks),
            Entry("task-a", SidebarScope.ProjectTasks),
        ],
        preferredId: "task-a");
        root.Synchronize(
        [
            Entry("root-b", SidebarScope.Projects),
            Entry("root-a", SidebarScope.Projects),
        ],
        preferredId: "root-b");

        Assert.Equal(
            new[] { "root-a", "root-b" },
            root.FrozenEntries.Select(entry => entry.Id));
        Assert.Equal(
            new[] { "task-a", "task-b" },
            project.FrozenEntries.Select(entry => entry.Id));
        Assert.Equal("root-b", root.SelectedEntry(root.FrozenEntries)?.Id);
        Assert.Equal(
            "task-a",
            project.SelectedEntry(project.FrozenEntries)?.Id);
    }

    [Fact]
    public void EachProjectAndPinnedFilterOwnsASeparateWheel()
    {
        var directory = new SidebarNavigationDirectory();
        var allOne = directory.Resolve(
            SidebarScope.ProjectTasks,
            @"D:\projects\one",
            pinnedOnly: false);
        var pinnedOne = directory.Resolve(
            SidebarScope.ProjectTasks,
            @"D:\projects\one",
            pinnedOnly: true);
        var allTwo = directory.Resolve(
            SidebarScope.ProjectTasks,
            @"D:\projects\two",
            pinnedOnly: false);

        Assert.NotSame(allOne, pinnedOne);
        Assert.NotSame(allOne, allTwo);
        Assert.Same(
            allOne,
            directory.Resolve(
                SidebarScope.ProjectTasks,
                @"d:\PROJECTS\ONE",
                pinnedOnly: false));
    }

    private static SidebarEntry Entry(
        string id,
        SidebarScope scope) =>
        new(
            id,
            id,
            string.Empty,
            scope == SidebarScope.Projects
                ? SidebarLayer.Projects
                : SidebarLayer.Tasks,
            NavigationScope: scope);
}
