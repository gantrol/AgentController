using AgentController.Platform.Controllers;
using AgentController.Platform.MacOS.Controllers;

namespace AgentController.Platform.MacOS.Tests;

public sealed class MacControllerTopologyTrackerTests
{
    [Fact]
    public void ReportsConnectionsAndInitialCurrentController()
    {
        var tracker = new MacControllerTopologyTracker();

        var update = tracker.Update(
        [
            Controller("alpha", "Xbox", isCurrent: true),
            Controller("beta", "DualSense"),
        ]);

        Assert.Equal(1, update.Revision);
        Assert.Equal("alpha", update.CurrentControllerId);
        Assert.Collection(
            update.Changes,
            change => AssertChange(
                change,
                MacControllerLifecycleChangeKind.Connected,
                "alpha"),
            change => AssertChange(
                change,
                MacControllerLifecycleChangeKind.Connected,
                "beta"),
            change => AssertChange(
                change,
                MacControllerLifecycleChangeKind.BecameCurrent,
                "alpha"));
    }

    [Fact]
    public void IgnoresArrayReorderingAndInputOnlyChanges()
    {
        var tracker = new MacControllerTopologyTracker();
        tracker.Update(
        [
            Controller("alpha", "Xbox", isCurrent: true),
            Controller("beta", "DualSense"),
        ]);

        var update = tracker.Update(
        [
            Controller(
                "beta",
                "DualSense",
                buttons: ControllerButtons.South),
            Controller("alpha", "Xbox", isCurrent: true),
        ]);

        Assert.Equal(1, update.Revision);
        Assert.Empty(update.Changes);
        Assert.Equal(["beta", "alpha"], update.Controllers.Select(c => c.Id));
    }

    [Fact]
    public void ReportsCurrentChangeAndDisconnectInDeterministicOrder()
    {
        var tracker = new MacControllerTopologyTracker();
        tracker.Update(
        [
            Controller("alpha", "Xbox", isCurrent: true),
            Controller("beta", "DualSense"),
        ]);

        var currentChanged = tracker.Update(
        [
            Controller("alpha", "Xbox"),
            Controller("beta", "DualSense", isCurrent: true),
        ]);
        var disconnected = tracker.Update(
        [
            Controller("beta", "DualSense", isCurrent: true),
        ]);

        Assert.Equal(2, currentChanged.Revision);
        Assert.Collection(
            currentChanged.Changes,
            change => AssertChange(
                change,
                MacControllerLifecycleChangeKind.StoppedBeingCurrent,
                "alpha"),
            change => AssertChange(
                change,
                MacControllerLifecycleChangeKind.BecameCurrent,
                "beta"));
        Assert.Equal(3, disconnected.Revision);
        var change = Assert.Single(disconnected.Changes);
        AssertChange(
            change,
            MacControllerLifecycleChangeKind.Disconnected,
            "alpha");
    }

    [Fact]
    public void RejectsAmbiguousCurrentControllerState()
    {
        var tracker = new MacControllerTopologyTracker();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            tracker.Update(
            [
                Controller("alpha", "Xbox", isCurrent: true),
                Controller("beta", "DualSense", isCurrent: true),
            ]));

        Assert.Contains("more than one current", exception.Message);
    }

    private static ControllerInputSnapshot Controller(
        string id,
        string name,
        bool isCurrent = false,
        ControllerButtons buttons = ControllerButtons.None) =>
        new(
            id,
            name,
            "Extended Gamepad",
            buttons,
            default,
            default,
            0,
            0,
            null,
            ControllerFeatures.ExtendedGamepad,
            isCurrent,
            IsIdentityStable: false);

    private static void AssertChange(
        MacControllerLifecycleChange change,
        MacControllerLifecycleChangeKind expectedKind,
        string expectedId)
    {
        Assert.Equal(expectedKind, change.Kind);
        Assert.Equal(expectedId, change.ControllerId);
    }
}
