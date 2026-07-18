using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Platform.Windowing;

namespace AgentController.Application.Navigation;

public enum ThreadOpenOutcome
{
    BlockedByForeground,
    ThreadUnavailable,
    DispatchFailed,
    Requested,
}

public sealed record ThreadOpenRequest(
    string ThreadId,
    string DisplayTitle,
    string NativeTitle,
    string DeviceId,
    string ControlId,
    bool PresentationIsActive);

public sealed record ThreadOpenResult(
    ThreadOpenOutcome Outcome,
    string? ErrorCode = null);

public enum ThreadNavigationNoticeKind
{
    UndoUnavailableNonUnique,
    ArrivalConfirmed,
    UndoAvailable,
    UndoQueued,
    UndoUnavailableUnconfirmed,
    UndoPageChanged,
    UndoSucceeded,
    UndoFailed,
}

public sealed record ThreadNavigationNotice(
    ThreadNavigationNoticeKind Kind,
    string TargetDisplayTitle,
    string? ErrorCode = null,
    bool UndoWasRequested = false);

public sealed record ThreadNavigationOptions(
    TimeSpan ConfirmationTimeout,
    TimeSpan ConfirmationPollInterval,
    TimeSpan UndoWindow,
    int ConsecutiveMatchesRequired = 2)
{
    public void Validate()
    {
        if (ConfirmationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConfirmationTimeout));
        }

        if (ConfirmationPollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConfirmationPollInterval));
        }

        if (UndoWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(UndoWindow));
        }

        if (ConsecutiveMatchesRequired <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ConsecutiveMatchesRequired));
        }
    }
}

public sealed class ThreadNavigationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly ActionDispatcher _actions;
    private readonly IThreadNavigationContext _context;
    private readonly IForegroundApplication _foregroundApplication;
    private readonly ThreadNavigationOptions _options;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<long> _tickCount;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private NavigationUndoSession? _undo;
    private CancellationTokenSource? _confirmationCancellation;
    private bool _disposed;

    public ThreadNavigationCoordinator(
        ActionDispatcher actions,
        IThreadNavigationContext context,
        IForegroundApplication foregroundApplication,
        ThreadNavigationOptions options,
        Func<DateTimeOffset>? clock = null,
        Func<long>? tickCount = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _actions = actions ??
            throw new ArgumentNullException(nameof(actions));
        _context = context ??
            throw new ArgumentNullException(nameof(context));
        _foregroundApplication = foregroundApplication ??
            throw new ArgumentNullException(
                nameof(foregroundApplication));
        options.Validate();
        _options = options;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _tickCount = tickCount ?? (() => Environment.TickCount64);
        _delay = delay ?? Task.Delay;
    }

    public event EventHandler<ThreadNavigationNotice>? NoticePublished;

    public async Task<ThreadOpenResult> OpenAsync(
        ThreadOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ThreadId);

        if (_context.RequiresForeground &&
            !_foregroundApplication.IsForeground &&
            !request.PresentationIsActive)
        {
            return new(ThreadOpenOutcome.BlockedByForeground);
        }

        if (!_context.IsThreadAvailable(request.ThreadId))
        {
            return new(ThreadOpenOutcome.ThreadUnavailable);
        }

        ClearUndo();
        var previousTitle = _context.ReadCurrentThreadTitle();
        ActionResult result;
        try
        {
            result = await _actions.ExecuteAsync(
                    OpenThreadActionContract.Id,
                    request.DeviceId,
                    request.ControlId,
                    "sidebar.task",
                    $"thread.open:{request.ThreadId}",
                    ActionSafetyLevel.Routine,
                    new Dictionary<string, string>
                    {
                        [OpenThreadActionContract.ThreadIdParameter] =
                            request.ThreadId,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (
            !cancellationToken.IsCancellationRequested)
        {
            return new(ThreadOpenOutcome.DispatchFailed);
        }

        if (result.Outcome is not (
                ActionOutcome.Succeeded or
                ActionOutcome.AcceptedUnverified))
        {
            return new(
                ThreadOpenOutcome.DispatchFailed,
                result.ErrorCode);
        }

        RegisterUndo(
            request.DisplayTitle,
            request.NativeTitle,
            previousTitle);
        return new(ThreadOpenOutcome.Requested);
    }

    public bool TryRequestUndo()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        NavigationUndoSession? undo;
        NavigationUndoPressAction action;
        lock (_gate)
        {
            undo = _undo;
            if (undo is null)
            {
                return false;
            }

            action = undo.RequestUndo(_clock());
        }

        if (action ==
            NavigationUndoPressAction.QueueUntilNavigationConfirms)
        {
            Publish(new(
                ThreadNavigationNoticeKind.UndoQueued,
                undo.TargetDisplayTitle));
            return true;
        }

        if (action ==
            NavigationUndoPressAction.ExpireAndBeginStopHold)
        {
            ClearUndo();
            return false;
        }

        _ = ExecuteUndoAsync(undo);
        return true;
    }

    public void ClearUndo()
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            _undo = null;
            cancellation = _confirmationCancellation;
            _confirmationCancellation = null;
        }

        cancellation?.Cancel();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ClearUndo();
    }

    private void RegisterUndo(
        string displayTitle,
        string nativeTitle,
        string? previousTitle)
    {
        if (string.IsNullOrWhiteSpace(nativeTitle) ||
            string.Equals(
                previousTitle,
                nativeTitle,
                StringComparison.Ordinal))
        {
            return;
        }

        if (_context.CountThreadTitleMatches(nativeTitle) != 1)
        {
            Publish(new(
                ThreadNavigationNoticeKind.UndoUnavailableNonUnique,
                displayTitle));
            return;
        }

        var undo = new NavigationUndoSession(
            displayTitle,
            nativeTitle);
        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            _undo = undo;
            _confirmationCancellation = cancellation;
        }

        _ = ConfirmUndoAsync(undo, cancellation);
    }

    private async Task ConfirmUndoAsync(
        NavigationUndoSession undo,
        CancellationTokenSource cancellation)
    {
        var consecutiveMatches = 0;
        var deadline =
            _tickCount() +
            (long)_options.ConfirmationTimeout.TotalMilliseconds;
        try
        {
            while (_tickCount() < deadline)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var currentTitle = await Task.Run(
                        _context.ReadCurrentThreadTitle,
                        cancellation.Token)
                    .ConfigureAwait(false);
                if (string.Equals(
                        currentTitle,
                        undo.TargetNativeTitle,
                        StringComparison.Ordinal))
                {
                    consecutiveMatches++;
                    if (consecutiveMatches >=
                        _options.ConsecutiveMatchesRequired)
                    {
                        bool confirmedUndoWasRequested;
                        lock (_gate)
                        {
                            if (!ReferenceEquals(_undo, undo))
                            {
                                return;
                            }

                            undo.MarkConfirmed(
                                _clock(),
                                _options.UndoWindow);
                            confirmedUndoWasRequested =
                                undo.UndoRequested;
                        }

                        Publish(new(
                            ThreadNavigationNoticeKind.ArrivalConfirmed,
                            undo.TargetDisplayTitle));
                        if (confirmedUndoWasRequested)
                        {
                            await ExecuteUndoAsync(undo)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            Publish(new(
                                ThreadNavigationNoticeKind.UndoAvailable,
                                undo.TargetDisplayTitle));
                        }

                        return;
                    }
                }
                else
                {
                    consecutiveMatches = 0;
                }

                await _delay(
                        _options.ConfirmationPollInterval,
                        cancellation.Token)
                    .ConfigureAwait(false);
            }

            bool timedOut;
            bool timeoutUndoWasRequested;
            lock (_gate)
            {
                timedOut = ReferenceEquals(_undo, undo);
                timeoutUndoWasRequested = undo.UndoRequested;
                if (timedOut)
                {
                    _undo = null;
                }
            }

            if (timedOut)
            {
                Publish(new(
                    ThreadNavigationNoticeKind.UndoUnavailableUnconfirmed,
                    undo.TargetDisplayTitle,
                    UndoWasRequested: timeoutUndoWasRequested));
            }
        }
        catch (OperationCanceledException)
        {
            // A newer navigation or an intentional action invalidated it.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(
                        _confirmationCancellation,
                        cancellation))
                {
                    _confirmationCancellation = null;
                }
            }

            cancellation.Dispose();
        }
    }

    private async Task ExecuteUndoAsync(NavigationUndoSession undo)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_undo, undo))
            {
                return;
            }
        }

        var currentTitle = _context.ReadCurrentThreadTitle();
        if (!string.Equals(
                currentTitle,
                undo.TargetNativeTitle,
                StringComparison.Ordinal))
        {
            ClearUndo();
            Publish(new(
                ThreadNavigationNoticeKind.UndoPageChanged,
                undo.TargetDisplayTitle));
            return;
        }

        ActionResult? result = null;
        try
        {
            result = await _actions.ExecuteAsync(
                    NavigationActionContract.UndoId,
                    "controller.active",
                    "controller.face.east",
                    "navigation.undo",
                    "navigation.undo",
                    ActionSafetyLevel.Routine)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The notice carries the same presentation-safe null error as the
            // former MainWindow exception boundary.
        }

        if (result?.Outcome is
            ActionOutcome.Succeeded or
            ActionOutcome.AcceptedUnverified)
        {
            ClearUndo();
            Publish(new(
                ThreadNavigationNoticeKind.UndoSucceeded,
                undo.TargetDisplayTitle));
            return;
        }

        Publish(new(
            ThreadNavigationNoticeKind.UndoFailed,
            undo.TargetDisplayTitle,
            result?.ErrorCode));
    }

    private void Publish(ThreadNavigationNotice notice) =>
        NoticePublished?.Invoke(this, notice);

    private enum NavigationUndoPressAction
    {
        QueueUntilNavigationConfirms,
        ExecuteUndo,
        ExpireAndBeginStopHold,
    }

    private sealed class NavigationUndoSession
    {
        internal NavigationUndoSession(
            string targetDisplayTitle,
            string targetNativeTitle)
        {
            TargetDisplayTitle = targetDisplayTitle;
            TargetNativeTitle = targetNativeTitle;
        }

        internal string TargetDisplayTitle { get; }

        internal string TargetNativeTitle { get; }

        internal bool Confirmed { get; private set; }

        internal bool UndoRequested { get; private set; }

        internal DateTimeOffset? ExpiresAt { get; private set; }

        internal void MarkConfirmed(
            DateTimeOffset now,
            TimeSpan undoWindow)
        {
            Confirmed = true;
            ExpiresAt = now + undoWindow;
        }

        internal NavigationUndoPressAction RequestUndo(
            DateTimeOffset now)
        {
            if (!Confirmed || ExpiresAt is null)
            {
                UndoRequested = true;
                return NavigationUndoPressAction
                    .QueueUntilNavigationConfirms;
            }

            return now > ExpiresAt
                ? NavigationUndoPressAction
                    .ExpireAndBeginStopHold
                : NavigationUndoPressAction.ExecuteUndo;
        }
    }
}
