using System.IO;
using System.Text.Json;
using CodexController.Models;
using CodexController.Services;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexMonitoredTask(
    string Id,
    string Title,
    ThreadStatus Status);

internal sealed record CodexTaskMonitorSnapshot(
    CodexAgentRosterSnapshot AgentRoster,
    IReadOnlyList<CodexMonitoredTask> Tasks);

internal sealed class CodexTaskMonitorService
{
    internal const int Capacity = 16;
    private readonly string _codexRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private readonly Dictionary<string, CodexRolloutStatusReader> _readers =
        new(StringComparer.Ordinal);

    private readonly CodexRecentThreadsService _recentThreads = new();
    private readonly CodexUnreadStateReader _unreadState = new();

    internal async Task<CodexTaskMonitorSnapshot?> ReadAsync(
        CancellationToken cancellationToken)
    {
        // App Server owns the source filters and the same recency order used
        // by the six native keys, including exclusion of internal review tasks.
        var threadsRead = _recentThreads.ReadAsync(cancellationToken, Capacity);
        var unreadRead = _unreadState.ReadAsync(cancellationToken);
        await Task.WhenAll(threadsRead, unreadRead).ConfigureAwait(false);
        var threads = await threadsRead.ConfigureAwait(false);
        if (threads is null)
        {
            return null;
        }

        var unread = await unreadRead.ConfigureAwait(false);
        if (unread is null)
        {
            return null;
        }

        return await Task.Run(() =>
        {
            var tasks = ReadStatuses(threads, unread.ThreadIds, cancellationToken);
            if (tasks is null)
            {
                return null;
            }

            // Both pages must assign identities and status from the same query.
            // Read the source setting last so an in-flight query cannot restore
            // recent slots after the user switches to another Agent source.
            var roster = CodexAgentRosterObserver.FromRecentThreads(
                threads,
                ReadSharedText(".codex-global-state.json"),
                ReadSharedText("config.toml"));
            return new CodexTaskMonitorSnapshot(roster, tasks);
        }, cancellationToken).ConfigureAwait(false);
    }

    private string? ReadSharedText(string name)
    {
        var path = Path.Combine(_codexRoot, name);
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private IReadOnlyList<CodexMonitoredTask>? ReadStatuses(
        IReadOnlyList<CodexRecentThread> threads,
        IReadOnlySet<string> unread,
        CancellationToken cancellationToken)
    {
        try
        {
            var tasks = new List<CodexMonitoredTask>(Capacity);
            var retained = new HashSet<string>(StringComparer.Ordinal);
            foreach (var thread in threads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Guid.TryParse(thread.ThreadId, out _) ||
                    thread.RolloutPath?.Contains("trash", StringComparison.OrdinalIgnoreCase) == true ||
                    !retained.Add(thread.ThreadId))
                {
                    continue;
                }

                var path = NormalizeRolloutPath(thread.RolloutPath);
                var status = ThreadStatus.Unknown;
                if (path is not null && File.Exists(path))
                {
                    if (!_readers.TryGetValue(thread.ThreadId, out var reader))
                    {
                        reader = new CodexRolloutStatusReader();
                        _readers[thread.ThreadId] = reader;
                    }
                    status = reader.Read(path);
                }

                if (status is not ThreadStatus.Thinking and not ThreadStatus.Error &&
                    unread.Contains(thread.ThreadId))
                {
                    status = ThreadStatus.CompleteUnread;
                }

                // A missing rollout affects state only; do not replace a recent
                // task with an older task because of its path or availability.
                tasks.Add(new(thread.ThreadId, thread.Title, status));
            }

            foreach (var id in _readers.Keys.Where(id => !retained.Contains(id)).ToArray())
            {
                _readers.Remove(id);
            }

            return tasks;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private string? NormalizeRolloutPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsAllowedPath(path))
        {
            return null;
        }

        try
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                path = @"\\" + path[8..];
            }
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                path = path[4..];
            }

            if (!Path.IsPathFullyQualified(path))
            {
                return null;
            }

            var normalized = Path.GetFullPath(path);
            var sessionsRoot = Path.GetFullPath(Path.Combine(_codexRoot, "sessions")) +
                Path.DirectorySeparatorChar;
            return normalized.StartsWith(sessionsRoot, StringComparison.OrdinalIgnoreCase)
                ? normalized : null;
        }
        catch (Exception exception) when (exception is
            ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static bool IsAllowedPath(string path) =>
        !path.Contains("trash", StringComparison.OrdinalIgnoreCase);
}
