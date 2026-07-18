using CodexController.Models;
using CodexController.ViewModels;

namespace CodexController.Tests;

public sealed class SidebarNavigationMenuViewModelTests
{
    [Fact]
    public void Update_ProjectsThreeRowsAndBoundaryHints()
    {
        var viewModel = new SidebarNavigationMenuViewModel();
        viewModel.Update(new SidebarNavigationMenuState(
            new SidebarNavigationMenuItem(
                "Pinned task",
                SidebarScope.PinnedTasks,
                "Pinned tasks",
                CrossesSectionBoundary: true),
            new SidebarNavigationMenuItem(
                "Project",
                SidebarScope.PinnedProjects,
                "Pinned projects",
                CrossesSectionBoundary: false),
            new SidebarNavigationMenuItem(
                "Next project",
                SidebarScope.Projects,
                "Projects",
                CrossesSectionBoundary: true),
            Position: 2,
            Count: 8)
        {
            Title = "Sidebar",
            NavigateGlyph = "LS",
            NavigateHint = "Move",
            CycleScopeGlyph = "L3",
            CycleScopeHint = "Region",
            OpenGlyph = "A",
            OpenHint = "Open",
        });

        Assert.Equal("Pinned task", viewModel.PreviousTitle);
        Assert.Equal("Pinned tasks", viewModel.PreviousBoundary);
        Assert.Equal("Project", viewModel.CurrentTitle);
        Assert.Equal("Pinned projects", viewModel.CurrentScope);
        Assert.Equal("Next project", viewModel.NextTitle);
        Assert.Equal("Projects", viewModel.NextBoundary);
        Assert.Equal("2 / 8", viewModel.Position);
        Assert.Equal("Sidebar", viewModel.Title);
        Assert.Equal("LS", viewModel.NavigateGlyph);
        Assert.Equal("Move", viewModel.NavigateHint);
        Assert.Equal("L3", viewModel.CycleScopeGlyph);
        Assert.Equal("Region", viewModel.CycleScopeHint);
        Assert.Equal("A", viewModel.OpenGlyph);
        Assert.Equal("Open", viewModel.OpenHint);
    }
}
