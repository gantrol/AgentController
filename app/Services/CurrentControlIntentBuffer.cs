namespace CodexController.Services;

internal readonly record struct CurrentControlIntent(
    ComposerDialNavigation Navigation,
    int Generation,
    long InputTimestamp);

/// <summary>
/// Keeps only the newest horizontal control intent and binds it to the
/// current dial epoch. Old input must never be replayed into a later popup.
/// </summary>
internal sealed class CurrentControlIntentBuffer
{
    private readonly object _gate = new();
    private CurrentControlIntent? _pending;

    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    public ComposerDialNavigation? PendingNavigation
    {
        get
        {
            lock (_gate)
            {
                return _pending?.Navigation;
            }
        }
    }

    public void Offer(
        ComposerDialNavigation navigation,
        int generation,
        long inputTimestamp)
    {
        if (navigation is not (
                ComposerDialNavigation.Left or
                ComposerDialNavigation.Right))
        {
            throw new ArgumentOutOfRangeException(nameof(navigation));
        }

        lock (_gate)
        {
            _pending = new(
                navigation,
                generation,
                inputTimestamp);
        }
    }

    public CurrentControlIntent? Take(
        int currentGeneration,
        long currentTimestamp,
        long maximumAgeTicks)
    {
        if (maximumAgeTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAgeTicks));
        }

        lock (_gate)
        {
            var intent = _pending;
            _pending = null;
            if (
                intent is null ||
                intent.Value.Generation != currentGeneration)
            {
                return null;
            }

            if (
                currentTimestamp >= intent.Value.InputTimestamp &&
                currentTimestamp - intent.Value.InputTimestamp >
                    maximumAgeTicks)
            {
                return null;
            }

            return intent;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending = null;
        }
    }
}

/// <summary>
/// Coalesces refresh requests without cancelling work already reading the UI.
/// A request made while the worker is active schedules exactly one more pass.
/// </summary>
internal sealed class CoalescingRequestGate
{
    private int _requested;
    private int _running;

    public bool Request()
    {
        Interlocked.Exchange(ref _requested, 1);
        return Interlocked.CompareExchange(
            ref _running,
            1,
            0) == 0;
    }

    public bool TryConsume() =>
        Interlocked.Exchange(ref _requested, 0) != 0;

    public bool Complete()
    {
        Volatile.Write(ref _running, 0);
        return
            Volatile.Read(ref _requested) != 0 &&
            Interlocked.CompareExchange(
                ref _running,
                1,
                0) == 0;
    }

    public void ClearPending() =>
        Interlocked.Exchange(ref _requested, 0);
}
