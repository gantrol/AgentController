using CodexController.Models;

namespace CodexController.Controllers;

public enum ControllerInteractionIntentKind
{
    CycleRootSidebarScope,
    BeginVirtualDialPress,
    EndVirtualDialPress,
    NavigateConversationTurn,
    EndConversationBoundaryHold,
    NavigateSidebarHorizontal,
    OpenActionPanel,
    SelectVirtualDialOption,
    OpenSelectedSidebarTask,
    SendPrompt,
    BeginBaseCancelPress,
    EndBaseCancelPress,
}

/// <summary>
/// An ordered, allocation-free controller intent. Payload fields are used only
/// by the corresponding intent kind.
/// </summary>
public readonly record struct ControllerInteractionIntent(
    ControllerInteractionIntentKind Kind,
    int Direction = 0,
    ConversationTurnInputAction ConversationAction =
        ConversationTurnInputAction.None,
    ControllerButtons ReleasedButtons = ControllerButtons.None);
