namespace CodexMicro.Desktop.Services;

internal sealed partial class CodexDraftComposerModelSelector
{
    private CodexModelCatalog? _reasoningCatalog;

    internal Task<bool> CanWatchNativeUltraAsync(
        IntPtr window, Func<bool> isCurrent, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            EnsureCurrent(window, isCurrent, cancellationToken);
            return !HasUltraWarning(RequireRoot(window));
        }, cancellationToken);

    internal Task<bool> TryConfirmNativeUltraAsync(
        IntPtr window, Func<bool> isCurrent, Action onPending,
        CancellationToken cancellationToken) => Task.Run(() =>
        {
            EnsureCurrent(window, isCurrent, cancellationToken);
            var root = RequireRoot(window);
            if (!HasUltraWarning(root))
            {
                return false;
            }
            EnsureNoUnexpectedDialog(root);
            onPending();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            WaitForUltraDecision(window, true, isCurrent, timeout.Token);
            return true;
        }, cancellationToken);

    internal async Task<(CodexQuickModel Model, string Effort)> StepReasoningAsync(
        IntPtr foregroundWindow,
        int direction,
        bool autoConfirmUltraFullAccess,
        Func<bool> isCurrent,
        CancellationToken cancellationToken)
    {
        var catalog = _reasoningCatalog ?? CodexModelCatalog.Load();
        if (!catalog.IsFresh)
        {
            catalog = await CodexDraftModelToggleService.FetchModelCatalogAsync(cancellationToken);
        }
        _reasoningCatalog = catalog;
        return await Task.Run(() =>
        {
            var previousCatalog = _operationCatalog;
            _operationCatalog = catalog;
            try
            {
                EnsureCurrent(foregroundWindow, isCurrent, cancellationToken);
                var root = RequireRoot(foregroundWindow);
                if (HasUltraWarning(root))
                {
                    throw new DraftUiException("draft-ui-decision-already-pending");
                }

                EnsureNoUnexpectedDialog(root);
                var current = ReadVerifiedSelection(
                    foregroundWindow, false, isCurrent, cancellationToken);
                var efforts = catalog.Find(current.Model.Id)?.SupportedEfforts ?? [];
                var index = efforts.ToList().FindIndex(effort =>
                    string.Equals(effort, current.Effort, StringComparison.OrdinalIgnoreCase));
                if (!catalog.IsFresh || index < 0)
                {
                    throw new DraftUiException("draft-ui-power-state-unavailable");
                }

                var target = efforts[Math.Clamp(index + Math.Sign(direction), 0, efforts.Count - 1)];
                if (!string.Equals(target, current.Effort, StringComparison.OrdinalIgnoreCase))
                {
                    SelectEffort(foregroundWindow, current.Model, target,
                        autoConfirmUltraFullAccess, isCurrent, cancellationToken);
                    VerifyFinalSelection(foregroundWindow, current.Model, target,
                        autoConfirmUltraFullAccess, isCurrent, cancellationToken);
                }

                return (current.Model, target);
            }
            finally
            {
                _operationCatalog = previousCatalog;
            }
        }, cancellationToken);
    }
}
