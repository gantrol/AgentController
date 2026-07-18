using CodexController.Core.Bridge;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Controllers;

public enum ControllerButtonTransition
{
    None,
    Pressed,
    Released,
}

public readonly record struct ControllerButtonEdges(
    ControllerButtons Down,
    ControllerButtons Up);

/// <summary>
/// Owns the stateful, presentation-independent stages of the legacy WPF
/// controller pipeline. MainWindow still decides which application action to
/// execute; this coordinator preserves input ordering, edge history, trigger
/// hysteresis, stick neutralization, and repeat timing between frames.
/// </summary>
public sealed class ControllerInteractionCoordinator
{
    private static readonly ControllerInteractionIntent
        CycleRootSidebarScopeIntent =
            new ControllerInteractionIntent.CycleRootSidebarScope();
    private static readonly ControllerInteractionIntent
        BeginVirtualDialPressIntent =
            new ControllerInteractionIntent.BeginVirtualDialPress();
    private static readonly ControllerInteractionIntent
        EndVirtualDialPressIntent =
            new ControllerInteractionIntent.EndVirtualDialPress();
    private static readonly ControllerInteractionIntent
        NavigateSidebarLeftIntent =
            new ControllerInteractionIntent.NavigateSidebarHorizontal(-1);
    private static readonly ControllerInteractionIntent
        NavigateSidebarRightIntent =
            new ControllerInteractionIntent.NavigateSidebarHorizontal(1);
    private static readonly ControllerInteractionIntent OpenActionPanelIntent =
        new ControllerInteractionIntent.OpenActionPanel();
    private static readonly ControllerInteractionIntent
        SelectVirtualDialOptionIntent =
            new ControllerInteractionIntent.SelectVirtualDialOption();
    private static readonly ControllerInteractionIntent
        OpenSelectedSidebarTaskIntent =
            new ControllerInteractionIntent.OpenSelectedSidebarTask();
    private static readonly ControllerInteractionIntent SendPromptIntent =
        new ControllerInteractionIntent.SendPrompt();
    private static readonly ControllerInteractionIntent
        BeginBaseCancelPressIntent =
            new ControllerInteractionIntent.BeginBaseCancelPress();
    private static readonly ControllerInteractionIntent
        EndBaseCancelPressIntent =
            new ControllerInteractionIntent.EndBaseCancelPress();

    private readonly ControllerStateBuffer _stateBuffer;
    private readonly AxisRepeater _axisRepeater;
    private readonly StickGestureRouter _leftStickRouter = new();
    private readonly StickGestureRouter _rightStickRouter = new();
    private readonly AnalogTriggerLatch _pushToTalkTrigger;
    private ControllerButtons _previousBaseButtons;
    private ControllerButtons _previousPhysicalButtons;

    public ControllerInteractionCoordinator(
        Func<long>? tickProvider = null,
        int stateBufferCapacity = ControllerStateBuffer.DefaultCapacity)
    {
        _stateBuffer = new ControllerStateBuffer(stateBufferCapacity);
        _axisRepeater = new AxisRepeater(tickProvider);
        _pushToTalkTrigger = new AnalogTriggerLatch(
            BridgeTimings.PushToTalkEngageThreshold,
            BridgeTimings.PushToTalkReleaseThreshold);
    }

    public bool PushToTalkBlocksBaseInput =>
        _pushToTalkTrigger.BlocksBaseInput;

    public bool EnqueueState(ControllerState state) =>
        _stateBuffer.Enqueue(state);

    public ControllerState[] DrainStates() =>
        _stateBuffer.Drain();

    public ControllerButtonEdges PhysicalEdges(
        ControllerButtons current) =>
        new(
            current & ~_previousPhysicalButtons,
            _previousPhysicalButtons & ~current);

    private ControllerButtons BaseDownEdges(
        ControllerButtons current) =>
        current & ~_previousBaseButtons;

    public IReadOnlyList<ControllerInteractionIntent> ResolveBaseIntents(
        ControllerButtons basePressed,
        ControllerButtonEdges physicalEdges,
        bool dialContextActive)
    {
        List<ControllerInteractionIntent>? intents = null;
        if (BaseButtonTransition(
                basePressed,
                ControllerButtons.LeftThumb) ==
            ControllerButtonTransition.Pressed)
        {
            AddIntent(ref intents, CycleRootSidebarScopeIntent);
        }

        if (
            physicalEdges.Down.HasFlag(ControllerButtons.RightThumb) &&
            basePressed.HasFlag(ControllerButtons.RightThumb))
        {
            AddIntent(ref intents, BeginVirtualDialPressIntent);
        }

        if (physicalEdges.Up.HasFlag(ControllerButtons.RightThumb))
        {
            AddIntent(ref intents, EndVirtualDialPressIntent);
        }

        var conversationNavigation = ConversationTurnInputMap.Resolve(
            BaseDownEdges(basePressed));
        if (conversationNavigation != ConversationTurnInputAction.None)
        {
            AddIntent(
                ref intents,
                new ControllerInteractionIntent.NavigateConversationTurn(
                    conversationNavigation));
        }

        if (
            physicalEdges.Up.HasFlag(ControllerButtons.DPadUp) ||
            physicalEdges.Up.HasFlag(ControllerButtons.DPadDown))
        {
            AddIntent(
                ref intents,
                new ControllerInteractionIntent.EndConversationBoundaryHold(
                    physicalEdges.Up));
        }

        AddPressIntent(
            ref intents,
            basePressed,
            ControllerButtons.DPadLeft,
            NavigateSidebarLeftIntent);
        AddPressIntent(
            ref intents,
            basePressed,
            ControllerButtons.DPadRight,
            NavigateSidebarRightIntent);
        AddPressIntent(
            ref intents,
            basePressed,
            ControllerButtons.Y,
            OpenActionPanelIntent);
        AddPressIntent(
            ref intents,
            basePressed,
            ControllerButtons.A,
            dialContextActive
                ? SelectVirtualDialOptionIntent
                : OpenSelectedSidebarTaskIntent);
        AddPressIntent(
            ref intents,
            basePressed,
            ControllerButtons.X,
            SendPromptIntent);

        var cancelTransition = BaseButtonTransition(
            basePressed,
            ControllerButtons.B);
        if (cancelTransition == ControllerButtonTransition.Pressed)
        {
            AddIntent(ref intents, BeginBaseCancelPressIntent);
        }
        else if (cancelTransition == ControllerButtonTransition.Released)
        {
            AddIntent(ref intents, EndBaseCancelPressIntent);
        }

        return intents is null
            ? Array.Empty<ControllerInteractionIntent>()
            : intents;
    }

    public ControllerButtonTransition BaseButtonTransition(
        ControllerButtons current,
        ControllerButtons button)
    {
        var isPressed = current.HasFlag(button);
        var wasPressed = _previousBaseButtons.HasFlag(button);
        if (isPressed == wasPressed)
        {
            return ControllerButtonTransition.None;
        }

        return isPressed
            ? ControllerButtonTransition.Pressed
            : ControllerButtonTransition.Released;
    }

    public void CommitButtonHistory(
        ControllerButtons baseButtons,
        ControllerButtons physicalButtons)
    {
        _previousBaseButtons = baseButtons;
        _previousPhysicalButtons = physicalButtons;
    }

    public void ClearButtonHistory() =>
        CommitButtonHistory(
            ControllerButtons.None,
            ControllerButtons.None);

    public AnalogTriggerTransition UpdatePushToTalk(
        double value,
        bool blocked) =>
        _pushToTalkTrigger.Update(value, blocked);

    public bool CancelPushToTalkUntilReleased() =>
        _pushToTalkTrigger.CancelUntilReleased();

    public StickGestureSample UpdateLeftStick(
        double x,
        double y,
        double deadZone,
        bool blocked) =>
        _leftStickRouter.Update(
            x,
            y,
            deadZone,
            invertVertical: true,
            blocked);

    public StickGestureSample UpdateRightStick(
        double x,
        double y,
        double deadZone,
        bool blocked) =>
        _rightStickRouter.Update(
            x,
            y,
            deadZone,
            invertVertical: false,
            blocked);

    public void RepeatAxis(
        string id,
        int direction,
        int repeatDelayMs,
        int repeatIntervalMs,
        Action<int> action) =>
        _axisRepeater.Update(
            id,
            direction,
            repeatDelayMs,
            repeatIntervalMs,
            action);

    public void RepeatAnalogAxis(
        string id,
        int direction,
        double magnitude,
        double engageDeadZone,
        int repeatDelayMs,
        int repeatIntervalMs,
        Action<int> action) =>
        _axisRepeater.UpdateAnalog(
            id,
            direction,
            magnitude,
            engageDeadZone,
            repeatDelayMs,
            repeatIntervalMs,
            action);

    public void ResetRouting()
    {
        _axisRepeater.Reset();
        _leftStickRouter.Reset();
        _rightStickRouter.Reset();
    }

    public void RequireNeutralRouting()
    {
        _axisRepeater.Reset();
        _leftStickRouter.RequireNeutral();
        _rightStickRouter.RequireNeutral();
    }

    public void ResetRepeats() =>
        _axisRepeater.Reset();

    public void RequireLeftStickNeutral() =>
        _leftStickRouter.RequireNeutral();

    public void RequireRightStickNeutral() =>
        _rightStickRouter.RequireNeutral();

    private void AddPressIntent(
        ref List<ControllerInteractionIntent>? intents,
        ControllerButtons current,
        ControllerButtons button,
        ControllerInteractionIntent intent)
    {
        if (BaseButtonTransition(current, button) ==
            ControllerButtonTransition.Pressed)
        {
            AddIntent(ref intents, intent);
        }
    }

    private static void AddIntent(
        ref List<ControllerInteractionIntent>? intents,
        ControllerInteractionIntent intent)
    {
        intents ??= new List<ControllerInteractionIntent>(4);
        intents.Add(intent);
    }
}
