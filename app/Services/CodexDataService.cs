using System.IO;
using System.Text.Json;
using CodexController.Localization;
using CodexController.Models;
using Microsoft.Data.Sqlite;

namespace CodexController.Services;

public sealed class CodexDataService
{
    private static readonly StringComparer PathComparer =
        StringComparer.OrdinalIgnoreCase;

    private readonly string _codexHome;
    private readonly string _sessionIndexPath;
    private readonly string _globalStatePath;
    private readonly string _sessionsPath;
    private readonly string _archivedSessionsPath;
    private readonly LocalizationService _localization;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly CodexRolloutStatusReader _rolloutStatusReader = new();
    private GlobalState? _lastGoodGlobalState;

    public CodexDataService()
        : this(new LocalizationService())
    {
    }

    public CodexDataService(
        LocalizationService localization,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
        _utcNowProvider =
            utcNowProvider ?? (() => DateTimeOffset.UtcNow);
        _codexHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        _sessionIndexPath = Path.Combine(_codexHome, "session_index.jsonl");
        _globalStatePath = Path.Combine(_codexHome, ".codex-global-state.json");
        _sessionsPath = Path.Combine(_codexHome, "sessions");
        _archivedSessionsPath = Path.Combine(
            _codexHome,
            "archived_sessions");
    }

    public CodexSnapshot LoadSnapshot()
    {
        var sessionItems = ReadSessionIndex();
        var state = ReadGlobalState();
        var catalog = ReadThreadCatalog();
        var pinnedThreadIds = state.PinnedThreadIds.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var unreadThreadIds = state.UnreadThreadIds.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var assignments = state.ThreadProjectAssignments;
        var threads = new List<CodexThread>();
        var archivedThreadCount = 0;
        var unavailableThreadCount = 0;

        foreach (var item in sessionItems.Values)
        {
            if (
                !Guid.TryParse(item.Id, out _) ||
                !catalog.TryGetValue(item.Id, out var metadata))
            {
                unavailableThreadCount++;
                continue;
            }

            if (metadata.IsArchived)
            {
                archivedThreadCount++;
                continue;
            }

            if (!metadata.IsDeepLinkCandidate)
            {
                unavailableThreadCount++;
                continue;
            }

            assignments.TryGetValue(item.Id, out var projectPath);
            projectPath ??= ResolveProjectPath(metadata.WorkingDirectory, state);
            var effectiveRecency =
                metadata.RecencyAt ??
                metadata.UpdatedAt ??
                item.UpdatedAt;
            threads.Add(new CodexThread(
                item.Id,
                NormalizeTitle(item.Title),
                effectiveRecency,
                projectPath,
                pinnedThreadIds.Contains(item.Id),
                NormalizeNativeTitle(item.Title)));
        }

        var loadedThreadIds = threads
            .Select(thread => thread.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in state.ProjectlessThreadIds)
        {
            if (
                sessionItems.ContainsKey(id) ||
                loadedThreadIds.Contains(id) ||
                !Guid.TryParse(id, out _) ||
                !catalog.TryGetValue(id, out var metadata))
            {
                continue;
            }

            if (metadata.IsArchived)
            {
                archivedThreadCount++;
                continue;
            }

            if (!metadata.IsDeepLinkCandidate)
            {
                unavailableThreadCount++;
                continue;
            }

            threads.Add(new CodexThread(
                id,
                string.Empty,
                metadata.RecencyAt ??
                metadata.UpdatedAt ??
                DateTimeOffset.MinValue,
                ProjectPath: null,
                IsPinned: false,
                NativeTitle: "New task"));
            loadedThreadIds.Add(id);
        }

        threads = threads
            .OrderByDescending(item => item.UpdatedAt)
            .ToList();

        // The Agent keypad currently exposes the six most recent threads.
        // Probe only those rollouts so refresh stays incremental and bounded.
        for (var index = 0; index < Math.Min(6, threads.Count); index++)
        {
            var thread = threads[index];
            if (catalog.TryGetValue(thread.Id, out var metadata))
            {
                var status = _rolloutStatusReader.Read(
                    metadata.RolloutPath);
                if (
                    status is not ThreadStatus.Thinking and
                        not ThreadStatus.Error &&
                    unreadThreadIds.Contains(thread.Id))
                {
                    status = ThreadStatus.CompleteUnread;
                }

                threads[index] = thread with
                {
                    Status = status,
                };
            }
        }

        var threadLookup = threads.ToDictionary(
            thread => thread.Id,
            StringComparer.OrdinalIgnoreCase);
        var pinned = state.PinnedThreadIds
            .Select(id => threadLookup.GetValueOrDefault(id))
            .Where(thread => thread is not null)
            .Cast<CodexThread>()
            .ToList();
        var projectlessCandidates = threads
            .Where(thread =>
                !thread.IsPinned &&
                string.IsNullOrWhiteSpace(thread.ProjectPath))
            .OrderByDescending(thread => thread.UpdatedAt)
            .ToList();
        var projectlessLookup = projectlessCandidates.ToDictionary(
            thread => thread.Id,
            StringComparer.OrdinalIgnoreCase);
        var explicitlyOrderedProjectless = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        // Codex persists this list in insertion order while rendering it
        // newest-first. Prefer that native order over activity timestamps;
        // recency remains the fallback for entries absent from persisted UI
        // state.
        var projectless = state.ProjectlessThreadIds
            .Reverse()
            .Select(id => projectlessLookup.GetValueOrDefault(id))
            .Where(thread =>
                thread is not null &&
                explicitlyOrderedProjectless.Add(thread.Id))
            .Cast<CodexThread>()
            .Concat(projectlessCandidates.Where(thread =>
                !explicitlyOrderedProjectless.Contains(thread.Id)))
            .ToList();

        var projectPaths = state.KnownProjectPaths
            .Concat(threads
                .Select(thread => thread.ProjectPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>())
            .Distinct(PathComparer)
            .ToList();
        var pinnedProjectOrder = state.PinnedProjectIds
            .Select((path, index) => new { path, index })
            .ToDictionary(
                item => item.path,
                item => item.index,
                PathComparer);
        var projectOrder = state.ProjectOrder
            .Select((path, index) => new { path, index })
            .ToDictionary(
                item => item.path,
                item => item.index,
                PathComparer);

        var projects = projectPaths
            .Select(path =>
            {
                var explicitThreadOrder = state.ProjectThreadOrders
                    .GetValueOrDefault(path, []);
                var explicitThreadIndexes = explicitThreadOrder
                    .Select((id, index) => new { id, index })
                    .ToDictionary(
                        item => item.id,
                        item => item.index,
                        StringComparer.OrdinalIgnoreCase);
                var projectThreads = threads
                    .Where(thread =>
                        thread.ProjectPath is not null &&
                        PathComparer.Equals(thread.ProjectPath, path))
                    .OrderBy(thread =>
                        explicitThreadIndexes.ContainsKey(thread.Id) ? 0 : 1)
                    .ThenBy(thread =>
                        explicitThreadIndexes.GetValueOrDefault(
                            thread.Id,
                            int.MaxValue))
                    .ThenByDescending(thread => thread.UpdatedAt)
                    .ToList();
                return new CodexProject(
                    path,
                    state.WorkspaceRootLabels.GetValueOrDefault(path) ??
                        GetProjectName(path),
                    pinnedProjectOrder.ContainsKey(path),
                    projectThreads);
            })
            .OrderBy(project =>
                pinnedProjectOrder.GetValueOrDefault(
                    project.Path,
                    int.MaxValue))
            .ThenBy(project =>
                projectOrder.GetValueOrDefault(
                    project.Path,
                    int.MaxValue))
            .ThenBy(
                project => project.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new CodexSnapshot
        {
            Threads = threads,
            PinnedThreads = pinned,
            ProjectlessThreads = projectless,
            Projects = projects,
            ArchivedThreadCount = archivedThreadCount,
            UnavailableThreadCount = unavailableThreadCount,
        };
    }

    public bool IsThreadAvailable(string threadId)
    {
        if (
            string.IsNullOrWhiteSpace(threadId) ||
            !Guid.TryParse(threadId, out _))
        {
            return false;
        }

        var catalog = ReadThreadCatalog();
        return
            catalog.TryGetValue(threadId, out var metadata) &&
            metadata.IsDeepLinkCandidate;
    }

    public IReadOnlyList<SidebarEntry> BuildEntries(
        CodexSnapshot snapshot,
        SidebarScope scope,
        string? selectedProjectPath)
    {
        var strings = _localization.Strings;
        return scope switch
        {
            SidebarScope.PinnedTasks => snapshot.PinnedThreads
                .Select(thread => new SidebarEntry(
                    thread.Id,
                    DisplayTitle(thread),
                    RelativeTime(thread.UpdatedAt),
                    SidebarLayer.Pinned,
                    ThreadId: thread.Id,
                    ProjectPath: thread.ProjectPath,
                    NativeTitle: thread.NativeTitle,
                    IsPinned: true,
                    PinBadge: strings.SidebarPinnedBadge,
                    ActionHint: strings.SidebarOpenAction,
                    NavigationScope: SidebarScope.PinnedTasks))
                .ToList(),

            SidebarScope.PinnedProjects => snapshot.Projects
                .Where(project => project.IsPinned)
                .Select(project => new SidebarEntry(
                    project.Path,
                    project.Name,
                    strings.SidebarProjectTaskCount(
                        project.Threads.Count),
                    SidebarLayer.Projects,
                    ProjectPath: project.Path,
                    NativeTitle: project.Name,
                    IsPinned: true,
                    ProjectIsPinned: true,
                    PinBadge: strings.SidebarPinnedBadge,
                    ActionHint: strings.SidebarEnterAction,
                    NavigationScope: SidebarScope.PinnedProjects))
                .ToList(),

            SidebarScope.Projects => snapshot.Projects
                .Where(project => !project.IsPinned)
                .Select(project => new SidebarEntry(
                    project.Path,
                    project.Name,
                    strings.SidebarProjectTaskCount(
                        project.Threads.Count),
                    SidebarLayer.Projects,
                    ProjectPath: project.Path,
                    NativeTitle: project.Name,
                    ProjectIsPinned: false,
                    ActionHint: strings.SidebarEnterAction,
                    NavigationScope: SidebarScope.Projects))
                .ToList(),

            SidebarScope.ProjectTasks => BuildProjectTaskEntries(
                snapshot,
                selectedProjectPath),

            SidebarScope.ProjectlessTasks => snapshot.ProjectlessThreads
                .Select((thread, index) => new SidebarEntry(
                    thread.Id,
                    DisplayTitle(thread),
                    RelativeTime(thread.UpdatedAt),
                    SidebarLayer.Tasks,
                    ThreadId: thread.Id,
                    ProjectPath: thread.ProjectPath,
                    NativeTitle: thread.NativeTitle,
                    NativeListIndex: index,
                    ActionHint: strings.SidebarOpenAction,
                    NavigationScope: SidebarScope.ProjectlessTasks))
                .ToList(),

            _ => [],
        };
    }

    public IReadOnlyList<SidebarEntry> BuildUnifiedEntries(
        CodexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return
        [
            .. BuildEntries(
                snapshot,
                SidebarScope.PinnedTasks,
                selectedProjectPath: null),
            .. BuildEntries(
                snapshot,
                SidebarScope.PinnedProjects,
                selectedProjectPath: null),
            .. BuildEntries(
                snapshot,
                SidebarScope.Projects,
                selectedProjectPath: null),
            .. BuildEntries(
                snapshot,
                SidebarScope.ProjectlessTasks,
                selectedProjectPath: null),
        ];
    }

    private IReadOnlyList<SidebarEntry> BuildProjectTaskEntries(
        CodexSnapshot snapshot,
        string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return [];
        }

        var project = snapshot.Projects.FirstOrDefault(item =>
            PathComparer.Equals(item.Path, projectPath));
        if (project is null)
        {
            return [];
        }

        var strings = _localization.Strings;
        return project.Threads
            .Select(thread => new SidebarEntry(
                thread.Id,
                DisplayTitle(thread),
                thread.IsPinned
                    ? strings.SidebarPinnedRelativeTime(
                        RelativeTime(thread.UpdatedAt))
                    : RelativeTime(thread.UpdatedAt),
                SidebarLayer.Tasks,
                ThreadId: thread.Id,
                ProjectPath: projectPath,
                NativeTitle: thread.NativeTitle,
                IsPinned: thread.IsPinned,
                ProjectIsPinned: project.IsPinned,
                PinBadge: thread.IsPinned
                    ? strings.SidebarPinnedBadge
                    : string.Empty,
                ActionHint: strings.SidebarOpenAction,
                NavigationScope: SidebarScope.ProjectTasks))
            .ToList();
    }

    private Dictionary<string, SessionIndexItem> ReadSessionIndex()
    {
        var result = new Dictionary<string, SessionIndexItem>(
            StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(_sessionIndexPath))
        {
            return result;
        }

        foreach (var line in File.ReadLines(_sessionIndexPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var id = root.GetProperty("id").GetString();
                var title = root.GetProperty("thread_name").GetString();
                var updatedText = root.GetProperty("updated_at").GetString();
                if (
                    string.IsNullOrWhiteSpace(id) ||
                    string.IsNullOrWhiteSpace(title) ||
                    !DateTimeOffset.TryParse(updatedText, out var updatedAt))
                {
                    continue;
                }

                var item = new SessionIndexItem(id, title, updatedAt);
                if (
                    !result.TryGetValue(id, out var existing) ||
                    item.UpdatedAt >= existing.UpdatedAt)
                {
                    result[id] = item;
                }
            }
            catch (JsonException)
            {
                // A partially written trailing line is safe to ignore.
            }
        }

        return result;
    }

    private Dictionary<string, ThreadMetadata> ReadThreadCatalog()
    {
        var databasePath = FindStateDatabasePath();
        if (databasePath is not null)
        {
            try
            {
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Mode = SqliteOpenMode.ReadOnly,
                    Cache = SqliteCacheMode.Private,
                    Pooling = false,
                    DefaultTimeout = 1,
                };
                using var connection = new SqliteConnection(builder.ToString());
                connection.Open();
                var columns = ReadThreadColumnNames(connection);
                var threadSource = columns.Contains("thread_source")
                    ? "COALESCE(thread_source, '')"
                    : columns.Contains("source")
                        ? "COALESCE(source, '')"
                        : "''";
                var updatedAt = columns.Contains("updated_at_ms")
                    ? "COALESCE(NULLIF(updated_at_ms, 0), updated_at * 1000)"
                    : "updated_at";
                var recencyAt = columns.Contains("recency_at_ms")
                    ? "NULLIF(recency_at_ms, 0)"
                    : columns.Contains("recency_at")
                        ? "NULLIF(recency_at, 0)"
                        : "NULL";
                using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                    SELECT
                        id,
                        archived,
                        preview,
                        {threadSource},
                        cwd,
                        rollout_path,
                        {updatedAt},
                        {recencyAt}
                    FROM threads;
                    """;
                using var reader = command.ExecuteReader();
                var result = new Dictionary<string, ThreadMetadata>(
                    StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    var id = reader.GetString(0);
                    var rolloutPath = NormalizePath(reader.GetString(5));
                    result[id] = new ThreadMetadata(
                        reader.GetInt64(1) != 0,
                        !string.IsNullOrWhiteSpace(reader.GetString(2)),
                        IsUserFacingSource(reader.GetString(3)),
                        NormalizePath(reader.GetString(4)),
                        rolloutPath,
                        File.Exists(rolloutPath),
                        ReadDatabaseTimestamp(reader, 6),
                        ReadDatabaseTimestamp(reader, 7));
                }

                return result;
            }
            catch (SqliteException)
            {
                // Codex can rotate or briefly lock the database while updating.
            }
            catch (IOException)
            {
                // Fall back to the rollout directories below.
            }
        }

        return ReadThreadCatalogFromRollouts();
    }

    private Dictionary<string, ThreadMetadata> ReadThreadCatalogFromRollouts()
    {
        var result = new Dictionary<string, ThreadMetadata>(
            StringComparer.OrdinalIgnoreCase);
        AddRolloutDirectory(result, _sessionsPath, isArchived: false);
        AddRolloutDirectory(result, _archivedSessionsPath, isArchived: true);
        return result;
    }

    private static HashSet<string> ReadThreadColumnNames(
        SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(threads);";
        using var reader = command.ExecuteReader();
        var result = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            result.Add(reader.GetString(1));
        }

        return result;
    }

    private static void AddRolloutDirectory(
        Dictionary<string, ThreadMetadata> result,
        string directory,
        bool isArchived)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(
                directory,
                "*.jsonl",
                SearchOption.AllDirectories))
            {
                if (!TryExtractThreadId(path, out var id))
                {
                    continue;
                }

                result[id] = new ThreadMetadata(
                    isArchived,
                    HasPreview: true,
                    IsUserFacing: true,
                    WorkingDirectory: null,
                    RolloutPath: path,
                    RolloutExists: true,
                    UpdatedAt: null,
                    RecencyAt: null);
            }
        }
        catch (IOException)
        {
            // A partially rotated folder is safe to skip for this refresh.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve the last safe behavior if a folder is inaccessible.
        }
    }

    private string? FindStateDatabasePath()
    {
        try
        {
            return Directory
                .EnumerateFiles(_codexHome, "state_*.sqlite")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private GlobalState ReadGlobalState()
    {
        if (!File.Exists(_globalStatePath))
        {
            return _lastGoodGlobalState ?? new GlobalState();
        }

        try
        {
            using var stream = File.Open(
                _globalStatePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            var pinnedThreads = new List<string>();
            var pinnedProjects = new List<string>();
            var knownProjects = new List<string>();
            var projectOrder = new List<string>();
            var projectlessThreads = new List<string>();
            var unreadThreads = new List<string>();
            var assignments = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var projectPathsById = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            var workspaceRootLabels = new Dictionary<string, string>(
                PathComparer);
            var projectThreadOrders =
                new Dictionary<string, IReadOnlyList<string>>(PathComparer);

            // Current Codex stores these keys at the document root.
            ReadStateContainer(
                root,
                pinnedThreads,
                pinnedProjects,
                knownProjects,
                projectOrder,
                projectlessThreads,
                unreadThreads,
                assignments,
                projectPathsById,
                workspaceRootLabels,
                projectThreadOrders);

            // Older releases stored them in electron-persisted-atom-state.
            if (
                root.TryGetProperty(
                    "electron-persisted-atom-state",
                    out var legacy))
            {
                ReadStateContainer(
                    legacy,
                    pinnedThreads,
                    pinnedProjects,
                    knownProjects,
                    projectOrder,
                    projectlessThreads,
                    unreadThreads,
                    assignments,
                    projectPathsById,
                    workspaceRootLabels,
                    projectThreadOrders);
            }

            foreach (var path in pinnedProjects)
            {
                AddDistinctPath(knownProjects, path);
            }

            foreach (var path in assignments.Values)
            {
                AddDistinctPath(knownProjects, path);
            }

            var state = new GlobalState
            {
                PinnedThreadIds = pinnedThreads,
                PinnedProjectIds = pinnedProjects,
                KnownProjectPaths = knownProjects,
                ProjectOrder = projectOrder,
                ProjectlessThreadIds = projectlessThreads,
                UnreadThreadIds = unreadThreads,
                ThreadProjectAssignments = assignments,
                WorkspaceRootLabels = workspaceRootLabels,
                ProjectThreadOrders = projectThreadOrders,
            };
            _lastGoodGlobalState = state;
            return state;
        }
        catch
        {
            // Electron replaces this file while saving. Preserve the last
            // complete snapshot so unread LEDs do not flicker to Idle on a
            // partially written refresh.
            return _lastGoodGlobalState ?? new GlobalState();
        }
    }

    private static void ReadStateContainer(
        JsonElement container,
        List<string> pinnedThreads,
        List<string> pinnedProjects,
        List<string> knownProjects,
        List<string> projectOrder,
        List<string> projectlessThreads,
        List<string> unreadThreads,
        Dictionary<string, string> assignments,
        Dictionary<string, string> projectPathsById,
        Dictionary<string, string> workspaceRootLabels,
        Dictionary<string, IReadOnlyList<string>> projectThreadOrders)
    {
        if (container.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddLocalProjects(
            container,
            projectPathsById,
            knownProjects,
            workspaceRootLabels);
        AddStringArray(
            container,
            "pinned-thread-ids",
            pinnedThreads,
            normalizeAsPath: false);
        AddProjectReferenceArray(
            container,
            "pinned-project-ids",
            pinnedProjects,
            projectPathsById);
        AddStringArray(
            container,
            "electron-saved-workspace-roots",
            knownProjects,
            normalizeAsPath: true);
        AddStringArray(
            container,
            "active-workspace-roots",
            knownProjects,
            normalizeAsPath: true);
        AddProjectReferenceArray(
            container,
            "project-order",
            projectOrder,
            projectPathsById);
        AddStringArray(
            container,
            "projectless-thread-ids",
            projectlessThreads,
            normalizeAsPath: false);
        AddUnreadThreadIds(container, unreadThreads);
        foreach (var path in projectOrder)
        {
            AddDistinctPath(knownProjects, path);
        }

        if (
            container.TryGetProperty(
                "electron-workspace-root-labels",
                out var labelElement) &&
            labelElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in labelElement.EnumerateObject())
            {
                if (property.Value.GetString() is { Length: > 0 } label)
                {
                    workspaceRootLabels[NormalizePath(property.Name)] = label;
                }
            }
        }

        if (
            container.TryGetProperty(
                "sidebar-project-thread-orders",
                out var threadOrderElement) &&
            threadOrderElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in threadOrderElement.EnumerateObject())
            {
                if (
                    property.Value.ValueKind != JsonValueKind.Object ||
                    !property.Value.TryGetProperty(
                        "threadIds",
                        out var idsElement) ||
                    idsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var projectPath = ResolveProjectReference(
                    property.Name,
                    projectPathsById);
                if (projectPath is null)
                {
                    continue;
                }

                projectThreadOrders[projectPath] =
                    idsElement
                        .EnumerateArray()
                        .Select(element => element.GetString())
                        .Where(id => !string.IsNullOrWhiteSpace(id))
                        .Cast<string>()
                        .ToList();
            }
        }

        if (
            !container.TryGetProperty(
                "thread-project-assignments",
                out var assignmentElement) ||
            assignmentElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in assignmentElement.EnumerateObject())
        {
            var projectReference =
                ReadString(property.Value, "path") ??
                ReadString(property.Value, "cwd") ??
                ReadString(property.Value, "projectId");
            var projectPath = ResolveProjectReference(
                projectReference,
                projectPathsById);
            if (projectPath is not null)
            {
                assignments[property.Name] = projectPath;
            }
        }
    }

    private static void AddLocalProjects(
        JsonElement container,
        Dictionary<string, string> projectPathsById,
        List<string> knownProjects,
        Dictionary<string, string> workspaceRootLabels)
    {
        if (
            !container.TryGetProperty(
                "local-projects",
                out var projects) ||
            projects.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in projects.EnumerateObject())
        {
            if (
                property.Value.ValueKind != JsonValueKind.Object ||
                !property.Value.TryGetProperty(
                    "rootPaths",
                    out var roots) ||
                roots.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var projectPath = roots
                .EnumerateArray()
                .Where(root => root.ValueKind == JsonValueKind.String)
                .Select(root => root.GetString())
                .FirstOrDefault(root =>
                    !string.IsNullOrWhiteSpace(root));
            if (projectPath is null)
            {
                continue;
            }

            projectPath = NormalizePath(projectPath);
            projectPathsById[property.Name] = projectPath;
            if (
                ReadString(property.Value, "id") is { Length: > 0 } id)
            {
                projectPathsById[id] = projectPath;
            }

            AddDistinctPath(knownProjects, projectPath);
            if (
                ReadString(property.Value, "name") is { Length: > 0 } name)
            {
                workspaceRootLabels[projectPath] = name;
            }
        }
    }

    private static void AddProjectReferenceArray(
        JsonElement parent,
        string propertyName,
        List<string> target,
        IReadOnlyDictionary<string, string> projectPathsById)
    {
        if (
            !parent.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in value.EnumerateArray())
        {
            var projectPath = ResolveProjectReference(
                element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : null,
                projectPathsById);
            if (
                projectPath is not null &&
                !target.Contains(projectPath, PathComparer))
            {
                target.Add(projectPath);
            }
        }
    }

    private static string? ResolveProjectReference(
        string? reference,
        IReadOnlyDictionary<string, string> projectPathsById)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (projectPathsById.TryGetValue(reference, out var projectPath))
        {
            return projectPath;
        }

        // Current Codex uses opaque `local-*` IDs in ordering and pinning
        // arrays. Never surface an unresolved ID as a project name/path.
        return reference.StartsWith(
            "local-",
            StringComparison.OrdinalIgnoreCase)
            ? null
            : NormalizePath(reference);
    }

    private static void AddStringArray(
        JsonElement parent,
        string propertyName,
        List<string> target,
        bool normalizeAsPath)
    {
        if (
            !parent.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in value.EnumerateArray())
        {
            if (element.GetString() is not { Length: > 0 } text)
            {
                continue;
            }

            text = normalizeAsPath ? NormalizePath(text) : text;
            if (
                !target.Contains(
                    text,
                    normalizeAsPath
                        ? PathComparer
                        : StringComparer.OrdinalIgnoreCase))
            {
                target.Add(text);
            }
        }
    }

    private static string? ReadString(
        JsonElement parent,
        string propertyName)
    {
        if (
            parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static string? ResolveProjectPath(
        string? workingDirectory,
        GlobalState state)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return null;
        }

        var normalized = NormalizePath(workingDirectory);
        return state.KnownProjectPaths
            .Where(path => IsSameOrChildPath(normalized, path))
            .OrderByDescending(path => path.Length)
            .FirstOrDefault();
    }

    private static bool IsSameOrChildPath(string path, string candidateRoot)
    {
        var root = candidateRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return
            PathComparer.Equals(path, root) ||
            path.StartsWith(
                $"{root}{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(
                $"{root}{Path.AltDirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUserFacingSource(string source)
    {
        return
            !source.Equals("subagent", StringComparison.OrdinalIgnoreCase) &&
            !source.Equals("automation", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ReadDatabaseTimestamp(
        SqliteDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetValue(ordinal);
        if (value is long integer)
        {
            try
            {
                return integer > 10_000_000_000
                    ? DateTimeOffset.FromUnixTimeMilliseconds(integer)
                    : DateTimeOffset.FromUnixTimeSeconds(integer);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        if (
            value is string text &&
            DateTimeOffset.TryParse(text, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryExtractThreadId(string path, out string id)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Length >= 36)
        {
            var candidate = name[^36..];
            if (Guid.TryParse(candidate, out _))
            {
                id = candidate;
                return true;
            }
        }

        id = string.Empty;
        return false;
    }

    private static void AddDistinctPath(List<string> target, string path)
    {
        path = NormalizePath(path);
        if (!target.Contains(path, PathComparer))
        {
            target.Add(path);
        }
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return $@"\\{path[8..]}";
        }

        return path.StartsWith(@"\\?\", StringComparison.Ordinal)
            ? path[4..]
            : path;
    }

    private static string GetProjectName(string path)
    {
        var normalized = NormalizePath(path).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(name) ? normalized : name;
    }

    private static string NormalizeTitle(string title)
    {
        var firstLine = NormalizeNativeTitle(title);
        if (
            string.IsNullOrWhiteSpace(firstLine) ||
            firstLine.Equals(
                "New task",
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return firstLine.Length > 70 ? $"{firstLine[..67]}…" : firstLine;
    }

    private static void AddUnreadThreadIds(
        JsonElement container,
        List<string> target)
    {
        if (
            !container.TryGetProperty(
                "unread-thread-ids-by-host-v1",
                out var byHost) ||
            byHost.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var host in byHost.EnumerateObject())
        {
            if (host.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var value in host.Value.EnumerateArray())
            {
                if (
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString() is { Length: > 0 } id &&
                    !target.Contains(
                        id,
                        StringComparer.OrdinalIgnoreCase))
                {
                    target.Add(id);
                }
            }
        }
    }

    private static string NormalizeNativeTitle(string title)
    {
        var firstLine = title
            .Replace("\r", string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return string.Empty;
        }

        return firstLine;
    }

    private string DisplayTitle(CodexThread thread)
    {
        return string.IsNullOrWhiteSpace(thread.Title)
            ? _localization.Strings.SidebarUntitledTask
            : thread.Title;
    }

    private string RelativeTime(DateTimeOffset updatedAt)
    {
        var elapsed =
            _utcNowProvider().ToUniversalTime() -
            updatedAt.ToUniversalTime();
        if (elapsed.TotalMinutes < 1)
        {
            return _localization.Strings.SidebarJustNow;
        }

        if (elapsed.TotalHours < 1)
        {
            return _localization.Strings.SidebarMinutesAgo(
                Math.Max(1, (int)elapsed.TotalMinutes));
        }

        if (elapsed.TotalDays < 1)
        {
            return _localization.Strings.SidebarHoursAgo(
                Math.Max(1, (int)elapsed.TotalHours));
        }

        if (elapsed.TotalDays < 7)
        {
            return _localization.Strings.SidebarDaysAgo(
                Math.Max(1, (int)elapsed.TotalDays));
        }

        return updatedAt.ToLocalTime().ToString(
            "d",
            _localization.Culture);
    }

    private sealed record SessionIndexItem(
        string Id,
        string Title,
        DateTimeOffset UpdatedAt);

    private sealed record ThreadMetadata(
        bool IsArchived,
        bool HasPreview,
        bool IsUserFacing,
        string? WorkingDirectory,
        string? RolloutPath,
        bool RolloutExists,
        DateTimeOffset? UpdatedAt,
        DateTimeOffset? RecencyAt)
    {
        public bool IsDeepLinkCandidate =>
            !IsArchived &&
            HasPreview &&
            IsUserFacing &&
            RolloutExists;
    }

    private sealed class GlobalState
    {
        public IReadOnlyList<string> PinnedThreadIds { get; init; } = [];
        public IReadOnlyList<string> PinnedProjectIds { get; init; } = [];
        public IReadOnlyList<string> KnownProjectPaths { get; init; } = [];
        public IReadOnlyList<string> ProjectOrder { get; init; } = [];
        public IReadOnlyList<string> ProjectlessThreadIds { get; init; } = [];
        public IReadOnlyList<string> UnreadThreadIds { get; init; } = [];
        public Dictionary<string, string> WorkspaceRootLabels { get; init; } =
            new(PathComparer);
        public Dictionary<string, IReadOnlyList<string>> ProjectThreadOrders
        {
            get;
            init;
        } = new(PathComparer);

        public Dictionary<string, string> ThreadProjectAssignments { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }
}
