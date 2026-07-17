using CodexController.Models;

namespace CodexController.Tests;

public sealed class RadialMenuInteractionStateTests
{
    [Fact]
    public void AcceptsOneInputThenMovesIntoWaiting()
    {
        var interaction = new RadialMenuInteractionState();

        Assert.True(interaction.TryAcceptInput("command-dispatch"));
        Assert.Equal(
            RadialMenuInteractionPhase.InputAccepted,
            interaction.Phase);
        Assert.Equal("command-dispatch", interaction.ActionId);
        Assert.False(interaction.TryAcceptInput("command-fast"));

        Assert.True(interaction.TryBeginWaiting());
        Assert.Equal(
            RadialMenuInteractionPhase.WaitingForResponse,
            interaction.Phase);
        Assert.False(interaction.TryBeginWaiting());
    }

    [Fact]
    public void ResetStartsAnIndependentInputCycle()
    {
        var interaction = new RadialMenuInteractionState();
        interaction.TryAcceptInput("agent-slot-1");
        interaction.TryBeginWaiting();

        interaction.Reset();

        Assert.Equal(
            RadialMenuInteractionPhase.AwaitingInput,
            interaction.Phase);
        Assert.Null(interaction.ActionId);
        Assert.True(interaction.TryAcceptInput("agent-slot-2"));
    }
}
