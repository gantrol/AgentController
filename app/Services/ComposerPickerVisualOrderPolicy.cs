namespace CodexController.Services;

internal static class ComposerPickerVisualOrderPolicy
{
    internal static int ResolveNextIndex(
        int currentIndex,
        int optionCount,
        int verticalDirection)
    {
        if (optionCount <= 0)
        {
            return -1;
        }

        var safeCurrent = Math.Clamp(
            currentIndex,
            0,
            optionCount - 1);
        return Math.Clamp(
            safeCurrent - Math.Sign(verticalDirection),
            0,
            optionCount - 1);
    }
}
