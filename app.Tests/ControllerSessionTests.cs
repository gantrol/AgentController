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
    public void ForegroundAgentAutomaticallyArmsConnectedController()
    {
        var session = new ControllerSession();

        var armed = session.TryAutoArm(
            bridgeEnabled: true,
            controllerConnected: true,
            agentForeground: true);

        Assert.True(armed);
        Assert.Equal(
            ControllerSessionPhase.WaitingForNeutral,
            session.Phase);
        Assert.False(session.TryActivate(isNeutral: false));
        Assert.True(session.TryActivate(isNeutral: true));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void AutoArmRequiresBridgeControllerAndForeground(
        bool bridgeEnabled,
        bool controllerConnected,
        bool agentForeground)
    {
        var session = new ControllerSession();

        var armed = session.TryAutoArm(
            bridgeEnabled,
            controllerConnected,
            agentForeground);

        Assert.False(armed);
        Assert.Equal(ControllerSessionPhase.Locked, session.Phase);
    }

    [Fact]
    public void AutoArmDoesNotResetExistingActiveSession()
    {
        var session = new ControllerSession();
        session.Arm();
        session.TryActivate(isNeutral: true);

        var armed = session.TryAutoArm(
            bridgeEnabled: true,
            controllerConnected: true,
            agentForeground: true);

        Assert.False(armed);
        Assert.Equal(ControllerSessionPhase.Active, session.Phase);
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
