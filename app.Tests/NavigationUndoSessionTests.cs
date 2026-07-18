using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class NavigationUndoSessionTests
{
    private static readonly TimeSpan UndoWindow =
        TimeSpan.FromSeconds(10);

    [Fact]
    public void RequestBeforeArrivalConfirmationQueuesUndo()
    {
        var session = CreateSession();

        var action = session.RequestUndo(DateTimeOffset.UtcNow);

        Assert.Equal(
            NavigationUndoPressAction.QueueUntilNavigationConfirms,
            action);
        Assert.True(session.UndoRequested);
        Assert.False(session.Confirmed);
        Assert.Null(session.ExpiresAt);
    }

    [Fact]
    public void ConfirmationOpensUndoWindowAndPreservesQueuedRequest()
    {
        var now = DateTimeOffset.Parse("2026-07-18T18:00:00Z");
        var session = CreateSession();
        session.RequestUndo(now);

        session.MarkConfirmed(now, UndoWindow);

        Assert.True(session.Confirmed);
        Assert.True(session.UndoRequested);
        Assert.Equal(now + UndoWindow, session.ExpiresAt);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void RequestWithinWindowExecutesUndo(
        int ticksFromExpiry)
    {
        var now = DateTimeOffset.Parse("2026-07-18T18:00:00Z");
        var session = CreateSession();
        session.MarkConfirmed(now, UndoWindow);
        var expiry = Assert.IsType<DateTimeOffset>(session.ExpiresAt);

        var action = session.RequestUndo(
            expiry.AddTicks(ticksFromExpiry));

        Assert.Equal(NavigationUndoPressAction.ExecuteUndo, action);
        Assert.False(session.UndoRequested);
    }

    [Fact]
    public void RequestAfterWindowExpiresFallsIntoStopHold()
    {
        var now = DateTimeOffset.Parse("2026-07-18T18:00:00Z");
        var session = CreateSession();
        session.MarkConfirmed(now, UndoWindow);

        var action = session.RequestUndo(
            now + UndoWindow + TimeSpan.FromTicks(1));

        Assert.Equal(
            NavigationUndoPressAction.ExpireAndBeginStopHold,
            action);
        Assert.False(session.UndoRequested);
    }

    [Fact]
    public void SessionRetainsTargetIdentity()
    {
        var session = CreateSession();

        Assert.Equal("Display title", session.TargetDisplayTitle);
        Assert.Equal("Native title", session.TargetNativeTitle);
    }

    private static NavigationUndoSession CreateSession() =>
        new("Display title", "Native title");
}
