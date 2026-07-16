using CodexController.Core.Bridge;

namespace CodexController.Tests;

public sealed class ForegroundContinuityGateTests
{
    [Fact]
    public void TransientForegroundLossRemainsWithinGrace()
    {
        var gate = new ForegroundContinuityGate();

        Assert.True(gate.AllowsInput(
            isForeground: true,
            timestampMilliseconds: 1000,
            graceMilliseconds: 300));
        Assert.True(gate.AllowsInput(
            isForeground: false,
            timestampMilliseconds: 1100,
            graceMilliseconds: 300));
        Assert.True(gate.AllowsInput(
            isForeground: true,
            timestampMilliseconds: 1200,
            graceMilliseconds: 300));
    }

    [Fact]
    public void SustainedForegroundLossEventuallyPauses()
    {
        var gate = new ForegroundContinuityGate();

        Assert.True(gate.AllowsInput(
            isForeground: false,
            timestampMilliseconds: 1000,
            graceMilliseconds: 300));
        Assert.False(gate.AllowsInput(
            isForeground: false,
            timestampMilliseconds: 1300,
            graceMilliseconds: 300));
        Assert.False(gate.AllowsInput(
            isForeground: false,
            timestampMilliseconds: 1600,
            graceMilliseconds: 300));
    }

    [Fact]
    public void WaitNoticeAppearsOnceUntilForegroundStablyReturns()
    {
        var gate = new ForegroundContinuityGate();

        Assert.True(gate.TryPresentWaitNotice());
        Assert.False(gate.TryPresentWaitNotice());

        _ = gate.AllowsInput(
            isForeground: true,
            timestampMilliseconds: 1000,
            graceMilliseconds: 300);
        Assert.False(gate.TryPresentWaitNotice());

        _ = gate.AllowsInput(
            isForeground: true,
            timestampMilliseconds: 1300,
            graceMilliseconds: 300);

        Assert.True(gate.TryPresentWaitNotice());
    }

    [Fact]
    public void TransientForegroundRecoveryDoesNotRepeatWaitNotice()
    {
        var gate = new ForegroundContinuityGate();

        _ = gate.AllowsInput(
            isForeground: false,
            timestampMilliseconds: 1000,
            graceMilliseconds: 300);
        Assert.True(gate.TryPresentWaitNotice());

        _ = gate.AllowsInput(
            isForeground: true,
            timestampMilliseconds: 1400,
            graceMilliseconds: 300);
        _ = gate.AllowsInput(
            isForeground: false,
            timestampMilliseconds: 1450,
            graceMilliseconds: 300);

        Assert.False(gate.TryPresentWaitNotice());
    }
}
