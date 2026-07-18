using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class ControllerHoldCoordinatorTests
{
    [Theory]
    [InlineData(
        ConversationTurnInputAction.PreviousUserMessage,
        ConversationBoundary.Top)]
    [InlineData(
        ConversationTurnInputAction.NextUserMessage,
        ConversationBoundary.Bottom)]
    public async Task ConversationHoldCompletesResolvedBoundary(
        ConversationTurnInputAction action,
        ConversationBoundary expected)
    {
        var completed =
            new TaskCompletionSource<ConversationBoundary>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new ControllerHoldCoordinator(
            delay: (_, _) => Task.CompletedTask);

        coordinator.BeginConversationBoundary(
            action,
            topHoldMs: 4000,
            bottomHoldMs: 3000,
            _ => true,
            boundary =>
            {
                completed.TrySetResult(boundary);
                return Task.CompletedTask;
            });

        Assert.Equal(
            expected,
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task ConversationReleaseCancelsBeforeThreshold()
    {
        var delayStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var delayStopped =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        using var coordinator = new ControllerHoldCoordinator(
            delay: async (_, cancellationToken) =>
            {
                delayStarted.TrySetResult();
                try
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                }
                finally
                {
                    delayStopped.TrySetResult();
                }
            });
        coordinator.BeginConversationBoundary(
            ConversationTurnInputAction.PreviousUserMessage,
            topHoldMs: 4000,
            bottomHoldMs: 3000,
            _ => true,
            _ =>
            {
                completed = true;
                return Task.CompletedTask;
            });
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        coordinator.EndConversationBoundary(
            ControllerButtons.DPadUp);
        await delayStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(completed);
    }

    [Fact]
    public async Task FailedConversationGuardSuppressesCompletion()
    {
        var completed = false;
        using var coordinator = new ControllerHoldCoordinator(
            delay: (_, _) => Task.CompletedTask);

        coordinator.BeginConversationBoundary(
            ConversationTurnInputAction.NextUserMessage,
            topHoldMs: 4000,
            bottomHoldMs: 3000,
            _ => false,
            _ =>
            {
                completed = true;
                return Task.CompletedTask;
            });
        await Task.Yield();

        Assert.False(completed);
    }

    [Fact]
    public void CancelHoldCountsDownOncePerSecondAndCompletes()
    {
        var ticks = new Queue<long>([0, 0, 1000, 2000, 3000]);
        var countdown = new List<int>();
        var completed = false;
        using var coordinator = new ControllerHoldCoordinator(
            tickCount: () => ticks.Count > 0
                ? ticks.Dequeue()
                : 3000,
            delay: (_, _) => Task.CompletedTask);

        coordinator.BeginCancelHold(
            holdMs: 3000,
            () => true,
            countdown.Add,
            () => completed = true);

        Assert.Equal([3, 2, 1], countdown);
        Assert.True(completed);
        Assert.False(coordinator.CancelBaseCancelHold());
    }

    [Fact]
    public async Task CancelHoldCanBeAbortedDuringCountdown()
    {
        var delayStarted =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var delayStopped =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        using var coordinator = new ControllerHoldCoordinator(
            tickCount: () => 0,
            delay: async (_, cancellationToken) =>
            {
                delayStarted.TrySetResult();
                try
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken);
                }
                finally
                {
                    delayStopped.TrySetResult();
                }
            });
        coordinator.BeginCancelHold(
            holdMs: 3000,
            () => true,
            _ => { },
            () => completed = true);
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var canceled = coordinator.CancelBaseCancelHold();
        await delayStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(canceled);
        Assert.False(completed);
        Assert.False(coordinator.CancelBaseCancelHold());
    }
}
