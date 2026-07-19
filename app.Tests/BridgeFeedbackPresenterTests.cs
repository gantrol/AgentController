using System.Collections.Concurrent;
using CodexController.Core.Bridge;
using CodexController.Presentation.Feedback;

namespace CodexController.Tests;

public sealed class BridgeFeedbackPresenterTests
{
    [Fact]
    public void KeepsOnlyFourNewestBindableLogRows()
    {
        using var hub = new BridgeEventHub();
        var overlay = new RecordingOverlayPresenter();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            new TestFormatter(),
            overlay);

        for (var index = 1; index <= 5; index++)
        {
            hub.Publish(
                BridgeEventKeys.SidebarFocusChanged,
                parameters: Parameters(("value", index.ToString())));
        }

        Assert.Equal(4, presenter.LogRows.Count);
        Assert.Equal(
            ["5", "4", "3", "2"],
            presenter.LogRows.Select(row => row.Text));
        Assert.All(
            presenter.LogRows,
            row => Assert.Equal(
                BridgeEventKeys.SidebarFocusChanged,
                row.Key));
        Assert.Empty(overlay.Requests);
    }

    [Fact]
    public void FooterAndToastEventUpdatesBothSurfacesWithSchedulingHints()
    {
        using var hub = new BridgeEventHub();
        var overlay = new RecordingOverlayPresenter();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            new TestFormatter(),
            overlay);
        var duration = TimeSpan.FromSeconds(2);

        var published = hub.Publish(
            BridgeEventKeys.ModelChanged,
            BridgeEventSeverity.Warning,
            Parameters(("value", "gpt-5")),
            new BridgeOverlayMetadata(
                BridgeOverlayTarget.FooterAndToast,
                duration,
                "model-selection"));

        var footer = Assert.IsType<BridgeFooterStatus>(presenter.Footer);
        Assert.Same(published, footer.Source);
        Assert.Equal("footer:gpt-5", footer.Text);
        Assert.Equal(BridgeEventSeverity.Warning, footer.Severity);

        var request = Assert.Single(overlay.Requests);
        Assert.Same(published, request.Source);
        Assert.Equal("title:gpt-5", request.Title);
        Assert.Equal("toast:gpt-5", request.Value);
        Assert.Equal(duration, request.Duration);
        Assert.Equal("model-selection", request.CoalesceKey);
    }

    [Fact]
    public void TargetRoutesFeedbackToOnlyTheRequestedSurface()
    {
        using var hub = new BridgeEventHub();
        var overlay = new RecordingOverlayPresenter();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            new TestFormatter(),
            overlay);

        hub.Publish(
            BridgeEventKeys.ControllerArmed,
            parameters: Parameters(("value", "armed")),
            overlay: new BridgeOverlayMetadata(
                BridgeOverlayTarget.Footer));
        var originalFooter = presenter.Footer;

        hub.Publish(
            BridgeEventKeys.SpeedChanged,
            parameters: Parameters(("value", "fast")),
            overlay: new BridgeOverlayMetadata(
                BridgeOverlayTarget.Toast));

        Assert.Same(originalFooter, presenter.Footer);
        Assert.Equal("footer:armed", presenter.Footer?.Text);
        Assert.Equal("toast:fast", Assert.Single(overlay.Requests).Value);
        Assert.Equal(2, presenter.LogRows.Count);
    }

    [Fact]
    public void RefreshRendersRetainedEventsAgainWithoutReplayingToast()
    {
        using var hub = new BridgeEventHub();
        var formatter = new TestFormatter();
        var overlay = new RecordingOverlayPresenter();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            formatter,
            overlay);

        hub.Publish(
            BridgeEventKeys.ReasoningEffortChanged,
            parameters: Parameters(("value", "high")),
            overlay: new BridgeOverlayMetadata(
                BridgeOverlayTarget.FooterAndToast));
        formatter.Prefix = "en:";

        presenter.Refresh();

        Assert.Equal("en:high", Assert.Single(presenter.LogRows).Text);
        Assert.Equal("en:footer:high", presenter.Footer?.Text);
        Assert.Single(overlay.Requests);
    }

    [Fact]
    public async Task BackgroundPublicationUsesCapturedSynchronizationContext()
    {
        using var hub = new BridgeEventHub();
        var context = new QueuedSynchronizationContext();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            new TestFormatter(),
            new RecordingOverlayPresenter(),
            context);

        await RunOnDedicatedThread(() =>
            hub.Publish(
                BridgeEventKeys.ControllerConnected,
                parameters: Parameters(("value", "connected"))));

        Assert.Empty(presenter.LogRows);

        context.Drain();

        Assert.Equal("connected", Assert.Single(presenter.LogRows).Text);
    }

    [Fact]
    public async Task DisposeUnsubscribesAndQueuedWorkCannotMutateState()
    {
        using var hub = new BridgeEventHub();
        var context = new QueuedSynchronizationContext();
        var presenter = new BridgeFeedbackPresenter(
            hub,
            new TestFormatter(),
            new RecordingOverlayPresenter(),
            context);

        await RunOnDedicatedThread(() =>
            hub.Publish(
                BridgeEventKeys.ControllerPaused,
                parameters: Parameters(("value", "paused"))));
        Assert.Equal(1, hub.SubscriberCount);

        presenter.Dispose();
        context.Drain();
        hub.Publish(
            BridgeEventKeys.ControllerResumed,
            parameters: Parameters(("value", "resumed")));

        Assert.Empty(presenter.LogRows);
        Assert.Equal(0, hub.SubscriberCount);
    }

    [Fact]
    public void PublicationOnOwningThreadIsPresentedSynchronously()
    {
        using var hub = new BridgeEventHub();
        var context = new QueuedSynchronizationContext();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            new TestFormatter(),
            new RecordingOverlayPresenter(),
            context);

        hub.Publish(
            BridgeEventKeys.ControllerConnected,
            parameters: Parameters(("value", "connected")));

        Assert.Equal("connected", Assert.Single(presenter.LogRows).Text);
        Assert.Equal(0, context.PendingCount);
    }

    [Fact]
    public void RejectsAnEmptyLogCapacity()
    {
        using var hub = new BridgeEventHub();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BridgeFeedbackPresenter(
                hub,
                new TestFormatter(),
                new RecordingOverlayPresenter(),
                maximumLogRows: 0));
    }

    [Fact]
    public void FormatterFailureFallsBackWithoutBreakingLaterEvents()
    {
        using var hub = new BridgeEventHub();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            new ThrowingFormatter(),
            new RecordingOverlayPresenter());

        hub.Publish(BridgeEventKeys.AppReady);
        hub.Publish(BridgeEventKeys.ControllerConnected);

        Assert.Equal(2, presenter.LogRows.Count);
        Assert.All(
            presenter.LogRows,
            row => Assert.Contains(row.Key.Value, row.Text));
    }

    [Fact]
    public void OverlayFailureDoesNotBreakEventRetention()
    {
        using var hub = new BridgeEventHub();
        using var presenter = new BridgeFeedbackPresenter(
            hub,
            new TestFormatter(),
            new ThrowingOverlayPresenter());

        hub.Publish(
            BridgeEventKeys.ControllerConnected,
            parameters: Parameters(("value", "connected")),
            overlay: new BridgeOverlayMetadata(
                BridgeOverlayTarget.Toast));

        Assert.Equal("connected", Assert.Single(presenter.LogRows).Text);
    }

    [Fact]
    public void RejectsLogCapacityAboveFour()
    {
        using var hub = new BridgeEventHub();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BridgeFeedbackPresenter(
                hub,
                new TestFormatter(),
                new RecordingOverlayPresenter(),
                maximumLogRows: 5));
    }

    private static IReadOnlyDictionary<string, string> Parameters(
        params (string Key, string Value)[] values)
    {
        return values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal);
    }

    private static Task RunOnDedicatedThread(Action action)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();
        return completion.Task;
    }

    private sealed class TestFormatter : IBridgeFeedbackFormatter
    {
        public string Prefix { get; set; } = string.Empty;

        public BridgeFeedbackContent Format(BridgeEvent bridgeEvent)
        {
            var value = bridgeEvent.Parameters["value"];
            return new BridgeFeedbackContent(
                $"{Prefix}{value}",
                $"{Prefix}footer:{value}",
                new BridgeToastText(
                    $"{Prefix}title:{value}",
                    $"{Prefix}toast:{value}"));
        }
    }

    private sealed class RecordingOverlayPresenter : IOverlayPresenter
    {
        public List<BridgeOverlayRequest> Requests { get; } = [];

        public void Present(BridgeOverlayRequest request)
        {
            Requests.Add(request);
        }
    }

    private sealed class ThrowingFormatter : IBridgeFeedbackFormatter
    {
        public BridgeFeedbackContent Format(BridgeEvent bridgeEvent)
        {
            throw new InvalidOperationException("test");
        }
    }

    private sealed class ThrowingOverlayPresenter : IOverlayPresenter
    {
        public void Present(BridgeOverlayRequest request)
        {
            throw new InvalidOperationException("test");
        }
    }

    private sealed class QueuedSynchronizationContext :
        SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback, object?)> _work =
            new();

        public int PendingCount => _work.Count;

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _work.Enqueue((callback, state));
        }

        public void Drain()
        {
            while (_work.TryDequeue(out var work))
            {
                work.Item1(work.Item2);
            }
        }
    }
}
