namespace CodexMicro.Desktop.Services;

/// <summary>
/// Keeps the optimistic unread projection attached to Codex threads while the
/// official desktop broadcast is being persisted and reflected in lighting.
/// This is never the source of truth: the roster can reorder at any time, so a
/// physical slot id is not a stable conversation identity.
/// </summary>
internal sealed class ManualUnreadThreadTracker
{
    private readonly HashSet<string> _threadIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _confirmedThreadIds = new(
        StringComparer.Ordinal);

    internal bool TryMarkUnread(string? threadId, bool isKeyLit)
    {
        var normalizedThreadId = Normalize(threadId);
        return !isKeyLit &&
            normalizedThreadId is not null &&
            _threadIds.Add(normalizedThreadId);
    }

    internal bool IsUnread(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        return normalizedThreadId is not null &&
            _threadIds.Contains(normalizedThreadId);
    }

    internal bool Confirm(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        return normalizedThreadId is not null &&
            _threadIds.Contains(normalizedThreadId) &&
            _confirmedThreadIds.Add(normalizedThreadId);
    }

    internal bool ClearConfirmed(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (normalizedThreadId is null ||
            !_confirmedThreadIds.Remove(normalizedThreadId))
        {
            return false;
        }

        _threadIds.Remove(normalizedThreadId);
        return true;
    }

    internal bool Clear(string? threadId)
    {
        var normalizedThreadId = Normalize(threadId);
        if (normalizedThreadId is null)
        {
            return false;
        }

        _confirmedThreadIds.Remove(normalizedThreadId);
        return _threadIds.Remove(normalizedThreadId);
    }

    private static string? Normalize(string? threadId) =>
        string.IsNullOrWhiteSpace(threadId) ? null : threadId.Trim();
}
