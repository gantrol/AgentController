using CodexController.Models;

namespace CodexController.Controllers;

/// <summary>
/// Describes an ordered controller intent without binding it to WPF controls,
/// Codex automation, or a concrete application action implementation.
/// </summary>
public abstract record ControllerInteractionIntent
{
    private ControllerInteractionIntent()
    {
    }

    public sealed record CycleRootSidebarScope :
        ControllerInteractionIntent;

    public sealed record BeginVirtualDialPress :
        ControllerInteractionIntent;

    public sealed record EndVirtualDialPress :
        ControllerInteractionIntent;

    public sealed record NavigateConversationTurn(
        ConversationTurnInputAction Action) :
        ControllerInteractionIntent;

    public sealed record EndConversationBoundaryHold(
        ControllerButtons ReleasedButtons) :
        ControllerInteractionIntent;

    public sealed record NavigateSidebarHorizontal(
        int Direction) :
        ControllerInteractionIntent;

    public sealed record OpenActionPanel :
        ControllerInteractionIntent;

    public sealed record SelectVirtualDialOption :
        ControllerInteractionIntent;

    public sealed record OpenSelectedSidebarTask :
        ControllerInteractionIntent;

    public sealed record SendPrompt :
        ControllerInteractionIntent;

    public sealed record BeginBaseCancelPress :
        ControllerInteractionIntent;

    public sealed record EndBaseCancelPress :
        ControllerInteractionIntent;
}
