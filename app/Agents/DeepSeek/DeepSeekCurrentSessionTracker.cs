namespace CodexController.Agents.DeepSeek;

/// <summary>
/// Distinguishes a real Harness navigation from an unchanged authoritative
/// session so periodic data refreshes do not reset the controller-owned
/// browsing cursor on every poll.
/// </summary>
internal sealed class DeepSeekCurrentSessionTracker
{
    private bool _hasObservation;
    private string? _currentSessionId;
    private string? _pendingInitialSessionId;

    internal bool Observe(
        string? currentSessionId,
        bool currentSessionMaterialized)
    {
        var normalized = string.IsNullOrWhiteSpace(currentSessionId)
            ? null
            : currentSessionId.Trim();
        if (normalized is null)
        {
            // The no-session landing view has no sidebar row to select. Treat
            // it as observed while deliberately preserving the local cursor.
            _hasObservation = true;
            _currentSessionId = null;
            _pendingInitialSessionId = null;
            return false;
        }

        if (!_hasObservation)
        {
            if (!currentSessionMaterialized)
            {
                _pendingInitialSessionId = normalized;
                return false;
            }

            var requiresInitialReanchor = _pendingInitialSessionId is not null;
            _hasObservation = true;
            _currentSessionId = normalized;
            _pendingInitialSessionId = null;
            return requiresInitialReanchor;
        }

        var changed = !string.Equals(
            _currentSessionId,
            normalized,
            StringComparison.Ordinal);
        // Do not acknowledge a changed authoritative id until the matching
        // row reaches the snapshot. Returning true without committing makes
        // every later poll retry the re-anchor instead of swallowing the id.
        if (changed && currentSessionMaterialized)
        {
            _currentSessionId = normalized;
        }

        return changed;
    }

    internal void Reset()
    {
        _hasObservation = false;
        _currentSessionId = null;
        _pendingInitialSessionId = null;
    }
}
