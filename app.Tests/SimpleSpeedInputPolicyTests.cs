using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class SimpleSpeedInputPolicyTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    public void FollowsLiveSubmenuVerticalOrder(
        int direction,
        bool expectedFast)
    {
        Assert.Equal(
            expectedFast,
            SimpleSpeedInputPolicy.ResolveFastTarget(direction));
    }

    [Fact]
    public void NeutralDoesNotChooseSpeed()
    {
        Assert.Null(
            SimpleSpeedInputPolicy.ResolveFastTarget(0));
    }
}
