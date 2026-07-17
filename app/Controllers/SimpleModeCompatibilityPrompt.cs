using CodexController.Models;
using CodexController.Services;

namespace CodexController.Controllers;

internal enum SimpleModeCompatibilityChoice
{
    None,
    SwitchToAdvanced,
    KeepSimple,
}

internal static class SimpleModeCompatibilityPrompt
{
    private const string SimplePickerMismatch =
        "composer-picker-view:simple";

    /// <summary>
    /// Offers the Advanced-mode switch on a capability signal: the Simple
    /// picker view exists but exposes no Power control for the current
    /// selection. The model name is display-only and no longer gates the
    /// prompt; a declined suppression key mutes repeats for the same
    /// model/effort until the composer selection changes.
    /// </summary>
    internal static bool ShouldOfferAdvanced(
        bool usesAdvancedMode,
        ComposerPickerResult result,
        string? declinedSuppressionKey)
    {
        if (usesAdvancedMode || result.Succeeded)
        {
            return false;
        }

        if (!string.Equals(
                result.ErrorDetail,
                SimplePickerMismatch,
                StringComparison.Ordinal))
        {
            return false;
        }

        var key = SuppressionKey(result.Value);
        if (key.Length == 0)
        {
            return false;
        }

        return !string.Equals(
            key,
            declinedSuppressionKey,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Normalizes a composer button name into a suppression key. The
    /// trailing Standard/Fast token is stripped so toggling speed does not
    /// re-trigger a prompt the user already declined for the same model.
    /// </summary>
    internal static string SuppressionKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = new string(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        if (normalized.EndsWith("standard", StringComparison.Ordinal))
        {
            return normalized[..^"standard".Length];
        }

        if (normalized.EndsWith("fast", StringComparison.Ordinal))
        {
            return normalized[..^"fast".Length];
        }

        return normalized;
    }

    internal static SimpleModeCompatibilityChoice ResolveChoice(
        ControllerButtons downEdges)
    {
        return (downEdges &
            (ControllerButtons.A | ControllerButtons.B)) switch
        {
            ControllerButtons.A =>
                SimpleModeCompatibilityChoice.SwitchToAdvanced,
            ControllerButtons.B =>
                SimpleModeCompatibilityChoice.KeepSimple,
            _ => SimpleModeCompatibilityChoice.None,
        };
    }

}
