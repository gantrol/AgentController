using CodexController.Core.Bridge;

namespace CodexController.Tests;

public sealed class ControllerSessionTests
{
    [Fact]
    public void ArmRequiresNeutralBeforeActivation()
    {
        var session = new ControllerSession();

        session.Arm();

        Assert.Equal(
            ControllerSessionPhase.WaitingForNeutral,
            session.Phase);
        Assert.False(session.TryActivate(isNeutral: false));
        Assert.True(session.TryActivate(isNeutral: true));
        Assert.True(session.IsActive);
    }

    [Fact]
    public void ForegroundPauseDoesNotRevokeArmedSession()
    {
        var session = new ControllerSession();
        session.Arm();
        session.TryActivate(isNeutral: true);

        session.Pause(requireNeutral: false);

        Assert.True(session.IsArmed);
        Assert.Equal(
            ControllerSessionPhase.ArmedPaused,
            session.Phase);
        Assert.True(session.TryActivate(isNeutral: true));
    }

    [Fact]
    public void DisconnectPauseRequiresNeutralAfterReconnect()
    {
        var session = new ControllerSession();
        session.Arm();
        session.TryActivate(isNeutral: true);

        session.Pause(requireNeutral: true);

        Assert.False(session.TryActivate(isNeutral: false));
        Assert.True(session.TryActivate(isNeutral: true));
    }

    [Fact]
    public void LockRequiresExplicitArmAgain()
    {
        var session = new ControllerSession();
        session.Arm();
        session.TryActivate(isNeutral: true);

        session.Lock();

        Assert.False(session.IsArmed);
        Assert.False(session.TryActivate(isNeutral: true));
    }
}
