using CodexController.Models;
using CodexController.ViewModels;

namespace CodexController.Tests;

public sealed class SidebarNavigationWheelViewModelTests
{
    [Fact]
    public void Update_ProjectsThreeRowsAndBoundaryHints()
    {
        var viewModel = new SidebarNavigationWheelViewModel();
        viewModel.Update(new SidebarNavigationWheelState(
            new SidebarNavigationWheelItem(
                "Pinned task",
                SidebarScope.PinnedTasks,
                "Pinned tasks",
                CrossesSectionBoundary: true),
            new SidebarNavigationWheelItem(
                "Project",
                SidebarScope.PinnedProjects,
                "Pinned projects",
                CrossesSectionBoundary: false),
            new SidebarNavigationWheelItem(
                "Next project",
                SidebarScope.Projects,
                "Projects",
                CrossesSectionBoundary: true),
            Position: 2,
            Count: 8));

        Assert.Equal("Pinned task", viewModel.PreviousTitle);
        Assert.Equal("↑ Pinned tasks", viewModel.PreviousBoundary);
        Assert.Equal("Project", viewModel.CurrentTitle);
        Assert.Equal("Pinned projects", viewModel.CurrentScope);
        Assert.Equal("Next project", viewModel.NextTitle);
        Assert.Equal("↓ Projects", viewModel.NextBoundary);
        Assert.Equal("2 / 8", viewModel.Position);
    }
}
