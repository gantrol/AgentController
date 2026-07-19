using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class SlotLightingPresentationTrackerTests
{
    [Fact]
    public void StartsWithNeutralScreenIdleLighting()
    {
        var tracker = new SlotLightingPresentationTracker();

        Assert.Equal(6, tracker.VisibleSnapshot.Slots.Count);
        Assert.All(
            tracker.VisibleSnapshot.Slots,
            slot => Assert.Equal(0x9EBDFF, slot.Color));
        Assert.True(tracker.IsInactivityLightingSuppressed);
    }

    [Fact]
    public void AllOffInactivityFrameKeepsLastVisibleLighting()
    {
        var tracker = new SlotLightingPresentationTracker();
        var lit = Snapshot(
            1,
            new SlotLighting(0, 0x304FFE, 1, 1, 0, false, false, false),
            new SlotLighting(1, 0xFFFFFF, 1, 1, 0, false, false, false));
        var allOff = Snapshot(
            2,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true),
            new SlotLighting(1, 0, 0, 0, 0, false, false, true));

        Assert.True(tracker.Observe(lit));
        Assert.True(tracker.Observe(allOff));

        Assert.Same(lit, tracker.VisibleSnapshot);
        Assert.True(tracker.IsInactivityLightingSuppressed);
    }

    [Fact]
    public void NextLitFrameResumesLivePresentation()
    {
        var tracker = new SlotLightingPresentationTracker();
        var first = Snapshot(
            1,
            new SlotLighting(0, 0xFFFFFF, 1, 1, 0, false, false, false));
        var allOff = Snapshot(
            2,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true));
        var resumed = Snapshot(
            3,
            new SlotLighting(0, 0x00FF4C, 1, 1, 0, false, false, false));

        tracker.Observe(first);
        tracker.Observe(allOff);
        Assert.True(tracker.Observe(resumed));

        Assert.Same(resumed, tracker.VisibleSnapshot);
        Assert.False(tracker.IsInactivityLightingSuppressed);
    }

    [Fact]
    public void StaleFrameCannotReplaceVisibleLighting()
    {
        var tracker = new SlotLightingPresentationTracker();
        var newest = Snapshot(
            4,
            new SlotLighting(0, 0xFF0033, 1, 1, 0, false, false, false));
        var stale = Snapshot(
            3,
            new SlotLighting(0, 0xFFFFFF, 1, 1, 0, false, false, false));

        Assert.True(tracker.Observe(newest));
        Assert.False(tracker.Observe(stale));

        Assert.Same(newest, tracker.VisibleSnapshot);
    }

    [Fact]
    public void FirstAllOffFrameUsesNeutralScreenIdleLighting()
    {
        var tracker = new SlotLightingPresentationTracker();
        var allOff = Snapshot(
            1,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true));

        Assert.True(tracker.Observe(allOff));

        Assert.NotSame(allOff, tracker.VisibleSnapshot);
        Assert.Equal(6, tracker.VisibleSnapshot.Slots.Count);
        Assert.All(
            tracker.VisibleSnapshot.Slots,
            slot =>
            {
                Assert.Equal(0x9EBDFF, slot.Color);
                Assert.Equal(1, slot.Brightness);
                Assert.True(slot.LightingAmbiguous);
            });
        Assert.True(tracker.IsInactivityLightingSuppressed);
    }

    [Fact]
    public void NewConnectionAcceptsAResetSequenceWithoutExtinguishingFirst()
    {
        var tracker = new SlotLightingPresentationTracker();
        tracker.BeginConnection(1);
        var previous = Snapshot(
            9,
            new SlotLighting(0, 0x304FFE, 1, 1, 0, false, false, false));
        tracker.Observe(previous);

        tracker.BeginConnection(2);
        Assert.Same(previous, tracker.VisibleSnapshot);
        Assert.True(tracker.IsInactivityLightingSuppressed);

        var current = Snapshot(
            1,
            new SlotLighting(0, 0x00FF4C, 1, 1, 0, false, false, false));
        Assert.True(tracker.Observe(current));
        Assert.Same(current, tracker.VisibleSnapshot);
        Assert.False(tracker.IsInactivityLightingSuppressed);
    }

    private static SlotLightingSnapshot Snapshot(
        long sequence,
        params SlotLighting[] slots) => new(
            sequence,
            DateTimeOffset.UtcNow,
            slots);
}
