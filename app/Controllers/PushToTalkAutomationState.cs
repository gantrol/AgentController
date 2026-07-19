namespace CodexController.Controllers;

public enum PushToTalkAutomationAction
{
    None,
    StartDictation,
    StopDictation,
}

public static class PushToTalkAutomationPolicy
{
    public static IReadOnlyList<string> StartActionNames { get; } =
    [
        "Dictate",
        "Start dictation",
        "听写",
        "开始听写",
    ];

    public static IReadOnlyList<string> StopActionNames { get; } =
    [
        "Stop dictation",
        "Stop recording",
        "Stop listening",
        "停止听写",
        "停止录音",
    ];

    public static bool AllowsShortcutFallback(
        PushToTalkAutomationAction action) =>
        action == PushToTalkAutomationAction.StartDictation;
}

/// <summary>
/// Serializes the desired push-to-talk state into automation work. A desired
/// state change made while work is in flight is reconciled by the next action
/// instead of racing a second UI Automation call.
/// </summary>
public sealed class PushToTalkAutomationState
{
    private long _revision;
    private long _attemptedRevision = -1;
    private PushToTalkAutomationAction _attemptedAction;
    private bool _restartRequired;
    public bool WantsDictation { get; private set; }

    public bool IsDictating { get; private set; }

    public bool HasPendingAction
    {
        get
        {
            var action = ResolveNextAction();
            return
                action != PushToTalkAutomationAction.None &&
                (_attemptedRevision != _revision ||
                 _attemptedAction != action);
        }
    }

    public void RequestStart()
    {
        _restartRequired |= IsDictating && !WantsDictation;
        WantsDictation = true;
        _revision++;
    }

    public void RequestStop()
    {
        WantsDictation = false;
        _restartRequired = false;
        _revision++;
    }

    public PushToTalkAutomationAction BeginNextAction()
    {
        var action = ResolveNextAction();
        if (
            action == PushToTalkAutomationAction.None ||
            (_attemptedRevision == _revision &&
             _attemptedAction == action))
        {
            return PushToTalkAutomationAction.None;
        }

        _attemptedRevision = _revision;
        _attemptedAction = action;
        return action;
    }

    public void Complete(
        PushToTalkAutomationAction action,
        bool succeeded)
    {
        switch (action)
        {
            case PushToTalkAutomationAction.StartDictation:
                if (succeeded)
                {
                    IsDictating = true;
                    _restartRequired = false;
                }
                else if (_attemptedRevision == _revision)
                {
                    WantsDictation = false;
                }
                break;
            case PushToTalkAutomationAction.StopDictation:
                if (succeeded)
                {
                    IsDictating = false;
                }
                break;
        }
    }

    public void Reset()
    {
        WantsDictation = false;
        IsDictating = false;
        _restartRequired = false;
        _revision++;
    }

    private PushToTalkAutomationAction ResolveNextAction()
    {
        if (WantsDictation && IsDictating && _restartRequired)
        {
            return PushToTalkAutomationAction.StopDictation;
        }

        if (WantsDictation && !IsDictating)
        {
            return PushToTalkAutomationAction.StartDictation;
        }

        if (!WantsDictation && IsDictating)
        {
            return PushToTalkAutomationAction.StopDictation;
        }

        return PushToTalkAutomationAction.None;
    }
}
