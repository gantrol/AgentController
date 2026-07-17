using CodexController.Controllers;
using CodexController.Models;

namespace CodexController.Tests;

public sealed class BridgeInputGateTests
{
    [Fact]
    public void NeutralControllerHasNoControlIntent()
    {
        Assert.False(BridgeInputGate.HasControlIntent(
            ControllerState.Disconnected with
            {
                IsConnected = true,
            },
            stickDeadZone: 0.58));
    }

    [Theory]
    [InlineData(ControllerButtons.Start)]
    [InlineData(ControllerButtons.Y)]
    [InlineData(ControllerButtons.RightThumb)]
    public void EveryButtonIncludingMenuIsControlIntent(
        ControllerButtons button)
    {
        var state = ControllerState.Disconnected with
        {
            IsConnected = true,
            Buttons = button,
        };

        Assert.True(BridgeInputGate.HasControlIntent(
            state,
            stickDeadZone: 0.58));
    }

    [Fact]
    public void AnalogIntentIgnoresNoiseButDetectsRealMovement()
    {
        var noise = ControllerState.Disconnected with
        {
            IsConnected = true,
            RightX = 0.20,
            LeftTrigger = 0.08,
        };
        var stick = noise with { RightX = 0.72 };
        var trigger = noise with { LeftTrigger = 0.35 };

        Assert.False(BridgeInputGate.HasControlIntent(
            noise,
            stickDeadZone: 0.58));
        Assert.True(BridgeInputGate.HasControlIntent(
            stick,
            stickDeadZone: 0.58));
        Assert.True(BridgeInputGate.HasControlIntent(
            trigger,
            stickDeadZone: 0.58));
    }
}
