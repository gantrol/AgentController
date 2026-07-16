namespace CodexController.Core.Bridge;

public sealed class BridgeEventHub : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<long, Action<BridgeEvent>> _subscribers = [];
    private readonly TimeProvider _timeProvider;
    private long _nextSubscriptionId;
    private bool _disposed;

    public BridgeEventHub(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IDisposable Subscribe(Action<BridgeEvent> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);

        long id;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            id = ++_nextSubscriptionId;
            _subscribers.Add(id, subscriber);
        }

        return new Subscription(this, id);
    }

    public BridgeEvent Publish(
        BridgeEventKey key,
        BridgeEventSeverity severity = BridgeEventSeverity.Info,
        IReadOnlyDictionary<string, string>? parameters = null,
        BridgeOverlayMetadata? overlay = null)
    {
        var bridgeEvent = new BridgeEvent(
            key,
            _timeProvider.GetUtcNow(),
            severity,
            parameters,
            overlay);
        Publish(bridgeEvent);
        return bridgeEvent;
    }

    public void Publish(BridgeEvent bridgeEvent)
    {
        ArgumentNullException.ThrowIfNull(bridgeEvent);

        Action<BridgeEvent>[] subscribers;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            subscribers = [.. _subscribers.Values];
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber(bridgeEvent);
            }
            catch
            {
                // A diagnostic or presentation subscriber must never break
                // controller input or automation orchestration.
            }
        }
    }

    public int SubscriberCount
    {
        get
        {
            lock (_gate)
            {
                return _subscribers.Count;
            }
        }
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
            _subscribers.Clear();
        }
    }

    private void Unsubscribe(long id)
    {
        lock (_gate)
        {
            _subscribers.Remove(id);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private BridgeEventHub? _hub;
        private readonly long _id;

        public Subscription(BridgeEventHub hub, long id)
        {
            _hub = hub;
            _id = id;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _hub, null)?.Unsubscribe(_id);
        }
    }
}
