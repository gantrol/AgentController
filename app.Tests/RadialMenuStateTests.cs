using CodexController.Models;

namespace CodexController.Tests;

public sealed class RadialMenuStateTests
{
    [Theory]
    [InlineData(
        RadialMenuDisplayMode.Always,
        true,
        false,
        true)]
    [InlineData(
        RadialMenuDisplayMode.Always,
        false,
        true,
        false)]
    [InlineData(
        RadialMenuDisplayMode.Learning,
        true,
        false,
        false)]
    [InlineData(
        RadialMenuDisplayMode.Learning,
        true,
        true,
        true)]
    [InlineData(
        RadialMenuDisplayMode.Off,
        true,
        true,
        false)]
    public void VisibilityCombinesPolicyAndLayerState(
        RadialMenuDisplayMode displayMode,
        bool isLayerEngaged,
        bool isLearningCueReady,
        bool expected)
    {
        var state = CreateState(
            displayMode,
            isLayerEngaged,
            isLearningCueReady);

        Assert.Equal(expected, state.IsVisible);
    }

    [Fact]
    public void FixedPhysicalPositionsRejectDuplicates()
    {
        var items = new[]
        {
            Item("one", RadialMenuSlotPosition.Top),
            Item("two", RadialMenuSlotPosition.Top),
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            new RadialMenuState(
                RadialMenuLayerKind.Agent,
                "Agent slots",
                "LB",
                items));

        Assert.Contains("duplicated", exception.Message);
    }

    [Fact]
    public void WaitingForResponseKeepsWheelHidden()
    {
        var state = new RadialMenuState(
            RadialMenuLayerKind.Command,
            "Commands",
            "RB",
            [Item("send", RadialMenuSlotPosition.Left)],
            RadialMenuDisplayMode.Always,
            interactionPhase:
                RadialMenuInteractionPhase.WaitingForResponse);

        Assert.False(state.IsVisible);
    }

    [Fact]
    public void ItemNormalizesTextAndClampsConfirmationProgress()
    {
        var item = new RadialMenuItemState(
            "approve",
            RadialMenuSlotPosition.Bottom,
            " A ",
            " Approve ",
            " Required ",
            confirmationProgress: 1.8);

        Assert.Equal("A", item.InputGlyph);
        Assert.Equal("Approve", item.Title);
        Assert.Equal("Required", item.Subtitle);
        Assert.Equal(1, item.ConfirmationProgress);
        Assert.True(item.HasConfirmationProgress);
    }

    [Theory]
    [InlineData("always", RadialMenuDisplayMode.Always)]
    [InlineData("ALWAYS", RadialMenuDisplayMode.Always)]
    [InlineData("learning", RadialMenuDisplayMode.Learning)]
    [InlineData("off", RadialMenuDisplayMode.Off)]
    public void DisplayModeParserAcceptsSettingsValues(
        string value,
        RadialMenuDisplayMode expected)
    {
        Assert.True(
            RadialMenuDisplayModeParser.TryParse(
                value,
                out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void DisplayModeParserUsesSafeFallbackForUnknownValue()
    {
        var actual = RadialMenuDisplayModeParser.ParseOrDefault(
            "unexpected",
            RadialMenuDisplayMode.Off);

        Assert.Equal(RadialMenuDisplayMode.Off, actual);
    }

    private static RadialMenuState CreateState(
        RadialMenuDisplayMode displayMode,
        bool isLayerEngaged,
        bool isLearningCueReady)
    {
        return new RadialMenuState(
            RadialMenuLayerKind.Command,
            "Commands",
            "RB",
            [Item("send", RadialMenuSlotPosition.Left)],
            displayMode,
            isLayerEngaged,
            isLearningCueReady);
    }

    private static RadialMenuItemState Item(
        string id,
        RadialMenuSlotPosition position)
    {
        return new RadialMenuItemState(
            id,
            position,
            position.ToString(),
            id);
    }
}
