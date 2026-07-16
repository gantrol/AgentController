using CodexController.Localization;
using CodexController.Models;
using CodexController.Services;
using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Text.Json;

namespace CodexController.Tests;

public sealed class CodexDataServiceTests
{
    private readonly CodexDataService _service = new();

    [Fact]
    public void BuildEntries_ProjectsEachRootDomainWithoutOverlap()
    {
        var fixture = CreateSnapshot();

        var pinnedTasks = _service.BuildEntries(
            fixture.Snapshot,
            SidebarScope.PinnedTasks,
            selectedProjectPath: null);
        var pinnedProjects = _service.BuildEntries(
            fixture.Snapshot,
            SidebarScope.PinnedProjects,
            selectedProjectPath: null);
        var projects = _service.BuildEntries(
            fixture.Snapshot,
            SidebarScope.Projects,
            selectedProjectPath: null);
        var projectlessTasks = _service.BuildEntries(
            fixture.Snapshot,
            SidebarScope.ProjectlessTasks,
            selectedProjectPath: null);

        Assert.Equal(
            new[]
            {
                fixture.PinnedRegularProjectTask.Id,
                fixture.PinnedPinnedProjectTask.Id,
            },
            pinnedTasks.Select(entry => entry.Id));
        Assert.All(
            pinnedTasks,
            entry =>
            {
                Assert.Equal(SidebarLayer.Pinned, entry.Layer);
                Assert.True(entry.IsPinned);
                Assert.Equal(
                    SidebarScope.PinnedTasks,
                    entry.NavigationScope);
            });

        var pinnedProject = Assert.Single(pinnedProjects);
        Assert.Equal(fixture.PinnedProject.Path, pinnedProject.Id);
        Assert.True(pinnedProject.IsPinned);
        Assert.True(pinnedProject.ProjectIsPinned);
        Assert.Null(pinnedProject.ThreadId);
        Assert.Equal(
            SidebarScope.PinnedProjects,
            pinnedProject.NavigationScope);

        var regularProject = Assert.Single(projects);
        Assert.Equal(fixture.RegularProject.Path, regularProject.Id);
        Assert.False(regularProject.IsPinned);
        Assert.False(regularProject.ProjectIsPinned);
        Assert.Null(regularProject.ThreadId);
        Assert.Equal(
            SidebarScope.Projects,
            regularProject.NavigationScope);

        var projectlessTask = Assert.Single(projectlessTasks);
        Assert.Equal(fixture.ProjectlessTask.Id, projectlessTask.Id);
        Assert.Equal(0, projectlessTask.NativeListIndex);
        Assert.Equal(SidebarLayer.Tasks, projectlessTask.Layer);
        Assert.Equal(
            SidebarScope.ProjectlessTasks,
            projectlessTask.NavigationScope);
    }

    [Fact]
    public void BuildUnifiedEntries_UsesContinuousRootOrder()
    {
        var fixture = CreateSnapshot();

        var entries = _service.BuildUnifiedEntries(fixture.Snapshot);

        Assert.Equal(
            new[]
            {
                fixture.PinnedRegularProjectTask.Id,
                fixture.PinnedPinnedProjectTask.Id,
                fixture.PinnedProject.Path,
                fixture.RegularProject.Path,
                fixture.ProjectlessTask.Id,
            },
            entries.Select(entry => entry.Id));
        Assert.Equal(
            new[]
            {
                SidebarScope.PinnedTasks,
                SidebarScope.PinnedTasks,
                SidebarScope.PinnedProjects,
                SidebarScope.Projects,
                SidebarScope.ProjectlessTasks,
            },
            entries.Select(entry => entry.NavigationScope));
    }

    [Fact]
    public void BuildEntries_ProjectTasksIncludesPinnedTaskOnceAndKeepsMembership()
    {
        var fixture = CreateSnapshot();

        var entries = _service.BuildEntries(
            fixture.Snapshot,
            SidebarScope.ProjectTasks,
            fixture.PinnedProject.Path);

        Assert.Equal(
            new[]
            {
                fixture.PinnedPinnedProjectTask.Id,
                fixture.RegularPinnedProjectTask.Id,
            },
            entries.Select(entry => entry.Id));
        Assert.Equal(
            entries.Count,
            entries.Select(entry => entry.Id).Distinct(
                StringComparer.OrdinalIgnoreCase).Count());

        var pinnedTask = entries[0];
        Assert.Equal(fixture.PinnedProject.Path, pinnedTask.ProjectPath);
        Assert.True(pinnedTask.IsPinned);
        Assert.True(pinnedTask.ProjectIsPinned);
        Assert.Equal(SidebarLayer.Tasks, pinnedTask.Layer);

        var regularTask = entries[1];
        Assert.Equal(fixture.PinnedProject.Path, regularTask.ProjectPath);
        Assert.False(regularTask.IsPinned);
        Assert.True(regularTask.ProjectIsPinned);
        Assert.Equal(SidebarLayer.Tasks, regularTask.Layer);
    }

    [Fact]
    public void BuildEntries_ProjectTasksPreserveProjectOrderAcrossPinBadges()
    {
        const string projectPath = @"D:\projects\ordered";
        var regularFirst = Thread(
            "regular-first",
            projectPath,
            isPinned: false,
            DateTimeOffset.UtcNow.AddMinutes(-3));
        var pinnedMiddle = Thread(
            "pinned-middle",
            projectPath,
            isPinned: true,
            DateTimeOffset.UtcNow);
        var regularLast = Thread(
            "regular-last",
            projectPath,
            isPinned: false,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var snapshot = new CodexSnapshot
        {
            Threads = [pinnedMiddle, regularLast, regularFirst],
            PinnedThreads = [pinnedMiddle],
            Projects =
            [
                new CodexProject(
                    projectPath,
                    "ordered",
                    IsPinned: true,
                    Threads: [regularFirst, pinnedMiddle, regularLast]),
            ],
        };

        var entries = _service.BuildEntries(
            snapshot,
            SidebarScope.ProjectTasks,
            projectPath);

        Assert.Equal(
            new[] { "regular-first", "pinned-middle", "regular-last" },
            entries.Select(entry => entry.Id));
        Assert.Equal(
            new[] { false, true, false },
            entries.Select(entry => entry.IsPinned));
        Assert.All(
            entries,
            entry => Assert.Equal(SidebarLayer.Tasks, entry.Layer));
    }

    [Fact]
    public void LoadSnapshotAndBuildEntries_PreferExplicitOrderBeforeUpdatedAt()
    {
        var codexHome = Path.Combine(
            Path.GetTempPath(),
            $"AgentController-tests-{Guid.NewGuid():N}");
        var sessionsPath = Path.Combine(codexHome, "sessions");
        var archivedSessionsPath = Path.Combine(
            codexHome,
            "archived_sessions");
        Directory.CreateDirectory(sessionsPath);
        Directory.CreateDirectory(archivedSessionsPath);

        try
        {
            var path = Path.Combine(codexHome, "ordered-project");
            var explicitFirstId = Guid.NewGuid().ToString();
            var newerSecondId = Guid.NewGuid().ToString();
            var explicitFirstAt = DateTimeOffset.UtcNow.AddDays(-10);
            var newerSecondAt = DateTimeOffset.UtcNow;
            var sessionIndexPath = Path.Combine(
                codexHome,
                "session_index.jsonl");
            File.WriteAllLines(
                sessionIndexPath,
                [
                    JsonSerializer.Serialize(new
                    {
                        id = explicitFirstId,
                        thread_name = "explicit-first",
                        updated_at = explicitFirstAt.ToString("O"),
                    }),
                    JsonSerializer.Serialize(new
                    {
                        id = newerSecondId,
                        thread_name = "newer-second",
                        updated_at = newerSecondAt.ToString("O"),
                    }),
                ]);
            File.WriteAllText(
                Path.Combine(codexHome, ".codex-global-state.json"),
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["electron-saved-workspace-roots"] = new[] { path },
                    ["thread-project-assignments"] =
                        new Dictionary<string, object?>
                        {
                            [explicitFirstId] = new { projectId = path },
                            [newerSecondId] = new { projectId = path },
                        },
                    ["sidebar-project-thread-orders"] =
                        new Dictionary<string, object?>
                        {
                            [path] = new
                            {
                                threadIds = new[]
                                {
                                    explicitFirstId,
                                    newerSecondId,
                                },
                            },
                        },
                }));
            File.WriteAllText(
                Path.Combine(sessionsPath, $"rollout-{explicitFirstId}.jsonl"),
                string.Empty);
            File.WriteAllText(
                Path.Combine(sessionsPath, $"rollout-{newerSecondId}.jsonl"),
                string.Empty);

            var service = CreateServiceForCodexHome(codexHome);
            var snapshot = service.LoadSnapshot();
            var project = Assert.Single(snapshot.Projects);
            Assert.Equal(
                new[] { explicitFirstId, newerSecondId },
                project.Threads.Select(thread => thread.Id));

            var entries = service.BuildEntries(
                snapshot,
                SidebarScope.ProjectTasks,
                path);

            Assert.Equal(
                new[] { explicitFirstId, newerSecondId },
                entries.Select(entry => entry.Id));
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public void BuildEntries_ProjectTasksRequiresASelectedKnownProject()
    {
        var fixture = CreateSnapshot();

        Assert.Empty(_service.BuildEntries(
            fixture.Snapshot,
            SidebarScope.ProjectTasks,
            selectedProjectPath: null));
        Assert.Empty(_service.BuildEntries(
            fixture.Snapshot,
            SidebarScope.ProjectTasks,
            @"D:\projects\missing"));
    }

    [Fact]
    public void LoadSnapshot_PrefersSqliteRecencyOverStaleSessionIndex()
    {
        var codexHome = Path.Combine(
            Path.GetTempPath(),
            $"AgentController-tests-{Guid.NewGuid():N}");
        var sessionsPath = Path.Combine(codexHome, "sessions");
        var archivedSessionsPath = Path.Combine(
            codexHome,
            "archived_sessions");
        Directory.CreateDirectory(sessionsPath);
        Directory.CreateDirectory(archivedSessionsPath);

        try
        {
            var staleFirstId = Guid.NewGuid().ToString();
            var freshFirstId = Guid.NewGuid().ToString();
            var staleFirstRollout = Path.Combine(
                sessionsPath,
                $"rollout-{staleFirstId}.jsonl");
            var freshFirstRollout = Path.Combine(
                sessionsPath,
                $"rollout-{freshFirstId}.jsonl");
            File.WriteAllText(staleFirstRollout, string.Empty);
            File.WriteAllText(freshFirstRollout, string.Empty);

            File.WriteAllLines(
                Path.Combine(codexHome, "session_index.jsonl"),
                [
                    JsonSerializer.Serialize(new
                    {
                        id = staleFirstId,
                        thread_name = "stale-index-first",
                        updated_at =
                            DateTimeOffset.UtcNow.ToString("O"),
                    }),
                    JsonSerializer.Serialize(new
                    {
                        id = freshFirstId,
                        thread_name = "sqlite-recency-first",
                        updated_at =
                            DateTimeOffset.UtcNow
                                .AddDays(-30)
                                .ToString("O"),
                    }),
                ]);
            File.WriteAllText(
                Path.Combine(codexHome, ".codex-global-state.json"),
                "{}");

            var staleRecency = DateTimeOffset.UtcNow.AddDays(-10);
            var freshRecency = DateTimeOffset.UtcNow.AddMinutes(-1);
            var databasePath = Path.Combine(
                codexHome,
                "state_1.sqlite");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
            }.ToString();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TABLE threads (
                        id TEXT PRIMARY KEY,
                        archived INTEGER NOT NULL,
                        preview TEXT NOT NULL,
                        thread_source TEXT NOT NULL,
                        cwd TEXT NOT NULL,
                        rollout_path TEXT NOT NULL,
                        updated_at INTEGER NOT NULL,
                        updated_at_ms INTEGER NOT NULL,
                        recency_at_ms INTEGER NOT NULL
                    );
                    INSERT INTO threads (
                        id,
                        archived,
                        preview,
                        thread_source,
                        cwd,
                        rollout_path,
                        updated_at,
                        updated_at_ms,
                        recency_at_ms
                    ) VALUES (
                        $staleId,
                        0,
                        'preview',
                        'cli',
                        '',
                        $staleRollout,
                        $staleUpdatedSeconds,
                        $staleUpdatedMilliseconds,
                        $staleRecencyMilliseconds
                    );
                    INSERT INTO threads (
                        id,
                        archived,
                        preview,
                        thread_source,
                        cwd,
                        rollout_path,
                        updated_at,
                        updated_at_ms,
                        recency_at_ms
                    ) VALUES (
                        $freshId,
                        0,
                        'preview',
                        'cli',
                        '',
                        $freshRollout,
                        $freshUpdatedSeconds,
                        $freshUpdatedMilliseconds,
                        $freshRecencyMilliseconds
                    );
                    """;
                command.Parameters.AddWithValue(
                    "$staleId",
                    staleFirstId);
                command.Parameters.AddWithValue(
                    "$staleRollout",
                    staleFirstRollout);
                command.Parameters.AddWithValue(
                    "$staleUpdatedSeconds",
                    staleRecency.ToUnixTimeSeconds());
                command.Parameters.AddWithValue(
                    "$staleUpdatedMilliseconds",
                    staleRecency.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue(
                    "$staleRecencyMilliseconds",
                    staleRecency.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue(
                    "$freshId",
                    freshFirstId);
                command.Parameters.AddWithValue(
                    "$freshRollout",
                    freshFirstRollout);
                command.Parameters.AddWithValue(
                    "$freshUpdatedSeconds",
                    freshRecency.ToUnixTimeSeconds());
                command.Parameters.AddWithValue(
                    "$freshUpdatedMilliseconds",
                    freshRecency.ToUnixTimeMilliseconds());
                command.Parameters.AddWithValue(
                    "$freshRecencyMilliseconds",
                    freshRecency.ToUnixTimeMilliseconds());
                command.ExecuteNonQuery();
            }

            var service = CreateServiceForCodexHome(codexHome);
            var snapshot = service.LoadSnapshot();

            Assert.Equal(
                new[] { freshFirstId, staleFirstId },
                snapshot.Threads.Select(thread => thread.Id));
            Assert.Equal(
                new[] { freshFirstId, staleFirstId },
                snapshot.ProjectlessThreads.Select(thread => thread.Id));
            Assert.Equal(
                freshRecency.ToUnixTimeMilliseconds(),
                snapshot.Threads[0].UpdatedAt.ToUnixTimeMilliseconds());
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public void LoadSnapshot_ProjectlessTasksPreferPersistedSidebarOrder()
    {
        var codexHome = Path.Combine(
            Path.GetTempPath(),
            $"AgentController-tests-{Guid.NewGuid():N}");
        var sessionsPath = Path.Combine(codexHome, "sessions");
        Directory.CreateDirectory(sessionsPath);

        try
        {
            var olderId = Guid.NewGuid().ToString();
            var newerId = Guid.NewGuid().ToString();
            File.WriteAllText(
                Path.Combine(
                    sessionsPath,
                    $"rollout-{olderId}.jsonl"),
                string.Empty);
            File.WriteAllText(
                Path.Combine(
                    sessionsPath,
                    $"rollout-{newerId}.jsonl"),
                string.Empty);
            File.WriteAllLines(
                Path.Combine(codexHome, "session_index.jsonl"),
                [
                    JsonSerializer.Serialize(new
                    {
                        id = olderId,
                        thread_name = "older",
                        updated_at = DateTimeOffset.UtcNow
                            .AddDays(-2)
                            .ToString("O"),
                    }),
                    JsonSerializer.Serialize(new
                    {
                        id = newerId,
                        thread_name = "newer",
                        updated_at = DateTimeOffset.UtcNow.ToString("O"),
                    }),
                ]);
            File.WriteAllText(
                Path.Combine(codexHome, ".codex-global-state.json"),
                JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["projectless-thread-ids"] =
                        new[] { newerId, olderId },
                }));

            var service = CreateServiceForCodexHome(codexHome);
            var snapshot = service.LoadSnapshot();

            Assert.Equal(
                new[] { newerId, olderId },
                snapshot.Threads.Select(thread => thread.Id));
            Assert.Equal(
                new[] { olderId, newerId },
                snapshot.ProjectlessThreads.Select(thread => thread.Id));
        }
        finally
        {
            Directory.Delete(codexHome, recursive: true);
        }
    }

    [Fact]
    public void BuildEntries_LocalizesPresentationAndRebuildsAfterSwitch()
    {
        var now = new DateTimeOffset(
            2026,
            1,
            10,
            12,
            0,
            0,
            TimeSpan.Zero);
        var localization = new LocalizationService(AppLanguage.EnUs);
        var service = new CodexDataService(
            localization,
            () => now);
        const string pinnedPath = @"D:\projects\pinned";
        const string regularPath = @"D:\projects\regular";
        var pinnedTask = new CodexThread(
            "pinned-task",
            string.Empty,
            now.AddHours(-2),
            pinnedPath,
            IsPinned: true,
            NativeTitle: "New task");
        var regularTask = new CodexThread(
            "regular-task",
            "Named task",
            now.AddMinutes(-4),
            pinnedPath,
            IsPinned: false,
            NativeTitle: "Named task");
        var loneTask = new CodexThread(
            "lone-task",
            "Only task",
            now,
            regularPath,
            IsPinned: false,
            NativeTitle: "Only task");
        var snapshot = new CodexSnapshot
        {
            Threads = [pinnedTask, regularTask, loneTask],
            PinnedThreads = [pinnedTask],
            Projects =
            [
                new CodexProject(
                    pinnedPath,
                    "Pinned project",
                    IsPinned: true,
                    Threads: [pinnedTask, regularTask]),
                new CodexProject(
                    regularPath,
                    "Regular project",
                    IsPinned: false,
                    Threads: [loneTask]),
            ],
        };

        var englishPinnedProject = Assert.Single(
            service.BuildEntries(
                snapshot,
                SidebarScope.PinnedProjects,
                selectedProjectPath: null));
        Assert.Equal("2 tasks", englishPinnedProject.Subtitle);
        Assert.Equal("Pinned", englishPinnedProject.PinBadge);
        Assert.Equal("→ Enter", englishPinnedProject.ActionHint);

        var englishRegularProject = Assert.Single(
            service.BuildEntries(
                snapshot,
                SidebarScope.Projects,
                selectedProjectPath: null));
        Assert.Equal("1 task", englishRegularProject.Subtitle);
        Assert.Equal(string.Empty, englishRegularProject.PinBadge);
        Assert.Equal("→ Enter", englishRegularProject.ActionHint);

        var englishTasks = service.BuildEntries(
            snapshot,
            SidebarScope.ProjectTasks,
            pinnedPath);
        Assert.Equal("Untitled task", englishTasks[0].Title);
        Assert.Equal(
            "Pinned · 2 hours ago",
            englishTasks[0].Subtitle);
        Assert.Equal("Pinned", englishTasks[0].PinBadge);
        Assert.Equal("A Open", englishTasks[0].ActionHint);
        Assert.Equal(string.Empty, englishTasks[1].PinBadge);
        Assert.Equal("4 minutes ago", englishTasks[1].Subtitle);

        localization.SetLanguage(AppLanguage.ZhCn);

        var chinesePinnedProject = Assert.Single(
            service.BuildEntries(
                snapshot,
                SidebarScope.PinnedProjects,
                selectedProjectPath: null));
        Assert.Equal("2 个任务", chinesePinnedProject.Subtitle);
        Assert.Equal("置顶", chinesePinnedProject.PinBadge);
        Assert.Equal("→ 进入", chinesePinnedProject.ActionHint);

        var chineseTasks = service.BuildEntries(
            snapshot,
            SidebarScope.ProjectTasks,
            pinnedPath);
        Assert.Equal("未命名任务", chineseTasks[0].Title);
        Assert.Equal(
            "置顶 · 2 小时前",
            chineseTasks[0].Subtitle);
        Assert.Equal("置顶", chineseTasks[0].PinBadge);
        Assert.Equal("A 打开", chineseTasks[0].ActionHint);
        Assert.Equal("4 分钟前", chineseTasks[1].Subtitle);
    }

    [Fact]
    public void BuildEntries_LocalizesAllRelativeTimeBuckets()
    {
        var now = new DateTimeOffset(
            2026,
            1,
            10,
            12,
            0,
            0,
            TimeSpan.Zero);
        var localization = new LocalizationService(AppLanguage.EnUs);
        var service = new CodexDataService(
            localization,
            () => now);
        var threads = new[]
        {
            Thread(
                "now",
                projectPath: null,
                isPinned: false,
                now.AddSeconds(-30)),
            Thread(
                "one-minute",
                projectPath: null,
                isPinned: false,
                now.AddMinutes(-1)),
            Thread(
                "minutes",
                projectPath: null,
                isPinned: false,
                now.AddMinutes(-5)),
            Thread(
                "one-hour",
                projectPath: null,
                isPinned: false,
                now.AddHours(-1)),
            Thread(
                "hours",
                projectPath: null,
                isPinned: false,
                now.AddHours(-3)),
            Thread(
                "one-day",
                projectPath: null,
                isPinned: false,
                now.AddDays(-1)),
            Thread(
                "days",
                projectPath: null,
                isPinned: false,
                now.AddDays(-2)),
        };
        var snapshot = new CodexSnapshot
        {
            Threads = threads,
            ProjectlessThreads = threads,
        };

        var english = service.BuildEntries(
            snapshot,
            SidebarScope.ProjectlessTasks,
            selectedProjectPath: null);
        Assert.Equal(
            new[]
            {
                "Just now",
                "1 minute ago",
                "5 minutes ago",
                "1 hour ago",
                "3 hours ago",
                "1 day ago",
                "2 days ago",
            },
            english.Select(entry => entry.Subtitle));

        localization.SetLanguage(AppLanguage.ZhCn);

        var chinese = service.BuildEntries(
            snapshot,
            SidebarScope.ProjectlessTasks,
            selectedProjectPath: null);
        Assert.Equal(
            new[]
            {
                "刚刚",
                "1 分钟前",
                "5 分钟前",
                "1 小时前",
                "3 小时前",
                "1 天前",
                "2 天前",
            },
            chinese.Select(entry => entry.Subtitle));
    }

    private static SnapshotFixture CreateSnapshot()
    {
        const string pinnedPath = @"D:\projects\pinned";
        const string regularPath = @"D:\projects\regular";

        var pinnedPinnedProjectTask = Thread(
            "pinned-in-pinned-project",
            pinnedPath,
            isPinned: true,
            DateTimeOffset.UtcNow.AddMinutes(-3));
        var regularPinnedProjectTask = Thread(
            "regular-in-pinned-project",
            pinnedPath,
            isPinned: false,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var pinnedRegularProjectTask = Thread(
            "pinned-in-regular-project",
            regularPath,
            isPinned: true,
            DateTimeOffset.UtcNow.AddMinutes(-2));
        var regularRegularProjectTask = Thread(
            "regular-in-regular-project",
            regularPath,
            isPinned: false,
            DateTimeOffset.UtcNow);
        var projectlessTask = Thread(
            "projectless",
            projectPath: null,
            isPinned: false,
            DateTimeOffset.UtcNow.AddMinutes(-4));

        var pinnedProject = new CodexProject(
            pinnedPath,
            "pinned",
            IsPinned: true,
            Threads:
            [
                pinnedPinnedProjectTask,
                regularPinnedProjectTask,
            ]);
        var regularProject = new CodexProject(
            regularPath,
            "regular",
            IsPinned: false,
            Threads:
            [
                pinnedRegularProjectTask,
                regularRegularProjectTask,
            ]);
        var snapshot = new CodexSnapshot
        {
            Threads =
            [
                regularRegularProjectTask,
                regularPinnedProjectTask,
                pinnedRegularProjectTask,
                pinnedPinnedProjectTask,
                projectlessTask,
            ],
            PinnedThreads =
            [
                pinnedRegularProjectTask,
                pinnedPinnedProjectTask,
            ],
            ProjectlessThreads = [projectlessTask],
            Projects = [pinnedProject, regularProject],
        };

        return new SnapshotFixture(
            snapshot,
            pinnedProject,
            regularProject,
            pinnedPinnedProjectTask,
            regularPinnedProjectTask,
            pinnedRegularProjectTask,
            regularRegularProjectTask,
            projectlessTask);
    }

    private static CodexThread Thread(
        string id,
        string? projectPath,
        bool isPinned,
        DateTimeOffset updatedAt)
    {
        return new CodexThread(
            id,
            id,
            updatedAt,
            projectPath,
            isPinned,
            NativeTitle: id);
    }

    private static CodexDataService CreateServiceForCodexHome(
        string codexHome)
    {
        var service = new CodexDataService();
        SetPrivatePath(service, "_codexHome", codexHome);
        SetPrivatePath(
            service,
            "_sessionIndexPath",
            Path.Combine(codexHome, "session_index.jsonl"));
        SetPrivatePath(
            service,
            "_globalStatePath",
            Path.Combine(codexHome, ".codex-global-state.json"));
        SetPrivatePath(
            service,
            "_sessionsPath",
            Path.Combine(codexHome, "sessions"));
        SetPrivatePath(
            service,
            "_archivedSessionsPath",
            Path.Combine(codexHome, "archived_sessions"));
        return service;
    }

    private static void SetPrivatePath(
        CodexDataService service,
        string fieldName,
        string value)
    {
        var field = typeof(CodexDataService).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field.SetValue(service, value);
    }

    private sealed record SnapshotFixture(
        CodexSnapshot Snapshot,
        CodexProject PinnedProject,
        CodexProject RegularProject,
        CodexThread PinnedPinnedProjectTask,
        CodexThread RegularPinnedProjectTask,
        CodexThread PinnedRegularProjectTask,
        CodexThread RegularRegularProjectTask,
        CodexThread ProjectlessTask);
}
