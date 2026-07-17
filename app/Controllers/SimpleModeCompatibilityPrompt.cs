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

    internal static bool ShouldOfferAdvanced(
        bool usesAdvancedMode,
        ComposerPickerResult result)
    {
        return
            !usesAdvancedMode &&
            !result.Succeeded &&
            string.Equals(
                result.ErrorDetail,
                SimplePickerMismatch,
                StringComparison.Ordinal) &&
            ContainsToken(result.Value, "Sol") &&
            ContainsToken(result.Value, "Max");
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

    private static bool ContainsToken(string? value, string token)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var span = value.AsSpan();
        var index = 0;
        while (index < span.Length)
        {
            while (
                index < span.Length &&
                !char.IsLetterOrDigit(span[index]))
            {
                index++;
            }

            var start = index;
            while (
                index < span.Length &&
                char.IsLetterOrDigit(span[index]))
            {
                index++;
            }

            if (
                start < index &&
                span[start..index].Equals(
                    token,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
