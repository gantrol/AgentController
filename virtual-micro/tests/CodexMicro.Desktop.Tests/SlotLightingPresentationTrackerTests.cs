using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class SlotLightingPresentationTrackerTests
{
    [Fact]
    public void StartsWithoutInventingScreenLighting()
    {
        var tracker = new SlotLightingPresentationTracker();

        Assert.Null(tracker.VisibleSnapshot);
        Assert.False(tracker.IsInactivityLightingSuppressed);
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

        var litUpdate = tracker.Observe(lit);
        var offUpdate = tracker.Observe(allOff);

        Assert.True(litUpdate.Accepted);
        Assert.False(litUpdate.ShouldWakeLighting);
        Assert.True(offUpdate.Accepted);
        Assert.True(offUpdate.ShouldWakeLighting);
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
        var update = tracker.Observe(resumed);

        Assert.True(update.Accepted);
        Assert.False(update.ShouldWakeLighting);
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

        Assert.True(tracker.Observe(newest).Accepted);
        var staleUpdate = tracker.Observe(stale);

        Assert.False(staleUpdate.Accepted);
        Assert.False(staleUpdate.ShouldWakeLighting);
        Assert.Same(newest, tracker.VisibleSnapshot);
    }

    [Fact]
    public void FirstAllOffFrameStaysOffAndRequestsOneWake()
    {
        var tracker = new SlotLightingPresentationTracker();
        var allOff = Snapshot(
            1,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true));

        var update = tracker.Observe(allOff);

        Assert.True(update.Accepted);
        Assert.True(update.ShouldWakeLighting);
        Assert.Same(allOff, tracker.VisibleSnapshot);
        Assert.DoesNotContain(
            tracker.VisibleSnapshot!.Slots,
            SlotLightingPresentationTracker.IsLit);
        Assert.True(tracker.IsInactivityLightingSuppressed);
    }

    [Fact]
    public void RepeatedAllOffFramesDoNotCreateAWakeStorm()
    {
        var tracker = new SlotLightingPresentationTracker();
        var first = Snapshot(
            1,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true));
        var repeated = Snapshot(
            2,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true));

        Assert.True(tracker.Observe(first).ShouldWakeLighting);
        var repeatedUpdate = tracker.Observe(repeated);

        Assert.True(repeatedUpdate.Accepted);
        Assert.False(repeatedUpdate.ShouldWakeLighting);
    }

    [Fact]
    public void RealLightingResetsTheNextInactivityWake()
    {
        var tracker = new SlotLightingPresentationTracker();
        var firstOff = Snapshot(
            1,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true));
        var resumed = Snapshot(
            2,
            new SlotLighting(0, 0x00FF4C, 1, 1, 0, false, false, false));
        var nextOff = Snapshot(
            3,
            new SlotLighting(0, 0, 0, 0, 0, false, false, true));

        Assert.True(tracker.Observe(firstOff).ShouldWakeLighting);
        Assert.False(tracker.Observe(resumed).ShouldWakeLighting);
        Assert.True(tracker.Observe(nextOff).ShouldWakeLighting);
    }

    [Fact]
    public void NewConnectionClearsOldLightingAndAllowsAResetSequence()
    {
        var tracker = new SlotLightingPresentationTracker();
        tracker.BeginConnection(1);
        var previous = Snapshot(
            9,
            new SlotLighting(0, 0x304FFE, 1, 1, 0, false, false, false));
        tracker.Observe(previous);

        tracker.BeginConnection(2);
        Assert.Null(tracker.VisibleSnapshot);
        Assert.False(tracker.IsInactivityLightingSuppressed);

        var current = Snapshot(
            1,
            new SlotLighting(0, 0x00FF4C, 1, 1, 0, false, false, false));
        Assert.True(tracker.Observe(current).Accepted);
        Assert.Same(current, tracker.VisibleSnapshot);
        Assert.False(tracker.IsInactivityLightingSuppressed);
    }

    [Fact]
    public void WakeInstructionIsAnIgnoredAct11Release()
    {
        var reports = CodexLightingWakeInstruction.Encode();
        using var payload = System.Text.Json.JsonDocument.Parse(
            MicroRpcCodec.DecodePayload(
                reports.Select(report => (ReadOnlyMemory<byte>)report)));

        Assert.Equal("v.oai.hid", payload.RootElement.GetProperty("m").GetString());
        var parameters = payload.RootElement.GetProperty("p");
        Assert.Equal("ACT11", parameters.GetProperty("k").GetString());
        Assert.Equal(0, parameters.GetProperty("act").GetInt32());
    }

    private static SlotLightingSnapshot Snapshot(
        long sequence,
        params SlotLighting[] slots) => new(
            sequence,
            DateTimeOffset.UtcNow,
            slots);
}
