using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexAgentRosterEntry(
    int SlotId,
    string ThreadId,
    string? ProjectName,
    string Title,
    DateTimeOffset UpdatedAt)
{
    public string DisplayTitle => string.IsNullOrWhiteSpace(ProjectName)
        ? Title
        : $"{ProjectName} › {Title}";
}

internal sealed record CodexAgentRosterSnapshot(
    IReadOnlyList<CodexAgentRosterEntry> Entries,
    string Source)
{
    public CodexAgentRosterEntry? GetSlot(int slotId) =>
        Entries.FirstOrDefault(entry => entry.SlotId == slotId);
}

/// <summary>
/// Mirrors the local "recent" Agent source used by Codex Micro. The official
/// VHF status report intentionally contains only slot lighting, so project and
/// title text are read independently from Codex-owned local indexes. This
/// observer never writes those files and never changes the HID protocol.
/// </summary>
internal sealed class CodexAgentRosterObserver : IDisposable
{
    private sealed record SessionEntry(
        string ThreadId,
        string Title,
        DateTimeOffset UpdatedAt);

    private sealed record ProjectAssignment(
        string? ProjectId,
        string? WorkspacePath);

    private readonly string _sessionIndexPath;
    private readonly string _globalStatePath;
    private readonly string _configPath;
    private readonly object _gate = new();
    private readonly object _reloadGate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private bool _started;
    private bool _disposed;

    public CodexAgentRosterObserver(
        string? sessionIndexPath = null,
        string? globalStatePath = null,
        string? configPath = null)
    {
        var codexRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        _sessionIndexPath = sessionIndexPath ??
            Path.Combine(codexRoot, "session_index.jsonl");
        _globalStatePath = globalStatePath ??
            Path.Combine(codexRoot, ".codex-global-state.json");
        _configPath = configPath ?? Path.Combine(codexRoot, "config.toml");
        Current = new CodexAgentRosterSnapshot([], "Codex local index");
    }

    public event EventHandler<CodexAgentRosterSnapshot>? RosterChanged;

    public CodexAgentRosterSnapshot Current { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            var directory = Path.GetDirectoryName(_sessionIndexPath);
            if (!string.IsNullOrWhiteSpace(directory) &&
                Directory.Exists(directory) &&
                PathsShareDirectory(
                    _sessionIndexPath,
                    _globalStatePath,
                    _configPath))
            {
                _watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += IndexChanged;
                _watcher.Created += IndexChanged;
                _watcher.Deleted += IndexChanged;
                _watcher.Renamed += IndexRenamed;
            }
        }

        Reload();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _reloadTimer?.Dispose();
            _reloadTimer = null;
        }
    }

    internal static CodexAgentRosterSnapshot Parse(
        string sessionIndexJsonLines,
        string? globalStateJson,
        string? configToml = null,
        string source = "Codex local index")
    {
        var latestByThread = new Dictionary<string, SessionEntry>(
            StringComparer.Ordinal);
        foreach (var rawLine in sessionIndexJsonLines.Split(['\r', '\n']))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !TryReadString(root, "id", out var threadId) ||
                    !TryReadString(root, "thread_name", out var title) ||
                    string.IsNullOrWhiteSpace(threadId) ||
                    string.IsNullOrWhiteSpace(title) ||
                    !TryReadTimestamp(root, "updated_at", out var updatedAt))
                {
                    continue;
                }

                var candidate = new SessionEntry(
                    threadId.Trim(),
                    title.Trim(),
                    updatedAt);
                if (!latestByThread.TryGetValue(candidate.ThreadId, out var current) ||
                    candidate.UpdatedAt >= current.UpdatedAt)
                {
                    latestByThread[candidate.ThreadId] = candidate;
                }
            }
            catch (JsonException)
            {
                // An atomically replaced or partially appended final line is
                // ignored; the next watcher notification reparses the index.
            }
        }

        var projectNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var assignments = new Dictionary<string, ProjectAssignment>(
            StringComparer.Ordinal);
        var workspaceHints = new Dictionary<string, string>(StringComparer.Ordinal);
        var agentSource = "recent";
        if (!string.IsNullOrWhiteSpace(globalStateJson))
        {
            try
            {
                using var document = JsonDocument.Parse(globalStateJson);
                var root = document.RootElement;
                if (TryReadAgentSource(root, out var configuredAgentSource))
                {
                    agentSource = configuredAgentSource;
                }

                ReadProjectNames(root, projectNames);
                ReadAssignments(root, assignments);
                ReadWorkspaceHints(root, workspaceHints);
            }
            catch (JsonException)
            {
                // Titles still remain useful when the global-state file is in
                // the middle of an atomic replacement.
            }
        }

        if (TryReadAgentSourceFromConfig(configToml, out var configAgentSource))
        {
            agentSource = configAgentSource;
        }

        if (!string.Equals(agentSource, "recent", StringComparison.Ordinal))
        {
            // Local disk cannot faithfully reconstruct renderer-only priority
            // state. Showing no title is safer than assigning a real title to
            // the wrong physical slot.
            return new CodexAgentRosterSnapshot(
                [],
                $"Codex {agentSource} roster is not locally provable");
        }

        var entries = latestByThread.Values
            .OrderByDescending(entry => entry.UpdatedAt)
            .ThenBy(entry => entry.ThreadId, StringComparer.Ordinal)
            .Take(6)
            .Select((entry, slotId) =>
            {
                var assignment = FindThreadValue(assignments, entry.ThreadId);
                var hint = FindThreadValue(workspaceHints, entry.ThreadId);
                string? projectName = null;
                if (assignment?.ProjectId is { Length: > 0 } projectId &&
                    projectNames.TryGetValue(projectId, out var configuredName))
                {
                    projectName = configuredName;
                }

                projectName ??= GetWorkspaceName(
                    assignment?.WorkspacePath ?? hint);
                return new CodexAgentRosterEntry(
                    slotId,
                    entry.ThreadId,
                    projectName,
                    entry.Title,
                    entry.UpdatedAt);
            })
            .ToArray();

        return new CodexAgentRosterSnapshot(entries, source);
    }

    private void IndexChanged(object sender, FileSystemEventArgs e)
    {
        if (IsObservedPath(e.FullPath))
        {
            ScheduleReload();
        }
    }

    private void IndexRenamed(object sender, RenamedEventArgs e)
    {
        if (IsObservedPath(e.FullPath) || IsObservedPath(e.OldFullPath))
        {
            ScheduleReload();
        }
    }

    private bool IsObservedPath(string path) =>
        string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(_sessionIndexPath),
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(_globalStatePath),
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(_configPath),
            StringComparison.OrdinalIgnoreCase);

    private void ScheduleReload()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _reloadTimer ??= new Timer(
                _ => Reload(),
                null,
                Timeout.Infinite,
                Timeout.Infinite);
            _reloadTimer.Change(160, Timeout.Infinite);
        }
    }

    private void Reload()
    {
        CodexAgentRosterSnapshot? changed = null;
        lock (_reloadGate)
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }
            }

            try
            {
                var sessionIndex =
                    ReadSharedText(_sessionIndexPath) ?? string.Empty;
                var globalState = ReadSharedText(_globalStatePath);
                var config = ReadSharedText(_configPath);
                var next = Parse(
                    sessionIndex,
                    globalState,
                    config,
                    _sessionIndexPath);
                lock (_gate)
                {
                    if (_disposed || Equivalent(Current, next))
                    {
                        return;
                    }

                    Current = next;
                    changed = next;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                ScheduleReload();
            }
        }

        if (changed is not null)
        {
            RosterChanged?.Invoke(this, changed);
        }
    }

    private static string? ReadSharedText(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(
            stream,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static void ReadProjectNames(
        JsonElement root,
        IDictionary<string, string> result)
    {
        if (!TryGetObject(root, "local-projects", out var projects))
        {
            return;
        }

        foreach (var project in projects.EnumerateObject())
        {
            if (project.Value.ValueKind == JsonValueKind.Object &&
                TryReadString(project.Value, "name", out var name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                result[project.Name] = name.Trim();
            }
        }
    }

    private static void ReadAssignments(
        JsonElement root,
        IDictionary<string, ProjectAssignment> result)
    {
        if (!TryGetObject(root, "thread-project-assignments", out var values))
        {
            return;
        }

        foreach (var assignment in values.EnumerateObject())
        {
            if (assignment.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            _ = TryReadString(assignment.Value, "projectId", out var projectId);
            var path = TryReadString(assignment.Value, "cwd", out var cwd)
                ? cwd
                : TryReadString(assignment.Value, "path", out var workspacePath)
                    ? workspacePath
                    : null;
            result[assignment.Name] = new ProjectAssignment(
                string.IsNullOrWhiteSpace(projectId) ? null : projectId,
                string.IsNullOrWhiteSpace(path) ? null : path);
        }
    }

    private static void ReadWorkspaceHints(
        JsonElement root,
        IDictionary<string, string> result)
    {
        if (!TryGetObject(root, "thread-workspace-root-hints", out var values))
        {
            return;
        }

        foreach (var hint in values.EnumerateObject())
        {
            if (hint.Value.ValueKind == JsonValueKind.String &&
                hint.Value.GetString() is { Length: > 0 } path)
            {
                result[hint.Name] = path;
            }
        }
    }

    private static bool TryReadAgentSource(
        JsonElement root,
        out string source)
    {
        source = string.Empty;
        if (TryReadString(root, "codex-micro-agent-source", out source))
        {
            return IsAgentSource(source);
        }

        if (TryGetObject(root, "electron-persisted-atom-state", out var atoms) &&
            TryReadString(atoms, "codex-micro-agent-source", out source))
        {
            return IsAgentSource(source);
        }

        source = string.Empty;
        return false;
    }

    private static bool TryReadAgentSourceFromConfig(
        string? configToml,
        out string source)
    {
        source = string.Empty;
        if (string.IsNullOrWhiteSpace(configToml))
        {
            return false;
        }

        var match = Regex.Match(
            configToml,
            "(?m)^\\s*(?:\\\"codex-micro-agent-source\\\"|codex-micro-agent-source)\\s*=\\s*\\\"(?<source>recent|pinned|priority|custom)\\\"\\s*(?:#.*)?$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        source = match.Groups["source"].Value;
        return true;
    }

    private static bool IsAgentSource(string value) =>
        value is "recent" or "pinned" or "priority" or "custom";

    private static TValue? FindThreadValue<TValue>(
        IReadOnlyDictionary<string, TValue> values,
        string threadId)
        where TValue : class
    {
        if (values.TryGetValue(threadId, out var direct))
        {
            return direct;
        }

        if (values.TryGetValue($"local:{threadId}", out var local))
        {
            return local;
        }

        return null;
    }

    private static bool TryGetObject(
        JsonElement root,
        string name,
        out JsonElement value)
    {
        value = default;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(name, out value) &&
            value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryReadString(
        JsonElement root,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            property.GetString() is not { } text)
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryReadTimestamp(
        JsonElement root,
        string name,
        out DateTimeOffset value)
    {
        value = default;
        if (!root.TryGetProperty(name, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.TryParse(
                property.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out value);
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out var unixSeconds))
        {
            try
            {
                value = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        return false;
    }

    private static string? GetWorkspaceName(string? workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return null;
        }

        var trimmed = workspacePath.Trim().TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0)
        {
            return null;
        }

        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static bool PathsShareDirectory(params string[] paths) =>
        paths.Length > 0 && paths
            .Select(path => Path.GetDirectoryName(Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() == 1;

    private static bool Equivalent(
        CodexAgentRosterSnapshot left,
        CodexAgentRosterSnapshot right) =>
        left.Entries.SequenceEqual(right.Entries);
}
