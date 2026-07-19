using CodexController.Controllers;

namespace CodexController.Tests;

public sealed class RadialActionConfirmationStateTests
{
    [Fact]
    public void FirstPressOnlyArmsAction()
    {
        var state = new RadialActionConfirmationState();

        Assert.False(state.TryConfirm(RadialInputAction.Approve));
        Assert.True(state.IsPending(RadialInputAction.Approve));
    }

    [Fact]
    public void SecondPressOfSameActionConfirmsAndConsumesArm()
    {
        var state = new RadialActionConfirmationState();
        state.TryConfirm(RadialInputAction.Approve);

        Assert.True(state.TryConfirm(RadialInputAction.Approve));
        Assert.False(state.IsPending(RadialInputAction.Approve));
        Assert.False(state.TryConfirm(RadialInputAction.Approve));
    }

    [Fact]
    public void DifferentActionStartsANewConfirmationSequence()
    {
        var state = new RadialActionConfirmationState();
        state.TryConfirm(RadialInputAction.Approve);

        Assert.False(state.TryConfirm(RadialInputAction.ClearComposer));
        Assert.False(state.IsPending(RadialInputAction.Approve));
        Assert.True(state.IsPending(RadialInputAction.ClearComposer));
    }

    [Fact]
    public void OnlyMatchingTimeoutCanExpirePendingAction()
    {
        var state = new RadialActionConfirmationState();
        state.TryConfirm(RadialInputAction.Approve);

        Assert.False(state.TryExpire(RadialInputAction.ClearComposer));
        Assert.True(state.IsPending(RadialInputAction.Approve));
        Assert.True(state.TryExpire(RadialInputAction.Approve));
        Assert.False(state.IsPending(RadialInputAction.Approve));
    }

    [Fact]
    public void DifferentInterveningActionCancelsPendingConfirmation()
    {
        var state = new RadialActionConfirmationState();
        state.TryConfirm(RadialInputAction.Approve);

        Assert.True(state.CancelUnless(RadialInputAction.Decline));
        Assert.False(state.IsPending(RadialInputAction.Approve));
        Assert.False(state.CancelUnless(RadialInputAction.Decline));
    }

    [Fact]
    public void ResetCancelsPendingAction()
    {
        var state = new RadialActionConfirmationState();
        state.TryConfirm(RadialInputAction.Approve);

        state.Reset();

        Assert.False(state.IsPending(RadialInputAction.Approve));
    }
}
