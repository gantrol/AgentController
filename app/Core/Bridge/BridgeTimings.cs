namespace CodexController.Core.Bridge;

/// <summary>
/// Interaction timing policy shared by the bridge orchestration layer.
/// Keeping these values named prevents input, navigation, and composer
/// state machines from silently drifting apart.
/// </summary>
public static class BridgeTimings
{
    public static readonly TimeSpan StatusPoll = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DataRefresh = TimeSpan.FromSeconds(12);

    public const int WakeInputGuardMs = 220;
    public const int GestureInputGuardMs = 180;
    public const int NavigationConfirmTimeoutMs = 8000;
    public const int NavigationConfirmPollMs = 130;
    public const int ComposerSettleMs = 520;
    public const int ComposerFallbackSettleMs = 180;
    public const int ComposerMenuPollMs = 55;

    public static readonly TimeSpan NavigationUndoWindow =
        TimeSpan.FromSeconds(10);
}
