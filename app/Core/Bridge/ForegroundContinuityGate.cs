namespace CodexController.Core.Bridge;

/// <summary>
/// Debounces transient foreground detection failures and limits the
/// foreground-wait toast to one presentation per away episode.
/// </summary>
public sealed class ForegroundContinuityGate
{
    private long? _foregroundLostAt;
    private long? _foregroundRestoredAt;
    private bool _waitNoticePresented;

    public bool AllowsInput(
        bool isForeground,
        long timestampMilliseconds,
        int graceMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(graceMilliseconds);

        if (isForeground)
        {
            _foregroundLostAt = null;
            if (!_waitNoticePresented)
            {
                _foregroundRestoredAt = null;
                return true;
            }

            _foregroundRestoredAt ??= timestampMilliseconds;
            if (
                timestampMilliseconds - _foregroundRestoredAt.Value >=
                graceMilliseconds)
            {
                _foregroundRestoredAt = null;
                _waitNoticePresented = false;
            }

            return true;
        }

        _foregroundRestoredAt = null;
        _foregroundLostAt ??= timestampMilliseconds;
        return timestampMilliseconds - _foregroundLostAt.Value <
               graceMilliseconds;
    }

    public bool TryPresentWaitNotice()
    {
        if (_waitNoticePresented)
        {
            return false;
        }

        _waitNoticePresented = true;
        return true;
    }

    public void Reset()
    {
        _foregroundLostAt = null;
        _foregroundRestoredAt = null;
        _waitNoticePresented = false;
    }
}
