namespace CodexController.Controllers;

internal enum NavigationUndoPressAction
{
    QueueUntilNavigationConfirms,
    ExecuteUndo,
    ExpireAndBeginStopHold,
}

internal static class NavigationUndoPressPolicy
{
    public static NavigationUndoPressAction Resolve(
        bool confirmed,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        if (!confirmed || expiresAt is null)
        {
            return NavigationUndoPressAction.QueueUntilNavigationConfirms;
        }

        return now > expiresAt
            ? NavigationUndoPressAction.ExpireAndBeginStopHold
            : NavigationUndoPressAction.ExecuteUndo;
    }
}
