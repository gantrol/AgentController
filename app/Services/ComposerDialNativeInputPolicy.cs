namespace CodexController.Services;

/// <summary>
/// Native keyboard transport for an already-owned Codex composer popup.
/// UI Automation remains responsible for discovering and verifying the
/// surface; it is not used to move the highlighted row on this path.
/// </summary>
internal static class ComposerDialNativeInputPolicy
{
    private static readonly string[] MicroSemanticControlTokens =
    [
        "power",
        "reasoning",
        "effort",
        "推理",
        "思考强度",
        "思考力度",
    ];

    internal const ushort EnterKey = 0x0D;
    internal const ushort EscapeKey = 0x1B;
    internal const ushort LeftKey = 0x25;
    internal const ushort UpKey = 0x26;
    internal const ushort RightKey = 0x27;
    internal const ushort DownKey = 0x28;

    internal static bool TryGetNavigationKey(
        ComposerDialNavigation navigation,
        out ushort virtualKey)
    {
        virtualKey = navigation switch
        {
            ComposerDialNavigation.Left => LeftKey,
            ComposerDialNavigation.Right => RightKey,
            ComposerDialNavigation.Up => UpKey,
            ComposerDialNavigation.Down => DownKey,
            _ => 0,
        };
        return virtualKey != 0;
    }

    internal static IReadOnlyList<ushort> BuildFocusNudgeSequence(
        int visualIndex,
        int optionCount)
    {
        if (
            visualIndex < 0 ||
            visualIndex >= optionCount ||
            optionCount <= 0)
        {
            return [];
        }

        if (optionCount == 1)
        {
            return [DownKey];
        }

        return visualIndex < optionCount - 1
            ? [DownKey, UpKey]
            : [UpKey, DownKey];
    }

    internal static bool RequiresMicroSemanticNavigation(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return MicroSemanticControlTokens.Any(token =>
            name.Contains(
                token,
                StringComparison.OrdinalIgnoreCase));
    }
}
