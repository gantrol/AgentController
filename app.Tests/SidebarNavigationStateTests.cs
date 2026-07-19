using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class SidebarNavigationStateTests
{
    [Fact]
    public void Synchronize_FreezesOrderWhileRefreshingMetadata()
    {
        var state = new SidebarNavigationState();
        var initial = new[]
        {
            Entry(
                "first",
                SidebarScope.ProjectlessTasks,
                SidebarLayer.Tasks,
                nativeListIndex: 0),
            Entry(
                "running",
                SidebarScope.ProjectlessTasks,
                SidebarLayer.Tasks,
                nativeListIndex: 1),
            Entry(
                "third",
                SidebarScope.ProjectlessTasks,
                SidebarLayer.Tasks,
                nativeListIndex: 2),
        };
        state.Synchronize(initial, preferredId: "running");

        var refreshed = new[]
        {
            Entry(
                "running",
                SidebarScope.ProjectlessTasks,
                SidebarLayer.Tasks,
                title: "running · updated",
                nativeListIndex: 0),
            Entry(
                "third",
                SidebarScope.ProjectlessTasks,
                SidebarLayer.Tasks,
                nativeListIndex: 1),
            Entry(
                "first",
                SidebarScope.ProjectlessTasks,
                SidebarLayer.Tasks,
                nativeListIndex: 2),
        };
        var result = state.Synchronize(
            refreshed,
            preferredId: "running");

        Assert.False(result.StructureChanged);
        Assert.False(result.OrderChanged);
        Assert.Equal(
            new[] { "first", "running", "third" },
            state.FrozenEntries.Select(entry => entry.Id));
        Assert.Equal(
            "running · updated",
            state.FrozenEntries[1].Title);
        Assert.Equal(2, state.FrozenEntries[0].NativeListIndex);
        Assert.Equal(0, state.FrozenEntries[1].NativeListIndex);
        Assert.Equal(1, state.FrozenEntries[2].NativeListIndex);
        Assert.Equal(1, state.SelectedIndex);
    }

    [Fact]
    public void Synchronize_StructureChangePreservesSurvivorOrderBySection()
    {
        var state = new SidebarNavigationState();
        state.Synchronize(
        [
            Entry("pin-b", SidebarScope.PinnedTasks, SidebarLayer.Pinned),
            Entry("pin-a", SidebarScope.PinnedTasks, SidebarLayer.Pinned),
            Entry("project-b", SidebarScope.Projects, SidebarLayer.Projects),
            Entry("project-a", SidebarScope.Projects, SidebarLayer.Projects),
        ],
        preferredId: "project-b");

        var result = state.Synchronize(
        [
            Entry("pin-a", SidebarScope.PinnedTasks, SidebarLayer.Pinned),
            Entry("pin-b", SidebarScope.PinnedTasks, SidebarLayer.Pinned),
            Entry("pin-new", SidebarScope.PinnedTasks, SidebarLayer.Pinned),
            Entry("project-a", SidebarScope.Projects, SidebarLayer.Projects),
            Entry("project-b", SidebarScope.Projects, SidebarLayer.Projects),
        ],
        preferredId: "project-b");

        Assert.True(result.StructureChanged);
        Assert.Equal(
            new[]
            {
                "pin-b",
                "pin-a",
                "pin-new",
                "project-b",
                "project-a",
            },
            state.FrozenEntries.Select(entry => entry.Id));
        Assert.Equal("project-b", state.FrozenEntries[state.SelectedIndex].Id);
    }

    [Fact]
    public void Synchronize_ExplicitReorderCanAdoptNewCandidateOrder()
    {
        var state = new SidebarNavigationState();
        state.Synchronize(
        [
            Entry("one", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
            Entry("two", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
        ],
        preferredId: "two");

        var result = state.Synchronize(
        [
            Entry("two", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
            Entry("one", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
        ],
        preferredId: "two",
        forceRebuild: true);

        Assert.True(result.OrderChanged);
        Assert.Equal(
            new[] { "two", "one" },
            state.FrozenEntries.Select(entry => entry.Id));
        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void Synchronize_RemovedSelectionKeepsNearestStablePosition()
    {
        var state = new SidebarNavigationState();
        state.Synchronize(
        [
            Entry("one", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
            Entry("running", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
            Entry("three", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
        ],
        preferredId: "running");

        state.Synchronize(
        [
            Entry("three", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
            Entry("one", SidebarScope.ProjectlessTasks, SidebarLayer.Tasks),
        ],
        preferredId: null);

        Assert.Equal(
            new[] { "one", "three" },
            state.FrozenEntries.Select(entry => entry.Id));
        Assert.Equal(1, state.SelectedIndex);
        Assert.Equal(
            "three",
            state.SelectedEntry(state.FrozenEntries)?.Id);
    }

    [Fact]
    public void TryMove_CrossesRootSectionBoundaryContinuously()
    {
        var entries = RootEntries();
        var state = new SidebarNavigationState();
        state.Restore(entries, "pinned-task");

        var moved = state.TryMove(entries, 1, out var selected);

        Assert.True(moved);
        Assert.NotNull(selected);
        Assert.Equal("pinned-project", selected.Id);
        Assert.Equal(
            SidebarScope.PinnedProjects,
            selected.NavigationScope);
        Assert.Equal(1, state.SelectedIndex);
    }

    [Fact]
    public void TryJumpToScope_OnlyMovesCursorWithinUnifiedList()
    {
        var entries = RootEntries();
        var originalIds = entries.Select(entry => entry.Id).ToArray();
        var state = new SidebarNavigationState();
        state.Restore(entries, "pinned-task");

        var jumped = state.TryJumpToScope(
            entries,
            SidebarScope.Projects,
            preferredId: null,
            out var selected);

        Assert.True(jumped);
        Assert.NotNull(selected);
        Assert.Equal("project", selected.Id);
        Assert.Equal(2, state.SelectedIndex);
        Assert.Equal(originalIds, entries.Select(entry => entry.Id));
    }

    [Fact]
    public void FindCurrentThread_RequiresUniqueExactTitle()
    {
        var unique = Thread("unique", "Current task");
        var duplicateOne = Thread("duplicate-one", "Repeated task");
        var duplicateTwo = Thread("duplicate-two", "Repeated task");
        var snapshot = new CodexSnapshot
        {
            Threads = [unique, duplicateOne, duplicateTwo],
        };

        Assert.Same(
            unique,
            SidebarNavigationState.FindCurrentThread(
                snapshot,
                "Current task"));
        Assert.Null(SidebarNavigationState.FindCurrentThread(
            snapshot,
            "Repeated task"));
        Assert.Null(SidebarNavigationState.FindCurrentThread(
            snapshot,
            "current task"));
    }

    private static IReadOnlyList<SidebarEntry> RootEntries() =>
    [
        Entry(
            "pinned-task",
            SidebarScope.PinnedTasks,
            SidebarLayer.Pinned),
        Entry(
            "pinned-project",
            SidebarScope.PinnedProjects,
            SidebarLayer.Projects),
        Entry(
            "project",
            SidebarScope.Projects,
            SidebarLayer.Projects),
        Entry(
            "projectless-task",
            SidebarScope.ProjectlessTasks,
            SidebarLayer.Tasks),
    ];

    private static SidebarEntry Entry(
        string id,
        SidebarScope scope,
        SidebarLayer layer,
        string? title = null,
        int? nativeListIndex = null) =>
        new(
            id,
            title ?? id,
            string.Empty,
            layer,
            NativeListIndex: nativeListIndex,
            NavigationScope: scope);

    private static CodexThread Thread(
        string id,
        string title) =>
        new(
            id,
            title,
            DateTimeOffset.UtcNow,
            ProjectPath: null,
            IsPinned: false,
            NativeTitle: title);
}
