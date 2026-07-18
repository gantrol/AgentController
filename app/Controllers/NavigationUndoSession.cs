namespace CodexController.Controllers;

internal enum NavigationUndoPressAction
{
    QueueUntilNavigationConfirms,
    ExecuteUndo,
    ExpireAndBeginStopHold,
}

internal sealed class NavigationUndoSession
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
            return NavigationUndoPressAction.QueueUntilNavigationConfirms;
        }

        return now > ExpiresAt
            ? NavigationUndoPressAction.ExpireAndBeginStopHold
            : NavigationUndoPressAction.ExecuteUndo;
    }
}
