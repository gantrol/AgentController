using CodexController.Presentation.Feedback;
using CodexController.Services;

namespace CodexController.Tests;

public sealed class ComposerPickerOverlayPolicyTests
{
    [Fact]
    public void SuccessfulLiveSelectionDoesNotShowDuplicateOverlay()
    {
        var result = new ComposerPickerResult(
            true,
            "5.6 Sol Ultra",
            IsMenuOpen: true);

        Assert.False(
            ComposerPickerOverlayPolicy.ShouldShow(result));
    }

    [Fact]
    public void FailureStillShowsActionableOverlay()
    {
        var result = new ComposerPickerResult(
            false,
            Error: "automation-element-not-found",
            ErrorDetail: "composer-speed-option");

        Assert.True(
            ComposerPickerOverlayPolicy.ShouldShow(result));
    }
}
