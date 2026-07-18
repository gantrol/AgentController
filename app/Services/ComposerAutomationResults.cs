namespace CodexController.Services;

internal static class ComposerAutomationResults
{
    internal static ComposerAutomationResult UiAutomationSucceeded(
        bool stateVerified = false) =>
        new(
            true,
            Channel: ComposerAutomationChannel.UiAutomation,
            StateVerified: stateVerified);
}
