using CodexController.Models;

namespace CodexController.Controllers;

[Flags]
internal enum RadialLayerEffect
{
    None = 0,
    StopLearningTimer = 1 << 0,
    StartLearningTimer = 1 << 1,
    RefreshMenu = 1 << 2,
    RefreshAgentData = 1 << 3,
    EndBaseCancelPress = 1 << 4,
    StopDictationPhysical = 1 << 5,
    StopDictationSynthetic = 1 << 6,
    HideMenu = 1 << 7,
    PresentCanceled = 1 << 8,
    OpenPreviousTask = 1 << 9,
    OpenNextTask = 1 << 10,
    ActionPanelOpened = 1 << 11,
    ActionPanelClosed = 1 << 12,
    ExecuteFollowUpAction = 1 << 13,
    AcknowledgeAction = 1 << 14,
}

internal readonly record struct RadialLayerUpdate(
    ControllerButtons FrozenButtons,
    RadialLayerEffect Effects = RadialLayerEffect.None,
    RadialInputAction Action = RadialInputAction.None);

internal sealed class RadialLayerCoordinator : IDisposable
{
    private readonly RadialMenuInteractionState _interaction = new();
    private readonly RadialActionConfirmationState _confirmation = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _confirmationCancellation;
    private bool _disposed;

    internal RadialLayerCoordinator(
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? Task.Delay;
    }

    internal RadialMenuLayerKind? Layer { get; private set; }

    internal bool IsEngaged { get; private set; }

    internal bool IsCancelled { get; private set; }

    internal bool IsPushToTalkActive { get; private set; }

    internal bool IsRightTriggerCandidate { get; private set; }

    internal string? HighlightedItemId { get; private set; }

    internal ControllerButtons SuppressedButtons { get; private set; }

    internal RadialMenuInteractionPhase InteractionPhase =>
        _interaction.Phase;

    internal void DrainSuppressedButtons(ControllerButtons pressed) =>
        SuppressedButtons &= pressed;

    internal void SuppressButtons(ControllerButtons buttons) =>
        SuppressedButtons |= buttons;

    internal void ClearRightTriggerCandidate() =>
        IsRightTriggerCandidate = false;

    internal RadialLayerUpdate ProcessFrame(
        ControllerState state,
        ControllerButtonEdges edges,
        long now)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pressed = state.Buttons;
        var effects = RadialLayerEffect.None;

        if (Layer is null)
        {
            if (!IsRightTriggerCandidate &&
                RadialInputMap.IsTurnCandidate(state.RightTrigger))
            {
                IsRightTriggerCandidate = true;
            }

            if (IsRightTriggerCandidate)
            {
                if (state.RightTrigger <=
                    RadialInputMap.TurnCandidateReleaseThreshold)
                {
                    SuppressButtons(
                        pressed &
                        RadialInputMap.FrozenTurnCandidateButtons);
                    IsRightTriggerCandidate = false;
                    return new(
                        RadialInputMap.FrozenTurnCandidateButtons);
                }

                if (RadialInputMap.CanAcceptTurnAction(
                        state.RightTrigger))
                {
                    effects |= BeginCore(
                        RadialMenuLayerKind.Turn,
                        now);
                }
                else
                {
                    return new(
                        RadialInputMap.FrozenTurnCandidateButtons);
                }
            }
            else if (edges.Down.HasFlag(
                         ControllerButtons.RightShoulder))
            {
                effects |= BeginCore(
                    RadialMenuLayerKind.Command,
                    now);
            }
            else if (edges.Down.HasFlag(
                         ControllerButtons.LeftShoulder))
            {
                effects |= BeginCore(
                    RadialMenuLayerKind.Agent,
                    now);
            }
        }

        if (Layer is not { } layer)
        {
            return new(ControllerButtons.None, effects);
        }

        if (layer == RadialMenuLayerKind.Agent &&
            !pressed.HasFlag(ControllerButtons.LeftShoulder))
        {
            effects |= CompleteShoulderCore(
                direction: -1,
                pressed,
                now);
            return new(RadialInputMap.FrozenBaseButtons, effects);
        }

        if (layer == RadialMenuLayerKind.Command &&
            !pressed.HasFlag(ControllerButtons.RightShoulder))
        {
            effects |= CompleteShoulderCore(
                direction: 1,
                pressed,
                now);
            return new(RadialInputMap.FrozenBaseButtons, effects);
        }

        if (layer == RadialMenuLayerKind.Turn &&
            edges.Up.HasFlag(ControllerButtons.B))
        {
            effects |= RadialLayerEffect.EndBaseCancelPress;
        }

        if (layer == RadialMenuLayerKind.Turn &&
            state.RightTrigger <= RadialInputMap.TurnReleaseThreshold)
        {
            effects |= EndCore(pressed);
            return new(RadialInputMap.FrozenBaseButtons, effects);
        }

        if (layer == RadialMenuLayerKind.Command &&
            IsPushToTalkActive &&
            edges.Up.HasFlag(ControllerButtons.Back))
        {
            IsPushToTalkActive = false;
            effects |= RadialLayerEffect.StopDictationPhysical;
        }

        if (IsPushToTalkActive || IsCancelled)
        {
            return new(RadialInputMap.FrozenBaseButtons, effects);
        }

        var action = RadialInputMap.Resolve(layer, edges.Down);
        if (action == RadialInputAction.None)
        {
            return new(RadialInputMap.FrozenBaseButtons, effects);
        }

        if (action == RadialInputAction.Cancel)
        {
            if (layer == RadialMenuLayerKind.Action)
            {
                effects |= CloseActionPanelCore(pressed);
            }
            else
            {
                effects |= CancelCore();
            }

            return new(RadialInputMap.FrozenBaseButtons, effects);
        }

        if (_confirmation.CancelUnless(action))
        {
            CancelConfirmationTimer();
        }

        var actionId = RadialInputMap.ActionId(action, Layer);
        if (KeepsLayerOpenForFollowUp(action))
        {
            MarkAction(actionId);
            effects |=
                RadialLayerEffect.StopLearningTimer |
                RadialLayerEffect.RefreshMenu |
                RadialLayerEffect.ExecuteFollowUpAction;
            return new(
                RadialInputMap.FrozenBaseButtons,
                effects,
                action);
        }

        if (!_interaction.TryAcceptInput(actionId))
        {
            return new(RadialInputMap.FrozenBaseButtons, effects);
        }

        MarkAction(actionId);
        _interaction.TryBeginWaiting();
        effects |=
            RadialLayerEffect.StopLearningTimer |
            RadialLayerEffect.AcknowledgeAction;
        return new(
            RadialInputMap.FrozenBaseButtons,
            effects,
            action);
    }

    internal RadialLayerUpdate PromoteLearningCue(
        ControllerButtons pressed)
    {
        var effects = RadialLayerEffect.StopLearningTimer;
        if (Layer is not (
                RadialMenuLayerKind.Agent or
                RadialMenuLayerKind.Command) ||
            IsCancelled)
        {
            return new(ControllerButtons.None, effects);
        }

        var modifierHeld = Layer == RadialMenuLayerKind.Agent
            ? pressed.HasFlag(ControllerButtons.LeftShoulder)
            : pressed.HasFlag(ControllerButtons.RightShoulder);
        if (modifierHeld)
        {
            IsEngaged = true;
            effects |= RadialLayerEffect.RefreshMenu;
        }

        return new(ControllerButtons.None, effects);
    }

    internal RadialLayerUpdate ToggleActionPanel(
        ControllerButtons pressed,
        long now)
    {
        if (Layer == RadialMenuLayerKind.Action)
        {
            return new(
                ControllerButtons.None,
                CloseActionPanelCore(pressed));
        }

        if (Layer is not null)
        {
            return default;
        }

        return new(
            ControllerButtons.None,
            BeginCore(RadialMenuLayerKind.Action, now) |
            RadialLayerEffect.ActionPanelOpened);
    }

    internal RadialLayerUpdate End(ControllerButtons pressed) =>
        new(
            ControllerButtons.None,
            EndCore(pressed));

    internal RadialLayerUpdate Reset(
        bool clearSuppression,
        bool preserveInputAcknowledgement = false) =>
        new(
            ControllerButtons.None,
            ResetCore(
                clearSuppression,
                preserveInputAcknowledgement));

    internal bool TryStartPushToTalk()
    {
        if (IsPushToTalkActive)
        {
            return false;
        }

        IsPushToTalkActive = true;
        return true;
    }

    internal bool IsConfirmationPending(RadialInputAction action) =>
        _confirmation.IsPending(action);

    internal bool TryConfirmAction(
        RadialInputAction action,
        string actionId,
        TimeSpan timeout,
        Action expired)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentNullException.ThrowIfNull(expired);
        if (_confirmation.TryConfirm(action))
        {
            CancelConfirmationTimer();
            return true;
        }

        HighlightedItemId = actionId;
        CancelConfirmationTimer();
        var cancellation = new CancellationTokenSource();
        _confirmationCancellation = cancellation;
        _ = ExpireConfirmationAsync(
            action,
            timeout,
            expired,
            cancellation);
        return false;
    }

    internal void CancelConfirmation()
    {
        _confirmation.Reset();
        CancelConfirmationTimer();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelConfirmation();
    }

    private RadialLayerEffect BeginCore(
        RadialMenuLayerKind layer,
        long now)
    {
        if (Layer is not null)
        {
            return RadialLayerEffect.None;
        }

        Layer = layer;
        IsRightTriggerCandidate = false;
        LayerStartedAt = now;
        IsEngaged = layer is
            RadialMenuLayerKind.Turn or
            RadialMenuLayerKind.Action;
        IsCancelled = false;
        ActionTriggered = false;
        IsPushToTalkActive = false;
        HighlightedItemId = null;
        _interaction.Reset();

        var effects =
            RadialLayerEffect.StopLearningTimer |
            RadialLayerEffect.RefreshMenu;
        if (layer is
            RadialMenuLayerKind.Agent or
            RadialMenuLayerKind.Command)
        {
            effects |= RadialLayerEffect.StartLearningTimer;
        }

        if (layer == RadialMenuLayerKind.Agent)
        {
            effects |= RadialLayerEffect.RefreshAgentData;
        }

        return effects;
    }

    private long LayerStartedAt { get; set; }

    private bool ActionTriggered { get; set; }

    private RadialLayerEffect CompleteShoulderCore(
        int direction,
        ControllerButtons pressed,
        long now)
    {
        var shouldMoveTask =
            !IsEngaged &&
            !IsCancelled &&
            !ActionTriggered &&
            now - LayerStartedAt < RadialInputMap.LearningDelayMs;
        var effects = EndCore(pressed);
        if (shouldMoveTask)
        {
            effects |= direction < 0
                ? RadialLayerEffect.OpenPreviousTask
                : RadialLayerEffect.OpenNextTask;
        }

        return effects;
    }

    private RadialLayerEffect CancelCore()
    {
        IsCancelled = true;
        ActionTriggered = true;
        var effects =
            RadialLayerEffect.StopLearningTimer |
            RadialLayerEffect.HideMenu |
            RadialLayerEffect.PresentCanceled;
        if (IsPushToTalkActive)
        {
            IsPushToTalkActive = false;
            effects |= RadialLayerEffect.StopDictationSynthetic;
        }

        return effects;
    }

    private RadialLayerEffect CloseActionPanelCore(
        ControllerButtons pressed)
    {
        SuppressButtons(
            pressed & RadialInputMap.FrozenBaseButtons);
        return ResetCore(clearSuppression: false) |
            RadialLayerEffect.ActionPanelClosed;
    }

    private RadialLayerEffect EndCore(ControllerButtons pressed)
    {
        SuppressButtons(
            pressed & RadialInputMap.FrozenBaseButtons);
        return ResetCore(
            clearSuppression: false,
            preserveInputAcknowledgement: true);
    }

    private RadialLayerEffect ResetCore(
        bool clearSuppression,
        bool preserveInputAcknowledgement = false)
    {
        var allowAcknowledgementToFinish =
            preserveInputAcknowledgement &&
            _interaction.Phase ==
                RadialMenuInteractionPhase.WaitingForResponse;
        var effects = RadialLayerEffect.StopLearningTimer;
        if (IsPushToTalkActive)
        {
            effects |= RadialLayerEffect.StopDictationSynthetic;
        }

        Layer = null;
        IsRightTriggerCandidate = false;
        IsEngaged = false;
        IsCancelled = false;
        ActionTriggered = false;
        IsPushToTalkActive = false;
        LayerStartedAt = 0;
        HighlightedItemId = null;
        _interaction.Reset();
        CancelConfirmation();
        if (clearSuppression)
        {
            SuppressedButtons = ControllerButtons.None;
        }

        if (!allowAcknowledgementToFinish)
        {
            effects |= RadialLayerEffect.HideMenu;
        }

        return effects;
    }

    private void MarkAction(string actionId)
    {
        ActionTriggered = true;
        IsEngaged = true;
        HighlightedItemId = actionId;
    }

    private bool KeepsLayerOpenForFollowUp(
        RadialInputAction action) =>
        action == RadialInputAction.PushToTalk ||
        action == RadialInputAction.BeginStopHold ||
        (RequiresSecondPress(action) &&
         !_confirmation.IsPending(action));

    private static bool RequiresSecondPress(
        RadialInputAction action) =>
        action is
            RadialInputAction.Approve or
            RadialInputAction.ClearComposer;

    private async Task ExpireConfirmationAsync(
        RadialInputAction action,
        TimeSpan timeout,
        Action expired,
        CancellationTokenSource cancellation)
    {
        try
        {
            await _delay(timeout, cancellation.Token)
                .ConfigureAwait(true);
            if (cancellation.IsCancellationRequested ||
                !ReferenceEquals(
                    _confirmationCancellation,
                    cancellation) ||
                !_confirmation.TryExpire(action))
            {
                return;
            }

            HighlightedItemId = null;
            _confirmationCancellation = null;
            expired();
        }
        catch (OperationCanceledException)
        {
            // Another action or panel close owns the current state.
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void CancelConfirmationTimer()
    {
        var cancellation = _confirmationCancellation;
        _confirmationCancellation = null;
        cancellation?.Cancel();
    }

}
