using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Switches Codex's renderer-owned blank composer through the official
/// App Server config writer and Codex Micro HID bridge. A blank composer has
/// no App Server thread owner, so thread/update cannot address it. A safe
/// downward reasoning nudge first invokes Codex's renderer setter and clears
/// each prewarmed draft. The explicit quick profile is then restored through
/// the official config writer before the foreground blank composer is rebuilt,
/// so the broken dialog Enter bridge is never used and the existing permission
/// profile is left intact.
/// </summary>
internal sealed class CodexDraftModelToggleService
{
    internal const string DraftThreadPrefix = "client-new-thread:";
    internal const string ComposerRebuildDispatchReceipt =
        "config-confirmed-rebuild-dispatched";

    private const string AppServerServiceName = "codex_micro_monitor";
    private const string ModelPickerViewKey =
        "composer-model-picker-menu-view-v1";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ProcessExitTimeout =
        TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan RendererConfigApplyDelay =
        TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan NativeSetterSettleDelay =
        TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan ComposerRebuildMountDelay =
        TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan FinalRendererRefreshDelay =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConfigTransitionTimeout =
        TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DraftReplacementGraceTimeout =
        TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan ConfigPollInterval =
        TimeSpan.FromMilliseconds(100);

    internal sealed record WorkspaceSelection(
        string Cwd,
        IReadOnlyList<string> Roots);

    internal readonly record struct DraftConfig(
        CodexQuickModel Model,
        string? Effort,
        string? EncoderMode = null);

    internal sealed record ModelCatalogEntry(
        string Id,
        CodexQuickModel Model,
        IReadOnlyList<string> SupportedEfforts,
        string? DefaultEffort,
        bool Hidden);

    internal readonly record struct EffortStep(
        bool Clockwise,
        string ExpectedEffort);

    internal readonly record struct TargetEffortProbe(
        string SeedEffort,
        EffortStep ProbeStep,
        EffortStep? FinalStep);

    internal enum ComposerRebuildDispatch
    {
        NotDispatched,
        Dispatched,
        OutcomeUnknown,
    }

    internal Task<CodexModelToggleResult> ToggleAsync(
        CodexQuickModel first,
        string? firstEffort,
        CodexQuickModel second,
        string? secondEffort,
        string draftOperationId,
        Func<bool> isDraftOperationCurrent,
        Func<bool> isFreshDraftCurrent,
        Func<CancellationToken, Task> invalidateRendererConfig,
        Func<bool, CancellationToken, Task<bool>> stepEncoder,
        Func<CancellationToken, Task<ComposerRebuildDispatch>>
            rebuildComposer,
        CancellationToken cancellationToken)
    {
        if (!IsDraftOperationId(draftOperationId))
        {
            throw new ArgumentException(
                "The draft bridge cannot target a real Codex thread.",
                nameof(draftOperationId));
        }

        ArgumentNullException.ThrowIfNull(isDraftOperationCurrent);
        ArgumentNullException.ThrowIfNull(isFreshDraftCurrent);
        ArgumentNullException.ThrowIfNull(invalidateRendererConfig);
        ArgumentNullException.ThrowIfNull(stepEncoder);
        ArgumentNullException.ThrowIfNull(rebuildComposer);
        ValidatePair(first, second);

        return ToggleCoreAsync(
            first,
            firstEffort,
            second,
            secondEffort,
            draftOperationId,
            isDraftOperationCurrent,
            isFreshDraftCurrent,
            invalidateRendererConfig,
            stepEncoder,
            rebuildComposer,
            cancellationToken);
    }

    internal static bool ShouldUseDraftFallback(string? threadId) =>
        IsDraftThreadId(threadId);

    internal static bool CanDispatchComposerRebuild(string? resolvedAction) =>
        string.Equals(
            resolvedAction,
            "newTask",
            StringComparison.Ordinal);

    internal static bool IsDraftOperationId(string? value) =>
        IsDraftThreadId(value) ||
        CodexModelToggleService.IsForegroundDraftOperationId(value);

    internal static bool IsDraftThreadId(string? threadId) =>
        threadId is not null &&
        threadId.Length > DraftThreadPrefix.Length &&
        string.Equals(threadId, threadId.Trim(), StringComparison.Ordinal) &&
        threadId.StartsWith(DraftThreadPrefix, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(threadId[DraftThreadPrefix.Length..]);

    internal static bool IsExpectedDraftCurrent(
        string? expectedDraftThreadId,
        string? currentVisibleThreadId) =>
        IsDraftOperationId(expectedDraftThreadId) &&
        string.Equals(
            expectedDraftThreadId,
            currentVisibleThreadId,
            StringComparison.Ordinal);

    internal static bool IsExpectedDraftCurrent(
        string? expectedDraftThreadId,
        Func<string?>? getCurrentVisibleThreadId)
    {
        if (getCurrentVisibleThreadId is null)
        {
            return false;
        }

        try
        {
            return IsExpectedDraftCurrent(
                expectedDraftThreadId,
                getCurrentVisibleThreadId());
        }
        catch
        {
            return false;
        }
    }

    internal static WorkspaceSelection? ResolveWorkspaceSelection(
        string? globalStateJson,
        Func<string, bool>? directoryExists = null)
    {
        if (string.IsNullOrWhiteSpace(globalStateJson))
        {
            return null;
        }

        directoryExists ??= Directory.Exists;
        try
        {
            using var document = JsonDocument.Parse(globalStateJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var activeRoots = new List<string>();
            if (root.TryGetProperty(
                    "active-workspace-roots",
                    out var activeWorkspaceRoots))
            {
                AddRootValues(
                    activeWorkspaceRoots,
                    activeRoots,
                    directoryExists);
            }

            if (activeRoots.Count > 0)
            {
                return new(activeRoots[0], activeRoots);
            }

            if (!TryReadSelectedProjectId(root, out var projectId) ||
                !root.TryGetProperty("local-projects", out var localProjects) ||
                localProjects.ValueKind != JsonValueKind.Object ||
                !localProjects.TryGetProperty(projectId, out var project) ||
                project.ValueKind != JsonValueKind.Object ||
                !project.TryGetProperty("rootPaths", out var rootPaths))
            {
                return null;
            }

            var projectRoots = new List<string>();
            AddRootValues(rootPaths, projectRoots, directoryExists);
            return projectRoots.Count == 0
                ? null
                : new(projectRoots[0], projectRoots);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static DraftConfig ParseCurrentConfig(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("config", out var config) ||
            config.ValueKind != JsonValueKind.Object)
        {
            return new(CodexQuickModel.Unknown, null);
        }

        var model = TryReadString(config, "model", out var modelId)
            ? CodexModelToggleService.ParseModelId(modelId)
            : CodexQuickModel.Unknown;
        var effort = TryReadString(
                config,
                "model_reasoning_effort",
                out var configuredEffort)
            ? NormalizeEffort(configuredEffort)
            : null;
        string? encoderMode = null;
        if (config.TryGetProperty("desktop", out var desktop) &&
            desktop.ValueKind == JsonValueKind.Object &&
            desktop.TryGetProperty(
                "codex-micro-layout",
                out var microLayout) &&
            TryReadString(microLayout, "encoderMode", out var configuredMode))
        {
            encoderMode = configuredMode;
        }

        return new(model, effort, encoderMode);
    }

    internal static bool TryReadManagedNewThreadRequirement(
        JsonElement result,
        out bool hasManagedNewThreadRequirement)
    {
        hasManagedNewThreadRequirement = false;
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("requirements", out var requirements))
        {
            return false;
        }

        if (requirements.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (requirements.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!requirements.TryGetProperty("models", out var models))
        {
            return true;
        }

        if (models.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (models.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        hasManagedNewThreadRequirement =
            models.TryGetProperty("newThread", out var newThread) &&
            newThread.ValueKind is not (
                JsonValueKind.Null or JsonValueKind.Undefined);
        return true;
    }

    internal static bool TryReadAuthMethod(
        JsonElement result,
        out string authMethod) =>
        TryReadString(result, "authMethod", out authMethod);

    internal static IReadOnlyList<ModelCatalogEntry> ParseModelCatalog(
        JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<ModelCatalogEntry>();
        foreach (var item in data.EnumerateArray())
        {
            if (!TryReadString(item, "model", out var id))
            {
                continue;
            }

            var efforts = new List<string>();
            if (item.TryGetProperty(
                    "supportedReasoningEfforts",
                    out var supported) &&
                supported.ValueKind == JsonValueKind.Array)
            {
                foreach (var effortEntry in supported.EnumerateArray())
                {
                    if (TryReadString(
                            effortEntry,
                            "reasoningEffort",
                            out var value) &&
                        NormalizeEffort(value) is { } effort &&
                        !efforts.Contains(effort, StringComparer.Ordinal))
                    {
                        efforts.Add(effort);
                    }
                }
            }

            var defaultEffort = TryReadString(
                    item,
                    "defaultReasoningEffort",
                    out var configuredDefault)
                ? NormalizeEffort(configuredDefault)
                : null;
            var hidden = item.TryGetProperty("hidden", out var hiddenValue) &&
                hiddenValue.ValueKind is JsonValueKind.True;
            models.Add(new(
                id,
                CodexModelToggleService.ParseModelId(id),
                efforts,
                defaultEffort,
                hidden));
        }

        return models;
    }

    internal static string? ParseModelPickerMenuView(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(source);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty(
                    "electron-persisted-atom-state",
                    out var atoms) ||
                !TryReadString(atoms, ModelPickerViewKey, out var view))
            {
                return null;
            }

            return view is "simple" or "advanced" ? view : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static TargetEffortProbe ResolveTargetEffortProbe(
        IReadOnlyList<string> supportedEfforts,
        string targetEffort)
    {
        ArgumentNullException.ThrowIfNull(supportedEfforts);
        var target = NormalizeEffort(targetEffort) ??
            throw new ArgumentOutOfRangeException(nameof(targetEffort));
        var normalized = supportedEfforts
            .Select(NormalizeEffort)
            .Where(value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var targetIndex = Array.IndexOf(normalized, target);
        if (targetIndex < 0 || normalized.Length < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(supportedEfforts));
        }

        if (targetIndex > 0)
        {
            var finalStep = string.Equals(
                    target,
                    "ultra",
                    StringComparison.Ordinal)
                ? (EffortStep?)null
                : new EffortStep(
                    Clockwise: false,
                    ExpectedEffort: target);
            return new(
                SeedEffort: target,
                ProbeStep: new(
                    Clockwise: true,
                    ExpectedEffort: normalized[targetIndex - 1]),
                FinalStep: finalStep);
        }

        // At the lowest supported level, seed the adjacent level and prove the
        // target with one downward step. Never step upward into Ultra.
        if (string.Equals(normalized[1], "ultra", StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(supportedEfforts));
        }

        return new(
            SeedEffort: normalized[1],
            ProbeStep: new(
                Clockwise: true,
                ExpectedEffort: target),
            FinalStep: null);
    }

    internal static DraftConfig ParseTopLevelUserConfig(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return new(CodexQuickModel.Unknown, null);
        }

        string? modelId = null;
        string? effort = null;
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                break;
            }

            if (TryReadTopLevelTomlString(trimmed, "model", out var model))
            {
                modelId = model;
            }
            else if (TryReadTopLevelTomlString(
                         trimmed,
                         "model_reasoning_effort",
                         out var configuredEffort))
            {
                effort = NormalizeEffort(configuredEffort);
            }
        }

        return new(
            CodexModelToggleService.ParseModelId(modelId),
            effort);
    }

    private static async Task<CodexModelToggleResult> ToggleCoreAsync(
        CodexQuickModel first,
        string? firstEffort,
        CodexQuickModel second,
        string? secondEffort,
        string draftOperationId,
        Func<bool> isDraftOperationCurrent,
        Func<bool> isFreshDraftCurrent,
        Func<CancellationToken, Task> invalidateRendererConfig,
        Func<bool, CancellationToken, Task<bool>> stepEncoder,
        Func<CancellationToken, Task<ComposerRebuildDispatch>>
            rebuildComposer,
        CancellationToken cancellationToken)
    {
#if DEBUG
        var started = Stopwatch.GetTimestamp();
#endif
        var previous = CodexQuickModel.Unknown;
        string? previousEffort = null;
        string? previousEncoderMode = null;
        AppServerSession? session = null;
        var encoderModeChanged = false;
        var modelConfigChanged = false;
        var rendererMutationConfirmed = false;
        var composerRebuildDispatched = false;
        var completed = false;

        CodexModelToggleResult Complete(CodexModelToggleResult result)
        {
#if DEBUG
            CodexModelToggleDiagnostics.Record(
                result,
                Stopwatch.GetElapsedTime(started));
#endif
            return result;
        }

        CodexModelToggleResult Fail(string error, string? detail = null)
        {
            if (rendererMutationConfirmed &&
                error is not
                    "draft-composer-rebuild-outcome-unknown" and not
                    "draft-renderer-mutation-outcome-unknown")
            {
                detail = detail is null
                    ? error
                    : $"{error}: {detail}";
                error = "draft-renderer-mutation-outcome-unknown";
            }

            return Complete(Failure(
                previous,
                previousEffort,
                draftOperationId,
                error,
                detail));
        }

        async Task<bool> SendEncoderStepAsync(
            bool clockwise,
            CancellationToken token)
        {
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return false;
            }

            return await stepEncoder(clockwise, token);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanStartDraftOperation(isDraftOperationCurrent))
            {
                return Fail(CodexWindowActivator.IsForeground()
                    ? "draft-context-changed"
                    : "draft-codex-not-foreground");
            }

            var codexHome = ResolveCodexHome();
            var userConfigPath = Path.Combine(codexHome, "config.toml");
            var globalStatePath = Path.Combine(
                codexHome,
                ".codex-global-state.json");
            var globalState = await ReadGlobalStateAsync(
                globalStatePath,
                cancellationToken);
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail("draft-context-changed");
            }

            if (globalState is null)
            {
                return Fail("draft-global-state-read-failed");
            }

            var workspace = ResolveWorkspaceSelection(globalState);
            if (workspace is null)
            {
                return Fail("draft-workspace-unavailable");
            }

            var cliPath = ResolveCliExecutable();
            if (cliPath is null)
            {
                return Fail("draft-cli-unavailable");
            }

            var sessionStart = await StartInitializedSessionAsync(
                cliPath,
                workspace.Cwd,
                cancellationToken);
            session = sessionStart.Session;
            if (session is null)
            {
                return Fail(
                    sessionStart.ExceptionDetail is null
                        ? "draft-app-server-unavailable"
                        : "draft-app-server-failed",
                    sessionStart.ExceptionDetail ??
                        sessionStart.InitializeError);
            }

            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail("draft-context-changed");
            }

            var configRead = await RequestWithTimeoutAsync(
                session,
                "config/read",
                new
                {
                    cwd = workspace.Cwd,
                    includeLayers = false,
                },
                cancellationToken);
            if (!configRead.Succeeded)
            {
                return Fail("draft-config-read-failed", configRead.Error);
            }

            var requirementsRead = await RequestWithTimeoutAsync(
                session,
                "configRequirements/read",
                new { },
                cancellationToken);
            if (!requirementsRead.Succeeded ||
                !TryReadManagedNewThreadRequirement(
                    requirementsRead.Result,
                    out var hasManagedNewThreadRequirement))
            {
                return Fail(
                    "draft-config-requirements-unavailable",
                    requirementsRead.Error);
            }

            if (hasManagedNewThreadRequirement)
            {
                return Fail("draft-managed-new-thread-unsupported");
            }

            var authStatus = await RequestWithTimeoutAsync(
                session,
                "getAuthStatus",
                new
                {
                    includeToken = false,
                    refreshToken = false,
                },
                cancellationToken);
            if (!authStatus.Succeeded ||
                !TryReadAuthMethod(
                    authStatus.Result,
                    out var authMethod))
            {
                return Fail(
                    "draft-auth-status-unavailable",
                    authStatus.Error);
            }

            if (authMethod.Equals(
                    "copilot",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Fail("draft-copilot-new-thread-unsupported");
            }

            var current = ParseCurrentConfig(configRead.Result);
            previous = current.Model;
            previousEffort = current.Effort;
            previousEncoderMode = current.EncoderMode;
            var target = CodexModelToggleService.ResolveToggleTarget(
                previous,
                first,
                second);
            var targetModelId = CodexModelToggleService.ToModelId(target);
            var configuredEffort = target == first
                ? firstEffort
                : secondEffort;
            var targetEffort = CodexModelToggleService.ResolveTargetEffort(
                targetModelId,
                configuredEffort);

            var modelList = await RequestWithTimeoutAsync(
                session,
                "model/list",
                new
                {
                    includeHidden = false,
                    cursor = (string?)null,
                    limit = 100,
                },
                cancellationToken);
            if (!modelList.Succeeded)
            {
                return Fail("draft-model-list-failed", modelList.Error);
            }

            var models = ParseModelCatalog(modelList.Result);
            var targetIndex = models.ToList().FindIndex(model =>
                string.Equals(
                    model.Id,
                    targetModelId,
                    StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0)
            {
                return Fail("draft-target-model-unavailable");
            }

            var targetModel = models[targetIndex];
            if (!targetModel.SupportedEfforts.Contains(
                    targetEffort,
                    StringComparer.Ordinal))
            {
                return Fail("draft-target-effort-unavailable");
            }

            TargetEffortProbe targetProbe;
            try
            {
                targetProbe = ResolveTargetEffortProbe(
                    targetModel.SupportedEfforts,
                    targetEffort);
            }
            catch (ArgumentOutOfRangeException)
            {
                return Fail("draft-effort-state-unavailable");
            }

#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-native-reasoning-nudge-start",
                new
                {
                    previous = previous.ToString(),
                    previousEffort,
                    target = target.ToString(),
                    targetEffort,
                    targetProbe,
                });
#endif

            // App Server discovery can consume a meaningful part of the
            // renderer-evidence budget. Recheck the original admission proof
            // immediately before the first config mutation; after mutation
            // starts, the live draft predicate—not wall-clock age—guards the
            // rest of the transaction and its rollback.
            if (!CanStartDraftOperation(isDraftOperationCurrent) ||
                !CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail("draft-context-changed");
            }

            // Route the physical dial to reasoning and seed the requested
            // model in one atomic config write. A single targeted renderer
            // refresh is enough for both values and avoids a redundant two-
            // second apply window in which the user can leave the draft.
            // The safe downward native step below proves that this exact
            // foreground composer loaded both values. The native setter also
            // clears every renderer prewarm. This replaces the unconfirmable
            // blank-to-blank NEW action.
            encoderModeChanged = true;
            modelConfigChanged = true;
            var seedWrite = await WriteDraftConfigAndEncoderModeAsync(
                session,
                targetModelId,
                targetProbe.SeedEffort,
                "reasoning",
                cancellationToken);
            if (!TryReadConfigWriteStatus(
                    seedWrite,
                    out var seedStatus,
                    out var seedError) ||
                seedStatus != "ok")
            {
                return Fail(
                    "draft-target-seed-write-failed",
                    seedError ?? seedStatus);
            }

#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-atomic-seed-write",
                new
                {
                    targetModelId,
                    effort = targetProbe.SeedEffort,
                    encoderMode = "reasoning",
                });
#endif
            await invalidateRendererConfig(cancellationToken);
            var transition = await WaitForPersistedConfigAsync(
                target,
                targetProbe.SeedEffort,
                userConfigPath,
                isFreshDraftCurrent,
                ConfigTransitionTimeout,
                cancellationToken);
            if (transition.Result != ConfigTransitionResult.Matched)
            {
                return Fail(transition.Result ==
                    ConfigTransitionResult.ContextChanged
                        ? "draft-context-changed"
                        : "draft-target-seed-timeout");
            }

            await Task.Delay(RendererConfigApplyDelay, cancellationToken);
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail("draft-context-changed");
            }

            if (!await SendEncoderStepAsync(
                    targetProbe.ProbeStep.Clockwise,
                    cancellationToken))
            {
                return Fail("draft-target-probe-rejected");
            }

            transition = await WaitForPersistedConfigAsync(
                target,
                targetProbe.ProbeStep.ExpectedEffort,
                userConfigPath,
                isFreshDraftCurrent,
                ConfigTransitionTimeout,
                cancellationToken);
            if (transition.Result != ConfigTransitionResult.Matched)
            {
                return Fail(transition.Result ==
                    ConfigTransitionResult.ContextChanged
                        ? "draft-context-changed"
                        : "draft-target-probe-timeout");
            }
            rendererMutationConfirmed = true;

            await Task.Delay(NativeSetterSettleDelay, cancellationToken);
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail("draft-context-changed");
            }

#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-target-probe-confirmed",
                new
                {
                    targetModelId,
                    effort = targetProbe.ProbeStep.ExpectedEffort,
                    prewarmClearedByNativeSetter = true,
                });
#endif

            var nativeFinalConfirmation = false;
            if (targetProbe.FinalStep is { } finalStep)
            {
                if (!await SendEncoderStepAsync(
                        finalStep.Clockwise,
                        cancellationToken))
                {
                    return Fail("draft-target-final-step-rejected");
                }

                transition = await WaitForPersistedConfigAsync(
                    target,
                    finalStep.ExpectedEffort,
                    userConfigPath,
                    isFreshDraftCurrent,
                    ConfigTransitionTimeout,
                    cancellationToken);
                if (transition.Result != ConfigTransitionResult.Matched)
                {
                    return Fail(transition.Result ==
                        ConfigTransitionResult.ContextChanged
                            ? "draft-context-changed"
                            : "draft-target-final-step-timeout");
                }

                await Task.Delay(
                    NativeSetterSettleDelay,
                    cancellationToken);
                if (!CanContinueFreshDraft(isFreshDraftCurrent))
                {
                    return Fail("draft-context-changed");
                }

                nativeFinalConfirmation = true;
            }

            // Luna/Max arrives here through a confirmed xhigh -> Max native
            // step. Sol/Ultra cannot: Max -> Ultra opens Codex's ordinary
            // modal, so Ultra is restored passively after the confirmed
            // Ultra -> Max probe. Full access is never changed.
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail("draft-context-changed");
            }

            var finalWrite = await WriteDraftConfigAndEncoderModeAsync(
                session,
                targetModelId,
                targetEffort,
                previousEncoderMode,
                cancellationToken);
            if (!TryReadConfigWriteStatus(
                    finalWrite,
                    out var finalStatus,
                    out var finalError) ||
                finalStatus != "ok")
            {
                return Fail(
                    "draft-final-write-failed",
                    finalError ?? finalStatus);
            }

#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-explicit-final-config-write",
                new
                {
                    targetModelId,
                    targetEffort,
                    encoderMode = previousEncoderMode,
                    permissionProfileChanged = false,
                });
#endif
            encoderModeChanged = false;
            await invalidateRendererConfig(cancellationToken);
            transition = await WaitForPersistedConfigAsync(
                target,
                targetEffort,
                userConfigPath,
                isFreshDraftCurrent,
                ConfigTransitionTimeout,
                cancellationToken);
            if (transition.Result != ConfigTransitionResult.Matched)
            {
                return Fail(transition.Result ==
                    ConfigTransitionResult.ContextChanged
                        ? "draft-context-changed"
                        : "draft-final-config-timeout");
            }
            await Task.Delay(RendererConfigApplyDelay, cancellationToken);
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail("draft-context-changed");
            }

#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-final-config-confirmed-before-rebuild",
                new { targetModelId, targetEffort });
#endif

            // The reasoning setter above clears renderer prewarms, but it does
            // not prove that the already-mounted blank ComposerScope adopted
            // the target model. Rebuild that same foreground blank composer
            // only after the exact final config is durable. Codex Micro HID has
            // no semantic completion ACK, so the callback confirms transport
            // only; the foreground/draft guards and persisted config are
            // rechecked after the renderer's known mount window.
            var rebuildDispatch = await rebuildComposer(cancellationToken);
            if (rebuildDispatch == ComposerRebuildDispatch.NotDispatched)
            {
                return Fail("draft-composer-rebuild-rejected");
            }
            composerRebuildDispatched = true;

            // OutcomeUnknown means the HID report may already have reached
            // Codex. Treat it as a commit boundary so disk and a possibly
            // rebuilt composer are not split by a one-sided rollback, but do
            // not call the toggle successful or renew renderer evidence.
            if (rebuildDispatch == ComposerRebuildDispatch.OutcomeUnknown)
            {
                return Fail(
                    "draft-composer-rebuild-outcome-unknown",
                    "hid-outcome-unknown");
            }

#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-composer-rebuild-dispatched",
                new
                {
                    targetModelId,
                    targetEffort,
                    semanticAcknowledgement = false,
                });
#endif
            await Task.Delay(ComposerRebuildMountDelay, cancellationToken);
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail(
                    "draft-composer-rebuild-outcome-unknown",
                    "draft-context-changed");
            }

            await invalidateRendererConfig(cancellationToken);
            await Task.Delay(FinalRendererRefreshDelay, cancellationToken);
            if (!CanContinueFreshDraft(isFreshDraftCurrent))
            {
                return Fail(
                    "draft-composer-rebuild-outcome-unknown",
                    "draft-context-changed");
            }

            transition = await WaitForPersistedConfigAsync(
                target,
                targetEffort,
                userConfigPath,
                isFreshDraftCurrent,
                ConfigTransitionTimeout,
                cancellationToken);
            if (transition.Result != ConfigTransitionResult.Matched)
            {
                return Fail(
                    "draft-composer-rebuild-outcome-unknown",
                    transition.Result == ConfigTransitionResult.ContextChanged
                        ? "draft-context-changed"
                        : "draft-final-config-timeout");
            }

            completed = true;
#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-final-config-applied-after-composer-rebuild",
                new
                {
                    targetModelId,
                    targetEffort,
                    nativeFinalConfirmation,
                    semanticAcknowledgement = false,
                    permissionProfileChanged = false,
                });
#endif
            return Complete(Success(
                previous,
                previousEffort,
                target,
                targetEffort,
                draftOperationId,
                ComposerRebuildDispatchReceipt));
        }
        catch (OperationCanceledException) when (composerRebuildDispatched)
        {
            return Fail(
                "draft-composer-rebuild-outcome-unknown",
                "cancelled-after-rebuild-dispatch");
        }
        catch (OperationCanceledException) when (rendererMutationConfirmed)
        {
            return Fail(
                "draft-renderer-mutation-outcome-unknown",
                "cancelled-after-native-probe");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                Win32Exception or
                JsonException or
                ObjectDisposedException)
        {
            return Fail(
                composerRebuildDispatched
                    ? "draft-composer-rebuild-outcome-unknown"
                    : "draft-app-server-failed",
                StableExceptionDetail(exception));
        }
        finally
        {
            // Once the native reasoning setter is confirmed—or NEW is
            // accepted by the HID transport—the mounted composer may already
            // differ from the previous config. A later local observation
            // failure cannot safely roll only config.toml back; that would
            // split disk and composer state. Keep the last durable state and
            // report an unknown outcome.
            if (!completed &&
                !rendererMutationConfirmed &&
                !composerRebuildDispatched &&
                session is not null &&
                modelConfigChanged)
            {
                await TryRestoreModelConfigAsync(
                    session,
                    previous,
                    previousEffort);
            }

            if (session is not null && encoderModeChanged)
            {
                _ = await TryRestoreEncoderModeAsync(
                    session,
                    previousEncoderMode);
            }

            if (!completed && session is not null)
            {
                try
                {
                    await invalidateRendererConfig(CancellationToken.None);
                }
                catch
                {
                    // The restored config remains authoritative on disk.
                }
            }

            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }
    }

    private static bool TryReadConfigWriteStatus(
        RpcResponse response,
        out string status,
        out string? error)
    {
        status = string.Empty;
        error = response.Error;
        if (!response.Succeeded ||
            response.Result.ValueKind != JsonValueKind.Object ||
            !TryReadString(response.Result, "status", out status) ||
            status is not ("ok" or "okOverridden"))
        {
            error ??= response.Result.ValueKind == JsonValueKind.Undefined
                ? null
                : response.Result.GetRawText();
            return false;
        }

        return true;
    }

    private static Task<RpcResponse>
        WriteDraftConfigAndEncoderModeAsync(
            AppServerSession session,
            string modelId,
            string effort,
            string? encoderMode,
            CancellationToken cancellationToken) =>
        RequestWithTimeoutAsync(
            session,
            "config/batchWrite",
            new
            {
                edits = new object[]
                {
                    new
                    {
                        keyPath = "model",
                        value = modelId,
                        mergeStrategy = "upsert",
                    },
                    new
                    {
                        keyPath = "model_reasoning_effort",
                        value = effort,
                        mergeStrategy = "upsert",
                    },
                    new
                    {
                        keyPath =
                            "desktop.codex-micro-layout.encoderMode",
                        value = encoderMode,
                        mergeStrategy = encoderMode is null
                            ? "replace"
                            : "upsert",
                    },
                },
                filePath = (string?)null,
                expectedVersion = (string?)null,
                reloadUserConfig = true,
            },
            cancellationToken);

    private static async Task<bool> TryRestoreEncoderModeAsync(
        AppServerSession session,
        string? previousEncoderMode)
    {
        try
        {
            var response = await RequestWithTimeoutAsync(
                session,
                "config/batchWrite",
                new
                {
                    edits = new object[]
                    {
                        new
                        {
                            keyPath =
                                "desktop.codex-micro-layout.encoderMode",
                            value = previousEncoderMode,
                            mergeStrategy = previousEncoderMode is null
                                ? "replace"
                                : "upsert",
                        },
                    },
                    reloadUserConfig = true,
                },
                CancellationToken.None);
            return TryReadConfigWriteStatus(
                    response,
                    out var status,
                    out _) &&
                status == "ok";
        }
        catch
        {
            return false;
        }
    }

    private static async Task TryRestoreModelConfigAsync(
        AppServerSession session,
        CodexQuickModel previous,
        string? previousEffort)
    {
        if (previous == CodexQuickModel.Unknown)
        {
            return;
        }

        try
        {
            _ = await RequestWithTimeoutAsync(
                session,
                "config/batchWrite",
                new
                {
                    edits = new object[]
                    {
                        new
                        {
                            keyPath = "model",
                            value = CodexModelToggleService.ToModelId(
                                previous),
                            mergeStrategy = "upsert",
                        },
                        new
                        {
                            keyPath = "model_reasoning_effort",
                            value = previousEffort,
                            mergeStrategy = previousEffort is null
                                ? "replace"
                                : "upsert",
                        },
                    },
                    reloadUserConfig = true,
                },
                CancellationToken.None);
        }
        catch
        {
            // Best effort only. The original failure remains primary.
        }
    }

    private static async Task<ConfigProbe> WaitForPersistedConfigAsync(
        CodexQuickModel expectedModel,
        string? expectedEffort,
        string userConfigPath,
        Func<bool> isFreshDraftCurrent,
        TimeSpan timeoutDuration,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(timeoutDuration);
        var latest = new DraftConfig(CodexQuickModel.Unknown, null);
        long? draftReplacementStarted = null;
        while (!timeout.IsCancellationRequested)
        {
            latest = TryReadPersistedUserConfig(userConfigPath);
            var freshDraftCurrent =
                CanContinueFreshDraft(isFreshDraftCurrent);
            if (CanAcceptPersistedConfig(
                    latest,
                    expectedModel,
                    expectedEffort,
                    freshDraftCurrent))
            {
                return new(ConfigTransitionResult.Matched, latest);
            }

            // Selecting a model clears Codex's prewarmed draft. During that
            // renderer-owned handoff the visible draft identity can change
            // before config.toml becomes observable. A matching write is
            // accepted only after the blank context is current again, with a
            // short no-input grace period for that replacement. A real
            // navigation that stays outside the blank composer aborts.
            if (freshDraftCurrent)
            {
                draftReplacementStarted = null;
            }
            else
            {
                draftReplacementStarted ??= Stopwatch.GetTimestamp();
                if (Stopwatch.GetElapsedTime(
                        draftReplacementStarted.Value) >=
                    DraftReplacementGraceTimeout)
                {
#if DEBUG
                    CodexModelToggleDiagnostics.RecordStage(
                        "draft-config-context-changed",
                        new
                        {
                            expectedModel = expectedModel.ToString(),
                            expectedEffort,
                            latestModel = latest.Model.ToString(),
                            latestEffort = latest.Effort,
                        });
#endif
                    return new(ConfigTransitionResult.ContextChanged, latest);
                }
            }

            try
            {
                await Task.Delay(ConfigPollInterval, timeout.Token);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(ConfigTransitionResult.TimedOut, latest);
    }

    internal static bool CanAcceptPersistedConfig(
        DraftConfig latest,
        CodexQuickModel expectedModel,
        string? expectedEffort,
        bool freshDraftCurrent) =>
        freshDraftCurrent &&
        latest.Model == expectedModel &&
        (expectedEffort is null ||
            string.Equals(
                latest.Effort,
                expectedEffort,
                StringComparison.Ordinal));

    private static DraftConfig TryReadPersistedUserConfig(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            return ParseTopLevelUserConfig(reader.ReadToEnd());
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new(CodexQuickModel.Unknown, null);
        }
    }

    private static CodexModelToggleResult Success(
        CodexQuickModel previous,
        string? previousEffort,
        CodexQuickModel current,
        string? currentEffort,
        string threadId,
        string? detail = null) =>
        new(
            Succeeded: true,
            Previous: previous,
            Current: current,
            ThreadId: threadId,
            PreviousEffort: previousEffort,
            CurrentEffort: currentEffort,
            Detail: detail);

    private static CodexModelToggleResult Failure(
        CodexQuickModel previous,
        string? previousEffort,
        string? threadId,
        string error,
        string? detail = null) =>
        new(
            Succeeded: false,
            Previous: previous,
            Current: previous,
            ThreadId: threadId,
            PreviousEffort: previousEffort,
            CurrentEffort: previousEffort,
            Error: error,
            Detail: detail);

    private static bool CanStartDraftOperation(
        Func<bool> isDraftOperationCurrent)
    {
        try
        {
            return CodexWindowActivator.IsForeground() &&
                isDraftOperationCurrent();
        }
        catch
        {
            return false;
        }
    }

    private static bool CanContinueFreshDraft(
        Func<bool> isFreshDraftCurrent)
    {
        try
        {
            return CodexWindowActivator.IsForeground() &&
                isFreshDraftCurrent();
        }
        catch
        {
            return false;
        }
    }

    private static async Task<RpcResponse> InitializeAsync(
        AppServerSession session,
        CancellationToken cancellationToken)
    {
        var initialize = await RequestWithTimeoutAsync(
            session,
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = AppServerServiceName,
                    title = "Codex Micro Monitor",
                    version = "0.3.0",
                },
                capabilities = new
                {
                    experimentalApi = true,
                },
            },
            cancellationToken);
        if (!initialize.Succeeded)
        {
            return initialize;
        }

        await session.SendNotificationAsync(
            "initialized",
            new { },
            cancellationToken);
        return initialize;
    }

    private static async Task<AppServerStartResult>
        StartInitializedSessionAsync(
            string cliPath,
            string workingDirectory,
            CancellationToken cancellationToken)
    {
        string? initializeError = null;
        string? exceptionDetail = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            AppServerSession? candidate = null;
            try
            {
                candidate = AppServerSession.Start(
                    cliPath,
                    workingDirectory);
                var initialize = await InitializeAsync(
                    candidate,
                    cancellationToken);
                if (initialize.Succeeded)
                {
                    return new(candidate, null, null);
                }

                initializeError = initialize.Error;
                exceptionDetail = null;
#if DEBUG
                CodexModelToggleDiagnostics.RecordStage(
                    "draft-app-server-start-attempt-failed",
                    new
                    {
                        attempt,
                        willRetry = attempt == 1,
                        kind = "initialize-response",
                        detail = initializeError,
                    });
#endif
            }
            catch (OperationCanceledException)
            {
                if (candidate is not null)
                {
                    await candidate.DisposeAsync();
                }

                throw;
            }
            catch (Exception exception) when (
                exception is IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    Win32Exception or
                    JsonException or
                    ObjectDisposedException)
            {
                initializeError = null;
                exceptionDetail = StableExceptionDetail(exception);
#if DEBUG
                CodexModelToggleDiagnostics.RecordStage(
                    "draft-app-server-start-attempt-failed",
                    new
                    {
                        attempt,
                        willRetry = attempt == 1,
                        kind = exception.GetType().Name,
                        detail = NormalizeDiagnosticText(
                            exception.Message),
                    });
#endif
            }

            if (candidate is not null)
            {
                await candidate.DisposeAsync();
            }

            if (attempt == 1)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(150),
                    cancellationToken);
            }
        }

        return new(null, initializeError, exceptionDetail);
    }

    private static async Task<RpcResponse> RequestWithTimeoutAsync(
        AppServerSession session,
        string method,
        object parameters,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        return await session.SendRequestAsync(
            method,
            parameters,
            timeout.Token);
    }

    private static string ResolveCodexHome()
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Path.Combine(userProfile, ".codex");
        }

        var expanded = Environment.ExpandEnvironmentVariables(
            configured.Trim());
        if (expanded == "~")
        {
            return userProfile;
        }

        if (expanded.StartsWith("~/", StringComparison.Ordinal) ||
            expanded.StartsWith("~\\", StringComparison.Ordinal))
        {
            expanded = Path.Combine(userProfile, expanded[2..]);
        }

        return Path.GetFullPath(expanded);
    }

    private static string? ResolveCliExecutable()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        try
        {
            if (Directory.Exists(binRoot))
            {
                var installed = Directory
                    .EnumerateFiles(
                        binRoot,
                        "codex.exe",
                        SearchOption.AllDirectories)
                    .Where(File.Exists)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (installed is not null)
                {
                    return installed;
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Try a directly installed executable on PATH below.
        }

        foreach (var pathEntry in (Environment.GetEnvironmentVariable("PATH") ??
                     string.Empty).Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(pathEntry))
            {
                continue;
            }

            try
            {
                var candidate = Path.Combine(
                    pathEntry.Trim().Trim('"'),
                    "codex.exe");
                if (Path.IsPathFullyQualified(candidate) &&
                    File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
            {
                // Ignore malformed PATH entries.
            }
        }

        return null;
    }

    private static async Task<string?> ReadGlobalStateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 6;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true);
                return await reader.ReadToEndAsync(cancellationToken);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
#if DEBUG
                CodexModelToggleDiagnostics.RecordStage(
                    "draft-global-state-read-attempt-failed",
                    new
                    {
                        attempt,
                        willRetry = attempt < maximumAttempts,
                        kind = exception.GetType().Name,
                    });
#endif
                if (attempt == maximumAttempts)
                {
                    return null;
                }

                var retryDelayMilliseconds = 35 *
                    (1 << (attempt - 1));
                await Task.Delay(
                    TimeSpan.FromMilliseconds(retryDelayMilliseconds),
                    cancellationToken);
            }
        }

        return null;
    }

    private static bool TryReadSelectedProjectId(
        JsonElement root,
        out string projectId)
    {
        projectId = string.Empty;
        if (!root.TryGetProperty("selected-project", out var selected))
        {
            return false;
        }

        if (selected.ValueKind == JsonValueKind.String)
        {
            projectId = selected.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(projectId);
        }

        return selected.ValueKind == JsonValueKind.Object &&
            TryReadString(selected, "projectId", out projectId);
    }

    private static void AddRootValues(
        JsonElement value,
        List<string> roots,
        Func<string, bool> directoryExists)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            AddRoot(value.GetString(), roots, directoryExists);
            return;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                AddRoot(item.GetString(), roots, directoryExists);
            }
        }
    }

    private static void AddRoot(
        string? value,
        List<string> roots,
        Func<string, bool> directoryExists)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(
                value.Trim());
            if (!Path.IsPathFullyQualified(expanded))
            {
                return;
            }

            var normalized = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(expanded));
            if (!directoryExists(normalized) ||
                roots.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            roots.Add(normalized);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException or
                System.Security.SecurityException)
        {
            // Invalid state is ignored; no guessed workspace is substituted.
        }
    }

    private static string? NormalizeEffort(string? effort) =>
        effort?.Trim().ToLowerInvariant() is
            "none" or "minimal" or "low" or "medium" or "high" or
                "xhigh" or "max" or "ultra"
                ? effort.Trim().ToLowerInvariant()
                : null;

    private static bool TryReadTopLevelTomlString(
        string line,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!line.StartsWith(key, StringComparison.Ordinal) ||
            line.Length <= key.Length ||
            !char.IsWhiteSpace(line[key.Length]))
        {
            return false;
        }

        var equals = line.IndexOf('=', key.Length);
        if (equals < 0)
        {
            return false;
        }

        var encoded = line[(equals + 1)..].TrimStart();
        if (encoded.Length < 2 || encoded[0] != '"')
        {
            return false;
        }

        var closingQuote = encoded.IndexOf('"', 1);
        if (closingQuote <= 1)
        {
            return false;
        }

        value = encoded[1..closingQuote].Trim();
        return value.Length > 0;
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(
                value = property.GetString()?.Trim() ?? string.Empty);
    }

    private static string StableExceptionDetail(Exception exception) =>
        exception.GetType().Name;

    private static string NormalizeDiagnosticText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = string.Join(
            " ",
            value.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));
        return normalized.Length <= 320
            ? normalized
            : normalized[..320];
    }

    private static void ValidatePair(
        CodexQuickModel first,
        CodexQuickModel second)
    {
        if (first == CodexQuickModel.Unknown ||
            second == CodexQuickModel.Unknown ||
            first == second)
        {
            throw new ArgumentException(
                "Quick-model slots must contain two distinct known models.");
        }
    }

    private readonly record struct RpcResponse(
        bool Succeeded,
        JsonElement Result,
        string? Error = null);

    private readonly record struct AppServerStartResult(
        AppServerSession? Session,
        string? InitializeError,
        string? ExceptionDetail);

    private enum ConfigTransitionResult
    {
        Matched,
        ContextChanged,
        TimedOut,
    }

    private readonly record struct ConfigProbe(
        ConfigTransitionResult Result,
        DraftConfig Current);

    private sealed class AppServerSession : IAsyncDisposable
    {
        private const int MaximumStderrTailLength = 800;

        private readonly Process _process;
        private readonly object _stderrSync = new();
        private readonly StringBuilder _stderrTail = new();
        private readonly Task _stderrDrain;
        private long _nextRequestId;
        private int _disposed;

        private AppServerSession(Process process)
        {
            _process = process;
            _stderrDrain = DrainStderrAsync(process.StandardError);
        }

        internal static AppServerSession Start(
            string cliPath,
            string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "Codex App Server did not start.");
                }

                return new AppServerSession(process);
            }
            catch
            {
                process.Dispose();
                throw;
            }
        }

        internal async Task<RpcResponse> SendRequestAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            var requestId = Interlocked.Increment(ref _nextRequestId);
            var request = new Dictionary<string, object?>
            {
                ["id"] = requestId,
                ["method"] = method,
                ["params"] = parameters,
            };
            var json = JsonSerializer.Serialize(request);
            try
            {
                await _process.StandardInput.WriteLineAsync(
                    json.AsMemory(),
                    cancellationToken);
                await _process.StandardInput.FlushAsync(cancellationToken);
            }
            catch (IOException exception)
            {
                await AwaitTerminationDiagnosticsAsync();
                throw new IOException(
                    "Codex App Server request write failed " +
                        BuildTransportDiagnostics() + ".",
                    exception);
            }

            while (true)
            {
                string? line;
                try
                {
                    line = await _process.StandardOutput.ReadLineAsync(
                        cancellationToken);
                }
                catch (IOException exception)
                {
                    await AwaitTerminationDiagnosticsAsync();
                    throw new IOException(
                        "Codex App Server response read failed " +
                            BuildTransportDiagnostics() + ".",
                        exception);
                }

                if (line is null)
                {
                    await AwaitTerminationDiagnosticsAsync();
                    throw new IOException(
                        BuildClosedResponseStreamMessage());
                }

                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (!ResponseIdEquals(root, requestId))
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    return new(false, default, ReadRpcError(error));
                }

                if (!root.TryGetProperty("result", out var result))
                {
                    return new(false, default, "rpc-result-missing");
                }

                return new(true, result.Clone());
            }
        }

        internal async Task SendNotificationAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            var notification = new Dictionary<string, object?>
            {
                ["method"] = method,
                ["params"] = parameters,
            };
            var json = JsonSerializer.Serialize(notification);
            await _process.StandardInput.WriteLineAsync(
                json.AsMemory(),
                cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _process.StandardInput.Close();
            }
            catch
            {
                // The server may already have exited.
            }

            try
            {
                using var timeout = new CancellationTokenSource(
                    ProcessExitTimeout);
                await _process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                TryKillOwnedProcess();
            }
            catch (InvalidOperationException)
            {
                // The process was already gone.
            }

            if (!_process.HasExited)
            {
                TryKillOwnedProcess();
            }

            try
            {
                await _stderrDrain.WaitAsync(TimeSpan.FromMilliseconds(300));
            }
            catch
            {
                // Diagnostic output is deliberately discarded in all builds.
            }

            _process.Dispose();
        }

        private void TryKillOwnedProcess()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(500);
                }
            }
            catch
            {
                // Only the short-lived process created above is targeted.
            }
        }

        private static bool ResponseIdEquals(
            JsonElement root,
            long expectedId) =>
            root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.Number &&
            id.TryGetInt64(out var actualId) &&
            actualId == expectedId;

        private static string ReadRpcError(JsonElement error)
        {
            if (TryReadString(error, "message", out var message))
            {
                return message.Length <= 240
                    ? message
                    : message[..240];
            }

            return "rpc-error";
        }

        private string BuildClosedResponseStreamMessage()
            => "Codex App Server closed its response stream " +
                BuildTransportDiagnostics() + ".";

        private string BuildTransportDiagnostics()
        {
            var exitCode = "running";
            try
            {
                if (_process.HasExited)
                {
                    exitCode = _process.ExitCode.ToString(
                        CultureInfo.InvariantCulture);
                }
            }
            catch (InvalidOperationException)
            {
                exitCode = "unavailable";
            }

            string stderr;
            lock (_stderrSync)
            {
                stderr = NormalizeDiagnosticText(_stderrTail.ToString());
            }

            return string.IsNullOrWhiteSpace(stderr)
                ? $"(exitCode={exitCode})"
                : $"(exitCode={exitCode}; stderr={stderr})";
        }

        private async Task AwaitTerminationDiagnosticsAsync()
        {
            try
            {
                await _stderrDrain.WaitAsync(
                    TimeSpan.FromMilliseconds(120));
            }
            catch (TimeoutException)
            {
                // The process can still be alive after closing one stream.
            }
            catch (IOException)
            {
                // The diagnostic stream itself can close during teardown.
            }
        }

        private async Task DrainStderrAsync(StreamReader reader)
        {
            var buffer = new char[1024];
            try
            {
                int count;
                while ((count = await reader.ReadAsync(buffer.AsMemory())) > 0)
                {
                    lock (_stderrSync)
                    {
                        _stderrTail.Append(buffer, 0, count);
                        if (_stderrTail.Length > MaximumStderrTailLength)
                        {
                            _stderrTail.Remove(
                                0,
                                _stderrTail.Length -
                                    MaximumStderrTailLength);
                        }
                    }
                }
            }
            catch
            {
                // Process teardown can close the redirected stream first.
            }
        }
    }
}
