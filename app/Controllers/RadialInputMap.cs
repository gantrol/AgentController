using CodexController.Models;

namespace CodexController.Controllers;

public enum RadialInputAction
{
    None,
    Cancel,
    AgentSlot1,
    AgentSlot2,
    AgentSlot3,
    AgentSlot4,
    AgentSlot5,
    AgentSlot6,
    ToggleFast,
    Approve,
    Decline,
    Fork,
    PushToTalk,
    Dispatch,
    Steer,
    Queue,
    StopTurn,
    NewTask,
    NavigateForward,
    ToggleSidebar,
    NavigateBack,
    ClearComposer,
    ProjectContext,
}

public static class RadialInputMap
{
    public const int LearningDelayMs = 180;
    public const double TurnCandidateThreshold = 0.12;
    public const double TurnCandidateReleaseThreshold = 0.08;
    public const double TurnEngageThreshold = 0.55;
    public const double TurnReleaseThreshold = 0.35;

    public const ControllerButtons FrozenTurnCandidateButtons =
        ControllerButtons.A |
        ControllerButtons.B |
        ControllerButtons.X |
        ControllerButtons.Y;

    public const ControllerButtons FrozenBaseButtons =
        ControllerButtons.DPadUp |
        ControllerButtons.DPadDown |
        ControllerButtons.DPadLeft |
        ControllerButtons.DPadRight |
        ControllerButtons.Start |
        ControllerButtons.Back |
        ControllerButtons.LeftThumb |
        ControllerButtons.RightThumb |
        ControllerButtons.A |
        ControllerButtons.B |
        ControllerButtons.X |
        ControllerButtons.Y;

    public static RadialInputAction Resolve(
        RadialMenuLayerKind layer,
        ControllerButtons downEdges)
    {
        return layer switch
        {
            RadialMenuLayerKind.Agent =>
                ResolveAgent(downEdges),
            RadialMenuLayerKind.Command =>
                ResolveCommand(downEdges),
            RadialMenuLayerKind.Turn =>
                ResolveTurn(downEdges),
            RadialMenuLayerKind.Action =>
                ResolveAction(downEdges),
            _ => RadialInputAction.None,
        };
    }

    public static int AgentSlotIndex(RadialInputAction action)
    {
        return action switch
        {
            RadialInputAction.AgentSlot1 => 0,
            RadialInputAction.AgentSlot2 => 1,
            RadialInputAction.AgentSlot3 => 2,
            RadialInputAction.AgentSlot4 => 3,
            RadialInputAction.AgentSlot5 => 4,
            RadialInputAction.AgentSlot6 => 5,
            _ => -1,
        };
    }

    public static bool IsTurnCandidate(double triggerValue)
    {
        return triggerValue >= TurnCandidateThreshold;
    }

    public static bool CanAcceptTurnAction(double triggerValue)
    {
        return triggerValue >= TurnEngageThreshold;
    }

    private static RadialInputAction ResolveAgent(
        ControllerButtons downEdges)
    {
        if (downEdges.HasFlag(ControllerButtons.B))
            return RadialInputAction.Cancel;
        if (downEdges.HasFlag(ControllerButtons.DPadUp))
            return RadialInputAction.AgentSlot1;
        if (downEdges.HasFlag(ControllerButtons.DPadRight))
            return RadialInputAction.AgentSlot2;
        if (downEdges.HasFlag(ControllerButtons.DPadDown))
            return RadialInputAction.AgentSlot3;
        if (downEdges.HasFlag(ControllerButtons.DPadLeft))
            return RadialInputAction.AgentSlot4;
        if (downEdges.HasFlag(ControllerButtons.Back))
            return RadialInputAction.AgentSlot5;
        if (downEdges.HasFlag(ControllerButtons.Start))
            return RadialInputAction.AgentSlot6;
        return RadialInputAction.None;
    }

    private static RadialInputAction ResolveCommand(
        ControllerButtons downEdges)
    {
        if (downEdges.HasFlag(ControllerButtons.LeftThumb))
            return RadialInputAction.Cancel;
        if (downEdges.HasFlag(ControllerButtons.Y))
            return RadialInputAction.ToggleFast;
        if (downEdges.HasFlag(ControllerButtons.A))
            return RadialInputAction.Approve;
        if (downEdges.HasFlag(ControllerButtons.B))
            return RadialInputAction.Decline;
        if (downEdges.HasFlag(ControllerButtons.X))
            return RadialInputAction.Fork;
        if (downEdges.HasFlag(ControllerButtons.Back))
            return RadialInputAction.PushToTalk;
        if (downEdges.HasFlag(ControllerButtons.Start))
            return RadialInputAction.Dispatch;
        return RadialInputAction.None;
    }

    private static RadialInputAction ResolveTurn(
        ControllerButtons downEdges)
    {
        if (downEdges.HasFlag(ControllerButtons.X))
            return RadialInputAction.Steer;
        if (downEdges.HasFlag(ControllerButtons.Y))
            return RadialInputAction.Queue;
        if (downEdges.HasFlag(ControllerButtons.B))
            return RadialInputAction.StopTurn;
        if (downEdges.HasFlag(ControllerButtons.A))
            return RadialInputAction.Fork;
        return RadialInputAction.None;
    }

    private static RadialInputAction ResolveAction(
        ControllerButtons downEdges)
    {
        if (
            downEdges.HasFlag(ControllerButtons.B) ||
            downEdges.HasFlag(ControllerButtons.Y))
        {
            return RadialInputAction.Cancel;
        }

        if (downEdges.HasFlag(ControllerButtons.DPadUp))
            return RadialInputAction.NewTask;
        if (downEdges.HasFlag(ControllerButtons.DPadRight))
            return RadialInputAction.NavigateForward;
        if (downEdges.HasFlag(ControllerButtons.DPadDown))
            return RadialInputAction.ToggleSidebar;
        if (downEdges.HasFlag(ControllerButtons.DPadLeft))
            return RadialInputAction.NavigateBack;
        if (downEdges.HasFlag(ControllerButtons.A))
            return RadialInputAction.ClearComposer;
        if (downEdges.HasFlag(ControllerButtons.X))
            return RadialInputAction.ProjectContext;
        return RadialInputAction.None;
    }
}
