using CodexController.Models;

namespace CodexController.Controllers;

internal sealed class ControllerHoldCoordinator : IDisposable
{
    private readonly Func<long> _tickCount;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _cancelPollInterval;
    private CancellationTokenSource? _cancelHoldCancellation;
    private CancellationTokenSource? _conversationHoldCancellation;
    private ConversationBoundary? _conversationHoldTarget;
    private bool _disposed;

    internal ControllerHoldCoordinator(
        Func<long>? tickCount = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? cancelPollInterval = null)
    {
        _tickCount = tickCount ?? (() => Environment.TickCount64);
        _delay = delay ?? Task.Delay;
        _cancelPollInterval = cancelPollInterval ??
            TimeSpan.FromMilliseconds(80);
    }

    internal void BeginConversationBoundary(
        ConversationTurnInputAction action,
        int topHoldMs,
        int bottomHoldMs,
        Func<ConversationBoundary, bool> canContinue,
        Func<ConversationBoundary, Task> complete)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canContinue);
        ArgumentNullException.ThrowIfNull(complete);
        CancelConversationBoundary();

        var boundary =
            ConversationBoundaryHoldPolicy.ResolveBoundary(action);
        var cancellation = new CancellationTokenSource();
        _conversationHoldTarget = boundary;
        _conversationHoldCancellation = cancellation;
        _ = RunConversationBoundaryAsync(
            boundary,
            topHoldMs,
            bottomHoldMs,
            canContinue,
            complete,
            cancellation);
    }

    internal void EndConversationBoundary(
        ControllerButtons releasedButtons)
    {
        if (_conversationHoldTarget is not { } boundary)
        {
            return;
        }

        if (releasedButtons.HasFlag(
                ConversationBoundaryHoldPolicy.ResolveButton(boundary)))
        {
            CancelConversationBoundary();
        }
    }

    internal void CancelConversationBoundary()
    {
        var cancellation = _conversationHoldCancellation;
        _conversationHoldCancellation = null;
        _conversationHoldTarget = null;
        cancellation?.Cancel();
    }

    internal void BeginCancelHold(
        int holdMs,
        Func<bool> canContinue,
        Action<int> countdownChanged,
        Action complete)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canContinue);
        ArgumentNullException.ThrowIfNull(countdownChanged);
        ArgumentNullException.ThrowIfNull(complete);
        if (_cancelHoldCancellation is not null)
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _cancelHoldCancellation = cancellation;
        _ = RunCancelHoldAsync(
            holdMs,
            canContinue,
            countdownChanged,
            complete,
            cancellation);
    }

    internal bool CancelBaseCancelHold()
    {
        var cancellation = _cancelHoldCancellation;
        if (cancellation is null)
        {
            return false;
        }

        _cancelHoldCancellation = null;
        cancellation.Cancel();
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelBaseCancelHold();
        CancelConversationBoundary();
    }

    private async Task RunConversationBoundaryAsync(
        ConversationBoundary boundary,
        int topHoldMs,
        int bottomHoldMs,
        Func<ConversationBoundary, bool> canContinue,
        Func<ConversationBoundary, Task> complete,
        CancellationTokenSource cancellation)
    {
        try
        {
            var holdMs =
                ConversationBoundaryHoldPolicy.ResolveHoldMilliseconds(
                    boundary,
                    topHoldMs,
                    bottomHoldMs);
            await _delay(
                    TimeSpan.FromMilliseconds(holdMs),
                    cancellation.Token)
                .ConfigureAwait(true);
            if (!IsCurrentConversationHold(
                    boundary,
                    cancellation) ||
                !canContinue(boundary))
            {
                return;
            }

            _conversationHoldCancellation = null;
            _conversationHoldTarget = null;
            await complete(boundary).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Releasing the D-pad before the threshold keeps short navigation.
        }
        finally
        {
            if (ReferenceEquals(
                    _conversationHoldCancellation,
                    cancellation))
            {
                _conversationHoldCancellation = null;
                _conversationHoldTarget = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task RunCancelHoldAsync(
        int holdMs,
        Func<bool> canContinue,
        Action<int> countdownChanged,
        Action complete,
        CancellationTokenSource cancellation)
    {
        var startedAt = _tickCount();
        var lastRemaining = -1;
        try
        {
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrentCancelHold(cancellation) ||
                    !canContinue())
                {
                    return;
                }

                var elapsed = _tickCount() - startedAt;
                if (CancelHoldCountdownPolicy.IsComplete(
                        elapsed,
                        holdMs))
                {
                    break;
                }

                var remaining =
                    CancelHoldCountdownPolicy.RemainingSeconds(
                        elapsed,
                        holdMs);
                if (remaining != lastRemaining)
                {
                    lastRemaining = remaining;
                    countdownChanged(remaining);
                }

                await _delay(
                        _cancelPollInterval,
                        cancellation.Token)
                    .ConfigureAwait(true);
            }

            if (!IsCurrentCancelHold(cancellation) ||
                !canContinue())
            {
                return;
            }

            _cancelHoldCancellation = null;
            complete();
        }
        catch (OperationCanceledException)
        {
            // Release, session loss, or shutdown disarms the hold.
        }
        finally
        {
            if (ReferenceEquals(_cancelHoldCancellation, cancellation))
            {
                _cancelHoldCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private bool IsCurrentConversationHold(
        ConversationBoundary boundary,
        CancellationTokenSource cancellation) =>
        ReferenceEquals(
            _conversationHoldCancellation,
            cancellation) &&
        _conversationHoldTarget == boundary &&
        !cancellation.IsCancellationRequested;

    private bool IsCurrentCancelHold(
        CancellationTokenSource cancellation) =>
        ReferenceEquals(_cancelHoldCancellation, cancellation) &&
        !cancellation.IsCancellationRequested;
}
