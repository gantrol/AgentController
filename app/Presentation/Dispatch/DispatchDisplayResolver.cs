using CodexController.Localization;

namespace CodexController.Presentation.Dispatch;

public enum DispatchTurnState
{
    Unknown,
    Idle,
    Running,
}

public enum DispatchFollowUpBehavior
{
    Unknown,
    Steer,
    Queue,
}

public enum DispatchDisplayKind
{
    Default,
    Send,
    Steer,
    Queue,
}

public sealed record DispatchDisplay(
    DispatchDisplayKind Kind,
    string Label,
    string Description);

/// <summary>
/// Converts already-observed turn and follow-up state into UI copy. This class
/// does not probe Codex and deliberately falls back to Default when either
/// part of the running-turn state has not been verified.
/// </summary>
public sealed class DispatchDisplayResolver
{
    private readonly LocalizedStrings _strings;

    public DispatchDisplayResolver(LocalizedStrings strings)
    {
        _strings = strings
            ?? throw new ArgumentNullException(nameof(strings));
    }

    public DispatchDisplay Resolve(
        DispatchTurnState turnState,
        DispatchFollowUpBehavior followUpBehavior)
    {
        return turnState switch
        {
            DispatchTurnState.Idle => new DispatchDisplay(
                DispatchDisplayKind.Send,
                _strings.DispatchSend,
                _strings.DispatchSendDescription),
            DispatchTurnState.Running
                when followUpBehavior ==
                     DispatchFollowUpBehavior.Steer =>
                new DispatchDisplay(
                    DispatchDisplayKind.Steer,
                    _strings.DispatchSteer,
                    _strings.DispatchSteerDescription),
            DispatchTurnState.Running
                when followUpBehavior ==
                     DispatchFollowUpBehavior.Queue =>
                new DispatchDisplay(
                    DispatchDisplayKind.Queue,
                    _strings.DispatchQueue,
                    _strings.DispatchQueueDescription),
            _ => new DispatchDisplay(
                DispatchDisplayKind.Default,
                _strings.DispatchDefault,
                _strings.DispatchDefaultDescription),
        };
    }
}
