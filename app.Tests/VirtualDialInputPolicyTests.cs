using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class VirtualDialInputPolicyTests
{
    [Theory]
    [InlineData(0.58, 0.24, 0.30)]
    [InlineData(0.58, 0.36, 0.36)]
    [InlineData(0.35, null, 0.35)]
    [InlineData(0.25, 0.40, 0.25)]
    public void ResolvesAResponsiveDiscreteDeadZone(
        double userDeadZone,
        double? profileDeadZone,
        double expected)
    {
        Assert.Equal(
            expected,
            VirtualDialInputPolicy.ResolveDeadZone(
                userDeadZone,
                profileDeadZone),
            precision: 6);
    }

    [Theory]
    [InlineData(true, 1, true)]
    [InlineData(true, -1, true)]
    [InlineData(false, 1, false)]
    [InlineData(false, -1, false)]
    [InlineData(true, 0, false)]
    public void QueuesOnlyTheInitialDialDetent(
        bool horizontalStarted,
        int direction,
        bool expected)
    {
        Assert.Equal(
            expected,
            VirtualDialInputPolicy.ShouldQueueStep(
                horizontalStarted,
                direction));
    }
}
