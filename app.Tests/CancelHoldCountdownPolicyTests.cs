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
}
