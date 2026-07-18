namespace CodexController.Controllers;

internal sealed class RadialActionConfirmationState
{
    private RadialInputAction? _pendingAction;

    internal bool IsPending(RadialInputAction action) =>
        _pendingAction == action;

    internal bool TryConfirm(RadialInputAction action)
    {
        if (_pendingAction == action)
        {
            _pendingAction = null;
            return true;
        }

        _pendingAction = action;
        return false;
    }

    internal bool TryExpire(RadialInputAction action)
    {
        if (_pendingAction != action)
        {
            return false;
        }

        _pendingAction = null;
        return true;
    }

    internal bool CancelUnless(RadialInputAction action)
    {
        if (_pendingAction is null || _pendingAction == action)
        {
            return false;
        }

        _pendingAction = null;
        return true;
    }

    internal void Reset() => _pendingAction = null;
}
