namespace CodexController.Services;

/// <summary>
/// Tracks whether an injected Codex composer shortcut (for example the
/// provisioned reasoning-effort or fast-toggle keybinding) can be trusted
/// as the primary transport. Readback-verified success proves the binding;
/// a silent miss that menu automation later contradicts suspends the
/// shortcut for a cooldown instead of latching it off forever, so a
/// one-off race cannot permanently force the slow menu path.
/// Callers are expected to serialize access through the composer
/// automation gate; the class itself is not thread-safe.
/// </summary>
internal sealed class ComposerShortcutHealth
{
    internal static readonly TimeSpan SuspectCooldown =
        TimeSpan.FromSeconds(30);

    private readonly Func<long> _clock;
    private long _suspendedUntil;
    private bool _proven;

    internal ComposerShortcutHealth(Func<long>? clock = null)
    {
        _clock = clock ?? (static () => Environment.TickCount64);
    }

    /// <summary>
    /// True once a shortcut press has been confirmed by readback. A later
    /// unchanged readback can then be reported as a value boundary instead
    /// of triggering menu automation that would double-apply the step.
    /// </summary>
    internal bool IsProven => _proven;

    internal bool ShouldAttempt() => _clock() >= _suspendedUntil;

    internal void MarkWorking()
    {
        _proven = true;
        _suspendedUntil = 0;
    }

    internal void MarkSuspect()
    {
        _proven = false;
        _suspendedUntil =
            _clock() + (long)SuspectCooldown.TotalMilliseconds;
    }
}
