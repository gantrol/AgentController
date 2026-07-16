using CodexController.Core.Bridge;

namespace CodexController.Tests;

public sealed class BridgeEventHubTests
{
    [Fact]
    public void PublishUsesInjectedClockAndCopiesParameters()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            16,
            12,
            30,
            0,
            TimeSpan.Zero);
        using var hub = new BridgeEventHub(new FixedTimeProvider(now));
        var parameters = new Dictionary<string, string>
        {
            ["scope"] = "projects",
        };
        BridgeEvent? received = null;
        using var subscription = hub.Subscribe(item => received = item);

        var published = hub.Publish(
            "navigation.scope.changed",
            parameters: parameters);
        parameters["scope"] = "pinned";

        Assert.Same(published, received);
        Assert.Equal(now, published.Timestamp);
        Assert.Equal(
            "projects",
            published.Parameters["scope"]);
    }

    [Fact]
    public void DisposedSubscriptionStopsReceivingEvents()
    {
        using var hub = new BridgeEventHub();
        var count = 0;
        var subscription = hub.Subscribe(_ => count++);

        hub.Publish("app.ready");
        subscription.Dispose();
        subscription.Dispose();
        hub.Publish("app.ready");

        Assert.Equal(1, count);
    }

    [Fact]
    public void SubscriberFailureDoesNotBlockOtherSubscribers()
    {
        using var hub = new BridgeEventHub();
        var received = false;
        using var failing = hub.Subscribe(
            _ => throw new InvalidOperationException("test"));
        using var healthy = hub.Subscribe(_ => received = true);

        hub.Publish("controller.connection.restored");

        Assert.True(received);
    }

    [Fact]
    public void EventCarriesStablePresentationMetadata()
    {
        using var hub = new BridgeEventHub(
            new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        var bridgeEvent = hub.Publish(
            "codex.wake.failed",
            BridgeEventSeverity.Error,
            overlay: new BridgeOverlayMetadata(
                BridgeOverlayTarget.FooterAndToast,
                TimeSpan.FromSeconds(2),
                "codex.wake"));

        Assert.Equal("codex.wake.failed", bridgeEvent.Key.Value);
        Assert.Equal(BridgeEventSeverity.Error, bridgeEvent.Severity);
        Assert.Equal("codex.wake", bridgeEvent.Overlay?.CoalesceKey);
    }

    [Fact]
    public void EventKeyRejectsBlankValues()
    {
        Assert.Throws<ArgumentException>(
            () => new BridgeEventKey(" "));
    }

    [Fact]
    public void EventRejectsAnUninitializedKey()
    {
        Assert.Throws<ArgumentException>(() =>
            new BridgeEvent(
                default,
                DateTimeOffset.UnixEpoch,
                BridgeEventSeverity.Info));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
