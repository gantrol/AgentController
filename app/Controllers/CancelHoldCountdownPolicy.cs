namespace CodexController.Controllers;

internal static class CancelHoldCountdownPolicy
{
    internal static bool IsComplete(long elapsedMilliseconds, int holdMs) =>
        elapsedMilliseconds >= Math.Max(1, holdMs);

    internal static int RemainingSeconds(
        long elapsedMilliseconds,
        int holdMs)
    {
        var remaining = Math.Max(
            0,
            Math.Max(1, holdMs) - Math.Max(0, elapsedMilliseconds));
        return (int)Math.Ceiling(remaining / 1000d);
    }
}
