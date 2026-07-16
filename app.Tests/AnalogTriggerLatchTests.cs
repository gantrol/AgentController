using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class AnalogTriggerLatchTests
{
    [Fact]
    public void UsesEngageAndReleaseHysteresis()
    {
        var latch = new AnalogTriggerLatch(0.35, 0.20);

        Assert.Equal(
            AnalogTriggerTransition.None,
            latch.Update(0.34));
        Assert.Equal(
            AnalogTriggerTransition.Pressed,
            latch.Update(0.35));
        Assert.True(latch.IsPressed);
        Assert.Equal(
            AnalogTriggerTransition.None,
            latch.Update(0.21));
        Assert.Equal(
            AnalogTriggerTransition.Released,
            latch.Update(0.20));
        Assert.False(latch.IsPressed);
    }

    [Fact]
    public void BlockedTriggerMustReleaseBeforeReengaging()
    {
        var latch = new AnalogTriggerLatch(0.35, 0.20);

        Assert.Equal(
            AnalogTriggerTransition.Pressed,
            latch.Update(0.8));
        Assert.Equal(
            AnalogTriggerTransition.Canceled,
            latch.Update(0.8, blocked: true));
        Assert.True(latch.RequiresRelease);
        Assert.Equal(
            AnalogTriggerTransition.None,
            latch.Update(0.8));
        Assert.Equal(
            AnalogTriggerTransition.None,
            latch.Update(0.2));
        Assert.False(latch.RequiresRelease);
        Assert.Equal(
            AnalogTriggerTransition.Pressed,
            latch.Update(0.35));
    }

    [Fact]
    public void CancelSuppressesHeldTriggerUntilRelease()
    {
        var latch = new AnalogTriggerLatch(0.35, 0.20);
        latch.Update(0.9);

        Assert.True(latch.CancelUntilReleased());
        Assert.False(latch.IsPressed);
        Assert.True(latch.BlocksBaseInput);
        Assert.Equal(
            AnalogTriggerTransition.None,
            latch.Update(0.9));
        Assert.Equal(
            AnalogTriggerTransition.None,
            latch.Update(0));
        Assert.False(latch.BlocksBaseInput);
    }

    [Fact]
    public void PushToTalkPolicyKeepsCancelAndFreezesOtherFaceButtons()
    {
        var frozen = PushToTalkInputPolicy.FrozenBaseButtons;

        Assert.False(frozen.HasFlag(ControllerButtons.B));
        Assert.True(frozen.HasFlag(ControllerButtons.A));
        Assert.True(frozen.HasFlag(ControllerButtons.X));
        Assert.True(frozen.HasFlag(ControllerButtons.Y));
        Assert.Equal(
            ControllerButtons.A |
            ControllerButtons.X |
            ControllerButtons.Y,
            PushToTalkInputPolicy.ButtonsToSuppress(
                ControllerButtons.A |
                ControllerButtons.B |
                ControllerButtons.X |
                ControllerButtons.Y));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void PushToTalkIsBlockedOnlyByRadialChordLayers(
        bool radialLayerActive,
        bool expected)
    {
        Assert.Equal(
            expected,
            PushToTalkInputPolicy.ShouldBlockTrigger(
                radialLayerActive));
    }

    [Theory]
    [InlineData(0.34, false)]
    [InlineData(0.35, true)]
    [InlineData(1.00, true)]
    public void PushToTalkCanPreemptDialReleaseDrain(
        double triggerValue,
        bool expected)
    {
        Assert.Equal(
            expected,
            PushToTalkInputPolicy.ShouldPreemptDialReleaseDrain(
                triggerValue,
                engageThreshold: 0.35));
    }
}
