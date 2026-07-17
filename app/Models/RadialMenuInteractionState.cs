namespace CodexController.Models;

/// <summary>
/// Keeps controller input acknowledgement separate from the slower agent
/// response path. One radial layer accepts at most one terminal command.
/// </summary>
public sealed class RadialMenuInteractionState
{
    public RadialMenuInteractionPhase Phase { get; private set; } =
        RadialMenuInteractionPhase.AwaitingInput;

    public string? ActionId { get; private set; }

    public bool TryAcceptInput(string actionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        if (Phase != RadialMenuInteractionPhase.AwaitingInput)
        {
            return false;
        }

        ActionId = actionId.Trim();
        Phase = RadialMenuInteractionPhase.InputAccepted;
        return true;
    }

    public bool TryBeginWaiting()
    {
        if (Phase != RadialMenuInteractionPhase.InputAccepted)
        {
            return false;
        }

        Phase = RadialMenuInteractionPhase.WaitingForResponse;
        return true;
    }

    public void Reset()
    {
        ActionId = null;
        Phase = RadialMenuInteractionPhase.AwaitingInput;
    }
}
