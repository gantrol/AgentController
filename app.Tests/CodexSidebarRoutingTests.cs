using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class CodexSidebarRoutingTests
{
    [Fact]
    public void ProjectTaskScopeWinsOverPinnedPresentationLayer()
    {
        var entry = new SidebarEntry(
            "task",
            "Pinned child",
            string.Empty,
            SidebarLayer.Pinned,
            ThreadId: "task",
            ProjectPath: @"D:\projects\one",
            IsPinned: true,
            ProjectIsPinned: true,
            NavigationScope: SidebarScope.ProjectTasks);

        Assert.Equal(
            SidebarFocusRoute.ProjectTask,
            CodexSidebarService.ResolveFocusRoute(entry));
    }

    [Theory]
    [InlineData(SidebarLayer.Projects, (int)SidebarFocusRoute.Project)]
    [InlineData(SidebarLayer.Tasks, (int)SidebarFocusRoute.Task)]
    [InlineData(SidebarLayer.Pinned, (int)SidebarFocusRoute.PinnedTask)]
    public void RootScopeStillUsesPresentationLayer(
        SidebarLayer layer,
        int expected)
    {
        var entry = new SidebarEntry(
            "entry",
            "Entry",
            string.Empty,
            layer,
            NavigationScope: SidebarScope.Projects);

        Assert.Equal(
            (SidebarFocusRoute)expected,
            CodexSidebarService.ResolveFocusRoute(entry));
    }
}
