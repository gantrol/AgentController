using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed class CodexUnreadStateReader
{
    private const string UnreadStateKey = "unread-thread-ids-by-host-v1";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly string _globalStatePath;

    public CodexUnreadStateReader(string? globalStatePath = null)
    {
        var codexRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        _globalStatePath = globalStatePath ??
            Path.Combine(codexRoot, ".codex-global-state.json");
    }

    internal static bool ContainsUnreadThread(
        string? globalStateJson,
        string threadId,
        string hostId = "local")
    {
        if (string.IsNullOrWhiteSpace(globalStateJson) ||
            string.IsNullOrWhiteSpace(threadId) ||
            string.IsNullOrWhiteSpace(hostId))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(globalStateJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var normalizedThreadId = threadId.Trim();
            var normalizedHostId = hostId.Trim();
            if (ContainsUnreadThread(
                    root,
                    normalizedThreadId,
                    normalizedHostId))
            {
                return true;
            }

            return root.TryGetProperty(
                    "electron-persisted-atom-state",
                    out var persistedAtoms) &&
                persistedAtoms.ValueKind == JsonValueKind.Object &&
                ContainsUnreadThread(
                    persistedAtoms,
                    normalizedThreadId,
                    normalizedHostId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal async Task<bool> WaitUntilUnreadAsync(
        string threadId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var normalizedThreadId = threadId.Trim();
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await ReadSharedTextAsync(cancellationToken);
                if (ContainsUnreadThread(json, normalizedThreadId))
                {
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(
                remaining < PollInterval ? remaining : PollInterval,
                cancellationToken);
        }
    }

    private static bool ContainsUnreadThread(
        JsonElement container,
        string threadId,
        string hostId)
    {
        if (!container.TryGetProperty(UnreadStateKey, out var byHost) ||
            byHost.ValueKind != JsonValueKind.Object ||
            !byHost.TryGetProperty(hostId, out var unreadThreads) ||
            unreadThreads.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in unreadThreads.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.String &&
                string.Equals(
                    candidate.GetString(),
                    threadId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string?> ReadSharedTextAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_globalStatePath))
        {
            return null;
        }

        await using var stream = new FileStream(
            _globalStatePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
