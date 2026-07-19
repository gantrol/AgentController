using CodexController.Services;

namespace CodexController.Tests;

public sealed class CurrentControlIntentBufferTests
{
    [Fact]
    public void NewestIntentReplacesUnsentDirection()
    {
        var buffer = new CurrentControlIntentBuffer();
        buffer.Offer(ComposerDialNavigation.Left, 4, 100);

        buffer.Offer(ComposerDialNavigation.Right, 4, 110);

        Assert.Equal(
            ComposerDialNavigation.Right,
            buffer.PendingNavigation);
        var intent = buffer.Take(4, 110, 50);
        Assert.Equal(
            ComposerDialNavigation.Right,
            intent?.Navigation);
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void IntentCannotCrossDialGeneration()
    {
        var buffer = new CurrentControlIntentBuffer();
        buffer.Offer(ComposerDialNavigation.Right, 4, 100);

        Assert.Null(buffer.Take(5, 100, 50));
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void StaleIntentIsNotReplayedAfterReadback()
    {
        var buffer = new CurrentControlIntentBuffer();
        buffer.Offer(ComposerDialNavigation.Left, 4, 100);

        Assert.Null(buffer.Take(4, 151, 50));
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void ClearDropsPendingIntent()
    {
        var buffer = new CurrentControlIntentBuffer();
        buffer.Offer(ComposerDialNavigation.Left, 4, 100);

        buffer.Clear();

        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void RefreshRequestDuringWorkSchedulesOneFollowUpPass()
    {
        var gate = new CoalescingRequestGate();

        Assert.True(gate.Request());
        Assert.True(gate.TryConsume());
        Assert.False(gate.TryConsume());

        Assert.False(gate.Request());
        Assert.False(gate.Request());
        Assert.True(gate.Complete());
        Assert.True(gate.TryConsume());
        Assert.False(gate.TryConsume());
        Assert.False(gate.Complete());
    }

    [Fact]
    public void ClearingRefreshRequestPreventsRestart()
    {
        var gate = new CoalescingRequestGate();
        Assert.True(gate.Request());
        Assert.True(gate.TryConsume());
        Assert.False(gate.Request());

        gate.ClearPending();

        Assert.False(gate.Complete());
    }
}
