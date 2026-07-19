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
    public const int ForegroundLossGraceMs = 300;
    public const int GestureInputGuardMs = 180;
    public const int SidebarFocusSettleMs = 150;
    public const int NavigationConfirmTimeoutMs = 8000;
    public const int NavigationConfirmPollMs = 130;
    public const int ComposerSettleMs = 520;
    public const int ComposerFallbackSettleMs = 180;
    public const int ComposerMenuPollMs = 55;
    public const int DialHoldMs = 500;
    public const int CancelHoldMs = 3000;
    public const int ConversationTopHoldMs = 4000;
    public const int ConversationBottomHoldMs = 3000;
    public const int DictationDialCloseTimeoutMs = 700;
    public const int DictationStartTimeoutMs = 900;
    public const int DictationStopTimeoutMs = 1200;
    public const int MicroReleaseRetryDelayMs = 1050;
    public const double PushToTalkEngageThreshold = 0.35;
    public const double PushToTalkReleaseThreshold = 0.20;

    public static readonly TimeSpan NavigationUndoWindow =
        TimeSpan.FromSeconds(10);
}
