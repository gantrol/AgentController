namespace CodexController.Core.Bridge;

public enum ControllerSessionPhase
{
    Locked,
    ArmedPaused,
    WaitingForNeutral,
    Active,
}

/// <summary>
/// Tracks whether controller input is authorized independently from temporary
/// foreground and connection pauses. Foreground loss must never silently turn
/// an armed session back into Locked.
/// </summary>
public sealed class ControllerSession
{
    public ControllerSessionPhase Phase { get; private set; } =
        ControllerSessionPhase.Locked;

    public bool IsArmed => Phase != ControllerSessionPhase.Locked;

    public bool IsActive => Phase == ControllerSessionPhase.Active;

    public bool IsPaused => IsArmed && !IsActive;

    public void Lock()
    {
        Phase = ControllerSessionPhase.Locked;
    }

    public void Arm()
    {
        Phase = ControllerSessionPhase.WaitingForNeutral;
    }

    public bool TryAutoArm(
        bool bridgeEnabled,
        bool controllerConnected,
        bool agentForeground)
    {
        if (
            IsArmed ||
            !bridgeEnabled ||
            !controllerConnected ||
            !agentForeground)
        {
            return false;
        }

        Arm();
        return true;
    }

    public void Pause(bool requireNeutral)
    {
        if (!IsArmed)
        {
            return;
        }

        Phase = requireNeutral
            ? ControllerSessionPhase.WaitingForNeutral
            : ControllerSessionPhase.ArmedPaused;
    }

    public bool TryActivate(bool isNeutral)
    {
        if (!IsArmed)
        {
            return false;
        }

        if (
            Phase == ControllerSessionPhase.WaitingForNeutral &&
            !isNeutral)
        {
            return false;
        }

        Phase = ControllerSessionPhase.Active;
        return true;
    }
}
