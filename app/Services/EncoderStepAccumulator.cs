namespace CodexController.Services;

internal readonly record struct EncoderStepIntent(
    int Direction,
    long InputTimestamp);

/// <summary>
/// Keeps a small net encoder intent so a busy Codex renderer never receives
/// a delayed replay of every historical stick repeat. This mirrors the
/// proven virtual-micro dial input policy and is owned by the UI thread.
/// </summary>
internal sealed class EncoderStepAccumulator
{
    private readonly int _maximumPending;
    private int _pending;
    private long _latestInputTimestamp;

    public EncoderStepAccumulator(int maximumPending = 3)
    {
        if (maximumPending <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPending));
        }

        _maximumPending = maximumPending;
    }

    public int Pending => _pending;

    public void Add(int steps, long inputTimestamp)
    {
        _pending = Math.Clamp(
            _pending + steps,
            -_maximumPending,
            _maximumPending);
        _latestInputTimestamp = _pending == 0 ? 0 : inputTimestamp;
    }

    public EncoderStepIntent? TakeNext(
        long currentTimestamp,
        long maximumAgeTicks)
    {
        if (maximumAgeTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAgeTicks));
        }

        if (_pending == 0)
        {
            return null;
        }

        if (
            currentTimestamp >= _latestInputTimestamp &&
            currentTimestamp - _latestInputTimestamp > maximumAgeTicks)
        {
            Clear();
            return null;
        }

        var next = Math.Sign(_pending);
        _pending -= next;
        var intent = new EncoderStepIntent(next, _latestInputTimestamp);
        if (_pending == 0)
        {
            _latestInputTimestamp = 0;
        }

        return intent;
    }

    public void Clear()
    {
        _pending = 0;
        _latestInputTimestamp = 0;
    }
}
