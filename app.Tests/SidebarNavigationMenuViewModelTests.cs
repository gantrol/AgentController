using CodexController.Models;
using CodexController.Services;
using CodexController.ViewModels;

namespace CodexController.Tests;

public sealed class SidebarNavigationMenuViewModelTests
{
    [Fact]
    public void Update_ProjectsAdjacentRootAndChildPanels()
    {
        var state = SidebarNavigationMenuProjector.Project(
            [
                Entry(
                    "pinned-task",
                    "Pinned task",
                    SidebarScope.PinnedTasks,
                    SidebarLayer.Pinned),
                Entry(
                    "project",
                    "Project",
                    SidebarScope.Projects,
                    SidebarLayer.Projects),
            ],
            selectedRootId: "project",
            scope => $"scope:{scope}",
            childEntries:
            [
                Entry(
                    "task-a",
                    "Task A",
                    SidebarScope.ProjectTasks,
                    SidebarLayer.Tasks),
                Entry(
                    "task-b",
                    "Task B",
                    SidebarScope.ProjectTasks,
                    SidebarLayer.Tasks),
            ],
            selectedChildId: "task-b",
            childTitle: "Project",
            childIsActive: true) with
        {
            Title = "Sidebar",
            NavigateGlyph = "LS",
            NavigateHint = "Move",
            CycleScopeGlyph = "L3",
            CycleScopeHint = "Region",
            OpenGlyph = "A",
            OpenHint = "Open",
        };
        var viewModel = new SidebarNavigationMenuViewModel();

        viewModel.Update(state);

        Assert.Equal(
            SidebarNavigationMenuViewModel.TwoPanelWidth,
            viewModel.ViewWidth);
        Assert.True(viewModel.HasChildPanel);
        Assert.Equal(4, viewModel.RootPanel.Sections.Count);
        Assert.False(viewModel.RootPanel.IsActive);
        Assert.Equal("Project", viewModel.ChildPanel?.Title);
        Assert.True(viewModel.ChildPanel?.IsActive);
        Assert.Equal("2 / 2", viewModel.ChildPanel?.PositionText);
        Assert.True(viewModel.RootPanel.Sections
            .SelectMany(section => section.Items)
            .Single(item => item.Id == "project")
            .HasChildren);
        Assert.True(viewModel.ChildPanel?.Sections
            .SelectMany(section => section.Items)
            .Single(item => item.Id == "task-b")
            .IsActiveSelection);
        Assert.Equal("LS", viewModel.NavigateGlyph);
        Assert.Equal("L3", viewModel.CycleScopeGlyph);
        Assert.Equal("A", viewModel.OpenGlyph);
    }

    [Fact]
    public void Update_LeafSelectionCollapsesChildPanelWidth()
    {
        var state = SidebarNavigationMenuProjector.Project(
            [
                Entry(
                    "task",
                    "Task",
                    SidebarScope.ProjectlessTasks,
                    SidebarLayer.Tasks),
            ],
            selectedRootId: "task",
            scope => scope.ToString());
        var viewModel = new SidebarNavigationMenuViewModel();

        viewModel.Update(state);

        Assert.False(viewModel.HasChildPanel);
        Assert.Null(viewModel.ChildPanel);
        Assert.Equal(
            SidebarNavigationMenuViewModel.SinglePanelWidth,
            viewModel.ViewWidth);
        Assert.True(viewModel.RootPanel.IsActive);
    }

    private static SidebarEntry Entry(
        string id,
        string title,
        SidebarScope scope,
        SidebarLayer layer) =>
        new(
            id,
            title,
            layer == SidebarLayer.Projects ? "3 tasks" : "just now",
            layer,
            ThreadId: layer == SidebarLayer.Tasks ? id : null,
            ProjectPath: layer == SidebarLayer.Projects ? id : null,
            NavigationScope: scope);
}
