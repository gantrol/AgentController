using CodexController.Services;

namespace CodexController.Presentation.Feedback;

internal static class ComposerPickerOverlayPolicy
{
    internal static bool ShouldShow(ComposerPickerResult result) =>
        !result.Succeeded;
}
