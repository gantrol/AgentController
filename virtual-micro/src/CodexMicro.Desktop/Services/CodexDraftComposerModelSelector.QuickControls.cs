namespace CodexMicro.Desktop.Services;

internal sealed record CodexComposerQuickSelection(string ModelId, string? Effort);

internal sealed partial class CodexDraftComposerModelSelector
{
    // This read never opens the native picker or injects input. A missing or
    // ambiguous composer stays unknown rather than inheriting config defaults.
    internal Task<CodexComposerQuickSelection?> ReadQuickSelectionAsync(
        IntPtr foregroundWindow,
        CodexModelCatalog catalog,
        Func<bool> isCurrent,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        var previousCatalog = _operationCatalog;
        _operationCatalog = catalog;
        try
        {
            EnsureCurrent(foregroundWindow, isCurrent, cancellationToken);
            var root = RequireRoot(foregroundWindow);
            EnsureNoUnexpectedDialog(root);
            if (HasUltraWarning(root))
            {
                return null;
            }

            var selection = ReadTriggerSelection(WaitForTrigger(
                foregroundWindow, isCurrent, cancellationToken));
            EnsureCurrent(foregroundWindow, isCurrent, cancellationToken);
            return selection.Model == CodexQuickModel.Unknown
                ? null
                : new CodexComposerQuickSelection(selection.Model.Id, selection.Effort);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            _operationCatalog = previousCatalog;
        }
    }, cancellationToken);

    internal Task<CodexModelToggleResult> SelectQuickEffortAsync(
        IntPtr foregroundWindow,
        string expectedModelId,
        string effort,
        string draftOperationId,
        CodexModelCatalog catalog,
        Func<bool> isCurrent,
        CancellationToken cancellationToken) => Task.Run(() =>
    {
        var previousCatalog = _operationCatalog;
        _operationCatalog = catalog;
        var model = CodexQuickModel.FromId(expectedModelId);
        string? previousEffort = null;
        try
        {
            if (!CodexDraftModelToggleService.IsDraftOperationId(draftOperationId))
            {
                throw new ArgumentException("A renderer-owned draft is required.",
                    nameof(draftOperationId));
            }

            EnsureCurrent(foregroundWindow, isCurrent, cancellationToken);
            var root = RequireRoot(foregroundWindow);
            EnsureNoUnexpectedDialog(root);
            if (HasUltraWarning(root))
            {
                throw new DraftUiException("draft-ui-decision-already-pending");
            }

            var selection = ReadTriggerSelection(WaitForTrigger(
                foregroundWindow, isCurrent, cancellationToken));
            previousEffort = selection.Effort;
            if (selection.Model != model)
            {
                throw new DraftUiException("draft-ui-selection-unavailable");
            }

            var targetEffort = catalog.ResolveEffort(expectedModelId, effort);
            SelectEffort(foregroundWindow, model, targetEffort,
                autoConfirmUltraFullAccess: false, isCurrent, cancellationToken);
            VerifyFinalSelection(foregroundWindow, model, targetEffort,
                autoConfirmUltraFullAccess: false, isCurrent, cancellationToken);
            return new CodexModelToggleResult(true, model, model,
                draftOperationId, previousEffort, targetEffort,
                Detail: CodexDraftModelToggleService.NativeTargetConfirmationReceipt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var error = exception is DraftUiException ui ? ui.Error
                : exception is CodexModelCapabilityException capability ? capability.Error
                : "draft-ui-selection-unavailable";
            return new CodexModelToggleResult(false, model, model,
                draftOperationId, previousEffort, previousEffort, Error: error);
        }
        finally
        {
            _operationCatalog = previousCatalog;
        }
    }, cancellationToken);
}
