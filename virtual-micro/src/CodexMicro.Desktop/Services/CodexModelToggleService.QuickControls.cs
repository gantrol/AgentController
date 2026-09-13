using System.IO;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed partial class CodexModelToggleService
{
    // A direct effort choice is scoped to the model and renderer-visible task
    // captured when the menu opened. It shares the model-toggle transaction gate.
    internal async Task<CodexModelToggleResult> SelectEffortAsync(
        string expectedThreadId,
        string expectedModelId,
        string effort,
        Func<bool> isTargetCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThreadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(effort);
        ArgumentNullException.ThrowIfNull(isTargetCurrent);
        var model = ParseModelId(expectedModelId);
        string? previousEffort = null;
        var entered = false;

        CodexModelToggleResult Failed(string error) => new(
            false, model, model, expectedThreadId,
            previousEffort, previousEffort, Error: error);

        try
        {
            await _toggleGate.WaitAsync(cancellationToken);
            entered = true;
            if (!isTargetCurrent() || !CanTrackSemanticThread(expectedThreadId))
            {
                return Failed("visible-thread-changed");
            }

            await EnsureConnectedAsync(cancellationToken);
            var visible = await WaitForExpectedVisibleThreadAsync(
                expectedThreadId, cancellationToken);
            if (visible.Error is not null || visible.ThreadId != expectedThreadId)
            {
                return Failed(visible.Error ?? "visible-thread-changed");
            }

            var catalog = await CodexDraftModelToggleService.FetchModelCatalogAsync(
                cancellationToken);
            var targetEffort = catalog.ResolveEffort(expectedModelId, effort);
            var context = await ResolveToggleThreadContextAsync(
                expectedThreadId, allowOtherVisibleThreads: true, cancellationToken);
            if (context.Error is not null || context.OwnerClientId is null ||
                context.State is null)
            {
                return Failed(context.Error ?? "thread-state-unavailable");
            }

            previousEffort = context.State.Effort;
            if (!isTargetCurrent() || context.State.ModelId != expectedModelId ||
                ValidateSelectedThreadIsStillVisible(
                    expectedThreadId, allowOtherVisibleThreads: true) is not null)
            {
                return Failed("visible-thread-changed");
            }

            if (previousEffort == targetEffort)
            {
                return new(true, model, model, expectedThreadId,
                    previousEffort, targetEffort);
            }

            var update = await UpdateThreadSettingsWithRetryAsync(
                expectedThreadId, context.OwnerClientId, expectedModelId,
                targetEffort, allowOtherVisibleThreads: true, cancellationToken,
                isTargetCurrent);
            if (!update.Succeeded)
            {
                return Failed(update.Error ?? "thread-settings-rejected");
            }

            RememberEffort(expectedThreadId, expectedModelId, targetEffort);
            ConfirmSuccessfulToggleState(expectedThreadId,
                update.OwnerClientId ?? context.OwnerClientId,
                expectedModelId, targetEffort);
            return new(true, model, model, expectedThreadId,
                previousEffort, targetEffort);
        }
        catch (OperationCanceledException)
        {
            // Do not claim a cancelled write was rolled back. The authoritative
            // stream, not a speculative UI value, owns subsequent presentation.
            throw;
        }
        catch (CodexModelCapabilityException exception)
        {
            return Failed(exception.Error);
        }
        catch (TimeoutException)
        {
            return Failed("ipc-timeout");
        }
        catch (Exception exception) when (exception is IOException or
            InvalidOperationException or ObjectDisposedException or JsonException)
        {
            return Failed("ipc-unavailable");
        }
        finally
        {
            if (entered)
            {
                _toggleGate.Release();
            }
        }
    }
}
