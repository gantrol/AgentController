using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class SidebarNavigationMenuProjectorTests
{
    [Fact]
    public void Project_KeepsAllFourRootSectionsAndMarksProjectsAsBranches()
    {
        var state = SidebarNavigationMenuProjector.Project(
            [
                Entry(
                    "pinned-task",
                    SidebarScope.PinnedTasks,
                    SidebarLayer.Pinned),
                Entry(
                    "project",
                    SidebarScope.Projects,
                    SidebarLayer.Projects),
            ],
            selectedRootId: "project",
            Label);

        Assert.Equal(
            SidebarNavigationMenuProjector.RootScopes,
            state.Root.Sections.Select(section => section.Scope));
        Assert.Equal(
            ["PinnedTasks", "PinnedProjects", "Projects", "ProjectlessTasks"],
            state.Root.Sections.Select(section => section.Title));
        Assert.Empty(state.Root.Sections[1].Items);
        Assert.Empty(state.Root.Sections[3].Items);

        var project = state.Root.SelectedItem;
        Assert.NotNull(project);
        Assert.Equal("project", project.Id);
        Assert.True(project.HasChildren);
        Assert.True(state.Root.IsActive);
        Assert.Null(state.Child);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_DisclosesSelectedProjectAndTracksActiveDepth(
        bool childIsActive)
    {
        var state = SidebarNavigationMenuProjector.Project(
            [
                Entry(
                    "project-a",
                    SidebarScope.Projects,
                    SidebarLayer.Projects),
                Entry(
                    "project-b",
                    SidebarScope.Projects,
                    SidebarLayer.Projects),
            ],
            selectedRootId: "project-b",
            Label,
            childEntries:
            [
                Entry(
                    "task-a",
                    SidebarScope.ProjectTasks,
                    SidebarLayer.Tasks),
                Entry(
                    "task-b",
                    SidebarScope.ProjectTasks,
                    SidebarLayer.Tasks),
            ],
            selectedChildId: "task-b",
            childTitle: "Project B",
            childIsActive: childIsActive);

        Assert.NotNull(state.Child);
        Assert.Equal("Project B", state.Child.Title);
        Assert.Equal("project-b", state.Root.SelectedItem?.Id);
        Assert.Equal("task-b", state.Child.SelectedItem?.Id);
        Assert.Equal(childIsActive, state.Child.IsActive);
        Assert.Equal(!childIsActive, state.Root.IsActive);
        Assert.Equal(
            childIsActive ? "task-b" : "project-b",
            state.ActiveItem?.Id);
    }

    [Fact]
    public void Project_DoesNotAttachChildrenToLeafRows()
    {
        var state = SidebarNavigationMenuProjector.Project(
            [
                Entry(
                    "task",
                    SidebarScope.ProjectlessTasks,
                    SidebarLayer.Tasks),
            ],
            selectedRootId: "task",
            Label,
            childEntries:
            [
                Entry(
                    "unexpected",
                    SidebarScope.ProjectTasks,
                    SidebarLayer.Tasks),
            ]);

        Assert.Null(state.Child);
        Assert.False(state.Root.SelectedItem?.HasChildren);
        Assert.Same(state.Root, state.ActivePanel);
    }

    private static string Label(SidebarScope scope) => scope.ToString();

    private static SidebarEntry Entry(
        string id,
        SidebarScope scope,
        SidebarLayer layer) =>
        new(
            id,
            id,
            layer == SidebarLayer.Projects ? "3 tasks" : "just now",
            layer,
            ThreadId: layer == SidebarLayer.Tasks ? id : null,
            ProjectPath: layer == SidebarLayer.Projects ? id : null,
            NavigationScope: scope);
}
