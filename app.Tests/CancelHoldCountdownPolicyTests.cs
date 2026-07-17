using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class CancelHoldCountdownPolicyTests
{
    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    [InlineData(999, 3)]
    [InlineData(1000, 2)]
    [InlineData(1999, 2)]
    [InlineData(2000, 1)]
    [InlineData(2999, 1)]
    [InlineData(3000, 0)]
    public void ReportsThreeSecondCountdown(
        long elapsedMilliseconds,
        int expectedSeconds)
    {
        Assert.Equal(
            expectedSeconds,
            CancelHoldCountdownPolicy.RemainingSeconds(
                elapsedMilliseconds,
                holdMs: 3000));
    }

    [Theory]
    [InlineData(2999, false)]
    [InlineData(3000, true)]
    [InlineData(3001, true)]
    public void CompletesOnlyAtThreshold(
        long elapsedMilliseconds,
        bool expected)
    {
        Assert.Equal(
            expected,
            CancelHoldCountdownPolicy.IsComplete(
                elapsedMilliseconds,
                holdMs: 3000));
    }

    [Theory]
    [InlineData(
        false,
        false,
        (int)NavigationUndoPressAction.QueueUntilNavigationConfirms)]
    [InlineData(
        true,
        false,
        (int)NavigationUndoPressAction.QueueUntilNavigationConfirms)]
    [InlineData(
        true,
        true,
        (int)NavigationUndoPressAction.ExecuteUndo)]
    public void NavigationUndoShortPressNeverStopsAnActiveTurn(
        bool confirmed,
        bool hasExpiry,
        int expected)
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(
            (NavigationUndoPressAction)expected,
            NavigationUndoPressPolicy.Resolve(
                confirmed,
                hasExpiry ? now.AddSeconds(1) : null,
                now));
    }

    [Fact]
    public void ExpiredNavigationUndoFallsIntoStopHold()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(
            NavigationUndoPressAction.ExpireAndBeginStopHold,
            NavigationUndoPressPolicy.Resolve(
                confirmed: true,
                expiresAt: now.AddTicks(-1),
                now));
    }
}
