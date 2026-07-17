using CodexController.Controllers;
using CodexController.Models;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class SimpleModeCompatibilityPromptTests
{
    [Theory]
    [InlineData("5.6 Sol Max")]
    [InlineData("GPT-5.6-Codex · SOL · MAX")]
    [InlineData("5.6 Sol High")]
    public void OffersAdvancedWhenSimpleViewLacksPower(
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
                result,
                declinedSuppressionKey: null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DoesNotOfferWithoutAKnownSelection(string? value)
    {
        var result = new ComposerPickerResult(
            false,
            value,
            ErrorDetail: "composer-picker-view:simple");

        Assert.False(
            SimpleModeCompatibilityPrompt.ShouldOfferAdvanced(
                usesAdvancedMode: false,
                result,
                declinedSuppressionKey: null));
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
                result,
                declinedSuppressionKey: null));
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
                result,
                declinedSuppressionKey: null));
    }

    [Theory]
    [InlineData("5.6 Sol Max")]
    [InlineData("5.6 Sol Max · Fast")]
    [InlineData("5.6 Sol Max · Standard")]
    public void DeclinedSelectionSuppressesRepeatPrompts(
        string laterValue)
    {
        var declinedKey = SimpleModeCompatibilityPrompt.SuppressionKey(
            "5.6 Sol Max · Standard");
        var result = new ComposerPickerResult(
            false,
            laterValue,
            ErrorDetail: "composer-picker-view:simple");

        Assert.False(
            SimpleModeCompatibilityPrompt.ShouldOfferAdvanced(
                usesAdvancedMode: false,
                result,
                declinedKey));
    }

    [Fact]
    public void DifferentSelectionPromptsAgainAfterDecline()
    {
        var declinedKey = SimpleModeCompatibilityPrompt.SuppressionKey(
            "5.6 Sol Max");
        var result = new ComposerPickerResult(
            false,
            "6.0 Nova Ultra",
            ErrorDetail: "composer-picker-view:simple");

        Assert.True(
            SimpleModeCompatibilityPrompt.ShouldOfferAdvanced(
                usesAdvancedMode: false,
                result,
                declinedKey));
    }

    [Theory]
    [InlineData("5.6 Sol Max · Fast", "56solmax")]
    [InlineData("5.6 Sol Max · Standard", "56solmax")]
    [InlineData("5.6 Sol Max", "56solmax")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void SuppressionKeyIgnoresTrailingSpeedToken(
        string? value,
        string expected)
    {
        Assert.Equal(
            expected,
            SimpleModeCompatibilityPrompt.SuppressionKey(value));
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
