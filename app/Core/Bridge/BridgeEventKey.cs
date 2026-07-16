namespace CodexController.Core.Bridge;

/// <summary>
/// A stable, locale-independent identifier for a bridge event.
/// Keys are suitable for localization lookup and diagnostic export.
/// </summary>
public readonly record struct BridgeEventKey
{
    private const int MaxLength = 160;

    public BridgeEventKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength || !IsValid(value))
        {
            throw new ArgumentException(
                "Bridge event keys must contain lowercase dot-separated " +
                "segments using letters, digits, hyphens, or underscores.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator BridgeEventKey(string value) => new(value);

    internal bool IsInitialized => !string.IsNullOrEmpty(Value);

    private static bool IsValid(string value)
    {
        var segmentStart = true;
        var hasSeparator = false;

        foreach (var character in value)
        {
            if (character == '.')
            {
                if (segmentStart)
                {
                    return false;
                }

                segmentStart = true;
                hasSeparator = true;
                continue;
            }

            if (segmentStart)
            {
                if (character is < 'a' or > 'z')
                {
                    return false;
                }

                segmentStart = false;
                continue;
            }

            if (
                character is not (>= 'a' and <= 'z') &&
                character is not (>= '0' and <= '9') &&
                character != '-' &&
                character != '_')
            {
                return false;
            }
        }

        return hasSeparator && !segmentStart;
    }
}

/// <summary>
/// First-party event keys. Values are part of the bridge diagnostic contract;
/// rename them only as an intentional compatibility change.
/// </summary>
public static class BridgeEventKeys
{
    public static readonly BridgeEventKey AppReady =
        new("app.ready");

    public static readonly BridgeEventKey LegacyMessage =
        new("diagnostic.legacy.message");

    public static readonly BridgeEventKey ControllerArmed =
        new("controller.session.armed");

    public static readonly BridgeEventKey ControllerLocked =
        new("controller.session.locked");

    public static readonly BridgeEventKey ControllerPaused =
        new("controller.session.paused");

    public static readonly BridgeEventKey ControllerResumed =
        new("controller.session.resumed");

    public static readonly BridgeEventKey ControllerConnected =
        new("controller.device.connected");

    public static readonly BridgeEventKey ControllerDisconnected =
        new("controller.device.disconnected");

    public static readonly BridgeEventKey CodexWakeRequested =
        new("codex.wake.requested");

    public static readonly BridgeEventKey CodexWakeSucceeded =
        new("codex.wake.succeeded");

    public static readonly BridgeEventKey CodexWakeFailed =
        new("codex.wake.failed");

    public static readonly BridgeEventKey SidebarFocusChanged =
        new("sidebar.focus.changed");

    public static readonly BridgeEventKey SidebarScopeChanged =
        new("sidebar.scope.changed");

    public static readonly BridgeEventKey SidebarEntryOpened =
        new("sidebar.entry.opened");

    public static readonly BridgeEventKey SidebarNavigationUndone =
        new("sidebar.navigation.undone");

    public static readonly BridgeEventKey ComposerDictationStarted =
        new("composer.dictation.started");

    public static readonly BridgeEventKey ComposerDictationStopped =
        new("composer.dictation.stopped");

    public static readonly BridgeEventKey ComposerPromptSent =
        new("composer.prompt.sent");

    public static readonly BridgeEventKey ComposerActionCancelled =
        new("composer.action.cancelled");

    public static readonly BridgeEventKey ModelChanged =
        new("model.selection.changed");

    public static readonly BridgeEventKey ReasoningEffortChanged =
        new("model.reasoning-effort.changed");

    public static readonly BridgeEventKey SpeedChanged =
        new("model.speed.changed");

    public static readonly BridgeEventKey AutomationFailed =
        new("automation.action.failed");
}
