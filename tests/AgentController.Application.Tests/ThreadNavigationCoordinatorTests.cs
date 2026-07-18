using System.Collections.Concurrent;
using AgentController.Application.Actions;
using AgentController.Application.Navigation;
using AgentController.Domain.Actions;
using AgentController.Platform.Windowing;
using Xunit;

namespace AgentController.Application.Tests;

public sealed class ThreadNavigationCoordinatorTests
{
    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-07-18T18:00:00Z");

    [Fact]
    public async Task ForegroundGateStopsBeforeAvailabilityAndDispatch()
    {
        var availabilityProbes = 0;
        var executor = new RecordingExecutor();
        using var coordinator = CreateCoordinator(
            executor,
            () => "Previous",
            requiresForeground: () => true,
            isAgentForeground: () => false,
            isThreadAvailable: _ =>
            {
                availabilityProbes++;
                return true;
            });

        var result = await coordinator.OpenAsync(
            Request(presentationIsActive: false));

        Assert.Equal(
            ThreadOpenOutcome.BlockedByForeground,
            result.Outcome);
        Assert.Equal(0, availabilityProbes);
        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task MissingThreadStopsBeforeDispatch()
    {
        var executor = new RecordingExecutor();
        using var coordinator = CreateCoordinator(
            executor,
            () => "Previous",
            isThreadAvailable: _ => false);

        var result = await coordinator.OpenAsync(Request());

        Assert.Equal(
            ThreadOpenOutcome.ThreadUnavailable,
            result.Outcome);
        Assert.Empty(executor.Requests);
    }

    [Fact]
    public async Task AcceptedOpenPublishesUndoAfterConfirmedArrival()
    {
        var titles = new ConcurrentQueue<string?>(
            ["Previous", "Target", "Target"]);
        var executor = new RecordingExecutor();
        using var coordinator = CreateCoordinator(
            executor,
            () => DequeueOrLast(titles, "Target"));
        var notices = new ConcurrentQueue<ThreadNavigationNotice>();
        var available = NoticeCompletion(
            coordinator,
            notices,
            ThreadNavigationNoticeKind.UndoAvailable);

        var result = await coordinator.OpenAsync(Request());
        await available.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ThreadOpenOutcome.Requested, result.Outcome);
        Assert.Collection(
            executor.Requests,
            request => Assert.Equal(
                OpenThreadActionContract.Id,
                request.ActionId));
        Assert.Contains(
            notices,
            notice => notice.Kind ==
                ThreadNavigationNoticeKind.ArrivalConfirmed);
    }

    [Fact]
    public async Task QueuedUndoExecutesAsSoonAsArrivalConfirms()
    {
        var titles = new ConcurrentQueue<string?>(
            ["Previous", "Loading", "Target", "Target", "Target"]);
        var firstPollCompleted =
            new TaskCompletionSource(TaskCreationOptions
                .RunContinuationsAsynchronously);
        var releasePolling =
            new TaskCompletionSource(TaskCreationOptions
                .RunContinuationsAsynchronously);
        var executor = new RecordingExecutor();
        using var coordinator = CreateCoordinator(
            executor,
            () => DequeueOrLast(titles, "Target"),
            delay: async (_, cancellationToken) =>
            {
                firstPollCompleted.TrySetResult();
                await releasePolling.Task.WaitAsync(cancellationToken);
            });
        var notices = new ConcurrentQueue<ThreadNavigationNotice>();
        var succeeded = NoticeCompletion(
            coordinator,
            notices,
            ThreadNavigationNoticeKind.UndoSucceeded);

        var result = await coordinator.OpenAsync(Request());
        await firstPollCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var consumed = coordinator.TryRequestUndo();
        releasePolling.TrySetResult();
        await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(ThreadOpenOutcome.Requested, result.Outcome);
        Assert.True(consumed);
        Assert.Contains(
            notices,
            notice => notice.Kind ==
                ThreadNavigationNoticeKind.UndoQueued);
        Assert.Equal(
            [
                OpenThreadActionContract.Id,
                NavigationActionContract.UndoId,
            ],
            executor.Requests.Select(request => request.ActionId));
    }

    [Fact]
    public async Task UndoIsRejectedAfterPageChanges()
    {
        var titles = new ConcurrentQueue<string?>(
            ["Previous", "Target", "Target", "Different"]);
        var executor = new RecordingExecutor();
        using var coordinator = CreateCoordinator(
            executor,
            () => DequeueOrLast(titles, "Different"));
        var notices = new ConcurrentQueue<ThreadNavigationNotice>();
        var available = NoticeCompletion(
            coordinator,
            notices,
            ThreadNavigationNoticeKind.UndoAvailable);
        var changed = NoticeCompletion(
            coordinator,
            notices,
            ThreadNavigationNoticeKind.UndoPageChanged);

        await coordinator.OpenAsync(Request());
        await available.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var consumed = coordinator.TryRequestUndo();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(consumed);
        Assert.Single(executor.Requests);
        Assert.False(coordinator.TryRequestUndo());
    }

    [Fact]
    public async Task ExpiredUndoFallsThroughToNormalCancelHandling()
    {
        var now = Start;
        var titles = new ConcurrentQueue<string?>(
            ["Previous", "Target", "Target"]);
        var executor = new RecordingExecutor();
        using var coordinator = CreateCoordinator(
            executor,
            () => DequeueOrLast(titles, "Target"),
            clock: () => now);
        var notices = new ConcurrentQueue<ThreadNavigationNotice>();
        var available = NoticeCompletion(
            coordinator,
            notices,
            ThreadNavigationNoticeKind.UndoAvailable);

        await coordinator.OpenAsync(Request());
        await available.Task.WaitAsync(TimeSpan.FromSeconds(2));
        now += TimeSpan.FromSeconds(11);

        Assert.False(coordinator.TryRequestUndo());
        Assert.Single(executor.Requests);
    }

    private static ThreadNavigationCoordinator CreateCoordinator(
        RecordingExecutor executor,
        Func<string?> readTitle,
        Func<bool>? requiresForeground = null,
        Func<bool>? isAgentForeground = null,
        Func<string, bool>? isThreadAvailable = null,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        var router = new ActionRouter([executor]);
        return new ThreadNavigationCoordinator(
            new ActionDispatcher(
                router,
                Guid.NewGuid,
                clock ?? (() => Start)),
            new StubThreadNavigationContext(
                readTitle,
                requiresForeground ?? (() => false),
                isThreadAvailable ?? (_ => true)),
            new StubForegroundApplication(
                isAgentForeground ?? (() => true)),
            new ThreadNavigationOptions(
                TimeSpan.FromSeconds(1),
                TimeSpan.Zero,
                TimeSpan.FromSeconds(10)),
            clock ?? (() => Start),
            () => 0,
            delay ?? ((_, _) => Task.CompletedTask));
    }

    private sealed class StubThreadNavigationContext :
        IThreadNavigationContext
    {
        private readonly Func<string?> _readTitle;
        private readonly Func<bool> _requiresForeground;
        private readonly Func<string, bool> _isThreadAvailable;

        internal StubThreadNavigationContext(
            Func<string?> readTitle,
            Func<bool> requiresForeground,
            Func<string, bool> isThreadAvailable)
        {
            _readTitle = readTitle;
            _requiresForeground = requiresForeground;
            _isThreadAvailable = isThreadAvailable;
        }

        public bool RequiresForeground => _requiresForeground();

        public bool IsThreadAvailable(string threadId) =>
            _isThreadAvailable(threadId);

        public string? ReadCurrentThreadTitle() => _readTitle();

        public int CountThreadTitleMatches(string nativeTitle) => 1;
    }

    private sealed class StubForegroundApplication :
        IForegroundApplication
    {
        private readonly Func<bool> _isForeground;

        internal StubForegroundApplication(Func<bool> isForeground)
        {
            _isForeground = isForeground;
        }

        public bool IsForeground => _isForeground();

        public bool TryActivate() => true;
    }

    private static ThreadOpenRequest Request(
        bool presentationIsActive = true) =>
        new(
            "thread-1",
            "Display",
            "Target",
            "test.controller",
            "controller.face.south",
            presentationIsActive);

    private static TaskCompletionSource<ThreadNavigationNotice>
        NoticeCompletion(
            ThreadNavigationCoordinator coordinator,
            ConcurrentQueue<ThreadNavigationNotice> notices,
            ThreadNavigationNoticeKind expected)
    {
        var completion =
            new TaskCompletionSource<ThreadNavigationNotice>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.NoticePublished += (_, notice) =>
        {
            notices.Enqueue(notice);
            if (notice.Kind == expected)
            {
                completion.TrySetResult(notice);
            }
        };
        return completion;
    }

    private static string? DequeueOrLast(
        ConcurrentQueue<string?> values,
        string? last) =>
        values.TryDequeue(out var value) ? value : last;

    private sealed class RecordingExecutor : IActionExecutor
    {
        public string Id => "recording.navigation";

        public ConcurrentQueue<ActionRequest> Requests { get; } = [];

        public ValueTask<ExecutorCapability> ProbeAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ExecutorCapability(
                Id,
                request.ActionId,
                ExecutorCapabilityStatus.Available,
                Priority: 100));

        public ValueTask<ActionResult> ExecuteAsync(
            ActionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Enqueue(request);
            return ValueTask.FromResult(new ActionResult(
                request.RequestId,
                request.ActionId,
                ActionOutcome.AcceptedUnverified,
                Id,
                request.RequestedAt));
        }
    }
}
