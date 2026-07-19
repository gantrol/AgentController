using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Tracks the last real lighting frame while distinguishing Codex's physical-
/// device inactivity frame. The first all-off frame in each inactivity episode
/// requests one harmless HID wake event; no screen-only color is invented.
/// </summary>
internal sealed class SlotLightingPresentationTracker
{
    private ulong? _connectionEpoch;
    private long _lastSequence;
    private bool _allOffEpisode;

    public SlotLightingSnapshot? VisibleSnapshot { get; private set; }

    public bool IsInactivityLightingSuppressed { get; private set; }

    public void BeginConnection(ulong connectionEpoch)
    {
        if (_connectionEpoch == connectionEpoch)
        {
            return;
        }

        _connectionEpoch = connectionEpoch;
        _lastSequence = 0;
        _allOffEpisode = false;
        VisibleSnapshot = null;
        IsInactivityLightingSuppressed = false;
    }

    public SlotLightingPresentationUpdate Observe(SlotLightingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Sequence <= _lastSequence)
        {
            return new(false, false);
        }

        _lastSequence = snapshot.Sequence;
        if (HasVisibleLighting(snapshot))
        {
            VisibleSnapshot = snapshot;
            IsInactivityLightingSuppressed = false;
            _allOffEpisode = false;
            return new(true, false);
        }

        var shouldWakeLighting = !_allOffEpisode;
        _allOffEpisode = true;
        if (VisibleSnapshot is null || !HasVisibleLighting(VisibleSnapshot))
        {
            VisibleSnapshot = snapshot;
        }

        IsInactivityLightingSuppressed = true;
        return new(true, shouldWakeLighting);
    }

    public static bool IsLit(SlotLighting lighting) =>
        lighting.SlotId is >= 0 and < 6 &&
        lighting.Color != 0 &&
        lighting.Brightness > 0;

    private static bool HasVisibleLighting(SlotLightingSnapshot snapshot) =>
        snapshot.Slots.Any(IsLit);
}

internal readonly record struct SlotLightingPresentationUpdate(
    bool Accepted,
    bool ShouldWakeLighting);
