namespace CodexMicro.Desktop.Services;

internal sealed partial class CodexModelToggleService
{
    internal async Task<CodexThreadModelState> StepCurrentThreadEffortAsync(
        string threadId, int direction, CodexModelCatalog catalog,
        Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        await _toggleGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            if (!isCurrent() || !CanTrackSemanticThread(threadId))
            {
                throw new InvalidOperationException("visible-thread-changed");
            }

            var context = await ResolveToggleThreadContextAsync(
                threadId, allowOtherVisibleThreads: true, cancellationToken);
            var state = context.State;
            if (context.Error is not null || context.OwnerClientId is null || state is null)
            {
                throw new InvalidOperationException(context.Error ?? "thread-state-unavailable");
            }

            var model = catalog.Find(state.ModelId);
            var efforts = model?.SupportedEfforts.ToList() ?? [];
            var index = efforts.FindIndex(effort => string.Equals(
                effort, state.Effort ?? model?.DefaultEffort, StringComparison.OrdinalIgnoreCase));
            if (!catalog.IsFresh || model is not { Hidden: false } || index < 0)
            {
                throw new InvalidOperationException("model-effort-unavailable");
            }

            var target = efforts[Math.Clamp(index + Math.Sign(direction), 0, efforts.Count - 1)];
            bool TargetIsCurrent() => isCurrent() &&
                CurrentThreadState is { } latest && latest.ThreadId == threadId &&
                latest.ModelId == state.ModelId;
            if (!TargetIsCurrent())
            {
                throw new InvalidOperationException("visible-thread-changed");
            }
            if (target == state.Effort)
            {
                return state;
            }

            var result = await UpdateThreadSettingsWithRetryAsync(threadId,
                context.OwnerClientId, state.ModelId, target,
                allowOtherVisibleThreads: true, cancellationToken, TargetIsCurrent);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Error ?? "thread-settings-rejected");
            }
            RememberEffort(threadId, state.ModelId, target);
            ConfirmSuccessfulToggleState(threadId, result.OwnerClientId ?? context.OwnerClientId,
                state.ModelId, target);
            return new(threadId, state.ModelId, target);
        }
        finally
        {
            _toggleGate.Release();
        }
    }
}
