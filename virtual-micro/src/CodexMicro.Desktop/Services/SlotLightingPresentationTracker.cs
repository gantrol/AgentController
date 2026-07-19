using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Keeps the screen simulator illuminated when Codex temporarily switches the
/// physical-device lighting model off after its inactivity timeout. The all-off
/// report is still acknowledged by the protocol layer; it is only suppressed
/// from replacing the last useful presentation snapshot.
/// </summary>
internal sealed class SlotLightingPresentationTracker
{
    private const int ScreenIdleColor = 0x9EBDFF;
    private ulong? _connectionEpoch;
    private long _lastSequence;

    public SlotLightingPresentationTracker()
    {
        VisibleSnapshot = CreateScreenIdleSnapshot(new SlotLightingSnapshot(
            0,
            DateTimeOffset.MinValue,
            []));
        IsInactivityLightingSuppressed = true;
    }

    public SlotLightingSnapshot VisibleSnapshot { get; private set; }

    public bool IsInactivityLightingSuppressed { get; private set; }

    public void BeginConnection(ulong connectionEpoch)
    {
        if (_connectionEpoch == connectionEpoch)
        {
            return;
        }

        _connectionEpoch = connectionEpoch;
        _lastSequence = 0;
        IsInactivityLightingSuppressed =
            HasVisibleLighting(VisibleSnapshot);
    }

    public bool Observe(SlotLightingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Sequence <= _lastSequence)
        {
            return false;
        }

        _lastSequence = snapshot.Sequence;
        if (HasVisibleLighting(snapshot))
        {
            VisibleSnapshot = snapshot;
            IsInactivityLightingSuppressed = false;
            return true;
        }

        if (HasVisibleLighting(VisibleSnapshot))
        {
            IsInactivityLightingSuppressed = true;
            return true;
        }

        VisibleSnapshot = CreateScreenIdleSnapshot(snapshot);
        IsInactivityLightingSuppressed = true;
        return true;
    }

    public static bool IsLit(SlotLighting lighting) =>
        lighting.SlotId is >= 0 and < 6 &&
        lighting.Color != 0 &&
        lighting.Brightness > 0;

    private static bool HasVisibleLighting(SlotLightingSnapshot snapshot) =>
        snapshot.Slots.Any(IsLit);

    private static SlotLightingSnapshot CreateScreenIdleSnapshot(
        SlotLightingSnapshot source) => new(
            source.Sequence,
            source.ObservedAt,
            Enumerable.Range(0, 6)
                .Select(slotId => new SlotLighting(
                    slotId,
                    ScreenIdleColor,
                    1,
                    1,
                    0,
                    false,
                    false,
                    true))
                .ToArray(),
            source.MappingKind);
}
