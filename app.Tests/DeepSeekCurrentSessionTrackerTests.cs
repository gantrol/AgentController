using CodexController.Agents.DeepSeek;

namespace CodexController.Tests;

public sealed class DeepSeekCurrentSessionTrackerTests
{
    [Fact]
    public void ObserveOnlySignalsRealAuthoritativeCurrentChanges()
    {
        var tracker = new DeepSeekCurrentSessionTracker();

        Assert.False(tracker.Observe("session-a", currentSessionMaterialized: true));
        Assert.False(tracker.Observe("session-a", currentSessionMaterialized: true));
        Assert.True(tracker.Observe("session-b", currentSessionMaterialized: true));
        Assert.False(tracker.Observe("session-b", currentSessionMaterialized: true));
        Assert.False(tracker.Observe(null, currentSessionMaterialized: false));
        Assert.False(tracker.Observe(null, currentSessionMaterialized: false));
    }

    [Fact]
    public void ResetMakesTheNextAgentSessionABaselineNotANavigation()
    {
        var tracker = new DeepSeekCurrentSessionTracker();
        _ = tracker.Observe("session-a", currentSessionMaterialized: true);
        Assert.True(tracker.Observe("session-b", currentSessionMaterialized: true));

        tracker.Reset();

        Assert.False(tracker.Observe("session-b", currentSessionMaterialized: true));
        Assert.True(tracker.Observe("session-c", currentSessionMaterialized: true));
    }

    [Fact]
    public void UnmaterializedChangedCurrentKeepsSignallingUntilItsRowArrives()
    {
        var tracker = new DeepSeekCurrentSessionTracker();
        Assert.False(tracker.Observe(
            "session-a",
            currentSessionMaterialized: true));

        Assert.True(tracker.Observe(
            "session-b",
            currentSessionMaterialized: false));
        Assert.True(tracker.Observe(
            "session-b",
            currentSessionMaterialized: true));
        Assert.False(tracker.Observe(
            "session-b",
            currentSessionMaterialized: true));
    }

    [Fact]
    public void InitiallyUnmaterializedCurrentSignalsWhenItsRowArrives()
    {
        var tracker = new DeepSeekCurrentSessionTracker();

        Assert.False(tracker.Observe(
            "session-b",
            currentSessionMaterialized: false));
        Assert.True(tracker.Observe(
            "session-b",
            currentSessionMaterialized: true));
        Assert.False(tracker.Observe(
            "session-b",
            currentSessionMaterialized: true));
    }
}
