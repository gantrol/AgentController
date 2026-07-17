using CodexController.Controllers;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class SimpleModeCompatibilityPromptTests
{
    [Theory]
    [InlineData("5.6 Sol Max")]
    [InlineData("GPT-5.6-Codex · SOL · MAX")]
    public void OffersAdvancedForSolMaxSimplePickerMismatch(
        string value)
    {
        var result = new ComposerPickerResult(
            false,
            value,
            IsMenuOpen: true,
            ErrorDetail: "composer-picker-view:simple");

        Assert.True(
            SimpleModeCompatibilityPrompt.ShouldOfferAdvanced(
                usesAdvancedMode: false,
                result));
    }

    [Theory]
    [InlineData("5.6 Solution Max")]
    [InlineData("5.6 Sol High")]
    [InlineData(null)]
    public void DoesNotOfferForAnotherSelection(string? value)
    {
        var result = new ComposerPickerResult(
            false,
            value,
            ErrorDetail: "composer-picker-view:simple");

        Assert.False(
            SimpleModeCompatibilityPrompt.ShouldOfferAdvanced(
                usesAdvancedMode: false,
                result));
    }

    [Fact]
    public void DoesNotOfferWhenAdvancedModeIsAlreadyConfigured()
    {
        var result = new ComposerPickerResult(
            false,
            "5.6 Sol Max",
            ErrorDetail: "composer-picker-view:simple");

        Assert.False(
            SimpleModeCompatibilityPrompt.ShouldOfferAdvanced(
                usesAdvancedMode: true,
                result));
    }

    [Fact]
    public void DoesNotOfferForUnrelatedFailure()
    {
        var result = new ComposerPickerResult(
            false,
            "5.6 Sol Max",
            ErrorDetail: "composer-power-input");

        Assert.False(
            SimpleModeCompatibilityPrompt.ShouldOfferAdvanced(
                usesAdvancedMode: false,
                result));
    }

    [Theory]
    [InlineData(
        ControllerButtons.A,
        (int)SimpleModeCompatibilityChoice.SwitchToAdvanced)]
    [InlineData(
        ControllerButtons.B,
        (int)SimpleModeCompatibilityChoice.KeepSimple)]
    [InlineData(
        ControllerButtons.X,
        (int)SimpleModeCompatibilityChoice.None)]
    [InlineData(
        ControllerButtons.A | ControllerButtons.B,
        (int)SimpleModeCompatibilityChoice.None)]
    public void ResolvesUnambiguousControllerChoice(
        ControllerButtons downEdges,
        int expected)
    {
        Assert.Equal(
            (SimpleModeCompatibilityChoice)expected,
            SimpleModeCompatibilityPrompt.ResolveChoice(downEdges));
    }
}
