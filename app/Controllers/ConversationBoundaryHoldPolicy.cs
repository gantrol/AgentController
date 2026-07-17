using CodexController.Models;

namespace CodexController.Controllers;

internal static class ConversationBoundaryHoldPolicy
{
    internal static ConversationBoundary ResolveBoundary(
        ConversationTurnInputAction action) =>
        action switch
        {
            ConversationTurnInputAction.PreviousUserMessage =>
                ConversationBoundary.Top,
            ConversationTurnInputAction.NextUserMessage =>
                ConversationBoundary.Bottom,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    internal static int ResolveHoldMilliseconds(
        ConversationBoundary boundary,
        int topHoldMs,
        int bottomHoldMs) =>
        boundary == ConversationBoundary.Top
            ? topHoldMs
            : bottomHoldMs;

    internal static ControllerButtons ResolveButton(
        ConversationBoundary boundary) =>
        boundary == ConversationBoundary.Top
            ? ControllerButtons.DPadUp
            : ControllerButtons.DPadDown;
}
