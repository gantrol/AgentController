using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class PushToTalkAutomationStateTests
{
    [Fact]
    public void AutomationPolicySupportsEnglishAndChineseWithoutStopShortcut()
    {
        Assert.Contains(
            "Dictate",
            PushToTalkAutomationPolicy.StartActionNames);
        Assert.Contains(
            "听写",
            PushToTalkAutomationPolicy.StartActionNames);
        Assert.Contains(
            "Stop dictation",
            PushToTalkAutomationPolicy.StopActionNames);
        Assert.Contains(
            "停止听写",
            PushToTalkAutomationPolicy.StopActionNames);
        Assert.True(
            PushToTalkAutomationPolicy.AllowsShortcutFallback(
                PushToTalkAutomationAction.StartDictation));
        Assert.False(
            PushToTalkAutomationPolicy.AllowsShortcutFallback(
                PushToTalkAutomationAction.StopDictation));
    }

    [Fact]
    public void StartIsNeverBlockedBehindPopupAutomation()
    {
        var state = new PushToTalkAutomationState();

        state.RequestStart();

        Assert.Equal(
            PushToTalkAutomationAction.StartDictation,
            state.BeginNextAction());
    }

    [Fact]
    public void ReleaseDuringStartImmediatelySchedulesStopAfterSuccess()
    {
        var state = new PushToTalkAutomationState();
        state.RequestStart();
        Assert.Equal(
            PushToTalkAutomationAction.StartDictation,
            state.BeginNextAction());

        state.RequestStop();
        state.Complete(
            PushToTalkAutomationAction.StartDictation,
            succeeded: true);

        Assert.True(state.IsDictating);
        Assert.Equal(
            PushToTalkAutomationAction.StopDictation,
            state.BeginNextAction());
        state.Complete(
            PushToTalkAutomationAction.StopDictation,
            succeeded: true);
        Assert.False(state.IsDictating);
        Assert.Equal(
            PushToTalkAutomationAction.None,
            state.BeginNextAction());
    }

    [Fact]
    public void FailedStartDoesNotSpinUntilASecondPhysicalPress()
    {
        var state = new PushToTalkAutomationState();
        state.RequestStart();
        var action = state.BeginNextAction();

        state.Complete(action, succeeded: false);

        Assert.False(state.IsDictating);
        Assert.False(state.WantsDictation);
        Assert.False(state.HasPendingAction);
        Assert.Equal(
            PushToTalkAutomationAction.None,
            state.BeginNextAction());
        state.RequestStop();
        state.RequestStart();
        Assert.Equal(
            PushToTalkAutomationAction.StartDictation,
            state.BeginNextAction());
    }

    [Fact]
    public void FailedStopKeepsRecordingStateVisibleWithoutSpinning()
    {
        var state = new PushToTalkAutomationState();
        state.RequestStart();
        var start = state.BeginNextAction();
        state.Complete(start, succeeded: true);
        state.RequestStop();
        var stop = state.BeginNextAction();

        state.Complete(stop, succeeded: false);

        Assert.False(state.WantsDictation);
        Assert.True(state.IsDictating);
        Assert.False(state.HasPendingAction);
        Assert.Equal(
            PushToTalkAutomationAction.None,
            state.BeginNextAction());
        state.RequestStop();
        Assert.Equal(
            PushToTalkAutomationAction.StopDictation,
            state.BeginNextAction());
    }

    [Fact]
    public void NewPressAfterFailedReleaseRetriesReleaseBeforeRestart()
    {
        var state = new PushToTalkAutomationState();
        state.RequestStart();
        state.Complete(
            state.BeginNextAction(),
            succeeded: true);
        state.RequestStop();
        state.Complete(
            state.BeginNextAction(),
            succeeded: false);

        state.RequestStart();

        Assert.Equal(
            PushToTalkAutomationAction.StopDictation,
            state.BeginNextAction());
        state.Complete(
            PushToTalkAutomationAction.StopDictation,
            succeeded: true);
        Assert.Equal(
            PushToTalkAutomationAction.StartDictation,
            state.BeginNextAction());
    }

    [Fact]
    public void NewPressDuringStopRestartsOnlyAfterSuccessfulStop()
    {
        var state = new PushToTalkAutomationState();
        state.RequestStart();
        var firstStart = state.BeginNextAction();
        state.Complete(firstStart, succeeded: true);
        state.RequestStop();
        var stop = state.BeginNextAction();

        state.RequestStart();
        state.Complete(stop, succeeded: true);

        Assert.True(state.WantsDictation);
        Assert.False(state.IsDictating);
        Assert.Equal(
            PushToTalkAutomationAction.StartDictation,
            state.BeginNextAction());
    }
}
