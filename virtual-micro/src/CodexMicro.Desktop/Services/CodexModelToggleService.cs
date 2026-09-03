using System.Buffers.Binary;
using System.Collections.Concurrent;
#if DEBUG
using System.Diagnostics;
#endif
using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexModelToggleResult(
    bool Succeeded,
    CodexQuickModel Previous,
    CodexQuickModel Current,
    string? ThreadId = null,
    string? PreviousEffort = null,
    string? CurrentEffort = null,
    string? Error = null,
    string? Detail = null);

internal sealed record CodexThreadModelState(
    string ThreadId,
    string ModelId,
    string? Effort);

internal readonly record struct CodexThreadUnreadResult(
    string ThreadId,
    bool WasPossiblySent,
    bool Confirmed,
    string? Error = null);

internal readonly record struct CodexThreadStateApplyResult(
    bool Applied,
    bool RequiresSnapshot,
    CodexThreadModelState? State);

/// <summary>
/// Accumulates the small, authoritative portion of a streamed conversation
/// state that Codex Micro needs. The IPC stream uses Immer-style patches, but
/// newer senders may serialize paths as JSON Pointers, so both forms are
/// accepted. Unrelated patches are deliberately ignored while their revision
/// still advances.
/// </summary>
internal sealed class CodexThreadModelStateAccumulator
{
    private string? _latestModel;
    private string? _latestReasoningEffort;
    private string? _settingsModel;
    private string? _settingsEffort;
    private bool _hasSettingsEffort;
    private bool _hasSnapshot;
    private bool _hasUnrevisionedConfirmation;
    private string? _unrevisionedConfirmedModel;
    private string? _unrevisionedConfirmedEffort;

    internal CodexThreadModelStateAccumulator(
        string threadId,
        string ownerClientId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerClientId);
        ThreadId = threadId;
        OwnerClientId = ownerClientId;
    }

    internal string ThreadId { get; }

    internal string OwnerClientId { get; }

    internal long? Revision { get; private set; }

    internal CodexThreadStateApplyResult ApplyChange(JsonElement change)
    {
        if (!TryReadString(change, "type", out var changeType))
        {
            return default;
        }

        if (changeType == "snapshot")
        {
            if (!TryReadRevision(change, "revision", out var revision) ||
                !change.TryGetProperty(
                    "conversationState",
                    out var conversationState) ||
                conversationState.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            if (_hasSnapshot &&
                Revision is long currentRevision &&
                revision <= currentRevision)
            {
                // Duplicate or delayed snapshots must not roll back patches
                // or a settings update that was already confirmed locally.
                return default;
            }

            ReadConversationState(conversationState);
            if (_hasUnrevisionedConfirmation &&
                !StateMatches(
                    BuildState(),
                    _unrevisionedConfirmedModel,
                    _unrevisionedConfirmedEffort))
            {
                // The owner can emit one pre-update snapshot after already
                // acknowledging the update. Keep the confirmed settings but
                // adopt this revision as the patch baseline; otherwise every
                // subsequent patch would be ignored forever.
                RestoreUnrevisionedConfirmation();
                ClearUnrevisionedConfirmation();
                Revision = revision;
                _hasSnapshot = true;
                return new(true, false, BuildState());
            }

            ClearUnrevisionedConfirmation();
            Revision = revision;
            _hasSnapshot = true;
            return new(true, false, BuildState());
        }

        if (changeType != "patches")
        {
            return default;
        }

        if (!_hasSnapshot || Revision is null)
        {
            return default;
        }

        if (!TryReadRevision(change, "baseRevision", out var baseRevision) ||
            !TryReadRevision(change, "revision", out var revisionValue) ||
            !change.TryGetProperty("patches", out var patches) ||
            patches.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        if (baseRevision != Revision.Value ||
            baseRevision == long.MaxValue ||
            revisionValue != baseRevision + 1)
        {
            Reset();
            return new(false, true, null);
        }

        foreach (var patch in patches.EnumerateArray())
        {
            ApplyPatch(patch);
        }

        Revision = revisionValue;
        return new(true, false, BuildState());
    }

    internal CodexThreadModelState ConfirmSettings(
        string modelId,
        string? effort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _latestModel = modelId;
        _settingsModel = modelId;
        _latestReasoningEffort = effort;
        _settingsEffort = effort;
        _hasSettingsEffort = true;
        _hasUnrevisionedConfirmation = !_hasSnapshot || Revision is null;
        _unrevisionedConfirmedModel = _hasUnrevisionedConfirmation
            ? modelId
            : null;
        _unrevisionedConfirmedEffort = _hasUnrevisionedConfirmation
            ? effort
            : null;
        return new(ThreadId, modelId, effort);
    }

    private void ReadConversationState(JsonElement state)
    {
        _latestModel = ReadOptionalString(state, "latestModel");
        _latestReasoningEffort = ReadOptionalString(
            state,
            "latestReasoningEffort");
        _settingsModel = null;
        _settingsEffort = null;
        _hasSettingsEffort = false;
        if (state.TryGetProperty("latestThreadSettings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object)
        {
            _settingsModel = ReadOptionalString(settings, "model");
            _hasSettingsEffort = settings.TryGetProperty(
                "effort",
                out var effort);
            _settingsEffort = _hasSettingsEffort
                ? ReadOptionalString(effort)
                : null;
        }
    }

    private void ApplyPatch(JsonElement patch)
    {
        if (!TryReadString(patch, "op", out var operation) ||
            operation is not ("add" or "replace" or "remove") ||
            !TryReadPatchPath(patch, out var path))
        {
            return;
        }

        var removesValue = operation == "remove";
        var value = default(JsonElement);
        var hasValue = !removesValue &&
            patch.TryGetProperty("value", out value);
        if (!removesValue && !hasValue)
        {
            return;
        }

        if (path.Length == 0)
        {
            if (removesValue || value.ValueKind != JsonValueKind.Object)
            {
                ClearStateFields();
            }
            else
            {
                ReadConversationState(value);
            }

            return;
        }

        if (path.Length == 1)
        {
            switch (path[0])
            {
                case "latestModel":
                    _latestModel = removesValue
                        ? null
                        : ReadOptionalString(value);
                    return;
                case "latestReasoningEffort":
                    _latestReasoningEffort = removesValue
                        ? null
                        : ReadOptionalString(value);
                    return;
                case "latestThreadSettings":
                    _settingsModel = null;
                    _settingsEffort = null;
                    _hasSettingsEffort = false;
                    if (!removesValue && value.ValueKind == JsonValueKind.Object)
                    {
                        _settingsModel = ReadOptionalString(value, "model");
                        _hasSettingsEffort = value.TryGetProperty(
                            "effort",
                            out var effort);
                        _settingsEffort = _hasSettingsEffort
                            ? ReadOptionalString(effort)
                            : null;
                    }

                    return;
            }
        }

        if (path.Length == 2 && path[0] == "latestThreadSettings")
        {
            switch (path[1])
            {
                case "model":
                    _settingsModel = removesValue
                        ? null
                        : ReadOptionalString(value);
                    return;
                case "effort":
                    _hasSettingsEffort = !removesValue;
                    _settingsEffort = removesValue
                        ? null
                        : ReadOptionalString(value);
                    return;
            }
        }
    }

    private CodexThreadModelState? BuildState()
    {
        var modelId = !string.IsNullOrWhiteSpace(_settingsModel)
            ? _settingsModel
            : _latestModel;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        var effort = _hasSettingsEffort
            ? _settingsEffort
            : _latestReasoningEffort;
        return new(ThreadId, modelId, effort);
    }

    private static bool StateMatches(
        CodexThreadModelState? state,
        string? modelId,
        string? effort) =>
        state is not null &&
        !string.IsNullOrWhiteSpace(modelId) &&
        state.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(state.Effort, effort, StringComparison.OrdinalIgnoreCase);

    private void RestoreUnrevisionedConfirmation()
    {
        _latestModel = _unrevisionedConfirmedModel;
        _settingsModel = _unrevisionedConfirmedModel;
        _latestReasoningEffort = _unrevisionedConfirmedEffort;
        _settingsEffort = _unrevisionedConfirmedEffort;
        _hasSettingsEffort = true;
    }

    private void ClearUnrevisionedConfirmation()
    {
        _hasUnrevisionedConfirmation = false;
        _unrevisionedConfirmedModel = null;
        _unrevisionedConfirmedEffort = null;
    }

    private void Reset()
    {
        ClearStateFields();
        Revision = null;
        _hasSnapshot = false;
        ClearUnrevisionedConfirmation();
    }

    private void ClearStateFields()
    {
        _latestModel = null;
        _latestReasoningEffort = null;
        _settingsModel = null;
        _settingsEffort = null;
        _hasSettingsEffort = false;
    }

    private static bool TryReadPatchPath(
        JsonElement patch,
        out string[] path)
    {
        path = [];
        if (!patch.TryGetProperty("path", out var pathElement))
        {
            return false;
        }

        if (pathElement.ValueKind == JsonValueKind.Array)
        {
            var segments = new List<string>();
            foreach (var segment in pathElement.EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.String)
                {
                    segments.Add(segment.GetString() ?? string.Empty);
                }
                else if (segment.ValueKind == JsonValueKind.Number)
                {
                    segments.Add(segment.GetRawText());
                }
                else
                {
                    return false;
                }
            }

            path = [.. segments];
            return true;
        }

        if (pathElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var pointer = pathElement.GetString() ?? string.Empty;
        if (pointer.Length == 0)
        {
            return true;
        }

        if (pointer[0] != '/')
        {
            return false;
        }

        path = pointer[1..]
            .Split('/')
            .Select(segment => segment
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal))
            .ToArray();
        return true;
    }

    private static bool TryReadRevision(
        JsonElement element,
        string propertyName,
        out long revision)
    {
        revision = default;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt64(out revision) &&
            revision >= 0;
    }

    private static string? ReadOptionalString(
        JsonElement element,
        string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var property)
            ? ReadOptionalString(property)
            : null;

    private static string? ReadOptionalString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()
            : null;

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }
}

/// <summary>
/// Changes the next-turn settings on the App Server already owned by Codex
/// Desktop. The bridge uses Codex's versioned cross-window IPC protocol; it
/// never opens or drives the model picker and never guesses from recent tasks.
/// </summary>
internal sealed class CodexModelToggleService : IAsyncDisposable
{
    internal readonly record struct VisibleThreadSelection(
        string? VisibleThreadId,
        string? SemanticThreadId);

    internal readonly record struct ForegroundDraftLease(
        string ClientId,
        long VisibilityGeneration,
        string OperationId,
        string? RendererClientId = null,
        long RendererDraftGeneration = 0,
        IntPtr ForegroundWindow = default,
        DateTimeOffset RendererDraftObservedAt = default);

    internal enum FollowerSignalIntent
    {
        TrackedTrue,
        WaiterTrue,
        Release,
    }

    private readonly record struct ToggleThreadContext(
        string? OwnerClientId,
        CodexThreadModelState? State,
        string? Error,
        string? Detail = null);

    private readonly record struct SettingsUpdateResult(
        bool Succeeded,
        string? OwnerClientId,
        string? Error,
        string? Detail = null);

    private sealed class SnapshotWaiter
    {
        internal SnapshotWaiter(
            string threadId,
            string ownerClientId,
            TaskCompletionSource<CodexThreadModelState> completion)
        {
            OwnerClientId = ownerClientId;
            Completion = completion;
            Accumulator = new(threadId, ownerClientId);
        }

        internal string OwnerClientId { get; }

        internal TaskCompletionSource<CodexThreadModelState> Completion { get; }

        internal CodexThreadModelStateAccumulator Accumulator { get; }
    }

    private sealed class VisibilityRefreshState
    {
        internal VisibilityRefreshState(IEnumerable<string> requestedThreadIds)
        {
            RequestedThreadIds = new(
                requestedThreadIds,
                StringComparer.Ordinal);
        }

        internal HashSet<string> RequestedThreadIds { get; }

        internal Dictionary<string, string> ReportedThreadByClient { get; } =
            new(StringComparer.Ordinal);
    }

    internal readonly record struct RendererDraftEvidence(
        long Generation,
        DateTimeOffset ObservedAt,
        IntPtr ForegroundWindow);

    private readonly record struct RendererDraftContinuity(
        DateTimeOffset ObservedAt,
        IntPtr ForegroundWindow);

    private readonly record struct ForegroundDraftNavigationExpectation(
        Guid Token,
        DateTimeOffset DispatchedAt,
        IntPtr ForegroundWindow);

    private const string PipeName = "codex-ipc";
    private const string LocalHostId = "local";
    private const string InitialClientId = "initializing-client";
    internal const string ForegroundDraftOperationPrefix =
        "foreground-new-task:";
    private const int MaximumFrameBytes = 256 * 1024 * 1024;
    private static readonly TimeSpan ConnectTimeout =
        TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CurrentThreadTimeout =
        TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan UnreadConfirmationTimeout =
        TimeSpan.FromSeconds(2);
    private static readonly TimeSpan VisibleThreadStabilityWindow =
        TimeSpan.FromMilliseconds(140);
    private static readonly TimeSpan VisibilityRefreshCollectionWindow =
        TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan ForegroundDraftStabilityWindow =
        TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan RendererDraftEvidenceLifetime =
        TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ForegroundDraftNavigationLifetime =
        TimeSpan.FromSeconds(5);
    // Leave enough time for the slow blank-composer path to finish before the
    // causal following=false evidence itself becomes stale. A lease admitted
    // near the 30-second cleanup boundary can otherwise outlive the route it
    // proved while App Server startup and renderer refreshes are still running.
    internal static readonly TimeSpan ForegroundDraftLeaseAdmissionLifetime =
        TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SnapshotTimeout =
        TimeSpan.FromSeconds(4);
    private static readonly TimeSpan MaximumTrackingRetryDelay =
        TimeSpan.FromSeconds(10);

    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _toggleGate = new(1, 1);
    private readonly SemaphoreSlim _visibilityRefreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>
        _pendingRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _visibleThreadByClient =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, RendererDraftEvidence>
        _rendererDraftEvidenceByClient = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RendererDraftContinuity>
        _rendererDraftContinuityByClient = new(StringComparer.Ordinal);
    private readonly Dictionary<IntPtr, string>
        _foregroundRendererClientByWindow = [];
    private readonly Dictionary<string, SnapshotWaiter>
        _snapshotWaiters = new(StringComparer.Ordinal);
    private readonly CodexThreadModelEffortStore _effortStore;
    private readonly CodexUnreadStateReader _unreadStateReader;
    private TaskCompletionSource<bool> _visibleThreadChanged = NewSignal();
    private NamedPipeClientStream? _pipe;
    private Task? _readerTask;
    private string? _clientId;
    private string? _selectedVisibleThreadId;
    private string? _trackedThreadId;
    private string? _trackedOwnerClientId;
    private CodexThreadModelStateAccumulator? _trackedStateAccumulator;
    private CodexThreadModelState? _currentThreadState;
    private VisibilityRefreshState? _visibilityRefresh;
    private ForegroundDraftNavigationExpectation?
        _foregroundDraftNavigationExpectation;
    private int _trackingGeneration;
    private long _visibilityGeneration;
    private long _rendererDraftEvidenceGeneration;
    private int _disposed;

    internal CodexModelToggleService(
        CodexThreadModelEffortStore? effortStore = null,
        CodexUnreadStateReader? unreadStateReader = null)
    {
        _effortStore = effortStore ?? new CodexThreadModelEffortStore();
        _unreadStateReader = unreadStateReader ?? new CodexUnreadStateReader();
    }

    /// <summary>
    /// The authoritative model state for the one locally-visible task, or
    /// <see langword="null"/> while no task is uniquely visible or its stream
    /// is being synchronized.
    /// </summary>
    internal CodexThreadModelState? CurrentThreadState
    {
        get
        {
            lock (_stateSync)
            {
                return _currentThreadState;
            }
        }
    }

    /// <summary>
    /// The exact renderer-visible identity when it is unique. Unlike
    /// <see cref="CurrentThreadState"/>, this includes new-task draft IDs;
    /// <see langword="null"/> means visibility is absent or ambiguous.
    /// </summary>
    internal string? CurrentVisibleThreadId
    {
        get
        {
            lock (_stateSync)
            {
                return ResolveVisibleThreadSelection(
                    _visibleThreadByClient.Values).VisibleThreadId;
            }
        }
    }

    internal string? CurrentForegroundVisibleThreadId(
        IntPtr foregroundWindow)
    {
        lock (_stateSync)
        {
            return ResolveForegroundVisibleThreadSelectionLocked(
                foregroundWindow).VisibleThreadId;
        }
    }

    internal async Task<VisibleThreadSelection>
        RefreshForegroundVisibleThreadSelectionAsync(
            IntPtr foregroundWindow,
            CancellationToken cancellationToken = default)
    {
        var aggregate = await RefreshVisibleThreadSelectionAsync(
            cancellationToken);
        if (foregroundWindow != IntPtr.Zero &&
            !CodexWindowActivator.IsForegroundWindow(foregroundWindow))
        {
            return aggregate;
        }

        lock (_stateSync)
        {
            return ResolveForegroundVisibleThreadSelectionLocked(
                foregroundWindow);
        }
    }

#if DEBUG
    internal object CaptureVisibilityDiagnostics()
    {
        lock (_stateSync)
        {
            return new
            {
                connected = _pipe is { IsConnected: true },
                clientId = _clientId,
                following = _visibleThreadByClient
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new
                    {
                        sourceClientId = pair.Key,
                        conversationId = pair.Value,
                    })
                    .ToArray(),
                draftEvidence = _rendererDraftEvidenceByClient
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new
                    {
                        sourceClientId = pair.Key,
                        pair.Value.Generation,
                        foregroundWindow =
                            pair.Value.ForegroundWindow.ToInt64(),
                        ageMilliseconds = Math.Max(
                            0,
                            (DateTimeOffset.UtcNow - pair.Value.ObservedAt)
                                .TotalMilliseconds),
                    })
                    .ToArray(),
                draftContinuity = _rendererDraftContinuityByClient
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new
                    {
                        sourceClientId = pair.Key,
                        foregroundWindow =
                            pair.Value.ForegroundWindow.ToInt64(),
                        ageMilliseconds = Math.Max(
                            0,
                            (DateTimeOffset.UtcNow - pair.Value.ObservedAt)
                                .TotalMilliseconds),
                    })
                    .ToArray(),
                foregroundRenderers = _foregroundRendererClientByWindow
                    .OrderBy(pair => pair.Key.ToInt64())
                    .Select(pair => new
                    {
                        foregroundWindow = pair.Key.ToInt64(),
                        sourceClientId = pair.Value,
                    })
                    .ToArray(),
                pendingDraftNavigation =
                    _foregroundDraftNavigationExpectation is { } navigation
                        ? new
                        {
                            foregroundWindow =
                                navigation.ForegroundWindow.ToInt64(),
                            ageMilliseconds = Math.Max(
                                0,
                                (DateTimeOffset.UtcNow -
                                    navigation.DispatchedAt)
                                    .TotalMilliseconds),
                        }
                        : null,
                refreshingVisibility = _visibilityRefresh is not null,
            };
        }
    }
#endif

    /// <summary>
    /// Re-samples the renderer visibility map before choosing the real-thread
    /// or blank-draft path. Codex only replays <c>following=true</c> when a
    /// renderer still follows the requested conversation, so replacing the
    /// accumulated map with those directed replies removes stale clients
    /// without guessing which of multiple live renderers is foreground.
    /// </summary>
    internal async Task<VisibleThreadSelection>
        RefreshVisibleThreadSelectionAsync(
            CancellationToken cancellationToken = default)
    {
        var gateEntered = false;
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            await _visibilityRefreshGate.WaitAsync(cancellationToken);
            gateEntered = true;

            VisibilityRefreshState refresh;
            lock (_stateSync)
            {
                var requestedThreadIds = _visibleThreadByClient.Values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (requestedThreadIds.Length == 0)
                {
                    return ResolveVisibleThreadSelection(
                        _visibleThreadByClient.Values);
                }

                refresh = new(requestedThreadIds);
                _visibilityRefresh = refresh;
                // A refresh started after a draft lease was captured, so that
                // lease can no longer prove a stable renderer snapshot.
                InvalidateForegroundDraftLeasesLocked();
            }

            try
            {
                foreach (var threadId in refresh.RequestedThreadIds)
                {
                    await SendBroadcastAsync(
                        "thread-stream-following-status-requested",
                        version: 1,
                        new
                        {
                            conversationId = threadId,
                            hostId = LocalHostId,
                        },
                        targetClientIds: null,
                        cancellationToken);
                }

                await Task.Delay(
                    VisibilityRefreshCollectionWindow,
                    cancellationToken);
            }
            catch
            {
                lock (_stateSync)
                {
                    if (ReferenceEquals(_visibilityRefresh, refresh))
                    {
                        _visibilityRefresh = null;
                    }
                }

                throw;
            }

            var changed = false;
            VisibleThreadSelection selection;
            lock (_stateSync)
            {
                if (!ReferenceEquals(_visibilityRefresh, refresh))
                {
                    return ResolveVisibleThreadSelection(
                        _visibleThreadByClient.Values);
                }

                changed = ReplaceVisibleThreadMap(
                    _visibleThreadByClient,
                    refresh.ReportedThreadByClient);
                _visibilityRefresh = null;
                if (changed)
                {
                    InvalidateForegroundDraftLeasesLocked();
                    PulseVisibleThreadChangedLocked();
                }

                selection = ResolveVisibleThreadSelection(
                    _visibleThreadByClient.Values);
            }

            if (changed)
            {
                RefreshCurrentThreadTracking();
            }

            return selection;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return CaptureVisibleThreadSelection();
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                InvalidDataException or
                ObjectDisposedException)
        {
            return CaptureVisibleThreadSelection();
        }
        finally
        {
            if (gateEntered)
            {
                _visibilityRefreshGate.Release();
            }
        }
    }

    /// <summary>
    /// Raised outside service locks. IPC updates can arrive on a background
    /// reader thread, so WPF subscribers should marshal through Dispatcher.
    /// </summary>
    internal event Action<CodexThreadModelState?>? CurrentThreadStateChanged;

    internal async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                OperationCanceledException or
                InvalidDataException or
                ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Marks one existing local Codex thread unread through the desktop
    /// renderer's versioned coordination channel. This is the same broadcast
    /// Codex uses for its own Mark unread action; the keypad never edits the
    /// persisted global-state file directly.
    /// </summary>
    internal async Task<CodexThreadUnreadResult> MarkThreadUnreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        var normalizedThreadId = threadId.Trim();
        var wasPossiblySent = false;
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            wasPossiblySent = true;
            await SendMessageAsync(
                CreateThreadReadStateBroadcast(
                    ReadClientId(),
                    normalizedThreadId,
                    hasUnreadTurn: true),
                cancellationToken);
            var confirmed = await _unreadStateReader.WaitUntilUnreadAsync(
                normalizedThreadId,
                UnreadConfirmationTimeout,
                cancellationToken);
            return new CodexThreadUnreadResult(
                normalizedThreadId,
                WasPossiblySent: true,
                Confirmed: confirmed,
                Error: confirmed
                    ? null
                    : "Codex did not confirm the unread state.");
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                OperationCanceledException or
                InvalidDataException or
                InvalidOperationException or
                ObjectDisposedException)
        {
            return new CodexThreadUnreadResult(
                normalizedThreadId,
                wasPossiblySent,
                Confirmed: false,
                Error: exception.Message);
        }
    }

    internal static JsonElement CreateThreadReadStateBroadcast(
        string sourceClientId,
        string threadId,
        bool hasUnreadTurn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return JsonSerializer.SerializeToElement(new
        {
            type = "broadcast",
            method = "thread-read-state-changed",
            sourceClientId = sourceClientId.Trim(),
            targetClientIds = (string[]?)null,
            @params = new
            {
                conversationId = threadId.Trim(),
                hostId = LocalHostId,
                hasUnreadTurn,
            },
            version = 2,
        });
    }

    /// <summary>
    /// Invalidates the renderer's cached user config after an official
    /// config/batchWrite performed by the blank-draft path. The broadcast is
    /// the same IPC notification used by Codex itself; it does
    /// not synthesize keyboard or UI-automation input.
    /// </summary>
    internal async Task InvalidateUserSavedConfigAsync(
        string rendererClientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rendererClientId);
        await EnsureConnectedAsync(cancellationToken);
        await SendBroadcastAsync(
            "query-cache-invalidate",
            version: 0,
            new
            {
                queryKey = new object[] { "user-saved-config" },
            },
            targetClientIds: [rendererClientId],
            cancellationToken);
    }

    /// <summary>
    /// Notifies every connected Codex renderer after the keypad settings
    /// surface writes the shared user config directly.
    /// </summary>
    internal async Task BroadcastUserSavedConfigInvalidationAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);
        await SendBroadcastAsync(
            "query-cache-invalidate",
            version: 0,
            new
            {
                queryKey = new object[] { "user-saved-config" },
            },
            targetClientIds: null,
            cancellationToken);
    }

    /// <summary>
    /// Captures a short-lived proof for a foreground blank draft. The strongest
    /// proof is a renderer that explicitly stopped following a real task and
    /// did not start following another one; this remains valid even while a
    /// background renderer follows an older task. An empty visibility map by
    /// itself is deliberately insufficient because it also occurs during IPC
    /// reconnects before real-task state is replayed.
    /// </summary>
    internal Guid? BeginForegroundDraftNavigation(
        IntPtr foregroundWindow,
        DateTimeOffset dispatchedAt)
    {
        if (foregroundWindow == IntPtr.Zero ||
            dispatchedAt == default ||
            !CodexWindowActivator.IsForegroundWindow(foregroundWindow))
        {
            return null;
        }

        lock (_stateSync)
        {
            if (_pipe is not { IsConnected: true } ||
                !IsInitializedClientId(_clientId))
            {
                return null;
            }

            var token = Guid.NewGuid();
            _foregroundDraftNavigationExpectation = new(
                token,
                dispatchedAt,
                foregroundWindow);
            return token;
        }
    }

    internal void CancelForegroundDraftNavigation(Guid token)
    {
        lock (_stateSync)
        {
            if (_foregroundDraftNavigationExpectation is { } navigation &&
                navigation.Token == token)
            {
                _foregroundDraftNavigationExpectation = null;
            }
        }
    }

    internal async Task<ForegroundDraftLease?>
        CaptureForegroundDraftLeaseAsync(
            CancellationToken cancellationToken = default,
            IntPtr foregroundWindow = default)
    {
        if (!CodexWindowActivator.IsForegroundWindow(foregroundWindow))
        {
            return null;
        }

        try
        {
            await EnsureConnectedAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                InvalidDataException or
                ObjectDisposedException)
        {
            return null;
        }

        if (!CodexWindowActivator.IsForegroundWindow(foregroundWindow))
        {
            return null;
        }

        ForegroundDraftLease? candidate;
        lock (_stateSync)
        {
            candidate = _visibilityRefresh is null
                ? TryCreateForegroundDraftLeaseLocked(foregroundWindow)
                : null;
        }

        if (candidate is null)
        {
            return null;
        }

        await Task.Delay(ForegroundDraftStabilityWindow, cancellationToken);
        return IsForegroundDraftLeaseCurrent(candidate.Value)
            ? candidate
            : null;
    }

    /// <summary>
    /// Revalidates a captured foreground-draft lease against the live IPC
    /// connection and the same renderer-owned draft evidence. Visibility in
    /// unrelated background renderers cannot invalidate a renderer-bound lease.
    /// </summary>
    internal bool IsForegroundDraftLeaseCurrent(ForegroundDraftLease lease)
    {
        lock (_stateSync)
        {
            if (_visibilityRefresh is not null ||
                _pipe is not { IsConnected: true } ||
                !IsInitializedClientId(_clientId) ||
                !string.Equals(
                    lease.ClientId,
                    _clientId,
                    StringComparison.Ordinal) ||
                !IsForegroundDraftOperationId(lease.OperationId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(lease.RendererClientId))
            {
                var rendererStillHasDraftContext =
                    !_visibleThreadByClient.TryGetValue(
                        lease.RendererClientId,
                        out var rendererThreadId) ||
                    CodexDraftModelToggleService.IsDraftThreadId(
                        rendererThreadId);
                if (rendererStillHasDraftContext &&
                    _rendererDraftEvidenceByClient.TryGetValue(
                        lease.RendererClientId,
                        out var evidence) &&
                    evidence.Generation == lease.RendererDraftGeneration &&
                    evidence.ForegroundWindow == lease.ForegroundWindow)
                {
                    return true;
                }

                return HasPreservedForegroundDraftContinuityLocked(
                    lease,
                    DateTimeOffset.UtcNow);
            }

            return false;
        }
    }

    internal static bool HasForegroundDraftOperationBudget(
        ForegroundDraftLease lease,
        DateTimeOffset now)
    {
        var age = now - lease.RendererDraftObservedAt;
        return lease.RendererDraftObservedAt != default &&
            age >= TimeSpan.Zero &&
            age <= ForegroundDraftLeaseAdmissionLifetime;
    }

    internal ForegroundDraftLease?
        TryCaptureForegroundDraftLeaseForReasoningStep(
            IntPtr foregroundWindow)
    {
        if (!CodexWindowActivator.IsForegroundWindow(foregroundWindow))
        {
            return null;
        }

        lock (_stateSync)
        {
            if (_visibilityRefresh is not null)
            {
                return null;
            }

            var lease = TryCreateForegroundDraftLeaseLocked(foregroundWindow);
            return lease is { } candidate &&
                HasForegroundDraftOperationBudget(
                    candidate,
                    DateTimeOffset.UtcNow)
                        ? candidate
                        : null;
        }
    }

    internal bool TryPreserveForegroundDraftAfterReasoningStep(
        ForegroundDraftLease lease)
    {
        if (string.IsNullOrWhiteSpace(lease.RendererClientId) ||
            !IsForegroundDraftOperationId(lease.OperationId) ||
            !CodexWindowActivator.IsForegroundWindow(lease.ForegroundWindow))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (!HasForegroundDraftOperationBudget(lease, now))
        {
            return false;
        }

        lock (_stateSync)
        {
            if (_visibleThreadByClient.TryGetValue(
                    lease.RendererClientId,
                    out var rendererThreadId) &&
                !CodexDraftModelToggleService.IsDraftThreadId(
                    rendererThreadId))
            {
                return false;
            }

            _rendererDraftContinuityByClient[lease.RendererClientId] = new(
                now,
                lease.ForegroundWindow);
            _foregroundRendererClientByWindow[lease.ForegroundWindow] =
                lease.RendererClientId;
            if (_pipe is { IsConnected: true } &&
                IsInitializedClientId(_clientId))
            {
                _rendererDraftEvidenceByClient[lease.RendererClientId] = new(
                    NextRendererDraftEvidenceGenerationLocked(),
                    now,
                    lease.ForegroundWindow);
            }

            return true;
        }
    }

    /// <summary>
    /// Renews renderer-owned blank-draft evidence only after the complete
    /// guarded draft transaction has confirmed its final renderer state.
    /// Updating the renderer evidence generation revokes
    /// every lease captured before that rebuild while allowing the next dial
    /// action to capture a fresh operation token and admission timestamp.
    /// </summary>
    internal bool TryRenewForegroundDraftEvidenceAfterGuardedRebuild(
        ForegroundDraftLease lease,
        CodexModelToggleResult result)
    {
        if (!CodexWindowActivator.IsForegroundWindow(
                lease.ForegroundWindow) ||
            !CanRenewForegroundDraftEvidenceAfterGuardedRebuild(
                lease,
                result))
        {
            return false;
        }

        lock (_stateSync)
        {
            var now = DateTimeOffset.UtcNow;
            if (_visibilityRefresh is not null ||
                _pipe is not { IsConnected: true } ||
                !IsInitializedClientId(_clientId) ||
                !string.Equals(
                    lease.ClientId,
                    _clientId,
                    StringComparison.Ordinal) ||
                !CodexWindowActivator.IsForegroundWindow(
                    lease.ForegroundWindow))
            {
                return false;
            }

            var renewedGeneration =
                NextRendererDraftEvidenceGenerationLocked();
            var renewedOriginalEvidence =
                TryCreateRenewedForegroundDraftEvidence(
                    lease,
                    _visibleThreadByClient,
                    _rendererDraftEvidenceByClient,
                    renewedGeneration,
                    now,
                    out var renewedEvidence,
                    allowExpiredEvidence:
                        result.Detail == CodexDraftModelToggleService
                            .NativeTargetConfirmationReceipt);
            if (!renewedOriginalEvidence)
            {
                if (!HasPreservedForegroundDraftContinuityLocked(
                        lease,
                        now))
                {
                    return false;
                }

                renewedEvidence = new(
                    renewedGeneration,
                    now,
                    lease.ForegroundWindow);
            }

            _rendererDraftEvidenceByClient[lease.RendererClientId!] =
                renewedEvidence;
            _rendererDraftContinuityByClient.Remove(
                lease.RendererClientId!);
            _foregroundRendererClientByWindow[lease.ForegroundWindow] =
                lease.RendererClientId!;
            return true;
        }
    }

    internal static bool CanRenewForegroundDraftEvidenceAfterGuardedRebuild(
        ForegroundDraftLease lease,
        CodexModelToggleResult result) =>
        result.Succeeded &&
        string.Equals(
            result.ThreadId,
            lease.OperationId,
            StringComparison.Ordinal) &&
        result.Detail is
            CodexDraftModelToggleService.ComposerRebuildDispatchReceipt or
            CodexDraftModelToggleService.NativeTargetConfirmationReceipt;

    internal static bool TryCreateRenewedForegroundDraftEvidence(
        ForegroundDraftLease lease,
        IReadOnlyDictionary<string, string> visibleThreadByClient,
        IReadOnlyDictionary<string, RendererDraftEvidence>
            rendererDraftEvidenceByClient,
        long renewedGeneration,
        DateTimeOffset observedAt,
        out RendererDraftEvidence renewedEvidence,
        bool allowExpiredEvidence = false)
    {
        renewedEvidence = default;
        if (!HasRenewableForegroundDraftEvidence(
                lease,
                visibleThreadByClient,
                rendererDraftEvidenceByClient) ||
            renewedGeneration == lease.RendererDraftGeneration ||
            observedAt == default ||
            !rendererDraftEvidenceByClient.TryGetValue(
                lease.RendererClientId!,
                out var currentEvidence) ||
            observedAt < currentEvidence.ObservedAt ||
            (!allowExpiredEvidence &&
                observedAt - currentEvidence.ObservedAt >
                    RendererDraftEvidenceLifetime))
        {
            return false;
        }

        renewedEvidence = new(
            renewedGeneration,
            observedAt,
            lease.ForegroundWindow);
        return true;
    }

    internal static bool HasRenewableForegroundDraftEvidence(
        ForegroundDraftLease lease,
        IReadOnlyDictionary<string, string> visibleThreadByClient,
        IReadOnlyDictionary<string, RendererDraftEvidence>
            rendererDraftEvidenceByClient)
    {
        ArgumentNullException.ThrowIfNull(visibleThreadByClient);
        ArgumentNullException.ThrowIfNull(rendererDraftEvidenceByClient);
        if (!IsForegroundDraftOperationId(lease.OperationId) ||
            string.IsNullOrWhiteSpace(lease.RendererClientId) ||
            lease.ForegroundWindow == IntPtr.Zero)
        {
            return false;
        }

        var rendererStillHasDraftContext =
            !visibleThreadByClient.TryGetValue(
                lease.RendererClientId,
                out var rendererThreadId) ||
            CodexDraftModelToggleService.IsDraftThreadId(rendererThreadId);
        return rendererStillHasDraftContext &&
            rendererDraftEvidenceByClient.TryGetValue(
                lease.RendererClientId,
                out var evidence) &&
            evidence.Generation == lease.RendererDraftGeneration &&
            evidence.ForegroundWindow == lease.ForegroundWindow;
    }

    private bool HasPreservedForegroundDraftContinuityLocked(
        ForegroundDraftLease lease,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(lease.RendererClientId) ||
            lease.ForegroundWindow == IntPtr.Zero ||
            !_rendererDraftContinuityByClient.TryGetValue(
                lease.RendererClientId,
                out var continuity) ||
            continuity.ForegroundWindow != lease.ForegroundWindow ||
            continuity.ObservedAt < lease.RendererDraftObservedAt ||
            now < continuity.ObservedAt ||
            now - continuity.ObservedAt >
                ForegroundDraftLeaseAdmissionLifetime ||
            !_foregroundRendererClientByWindow.TryGetValue(
                lease.ForegroundWindow,
                out var foregroundRendererClientId) ||
            !string.Equals(
                foregroundRendererClientId,
                lease.RendererClientId,
                StringComparison.Ordinal) ||
            (_visibleThreadByClient.TryGetValue(
                 lease.RendererClientId,
                 out var rendererThreadId) &&
             !CodexDraftModelToggleService.IsDraftThreadId(
                 rendererThreadId)) ||
            !_rendererDraftEvidenceByClient.TryGetValue(
                lease.RendererClientId,
                out var evidence) ||
            evidence.ForegroundWindow != lease.ForegroundWindow ||
            evidence.ObservedAt < continuity.ObservedAt ||
            now < evidence.ObservedAt ||
            now - evidence.ObservedAt > RendererDraftEvidenceLifetime)
        {
            return false;
        }

        return true;
    }

    internal async Task<CodexModelToggleResult> ToggleAsync(
        CodexQuickModel first,
        CodexQuickModel second,
        CancellationToken cancellationToken) =>
        await ToggleAsync(
            first,
            firstEffort: null,
            second,
            secondEffort: null,
            cancellationToken);

    internal async Task<CodexModelToggleResult> ToggleAsync(
        CodexQuickModel first,
        string? firstEffort,
        CodexQuickModel second,
        string? secondEffort,
        CancellationToken cancellationToken) =>
        await ToggleCoreAsync(
            first,
            firstEffort,
            second,
            secondEffort,
            expectedThreadId: null,
            cancellationToken);

    internal async Task<CodexModelToggleResult> ToggleAsync(
        CodexQuickModel first,
        string? firstEffort,
        CodexQuickModel second,
        string? secondEffort,
        string expectedThreadId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThreadId);
        return await ToggleCoreAsync(
            first,
            firstEffort,
            second,
            secondEffort,
            expectedThreadId.Trim(),
            cancellationToken);
    }

    private async Task<CodexModelToggleResult> ToggleCoreAsync(
        CodexQuickModel first,
        string? firstEffort,
        CodexQuickModel second,
        string? secondEffort,
        string? expectedThreadId,
        CancellationToken cancellationToken)
    {
        ValidatePair(first, second);
#if DEBUG
        var started = Stopwatch.GetTimestamp();
#endif
        var gateEntered = false;
        string? attemptedThreadId = null;
        var attemptedPrevious = CodexQuickModel.Unknown;
        string? attemptedPreviousEffort = null;

        CodexModelToggleResult Complete(CodexModelToggleResult result)
        {
#if DEBUG
            CodexModelToggleDiagnostics.Record(
                result,
                Stopwatch.GetElapsedTime(started));
#endif
            return result;
        }

        try
        {
            await _toggleGate.WaitAsync(cancellationToken);
            gateEntered = true;
            await EnsureConnectedAsync(cancellationToken);
            var hasExplicitSemanticTarget =
                !string.IsNullOrWhiteSpace(expectedThreadId);
            var currentThread = hasExplicitSemanticTarget
                ? await WaitForExpectedVisibleThreadAsync(
                    expectedThreadId!,
                    cancellationToken)
                : await WaitForSingleVisibleThreadAsync(cancellationToken);
            if (currentThread.Error is not null)
            {
                return Complete(Failure(currentThread.Error));
            }

            var threadId = currentThread.ThreadId!;
            if (!string.IsNullOrWhiteSpace(expectedThreadId) &&
                !threadId.Equals(
                    expectedThreadId,
                    StringComparison.Ordinal))
            {
                return Complete(Failure(
                    "visible-thread-changed",
                    expectedThreadId));
            }

            if (!CanTrackSemanticThread(threadId))
            {
                return Complete(Failure(
                    "draft-thread-requires-picker",
                    threadId));
            }

            attemptedThreadId = threadId;
            var context = await ResolveToggleThreadContextAsync(
                threadId,
                allowOtherVisibleThreads: hasExplicitSemanticTarget,
                cancellationToken);
            if (context.Error is not null ||
                context.OwnerClientId is null ||
                context.State is null)
            {
                return Complete(Failure(
                    context.Error ?? "thread-state-unavailable",
                    threadId,
                    detail: context.Detail));
            }

            var owner = context.OwnerClientId;
            var state = context.State;
            var previous = ParseModelId(state.ModelId);
            var previousEffort = state.Effort;
            attemptedPrevious = previous;
            attemptedPreviousEffort = previousEffort;
            var visibilityError = ValidateSelectedThreadIsStillVisible(
                threadId,
                allowOtherVisibleThreads: hasExplicitSemanticTarget);
            if (visibilityError is not null)
            {
                return Complete(Failure(
                    visibilityError,
                    threadId,
                    previous,
                    previousEffort));
            }

            var target = ResolveToggleTarget(previous, first, second);
            var targetModelId = ToModelId(target);
            RememberEffort(
                state.ThreadId,
                state.ModelId,
                state.Effort);
            var configuredEffort = target == first
                ? firstEffort
                : secondEffort;
            var targetEffort = ResolveTargetEffort(
                targetModelId,
                string.IsNullOrWhiteSpace(configuredEffort)
                    ? RecallEffort(state.ThreadId, targetModelId)
                    : configuredEffort);

            var update = await UpdateThreadSettingsWithRetryAsync(
                threadId,
                owner,
                targetModelId,
                targetEffort,
                allowOtherVisibleThreads: hasExplicitSemanticTarget,
                cancellationToken);
            if (!update.Succeeded)
            {
                return Complete(Failure(
                    update.Error ?? "thread-settings-rejected",
                    threadId,
                    previous,
                    previousEffort,
                    detail: update.Detail));
            }

            RememberEffort(threadId, targetModelId, targetEffort);
            ConfirmSuccessfulToggleState(
                threadId,
                update.OwnerClientId ?? owner,
                targetModelId,
                targetEffort);
            return Complete(new(
                true,
                previous,
                target,
                threadId,
                previousEffort,
                targetEffort));
        }
        catch (OperationCanceledException)
        {
            Complete(Failure(
                "cancelled",
                attemptedThreadId,
                attemptedPrevious,
                attemptedPreviousEffort));
            throw;
        }
        catch (TimeoutException)
        {
            return Complete(Failure(
                "ipc-timeout",
                attemptedThreadId,
                attemptedPrevious,
                attemptedPreviousEffort));
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidOperationException or
                ObjectDisposedException)
        {
            return Complete(Failure(
                "ipc-disconnected",
                attemptedThreadId,
                attemptedPrevious,
                attemptedPreviousEffort,
                detail: exception.GetType().Name));
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            return Complete(Failure(
                "ipc-unavailable",
                attemptedThreadId,
                attemptedPrevious,
                attemptedPreviousEffort,
                detail: exception.GetType().Name));
        }
        finally
        {
            if (gateEntered)
            {
                _toggleGate.Release();
            }
        }
    }

    private async Task<ToggleThreadContext> ResolveToggleThreadContextAsync(
        string threadId,
        bool allowOtherVisibleThreads,
        CancellationToken cancellationToken)
    {
        const int maximumAttempts = 2;
        string? lastOwnerClientId = null;
        var lastError = "thread-owner-unavailable";
        string? lastDetail = null;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            var visibilityError = ValidateSelectedThreadIsStillVisible(
                threadId,
                allowOtherVisibleThreads);
            if (visibilityError == "visible-thread-changed")
            {
                return new(null, null, visibilityError);
            }

            if (visibilityError is not null)
            {
                lastError = visibilityError;
                if (attempt + 1 < maximumAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken);
                    continue;
                }

                return new(null, null, lastError);
            }

            var tracked = ReadTrackedThreadContext(threadId);
            if (tracked.OwnerClientId is not null && tracked.State is not null)
            {
                return new(tracked.OwnerClientId, tracked.State, null);
            }

            var ownerClientId = tracked.OwnerClientId;
            if (ownerClientId is null || attempt > 0)
            {
                try
                {
                    ownerClientId = await DiscoverOwnerAsync(
                        threadId,
                        cancellationToken);
                }
                catch (TimeoutException exception)
                {
                    lastError = "ipc-timeout";
                    lastDetail = exception.GetType().Name;
                    if (attempt + 1 < maximumAttempts)
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(180),
                            cancellationToken);
                        continue;
                    }

                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(ownerClientId))
            {
                lastError = "thread-owner-unavailable";
            }
            else
            {
                lastOwnerClientId = ownerClientId;
                var state = ReadTrackedThreadContext(
                    threadId,
                    ownerClientId).State;
                state ??= await ReadThreadStateAsync(
                    threadId,
                    ownerClientId,
                    cancellationToken);
                state ??= ReadTrackedThreadContext(
                    threadId,
                    ownerClientId).State;
                if (state is not null)
                {
                    return new(ownerClientId, state, null);
                }

                lastError = "thread-state-unavailable";
            }

            if (attempt + 1 < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(180), cancellationToken);
            }
        }

        return new(lastOwnerClientId, null, lastError, lastDetail);
    }

    private (string? OwnerClientId, CodexThreadModelState? State)
        ReadTrackedThreadContext(
            string threadId,
            string? requiredOwnerClientId = null)
    {
        lock (_stateSync)
        {
            if (_trackedThreadId != threadId ||
                _trackedOwnerClientId is null ||
                requiredOwnerClientId is not null &&
                _trackedOwnerClientId != requiredOwnerClientId)
            {
                return default;
            }

            var state = _currentThreadState?.ThreadId == threadId
                ? _currentThreadState
                : null;
            return (_trackedOwnerClientId, state);
        }
    }

    private async Task<SettingsUpdateResult>
        UpdateThreadSettingsWithRetryAsync(
            string threadId,
            string initialOwnerClientId,
            string targetModelId,
            string targetEffort,
            bool allowOtherVisibleThreads,
            CancellationToken cancellationToken)
    {
        const int maximumAttempts = 2;
        var ownerClientId = initialOwnerClientId;
        var lastError = "thread-settings-rejected";
        string? lastDetail = null;

        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            if (attempt > 0)
            {
                var reconciledOwner = ReadTrackedTargetOwner(
                    threadId,
                    targetModelId,
                    targetEffort);
                if (reconciledOwner is not null)
                {
                    return new(true, reconciledOwner, null);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(220), cancellationToken);
                try
                {
                    await EnsureConnectedAsync(cancellationToken);
                }
                catch (TimeoutException exception)
                {
                    lastError = "ipc-timeout";
                    lastDetail = exception.GetType().Name;
                    continue;
                }
                catch (Exception exception) when (
                    exception is IOException or
                        InvalidOperationException or
                        ObjectDisposedException)
                {
                    lastError = "ipc-disconnected";
                    lastDetail = exception.GetType().Name;
                    continue;
                }

                var visibleThread = allowOtherVisibleThreads
                    ? await WaitForExpectedVisibleThreadAsync(
                        threadId,
                        cancellationToken)
                    : await WaitForSingleVisibleThreadAsync(
                        cancellationToken);
                if (visibleThread.Error is not null)
                {
                    return new(
                        false,
                        ownerClientId,
                        visibleThread.Error);
                }

                if (!threadId.Equals(
                    visibleThread.ThreadId,
                    StringComparison.Ordinal))
                {
                    return new(
                        false,
                        ownerClientId,
                        "visible-thread-changed");
                }

                try
                {
                    ownerClientId = await DiscoverOwnerAsync(
                        threadId,
                        cancellationToken);
                }
                catch (TimeoutException exception)
                {
                    lastError = "ipc-timeout";
                    lastDetail = exception.GetType().Name;
                    continue;
                }
                catch (Exception exception) when (
                    exception is IOException or
                        InvalidOperationException or
                        ObjectDisposedException)
                {
                    lastError = "ipc-disconnected";
                    lastDetail = exception.GetType().Name;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ownerClientId))
                {
                    lastError = "thread-owner-unavailable";
                    continue;
                }
            }

            var visibility = ValidateSelectedThreadIsStillVisible(
                threadId,
                allowOtherVisibleThreads);
            if (visibility is not null)
            {
                return new(false, ownerClientId, visibility);
            }

            JsonElement response;
            try
            {
                response = await SendRequestAsync(
                    "thread-follower-update-thread-settings",
                    version: 1,
                    new
                    {
                        conversationId = threadId,
                        threadSettings = new
                        {
                            model = targetModelId,
                            effort = targetEffort,
                        },
                    },
                    ownerClientId,
                    RequestTimeout,
                    cancellationToken);
            }
            catch (TimeoutException exception)
            {
                lastError = "ipc-timeout";
                lastDetail = exception.GetType().Name;
                continue;
            }
            catch (Exception exception) when (
                exception is IOException or
                    InvalidOperationException or
                    ObjectDisposedException)
            {
                lastError = "ipc-disconnected";
                lastDetail = exception.GetType().Name;
                continue;
            }

            if (IsSuccessfulUpdate(response))
            {
                return new(true, ownerClientId, null);
            }

            lastDetail = ReadResponseError(response);
            if (!IsTransientSettingsUpdateFailure(lastDetail))
            {
                return new(
                    false,
                    ownerClientId,
                    "thread-settings-rejected",
                    lastDetail);
            }

            lastError = lastDetail?.Contains(
                "no-client-found",
                StringComparison.OrdinalIgnoreCase) == true ||
                lastDetail?.Contains(
                    "client-disconnected",
                    StringComparison.OrdinalIgnoreCase) == true
                    ? "thread-owner-unavailable"
                    : "ipc-timeout";
        }

        var finalOwner = await WaitForTrackedTargetAsync(
            threadId,
            targetModelId,
            targetEffort,
            TimeSpan.FromMilliseconds(650),
            cancellationToken);
        return finalOwner is not null
            ? new(true, finalOwner, null)
            : new(false, ownerClientId, lastError, lastDetail);
    }

    private async Task<string?> WaitForTrackedTargetAsync(
        string threadId,
        string targetModelId,
        string targetEffort,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var ownerClientId = ReadTrackedTargetOwner(
                threadId,
                targetModelId,
                targetEffort);
            if (ownerClientId is not null)
            {
                return ownerClientId;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return null;
            }

            await Task.Delay(
                remaining < TimeSpan.FromMilliseconds(50)
                    ? remaining
                    : TimeSpan.FromMilliseconds(50),
                cancellationToken);
        }
    }

    private string? ReadTrackedTargetOwner(
        string threadId,
        string targetModelId,
        string targetEffort)
    {
        var tracked = ReadTrackedThreadContext(threadId);
        return tracked.OwnerClientId is not null &&
            tracked.State is not null &&
            ThreadStateMatchesTarget(
                tracked.State,
                targetModelId,
                targetEffort)
                ? tracked.OwnerClientId
                : null;
    }

    internal static bool ThreadStateMatchesTarget(
        CodexThreadModelState state,
        string targetModelId,
        string? targetEffort)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.ModelId.Equals(
                targetModelId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                state.Effort,
                targetEffort,
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsTransientSettingsUpdateFailure(string? error) =>
        !string.IsNullOrWhiteSpace(error) &&
        (error.Contains("no-client-found", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("client-disconnected", StringComparison.OrdinalIgnoreCase) ||
         error.Contains("request-timeout", StringComparison.OrdinalIgnoreCase) ||
         error.Contains(
             "thread-follower-update-thread-settings-timeout",
             StringComparison.OrdinalIgnoreCase));

    private static string? ReadResponseError(JsonElement response)
    {
        if (!response.TryGetProperty("error", out var error))
        {
            return null;
        }

        var value = error.ValueKind == JsonValueKind.String
            ? error.GetString()
            : error.GetRawText();
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= 500
                ? value
                : value[..500];
    }

    internal static CodexQuickModel ParseModelId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CodexQuickModel.Unknown;
        }

        if (value.Contains("luna", StringComparison.OrdinalIgnoreCase))
        {
            return CodexQuickModel.Luna;
        }

        if (value.Contains("terra", StringComparison.OrdinalIgnoreCase))
        {
            return CodexQuickModel.Terra;
        }

        return value.Contains("sol", StringComparison.OrdinalIgnoreCase)
            ? CodexQuickModel.Sol
            : CodexQuickModel.Unknown;
    }

    internal static CodexQuickModel ResolveToggleTarget(
        CodexQuickModel current,
        CodexQuickModel first,
        CodexQuickModel second)
    {
        ValidatePair(first, second);
        return current == first ? second : first;
    }

    internal static string ToModelId(CodexQuickModel model) =>
        model switch
        {
            CodexQuickModel.Sol => "gpt-5.6-sol",
            CodexQuickModel.Terra => "gpt-5.6-terra",
            CodexQuickModel.Luna => "gpt-5.6-luna",
            _ => throw new ArgumentOutOfRangeException(nameof(model)),
        };

    internal static string ResolveTargetEffort(
        string modelId,
        string? rememberedEffort,
        string? modelsCachePath = null)
    {
        var fallback = modelId.Equals(
            "gpt-5.6-sol",
            StringComparison.OrdinalIgnoreCase)
                ? "low"
                : "medium";
        var knownRememberedEffort = IsKnownReasoningEffort(rememberedEffort)
            ? rememberedEffort!.Trim().ToLowerInvariant()
            : null;
        try
        {
            var path = modelsCachePath ?? ResolveModelsCachePath(
                Environment.GetEnvironmentVariable("CODEX_HOME"));
            if (!File.Exists(path))
            {
                return knownRememberedEffort ?? fallback;
            }

            using var cache = JsonDocument.Parse(File.ReadAllText(path));
            if (!cache.RootElement.TryGetProperty("models", out var models) ||
                models.ValueKind != JsonValueKind.Array)
            {
                return knownRememberedEffort ?? fallback;
            }

            foreach (var model in models.EnumerateArray())
            {
                if (!TryReadString(model, "slug", out var slug) ||
                    !slug.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var supported = model.TryGetProperty(
                        "supported_reasoning_levels",
                        out var levels) &&
                    levels.ValueKind == JsonValueKind.Array
                        ? levels.EnumerateArray()
                            .Select(level =>
                                TryReadString(level, "effort", out var effort)
                                    ? effort
                                    : null)
                            .Where(effort => effort is not null)
                            .Select(effort => effort!)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : [];
                if (knownRememberedEffort is not null &&
                    supported.Contains(knownRememberedEffort))
                {
                    return knownRememberedEffort;
                }

                if (TryReadString(
                        model,
                        "default_reasoning_level",
                        out var defaultEffort) &&
                    (supported.Count == 0 || supported.Contains(defaultEffort)))
                {
                    return defaultEffort;
                }

                return fallback;
            }
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            // A stale or partially-written cache is handled by the offline
            // policy below.
        }

        // When model metadata is unavailable, retain an explicit/remembered
        // effort that is part of Codex's known vocabulary. Silently replacing
        // a user-selected Ultra with Low would make the settings UI lie.
        return knownRememberedEffort ?? fallback;
    }

    private static bool IsKnownReasoningEffort(string? effort) =>
        effort?.Trim().ToLowerInvariant() is
            "low" or "medium" or "high" or "xhigh" or "max" or "ultra";

    internal static string ResolveModelsCachePath(string? codexHome)
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var root = string.IsNullOrWhiteSpace(codexHome)
            ? Path.Combine(userProfile, ".codex")
            : Environment.ExpandEnvironmentVariables(codexHome.Trim());
        if (root == "~")
        {
            root = userProfile;
        }
        else if (root.StartsWith("~/", StringComparison.Ordinal) ||
                 root.StartsWith("~\\", StringComparison.Ordinal))
        {
            root = Path.Combine(userProfile, root[2..]);
        }

        return Path.Combine(root, "models_cache.json");
    }

    internal static string? ReadSnapshotEffort(JsonElement state)
    {
        if (state.TryGetProperty("latestThreadSettings", out var settings) &&
            settings.ValueKind == JsonValueKind.Object &&
            TryReadString(settings, "effort", out var configuredEffort))
        {
            return configuredEffort;
        }

        return TryReadString(
            state,
            "latestReasoningEffort",
            out var legacyEffort)
                ? legacyEffort
                : null;
    }

    internal static byte[] EncodeFrame<T>(T message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message);
        if (json.Length > MaximumFrameBytes)
        {
            throw new InvalidDataException("IPC frame is too large.");
        }

        var frame = new byte[sizeof(uint) + json.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(
            frame.AsSpan(0, sizeof(uint)),
            checked((uint)json.Length));
        json.CopyTo(frame.AsSpan(sizeof(uint)));
        return frame;
    }

    internal static async Task<JsonDocument> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var prefix = new byte[sizeof(uint)];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(prefix);
        if (length == 0 || length > MaximumFrameBytes)
        {
            throw new InvalidDataException(
                $"Invalid Codex IPC frame length: {length}.");
        }

        var payload = new byte[checked((int)length)];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonDocument.Parse(payload);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        lock (_stateSync)
        {
            if (_pipe is { IsConnected: true } &&
                !string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }
        }

        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            lock (_stateSync)
            {
                if (_pipe is { IsConnected: true } &&
                    !string.IsNullOrWhiteSpace(_clientId))
                {
                    return;
                }
            }

            var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough);
            using var connectTimeout = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            connectTimeout.CancelAfter(ConnectTimeout);
            try
            {
                await pipe.ConnectAsync(connectTimeout.Token);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            SnapshotWaiter[] resetWaiters;
            lock (_stateSync)
            {
                _pipe?.Dispose();
                _pipe = pipe;
                _clientId = null;
                _visibleThreadByClient.Clear();
                _rendererDraftEvidenceByClient.Clear();
                _rendererDraftContinuityByClient.Clear();
                _foregroundRendererClientByWindow.Clear();
                _foregroundDraftNavigationExpectation = null;
                _visibilityRefresh = null;
                InvalidateForegroundDraftLeasesLocked();
                _selectedVisibleThreadId = null;
                _trackingGeneration++;
                _trackedThreadId = null;
                _trackedOwnerClientId = null;
                _trackedStateAccumulator = null;
                _currentThreadState = null;
                resetWaiters = [.. _snapshotWaiters.Values];
                _snapshotWaiters.Clear();
                PulseVisibleThreadChangedLocked();
            }

            RaiseCurrentThreadStateChanged(null);
            foreach (var waiter in resetWaiters)
            {
                waiter.Completion.TrySetException(
                    new IOException("Codex IPC connection was replaced."));
            }

            _readerTask = Task.Run(
                () => ReadLoopAsync(pipe, _lifetime.Token),
                CancellationToken.None);
            var initialized = await SendRequestAsync(
                "initialize",
                version: 0,
                new { clientType = "codexmicro-model-settings" },
                targetClientId: null,
                RequestTimeout,
                cancellationToken,
                InitialClientId);
            if (!TryReadInitializedClientId(initialized, out var clientId))
            {
                throw new InvalidDataException(
                    "Codex IPC initialize response was not recognized.");
            }

            lock (_stateSync)
            {
                if (ReferenceEquals(_pipe, pipe))
                {
                    _clientId = clientId;
                    InvalidateForegroundDraftLeasesLocked();
                    _trackingGeneration++;
                    _trackedThreadId = null;
                    _trackedOwnerClientId = null;
                    _trackedStateAccumulator = null;
                    _currentThreadState = null;
                }
            }

            RefreshCurrentThreadTracking();
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task ReadLoopAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   pipe.IsConnected)
            {
                using var message = await ReadFrameAsync(
                    pipe,
                    cancellationToken);
                lock (_stateSync)
                {
                    if (!ReferenceEquals(_pipe, pipe))
                    {
                        break;
                    }

                    // Keep connection replacement from interleaving between
                    // validation and broadcast state mutation. Monitor locks
                    // are re-entrant for the handlers below.
                    ProcessMessage(message.RootElement);
                }
            }
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            // Disposal owns cancellation.
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidDataException or
                JsonException or
                ObjectDisposedException)
        {
            failure = exception;
        }
        finally
        {
            HandleDisconnect(pipe, failure);
        }
    }

    private void ProcessMessage(JsonElement message)
    {
        if (!TryReadString(message, "type", out var type))
        {
            return;
        }

        if (type == "client-discovery-request")
        {
            if (TryReadString(message, "requestId", out var discoveryId))
            {
                _ = RespondCannotHandleAsync(discoveryId);
            }

            return;
        }

        if (type == "response")
        {
            if (TryReadString(message, "requestId", out var requestId) &&
                _pendingRequests.TryRemove(requestId, out var pending))
            {
                pending.TrySetResult(message.Clone());
            }

            return;
        }

        if (type != "broadcast" ||
            !TryReadString(message, "method", out var method))
        {
            return;
        }

        switch (method)
        {
            case "thread-stream-following-changed":
                ProcessFollowingChanged(message);
                break;
            case "thread-stream-state-changed":
                ProcessThreadStateChanged(message);
                break;
            case "client-status-changed":
                ProcessClientStatusChanged(message);
                break;
            case "ipc-connection-reset":
                ProcessConnectionReset();
                break;
        }
    }

    private async Task RespondCannotHandleAsync(string requestId)
    {
        try
        {
            await SendMessageAsync(
                new
                {
                    type = "client-discovery-response",
                    requestId,
                    response = new { canHandle = false },
                },
                _lifetime.Token);
        }
        catch
        {
            // The router treats a disconnected reader as unable to handle it.
        }
    }

    private void ProcessFollowingChanged(JsonElement message)
    {
        if (!TryReadString(message, "sourceClientId", out var sourceClientId) ||
            !message.TryGetProperty("params", out var parameters) ||
            !TryReadString(parameters, "hostId", out var hostId) ||
            hostId != LocalHostId ||
            !TryReadString(parameters, "conversationId", out var threadId) ||
            !parameters.TryGetProperty("following", out var followingValue) ||
            followingValue.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            return;
        }

        var following = followingValue.GetBoolean();
        var observedForegroundWindow =
            CodexWindowActivator.CaptureForegroundWindow();
        var changed = false;
        var displacedDraftEvidence = false;
        lock (_stateSync)
        {
            if (sourceClientId == _clientId)
            {
                return;
            }

            _visibleThreadByClient.TryGetValue(
                sourceClientId,
                out var previouslyVisibleThreadId);
            var rendererDraftThread =
                CodexDraftModelToggleService.IsDraftThreadId(threadId);
            if (following && !rendererDraftThread)
            {
                _rendererDraftEvidenceByClient.Remove(sourceClientId);
                _rendererDraftContinuityByClient.Remove(sourceClientId);
            }

            if (_visibilityRefresh is { } refresh)
            {
                ApplyVisibleThreadFollowingChange(
                    refresh.ReportedThreadByClient,
                    sourceClientId,
                    threadId,
                    following);
            }

            // Keep renderer-owned draft IDs in the visibility map. They are
            // exact UI identities even though they have no App Server owner;
            // RefreshCurrentThreadTracking excludes them from semantic work.
            changed = ApplyVisibleThreadFollowingChange(
                _visibleThreadByClient,
                sourceClientId,
                threadId,
                following);

            if (changed &&
                ((!following) || rendererDraftThread))
            {
                var evidenceObservedAt = DateTimeOffset.UtcNow;
                var hasExistingEvidence =
                    _rendererDraftEvidenceByClient.TryGetValue(
                        sourceClientId,
                        out var existingEvidence);
                var keepsExistingGeneration = hasExistingEvidence &&
                    (CodexDraftModelToggleService.IsDraftThreadId(
                        previouslyVisibleThreadId) ||
                     rendererDraftThread);
                var generation = keepsExistingGeneration
                    ? existingEvidence.Generation
                    : NextRendererDraftEvidenceGenerationLocked();
                var navigationWindow = keepsExistingGeneration
                    ? IntPtr.Zero
                    : TryConsumeForegroundDraftNavigationLocked(
                        sourceClientId,
                        previouslyVisibleThreadId,
                        following,
                        observedForegroundWindow,
                        evidenceObservedAt);
                var evidenceWindow = keepsExistingGeneration
                    ? existingEvidence.ForegroundWindow
                    : navigationWindow != IntPtr.Zero
                        ? navigationWindow
                        : ResolveRendererForegroundWindowLocked(
                            sourceClientId,
                            observedForegroundWindow);
                _rendererDraftEvidenceByClient[sourceClientId] = new(
                    generation,
                    evidenceObservedAt,
                    evidenceWindow);
                if (evidenceWindow != IntPtr.Zero)
                {
                    displacedDraftEvidence |=
                        BindRendererToForegroundWindowLocked(
                            sourceClientId,
                            evidenceWindow);
                }
            }

            if (changed || displacedDraftEvidence)
            {
                InvalidateForegroundDraftLeasesLocked();
            }

            if (changed)
            {
                PulseVisibleThreadChangedLocked();
            }
        }

        if (changed)
        {
            RefreshCurrentThreadTracking();
        }
    }

    private IntPtr TryConsumeForegroundDraftNavigationLocked(
        string rendererClientId,
        string? previouslyVisibleThreadId,
        bool following,
        IntPtr observedForegroundWindow,
        DateTimeOffset observedAt)
    {
        if (_foregroundDraftNavigationExpectation is not { } navigation)
        {
            return IntPtr.Zero;
        }

        var age = observedAt - navigation.DispatchedAt;
        if (age < TimeSpan.Zero ||
            age > ForegroundDraftNavigationLifetime)
        {
            _foregroundDraftNavigationExpectation = null;
            return IntPtr.Zero;
        }

        if (following ||
            (observedForegroundWindow != IntPtr.Zero &&
             observedForegroundWindow != navigation.ForegroundWindow) ||
            string.IsNullOrWhiteSpace(previouslyVisibleThreadId) ||
            CodexDraftModelToggleService.IsDraftThreadId(
                previouslyVisibleThreadId) ||
            (_foregroundRendererClientByWindow.TryGetValue(
                 navigation.ForegroundWindow,
                 out var expectedRendererClientId) &&
             !string.Equals(
                 expectedRendererClientId,
                 rendererClientId,
                 StringComparison.Ordinal)))
        {
            return IntPtr.Zero;
        }

        _foregroundDraftNavigationExpectation = null;
        return navigation.ForegroundWindow;
    }

    private IntPtr ResolveRendererForegroundWindowLocked(
        string rendererClientId,
        IntPtr observedForegroundWindow)
    {
        var knownWindows = _foregroundRendererClientByWindow
            .Where(pair => string.Equals(
                pair.Value,
                rendererClientId,
                StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .Take(2)
            .ToArray();
        return knownWindows.Length == 1
            ? knownWindows[0]
            : observedForegroundWindow;
    }

    private bool BindRendererToForegroundWindowLocked(
        string rendererClientId,
        IntPtr foregroundWindow)
    {
        var displacedDraftEvidence = false;
        if (_foregroundRendererClientByWindow.TryGetValue(
                foregroundWindow,
                out var displacedRendererClientId) &&
            !string.Equals(
                displacedRendererClientId,
                rendererClientId,
                StringComparison.Ordinal))
        {
            displacedDraftEvidence =
                _rendererDraftEvidenceByClient.Remove(
                    displacedRendererClientId) |
                _rendererDraftContinuityByClient.Remove(
                    displacedRendererClientId);
        }

        var staleWindows = _foregroundRendererClientByWindow
            .Where(pair =>
                pair.Key != foregroundWindow &&
                string.Equals(
                    pair.Value,
                    rendererClientId,
                    StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var staleWindow in staleWindows)
        {
            _foregroundRendererClientByWindow.Remove(staleWindow);
        }

        _foregroundRendererClientByWindow[foregroundWindow] =
            rendererClientId;
        return displacedDraftEvidence;
    }

    private void ProcessThreadStateChanged(JsonElement message)
    {
        if (!TryReadString(message, "sourceClientId", out var sourceClientId) ||
            !message.TryGetProperty("params", out var parameters) ||
            !TryReadString(parameters, "hostId", out var hostId) ||
            hostId != LocalHostId ||
            !TryReadString(parameters, "conversationId", out var threadId) ||
            !parameters.TryGetProperty("change", out var change))
        {
            return;
        }

        CodexThreadModelState? stateToPublish = null;
        var publishState = false;
        TaskCompletionSource<CodexThreadModelState>? completion = null;
        CodexThreadModelState? completedState = null;
        var requestTrackedSnapshot = false;
        var trackedGeneration = 0;
        SnapshotWaiter? waiterNeedingSnapshot = null;
        lock (_stateSync)
        {
            if (_trackedThreadId == threadId &&
                _trackedOwnerClientId == sourceClientId &&
                _trackedStateAccumulator is not null)
            {
                var result = _trackedStateAccumulator.ApplyChange(change);
                if (result.RequiresSnapshot)
                {
                    _currentThreadState = null;
                    stateToPublish = null;
                    publishState = true;
                    requestTrackedSnapshot = true;
                    trackedGeneration = _trackingGeneration;
                }
                else if (result.Applied &&
                         !Equals(_currentThreadState, result.State))
                {
                    _currentThreadState = result.State;
                    stateToPublish = result.State;
                    publishState = true;
                }
            }

            if (_snapshotWaiters.TryGetValue(threadId, out var waiter) &&
                waiter.OwnerClientId == sourceClientId)
            {
                var result = waiter.Accumulator.ApplyChange(change);
                if (result.RequiresSnapshot)
                {
                    waiterNeedingSnapshot = waiter;
                }
                else if (result.State is not null)
                {
                    _snapshotWaiters.Remove(threadId);
                    completion = waiter.Completion;
                    completedState = result.State;
                }
            }
        }

        if (publishState)
        {
            RaiseCurrentThreadStateChanged(stateToPublish);
        }

        if (completion is not null && completedState is not null)
        {
            completion.TrySetResult(completedState);
        }

        if (requestTrackedSnapshot)
        {
            _ = RequestTrackedSnapshotAsync(
                threadId,
                sourceClientId,
                trackedGeneration);
        }

        if (waiterNeedingSnapshot is not null)
        {
            _ = RequestWaiterSnapshotAsync(threadId, waiterNeedingSnapshot);
        }
    }

    private void ProcessClientStatusChanged(JsonElement message)
    {
        if (!message.TryGetProperty("params", out var parameters) ||
            !TryReadString(parameters, "clientId", out var clientId) ||
            !TryReadString(parameters, "status", out var status) ||
            status != "disconnected")
        {
            return;
        }

        var changed = false;
        var ownerDisconnected = false;
        SnapshotWaiter[] disconnectedWaiters;
        lock (_stateSync)
        {
            var evidenceRemoved =
                _rendererDraftEvidenceByClient.Remove(clientId);
            var continuityRemoved =
                _rendererDraftContinuityByClient.Remove(clientId);
            var foregroundWindows = _foregroundRendererClientByWindow
                .Where(pair => string.Equals(
                    pair.Value,
                    clientId,
                    StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var foregroundWindow in foregroundWindows)
            {
                _foregroundRendererClientByWindow.Remove(foregroundWindow);
            }
            var visibilityRemoved =
                _visibleThreadByClient.Remove(clientId);
            if (evidenceRemoved ||
                continuityRemoved ||
                foregroundWindows.Length > 0 ||
                visibilityRemoved)
            {
                InvalidateForegroundDraftLeasesLocked();
            }

            if (visibilityRemoved)
            {
                PulseVisibleThreadChangedLocked();
                changed = true;
            }

            _visibilityRefresh?.ReportedThreadByClient.Remove(clientId);

            if (_trackedOwnerClientId == clientId)
            {
                _trackingGeneration++;
                _trackedThreadId = null;
                _trackedOwnerClientId = null;
                _trackedStateAccumulator = null;
                _currentThreadState = null;
                ownerDisconnected = true;
            }

            disconnectedWaiters = _snapshotWaiters
                .Where(pair => pair.Value.OwnerClientId == clientId)
                .Select(pair => pair.Value)
                .ToArray();
            foreach (var waiter in disconnectedWaiters)
            {
                _snapshotWaiters.Remove(waiter.Accumulator.ThreadId);
            }
        }

        if (ownerDisconnected)
        {
            RaiseCurrentThreadStateChanged(null);
        }

        foreach (var waiter in disconnectedWaiters)
        {
            waiter.Completion.TrySetException(
                new IOException("Codex thread owner disconnected."));
        }

        if (changed || ownerDisconnected)
        {
            RefreshCurrentThreadTracking();
        }
    }

    private void ProcessConnectionReset()
    {
        SnapshotWaiter[] resetWaiters;
        lock (_stateSync)
        {
            _visibleThreadByClient.Clear();
            _rendererDraftEvidenceByClient.Clear();
            _rendererDraftContinuityByClient.Clear();
            _foregroundRendererClientByWindow.Clear();
            _foregroundDraftNavigationExpectation = null;
            _visibilityRefresh = null;
            InvalidateForegroundDraftLeasesLocked();
            _selectedVisibleThreadId = null;
            _trackingGeneration++;
            _trackedThreadId = null;
            _trackedOwnerClientId = null;
            _trackedStateAccumulator = null;
            _currentThreadState = null;
            resetWaiters = [.. _snapshotWaiters.Values];
            _snapshotWaiters.Clear();
            PulseVisibleThreadChangedLocked();
        }

        RaiseCurrentThreadStateChanged(null);
        foreach (var waiter in resetWaiters)
        {
            waiter.Completion.TrySetException(
                new IOException("Codex IPC connection was reset."));
        }
    }

    private void RefreshCurrentThreadTracking()
    {
        string? nextVisibleThreadId;
        string? nextThreadId;
        string? previousThreadId;
        string? previousOwnerClientId;
        int generation;
        lock (_stateSync)
        {
            // Renderer visibility broadcasts can arrive before the initialize
            // response publishes this client's real IPC identity. Keep the
            // visibility snapshot, then let EnsureConnectedAsync start
            // semantic tracking after initialization completes.
            if (string.IsNullOrWhiteSpace(_clientId))
            {
                return;
            }

            var selection = ResolveVisibleThreadSelection(
                _visibleThreadByClient.Values);
            nextVisibleThreadId = selection.VisibleThreadId;
            nextThreadId = selection.SemanticThreadId;
            if (nextVisibleThreadId == _selectedVisibleThreadId &&
                nextThreadId == _trackedThreadId)
            {
                return;
            }

            previousThreadId = _trackedThreadId;
            previousOwnerClientId = _trackedOwnerClientId;
            generation = ++_trackingGeneration;
            _selectedVisibleThreadId = nextVisibleThreadId;
            _trackedThreadId = nextThreadId;
            _trackedOwnerClientId = null;
            _trackedStateAccumulator = null;
            _currentThreadState = null;
        }

        RaiseCurrentThreadStateChanged(null);
        if (!string.IsNullOrWhiteSpace(previousThreadId) &&
            !string.IsNullOrWhiteSpace(previousOwnerClientId))
        {
            _ = ReleaseFollowerLeaseAsync(
                previousThreadId,
                previousOwnerClientId);
        }

        if (!string.IsNullOrWhiteSpace(nextThreadId))
        {
            _ = SynchronizeCurrentThreadAsync(nextThreadId, generation);
        }
    }

    private async Task SynchronizeCurrentThreadAsync(
        string threadId,
        int generation)
    {
        if (!CanTrackSemanticThread(threadId))
        {
            return;
        }

        var lifetimeToken = _lifetime.Token;
        var retryDelay = TimeSpan.Zero;
        while (!lifetimeToken.IsCancellationRequested)
        {
            lock (_stateSync)
            {
                if (_trackingGeneration != generation ||
                    _trackedThreadId != threadId)
                {
                    return;
                }
            }

            if (retryDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(retryDelay, lifetimeToken);
                }
                catch (OperationCanceledException) when (
                    lifetimeToken.IsCancellationRequested)
                {
                    return;
                }
            }

            lock (_stateSync)
            {
                if (_trackingGeneration != generation ||
                    _trackedThreadId != threadId)
                {
                    return;
                }
            }

            string? ownerClientId = null;
            try
            {
                ownerClientId = await DiscoverOwnerAsync(
                    threadId,
                    lifetimeToken);
                if (string.IsNullOrWhiteSpace(ownerClientId))
                {
                    retryDelay = NextTrackingRetryDelay(retryDelay);
                    continue;
                }

                lock (_stateSync)
                {
                    if (_trackingGeneration != generation ||
                        _trackedThreadId != threadId)
                    {
                        return;
                    }

                    _trackedOwnerClientId = ownerClientId;
                    _trackedStateAccumulator = new(threadId, ownerClientId);
                }

                await SendTrackedFollowingAsync(
                    threadId,
                    ownerClientId,
                    generation,
                    lifetimeToken);

                var leaseIsCurrent = false;
                lock (_stateSync)
                {
                    leaseIsCurrent = _trackingGeneration == generation &&
                        _trackedThreadId == threadId &&
                        _trackedOwnerClientId == ownerClientId;
                }

                if (!leaseIsCurrent)
                {
                    await ReleaseFollowerLeaseAsync(threadId, ownerClientId);
                }

                return;
            }
            catch (Exception exception) when (
                exception is IOException or
                    TimeoutException or
                    OperationCanceledException or
                    InvalidDataException or
                    InvalidOperationException or
                    JsonException or
                    ObjectDisposedException)
            {
                if (lifetimeToken.IsCancellationRequested)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(ownerClientId))
                {
                    lock (_stateSync)
                    {
                        if (_trackingGeneration == generation &&
                            _trackedThreadId == threadId &&
                            _trackedOwnerClientId == ownerClientId)
                        {
                            _trackedOwnerClientId = null;
                            _trackedStateAccumulator = null;
                        }
                    }

                    await ReleaseFollowerLeaseAsync(threadId, ownerClientId);
                }

                retryDelay = NextTrackingRetryDelay(retryDelay);
            }
        }
    }

    private static TimeSpan NextTrackingRetryDelay(TimeSpan current)
    {
        if (current <= TimeSpan.Zero)
        {
            return TimeSpan.FromMilliseconds(250);
        }

        return TimeSpan.FromMilliseconds(Math.Min(
            MaximumTrackingRetryDelay.TotalMilliseconds,
            current.TotalMilliseconds * 2));
    }

    private async Task RequestTrackedSnapshotAsync(
        string threadId,
        string ownerClientId,
        int generation)
    {
        try
        {
            lock (_stateSync)
            {
                if (_trackingGeneration != generation ||
                    _trackedThreadId != threadId ||
                    _trackedOwnerClientId != ownerClientId)
                {
                    return;
                }
            }

            await SendTrackedFollowingAsync(
                threadId,
                ownerClientId,
                generation,
                _lifetime.Token);
        }
        catch (Exception exception) when (
            exception is IOException or
                OperationCanceledException or
                InvalidOperationException or
                ObjectDisposedException)
        {
            // The next visibility or connection event will resynchronize.
        }
    }

    private async Task RequestWaiterSnapshotAsync(
        string threadId,
        SnapshotWaiter waiter)
    {
        try
        {
            lock (_stateSync)
            {
                if (!_snapshotWaiters.TryGetValue(threadId, out var current) ||
                    !ReferenceEquals(current, waiter))
                {
                    return;
                }
            }

            await SendWaiterFollowingAsync(
                threadId,
                waiter,
                _lifetime.Token);
        }
        catch (Exception exception) when (
            exception is IOException or
                OperationCanceledException or
                InvalidOperationException or
                ObjectDisposedException)
        {
            // The original snapshot timeout remains authoritative.
        }
    }

    private async Task ReleaseFollowerLeaseAsync(
        string threadId,
        string ownerClientId)
    {
        try
        {
            await SendFollowingConditionallyAsync(
                threadId,
                ownerClientId,
                following: false,
                () => ShouldWriteFollowerSignal(
                    FollowerSignalIntent.Release,
                    expectedGeneration: 0,
                    currentGeneration: 0,
                    trackedKeyMatches:
                        _trackedThreadId == threadId &&
                        _trackedOwnerClientId == ownerClientId,
                    expectedWaiterIsCurrent: false,
                    matchingWaiterExists:
                        _snapshotWaiters.TryGetValue(
                            threadId,
                            out var waiter) &&
                        waiter.OwnerClientId == ownerClientId),
                CancellationToken.None);
        }
        catch
        {
            // Releasing a follower lease is best-effort during transitions.
        }
    }

    private async Task<(string? ThreadId, string? Error)>
        WaitForSingleVisibleThreadAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + CurrentThreadTimeout;
        string? stableCandidate = null;
        var stableSince = DateTimeOffset.MinValue;
        var lastError = "no-visible-thread";
        while (true)
        {
            Task signal;
            TimeSpan nextWake;
            lock (_stateSync)
            {
                var threadIds = _visibleThreadByClient.Values
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (threadIds.Length == 1)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (!string.Equals(
                        stableCandidate,
                        threadIds[0],
                        StringComparison.Ordinal))
                    {
                        stableCandidate = threadIds[0];
                        stableSince = now;
                    }

                    var stableFor = now - stableSince;
                    if (stableFor >= VisibleThreadStabilityWindow)
                    {
                        return (threadIds[0], null);
                    }

                    nextWake = VisibleThreadStabilityWindow - stableFor;
                }
                else
                {
                    stableCandidate = null;
                    stableSince = DateTimeOffset.MinValue;
                    lastError = threadIds.Length > 1
                        ? "multiple-visible-threads"
                        : "no-visible-thread";
                    nextWake = deadline - DateTimeOffset.UtcNow;
                }

                signal = _visibleThreadChanged.Task;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return (null, lastError);
            }

            try
            {
                await signal.WaitAsync(
                    nextWake < remaining ? nextWake : remaining,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                // Re-evaluate: this may be the stability timer rather than
                // the overall selection deadline.
            }
        }
    }

    private async Task<(string? ThreadId, string? Error)>
        WaitForExpectedVisibleThreadAsync(
            string expectedThreadId,
            CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThreadId);
        var normalizedExpectedThreadId = expectedThreadId.Trim();
        var deadline = DateTimeOffset.UtcNow + CurrentThreadTimeout;
        var stableSince = DateTimeOffset.MinValue;
        var lastError = "no-visible-thread";
        while (true)
        {
            Task signal;
            TimeSpan nextWake;
            lock (_stateSync)
            {
                var visibleThreadIds = _visibleThreadByClient.Values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (visibleThreadIds.Contains(
                        normalizedExpectedThreadId,
                        StringComparer.Ordinal))
                {
                    var now = DateTimeOffset.UtcNow;
                    if (stableSince == DateTimeOffset.MinValue)
                    {
                        stableSince = now;
                    }

                    var stableFor = now - stableSince;
                    if (stableFor >= VisibleThreadStabilityWindow)
                    {
                        return (normalizedExpectedThreadId, null);
                    }

                    nextWake = VisibleThreadStabilityWindow - stableFor;
                }
                else
                {
                    stableSince = DateTimeOffset.MinValue;
                    lastError = visibleThreadIds.Length == 0
                        ? "no-visible-thread"
                        : "visible-thread-changed";
                    nextWake = deadline - DateTimeOffset.UtcNow;
                }

                signal = _visibleThreadChanged.Task;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return (null, lastError);
            }

            try
            {
                await signal.WaitAsync(
                    nextWake < remaining ? nextWake : remaining,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
            }
        }
    }

    private async Task<string?> DiscoverOwnerAsync(
        string threadId,
        CancellationToken cancellationToken)
    {
        var response = await SendRequestAsync(
            "thread-owner-discovery",
            version: 1,
            new
            {
                hostId = LocalHostId,
                conversationId = threadId,
            },
            targetClientId: null,
            RequestTimeout,
            cancellationToken);
        return IsSuccess(response) &&
            TryReadString(response, "handledByClientId", out var owner)
                ? owner
                : null;
    }

    private string? ValidateSelectedThreadIsStillVisible(
        string threadId,
        bool allowOtherVisibleThreads = false)
    {
        string[] visibleThreadIds;
        lock (_stateSync)
        {
            visibleThreadIds = _visibleThreadByClient.Values.ToArray();
        }

        return allowOtherVisibleThreads
            ? ValidateExpectedVisibleThreadSelection(
                visibleThreadIds,
                threadId)
            : ValidateVisibleThreadSelection(visibleThreadIds, threadId);
    }

    internal static string? ValidateExpectedVisibleThreadSelection(
        IEnumerable<string> visibleThreadIds,
        string expectedThreadId)
    {
        ArgumentNullException.ThrowIfNull(visibleThreadIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedThreadId);
        var distinct = visibleThreadIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length == 0)
        {
            return "no-visible-thread";
        }

        return distinct.Contains(
            expectedThreadId.Trim(),
            StringComparer.Ordinal)
                ? null
                : "visible-thread-changed";
    }

    private VisibleThreadSelection CaptureVisibleThreadSelection()
    {
        lock (_stateSync)
        {
            return ResolveVisibleThreadSelection(
                _visibleThreadByClient.Values);
        }
    }

    internal static string? ValidateVisibleThreadSelection(
        IEnumerable<string> visibleThreadIds,
        string selectedThreadId)
    {
        ArgumentNullException.ThrowIfNull(visibleThreadIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedThreadId);
        var distinct = visibleThreadIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (distinct.Length == 0)
        {
            return "no-visible-thread";
        }

        if (distinct.Length > 1)
        {
            return "multiple-visible-threads";
        }

        return distinct[0].Equals(selectedThreadId, StringComparison.Ordinal)
            ? null
            : "visible-thread-changed";
    }

    internal static bool CanTrackSemanticThread(string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        return !CodexDraftModelToggleService.IsDraftThreadId(threadId);
    }

    internal static bool IsForegroundDraftOperationId(string? operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) ||
            !operationId.StartsWith(
                ForegroundDraftOperationPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        var token = operationId[ForegroundDraftOperationPrefix.Length..];
        return token.Length == 32 &&
            Guid.TryParseExact(token, "N", out _);
    }

    private ForegroundDraftLease? TryCreateForegroundDraftLeaseLocked(
        IntPtr foregroundWindow)
    {
        if (_pipe is not { IsConnected: true } ||
            !IsInitializedClientId(_clientId))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var expiredClientIds = _rendererDraftEvidenceByClient
            .Where(pair =>
                now - pair.Value.ObservedAt > RendererDraftEvidenceLifetime)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var clientId in expiredClientIds)
        {
            _rendererDraftEvidenceByClient.Remove(clientId);
        }

        var expiredContinuityClientIds = _rendererDraftContinuityByClient
            .Where(pair =>
                now - pair.Value.ObservedAt >
                    ForegroundDraftLeaseAdmissionLifetime)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var clientId in expiredContinuityClientIds)
        {
            _rendererDraftContinuityByClient.Remove(clientId);
        }

        TryBindSingleUnboundDraftEvidenceLocked(
            foregroundWindow,
            now);

        var rendererClientId = SelectRendererDraftEvidenceClient(
            _visibleThreadByClient,
            _rendererDraftEvidenceByClient,
            now,
            RendererDraftEvidenceLifetime,
            foregroundWindow);
        if (rendererClientId is null)
        {
            rendererClientId = _rendererDraftContinuityByClient
                .Where(pair =>
                    now >= pair.Value.ObservedAt &&
                    now - pair.Value.ObservedAt <=
                        ForegroundDraftLeaseAdmissionLifetime &&
                    pair.Value.ForegroundWindow == foregroundWindow &&
                    (!_visibleThreadByClient.TryGetValue(
                         pair.Key,
                         out var rendererThreadId) ||
                     CodexDraftModelToggleService.IsDraftThreadId(
                         rendererThreadId)))
                .OrderByDescending(pair => pair.Value.ObservedAt)
                .Select(pair => pair.Key)
                .FirstOrDefault();
            if (rendererClientId is not null)
            {
                var continuity =
                    _rendererDraftContinuityByClient[rendererClientId];
                _rendererDraftEvidenceByClient[rendererClientId] = new(
                    NextRendererDraftEvidenceGenerationLocked(),
                    continuity.ObservedAt,
                    continuity.ForegroundWindow);
            }
        }

        if (rendererClientId is not null)
        {
            var rendererEvidence =
                _rendererDraftEvidenceByClient[rendererClientId];
            _foregroundRendererClientByWindow[foregroundWindow] =
                rendererClientId;
            return new ForegroundDraftLease(
                _clientId!,
                _visibilityGeneration,
                ForegroundDraftOperationPrefix +
                    Guid.NewGuid().ToString("N"),
                rendererClientId,
                rendererEvidence.Generation,
                foregroundWindow,
                rendererEvidence.ObservedAt);
        }

        return null;
    }

    private void TryBindSingleUnboundDraftEvidenceLocked(
        IntPtr foregroundWindow,
        DateTimeOffset now)
    {
        if (foregroundWindow == IntPtr.Zero ||
            _visibleThreadByClient.Values.Any(CanTrackSemanticThread))
        {
            return;
        }

        var candidates = _rendererDraftEvidenceByClient
            .Where(pair =>
                pair.Value.ForegroundWindow == IntPtr.Zero &&
                now >= pair.Value.ObservedAt &&
                now - pair.Value.ObservedAt <=
                    RendererDraftEvidenceLifetime &&
                (!_visibleThreadByClient.TryGetValue(
                     pair.Key,
                     out var rendererThreadId) ||
                 CodexDraftModelToggleService.IsDraftThreadId(
                     rendererThreadId)))
            .OrderByDescending(pair => pair.Value.Generation)
            .Take(2)
            .ToArray();
        if (candidates.Length != 1)
        {
            return;
        }

        var candidate = candidates[0];
        _rendererDraftEvidenceByClient[candidate.Key] =
            candidate.Value with
            {
                ForegroundWindow = foregroundWindow,
            };
        if (BindRendererToForegroundWindowLocked(
                candidate.Key,
                foregroundWindow))
        {
            InvalidateForegroundDraftLeasesLocked();
        }
    }

    internal static string? SelectRendererDraftEvidenceClient(
        IReadOnlyDictionary<string, string> visibleThreadByClient,
        IReadOnlyDictionary<string, RendererDraftEvidence>
            rendererDraftEvidenceByClient,
        DateTimeOffset now,
        TimeSpan evidenceLifetime,
        IntPtr foregroundWindow)
    {
        ArgumentNullException.ThrowIfNull(visibleThreadByClient);
        ArgumentNullException.ThrowIfNull(rendererDraftEvidenceByClient);
        if (foregroundWindow == IntPtr.Zero ||
            evidenceLifetime <= TimeSpan.Zero)
        {
            return null;
        }

        return rendererDraftEvidenceByClient
            .Where(pair =>
                now >= pair.Value.ObservedAt &&
                now - pair.Value.ObservedAt <= evidenceLifetime &&
                pair.Value.ForegroundWindow == foregroundWindow &&
                (!visibleThreadByClient.TryGetValue(
                     pair.Key,
                     out var rendererThreadId) ||
                 CodexDraftModelToggleService.IsDraftThreadId(
                     rendererThreadId)))
            .OrderByDescending(pair => pair.Value.Generation)
            .Select(pair => pair.Key)
            .FirstOrDefault();
    }

    private static bool IsInitializedClientId(string? clientId) =>
        !string.IsNullOrWhiteSpace(clientId) &&
        !clientId.Equals(InitialClientId, StringComparison.Ordinal);

    internal static VisibleThreadSelection ResolveVisibleThreadSelection(
        IEnumerable<string> visibleThreadIds)
    {
        ArgumentNullException.ThrowIfNull(visibleThreadIds);
        var distinct = visibleThreadIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var visibleThreadId = distinct.Length == 1
            ? distinct[0]
            : null;
        var semanticThreadId = visibleThreadId is not null &&
            CanTrackSemanticThread(visibleThreadId)
                ? visibleThreadId
                : null;
        return new(visibleThreadId, semanticThreadId);
    }

    private VisibleThreadSelection
        ResolveForegroundVisibleThreadSelectionLocked(
            IntPtr foregroundWindow)
    {
        string? rendererClientId = null;
        if (foregroundWindow != IntPtr.Zero)
        {
            _foregroundRendererClientByWindow.TryGetValue(
                foregroundWindow,
                out rendererClientId);
        }
        else
        {
            var knownRendererClientIds =
                _foregroundRendererClientByWindow.Values
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            if (knownRendererClientIds.Length == 1)
            {
                rendererClientId = knownRendererClientIds[0];
            }
        }

        if (!string.IsNullOrWhiteSpace(rendererClientId))
        {
            if (_visibleThreadByClient.TryGetValue(
                    rendererClientId,
                    out var rendererThreadId) &&
                !string.IsNullOrWhiteSpace(rendererThreadId))
            {
                var normalizedThreadId = rendererThreadId.Trim();
                return new(
                    normalizedThreadId,
                    CanTrackSemanticThread(normalizedThreadId)
                        ? normalizedThreadId
                        : null);
            }

            return new(null, null);
        }

        return ResolveVisibleThreadSelection(
            _visibleThreadByClient.Values);
    }

    internal static bool ApplyVisibleThreadFollowingChange(
        IDictionary<string, string> visibleThreadByClient,
        string sourceClientId,
        string threadId,
        bool following)
    {
        ArgumentNullException.ThrowIfNull(visibleThreadByClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);

        if (following)
        {
            if (visibleThreadByClient.TryGetValue(
                    sourceClientId,
                    out var currentThreadId) &&
                currentThreadId == threadId)
            {
                return false;
            }

            visibleThreadByClient[sourceClientId] = threadId;
            return true;
        }

        return visibleThreadByClient.TryGetValue(
                   sourceClientId,
                   out var previouslyFollowedThread) &&
               previouslyFollowedThread == threadId &&
               visibleThreadByClient.Remove(sourceClientId);
    }

    private static bool ReplaceVisibleThreadMap(
        IDictionary<string, string> current,
        IReadOnlyDictionary<string, string> replacement)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        if (current.Count == replacement.Count &&
            current.All(pair =>
                replacement.TryGetValue(pair.Key, out var threadId) &&
                threadId.Equals(pair.Value, StringComparison.Ordinal)))
        {
            return false;
        }

        current.Clear();
        foreach (var pair in replacement)
        {
            current[pair.Key] = pair.Value;
        }

        return true;
    }

    private async Task<CodexThreadModelState?> ReadThreadStateAsync(
        string threadId,
        string ownerClientId,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<CodexThreadModelState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = new SnapshotWaiter(
            threadId,
            ownerClientId,
            completion);
        lock (_stateSync)
        {
            if (_snapshotWaiters.Remove(threadId, out var replaced))
            {
                replaced.Completion.TrySetCanceled();
            }

            _snapshotWaiters[threadId] = waiter;
        }

        try
        {
            await SendWaiterFollowingAsync(
                threadId,
                waiter,
                cancellationToken);
            try
            {
                return await completion.Task.WaitAsync(
                    SnapshotTimeout,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
        finally
        {
            lock (_stateSync)
            {
                if (_snapshotWaiters.TryGetValue(threadId, out var pending) &&
                    ReferenceEquals(pending, waiter))
                {
                    _snapshotWaiters.Remove(threadId);
                }
            }

            // The release is guarded again only after it owns _writeGate. A
            // newer persistent tracker or replacement waiter for this key
            // therefore cannot be closed by this older waiter.
            await ReleaseFollowerLeaseAsync(threadId, ownerClientId);
        }
    }

    private Task<bool> SendTrackedFollowingAsync(
        string threadId,
        string ownerClientId,
        int generation,
        CancellationToken cancellationToken) =>
        SendFollowingConditionallyAsync(
            threadId,
            ownerClientId,
            following: true,
            () => ShouldWriteFollowerSignal(
                FollowerSignalIntent.TrackedTrue,
                generation,
                _trackingGeneration,
                trackedKeyMatches:
                    _trackedThreadId == threadId &&
                    _trackedOwnerClientId == ownerClientId,
                expectedWaiterIsCurrent: false,
                matchingWaiterExists: false),
            cancellationToken);

    private Task<bool> SendWaiterFollowingAsync(
        string threadId,
        SnapshotWaiter waiter,
        CancellationToken cancellationToken) =>
        SendFollowingConditionallyAsync(
            threadId,
            waiter.OwnerClientId,
            following: true,
            () => ShouldWriteFollowerSignal(
                FollowerSignalIntent.WaiterTrue,
                expectedGeneration: 0,
                currentGeneration: 0,
                trackedKeyMatches: false,
                expectedWaiterIsCurrent:
                    _snapshotWaiters.TryGetValue(
                        threadId,
                        out var current) &&
                    ReferenceEquals(current, waiter),
                matchingWaiterExists: false),
            cancellationToken);

    internal static bool ShouldWriteFollowerSignal(
        FollowerSignalIntent intent,
        int expectedGeneration,
        int currentGeneration,
        bool trackedKeyMatches,
        bool expectedWaiterIsCurrent,
        bool matchingWaiterExists) =>
        intent switch
        {
            FollowerSignalIntent.TrackedTrue =>
                expectedGeneration == currentGeneration &&
                trackedKeyMatches,
            FollowerSignalIntent.WaiterTrue => expectedWaiterIsCurrent,
            FollowerSignalIntent.Release =>
                !trackedKeyMatches &&
                !matchingWaiterExists,
            _ => false,
        };

    private async Task<bool> SendFollowingConditionallyAsync(
        string threadId,
        string ownerClientId,
        bool following,
        Func<bool> shouldSendLocked,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            NamedPipeClientStream pipe;
            string sourceClientId;
            lock (_stateSync)
            {
                if (!shouldSendLocked())
                {
                    return false;
                }

                if (_pipe is not { IsConnected: true } connected ||
                    string.IsNullOrWhiteSpace(_clientId))
                {
                    return false;
                }

                pipe = connected;
                sourceClientId = _clientId;
            }

            var frame = EncodeFrame(new
            {
                type = "broadcast",
                method = "thread-stream-following-changed",
                sourceClientId,
                targetClientIds = new[] { ownerClientId },
                @params = new
                {
                    conversationId = threadId,
                    hostId = LocalHostId,
                    following,
                },
                version = 1,
            });
            await pipe.WriteAsync(frame, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
            return true;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task<JsonElement> SendRequestAsync<T>(
        string method,
        int version,
        T parameters,
        string? targetClientId,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? sourceClientId = null)
    {
        var requestId = Guid.NewGuid().ToString();
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pendingRequests.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Duplicate Codex IPC request id.");
        }

        try
        {
            await SendMessageAsync(
                new
                {
                    type = "request",
                    requestId,
                    sourceClientId = sourceClientId ?? ReadClientId(),
                    version,
                    method,
                    @params = parameters,
                    targetClientId,
                    timeoutMs = checked((int)timeout.TotalMilliseconds),
                },
                cancellationToken);
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private Task SendBroadcastAsync<T>(
        string method,
        int version,
        T parameters,
        string[]? targetClientIds,
        CancellationToken cancellationToken) =>
        SendMessageAsync(
            new
            {
                type = "broadcast",
                method,
                sourceClientId = ReadClientId(),
                targetClientIds,
                @params = parameters,
                version,
            },
            cancellationToken);

    private async Task SendMessageAsync<T>(
        T message,
        CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe;
        lock (_stateSync)
        {
            pipe = _pipe is { IsConnected: true } connected
                ? connected
                : throw new IOException("Codex IPC is not connected.");
        }

        var frame = EncodeFrame(message);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await pipe.WriteAsync(frame, cancellationToken);
            await pipe.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private string ReadClientId()
    {
        lock (_stateSync)
        {
            return !string.IsNullOrWhiteSpace(_clientId)
                ? _clientId
                : throw new InvalidOperationException(
                    "Codex IPC is not initialized.");
        }
    }

    private void HandleDisconnect(
        NamedPipeClientStream pipe,
        Exception? failure)
    {
        SnapshotWaiter[] snapshotWaiters;
        lock (_stateSync)
        {
            if (!ReferenceEquals(_pipe, pipe))
            {
                return;
            }

            _pipe = null;
            _clientId = null;
            _visibleThreadByClient.Clear();
            _rendererDraftEvidenceByClient.Clear();
            _rendererDraftContinuityByClient.Clear();
            _foregroundRendererClientByWindow.Clear();
            _foregroundDraftNavigationExpectation = null;
            _visibilityRefresh = null;
            InvalidateForegroundDraftLeasesLocked();
            _selectedVisibleThreadId = null;
            _trackingGeneration++;
            _trackedThreadId = null;
            _trackedOwnerClientId = null;
            _trackedStateAccumulator = null;
            _currentThreadState = null;
            PulseVisibleThreadChangedLocked();
            snapshotWaiters = [.. _snapshotWaiters.Values];
            _snapshotWaiters.Clear();
        }

        pipe.Dispose();
        var disconnected = failure ?? new IOException("Codex IPC disconnected.");
        RaiseCurrentThreadStateChanged(null);
        foreach (var waiter in snapshotWaiters)
        {
            waiter.Completion.TrySetException(disconnected);
        }

        foreach (var request in _pendingRequests.ToArray())
        {
            if (_pendingRequests.TryRemove(request.Key, out var pending))
            {
                pending.TrySetException(disconnected);
            }
        }
    }

    private void RememberEffort(
        string threadId,
        string modelId,
        string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return;
        }

        _effortStore.Remember(threadId, modelId, effort);
    }

    private string? RecallEffort(string threadId, string modelId)
    {
        return _effortStore.Recall(threadId, modelId);
    }

    private void ConfirmSuccessfulToggleState(
        string threadId,
        string ownerClientId,
        string modelId,
        string? effort)
    {
        CodexThreadModelState? state = null;
        var requestSnapshot = false;
        var generation = 0;
        lock (_stateSync)
        {
            var visibleThreadIds = _visibleThreadByClient.Values
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (visibleThreadIds.Length != 1 ||
                visibleThreadIds[0] != threadId ||
                _trackedThreadId != threadId)
            {
                return;
            }

            if (_trackedStateAccumulator is null ||
                _trackedOwnerClientId != ownerClientId)
            {
                _trackedOwnerClientId = ownerClientId;
                _trackedStateAccumulator = new(threadId, ownerClientId);
                requestSnapshot = true;
                generation = _trackingGeneration;
            }

            state = _trackedStateAccumulator.ConfirmSettings(modelId, effort);
            _currentThreadState = state;
        }

        RaiseCurrentThreadStateChanged(state);
        if (requestSnapshot)
        {
            _ = RequestTrackedSnapshotAsync(
                threadId,
                ownerClientId,
                generation);
        }
    }

    private void RaiseCurrentThreadStateChanged(
        CodexThreadModelState? state)
    {
        var handlers = CurrentThreadStateChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (Action<CodexThreadModelState?> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(state);
            }
            catch
            {
                // A UI subscriber must not tear down the IPC reader loop.
            }
        }
    }

    private static bool IsSuccessfulUpdate(JsonElement response) =>
        IsSuccess(response) &&
        TryReadString(response, "method", out var method) &&
        method == "thread-follower-update-thread-settings" &&
        response.TryGetProperty("result", out var result) &&
        result.TryGetProperty("ok", out var ok) &&
        ok.ValueKind == JsonValueKind.True;

    private static bool IsSuccess(JsonElement response) =>
        TryReadString(response, "resultType", out var resultType) &&
        resultType == "success";

    private static bool TryReadInitializedClientId(
        JsonElement response,
        out string clientId)
    {
        clientId = string.Empty;
        return IsSuccess(response) &&
            TryReadString(response, "method", out var method) &&
            method == "initialize" &&
            response.TryGetProperty("result", out var result) &&
            TryReadString(result, "clientId", out clientId);
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
            !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }

    private static CodexModelToggleResult Failure(
        string error,
        string? threadId = null,
        CodexQuickModel previous = CodexQuickModel.Unknown,
        string? previousEffort = null,
        string? detail = null) =>
        new(
            false,
            previous,
            previous,
            threadId,
            previousEffort,
            previousEffort,
            error,
            detail);

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

    private static TaskCompletionSource<bool> NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void PulseVisibleThreadChangedLocked()
    {
        var previous = _visibleThreadChanged;
        _visibleThreadChanged = NewSignal();
        previous.TrySetResult(true);
    }

    private void InvalidateForegroundDraftLeasesLocked()
    {
        unchecked
        {
            _visibilityGeneration++;
        }
    }

    private long NextRendererDraftEvidenceGenerationLocked()
    {
        unchecked
        {
            _rendererDraftEvidenceGeneration++;
        }

        return _rendererDraftEvidenceGeneration;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        NamedPipeClientStream? pipe;
        Task? reader;
        SnapshotWaiter[] snapshotWaiters;
        lock (_stateSync)
        {
            pipe = _pipe;
            _pipe = null;
            reader = _readerTask;
            _readerTask = null;
            _clientId = null;
            _visibleThreadByClient.Clear();
            _rendererDraftEvidenceByClient.Clear();
            _rendererDraftContinuityByClient.Clear();
            _foregroundRendererClientByWindow.Clear();
            _foregroundDraftNavigationExpectation = null;
            _visibilityRefresh = null;
            InvalidateForegroundDraftLeasesLocked();
            _selectedVisibleThreadId = null;
            _trackingGeneration++;
            _trackedThreadId = null;
            _trackedOwnerClientId = null;
            _trackedStateAccumulator = null;
            _currentThreadState = null;
            snapshotWaiters = [.. _snapshotWaiters.Values];
            _snapshotWaiters.Clear();
            PulseVisibleThreadChangedLocked();
        }

        RaiseCurrentThreadStateChanged(null);
        foreach (var waiter in snapshotWaiters)
        {
            waiter.Completion.TrySetCanceled();
        }

        if (reader is not null)
        {
            try
            {
                await reader;
            }
            catch
            {
                // Reader teardown is contained during application shutdown.
            }
        }

        pipe?.Dispose();
        _lifetime.Dispose();
        // Start/toggle/tracking continuations can still be unwinding after the
        // pipe and lifetime token are cancelled. SemaphoreSlim owns no native
        // resource unless its WaitHandle is requested (it is not here), so
        // leaving these managed gates for GC avoids Release-after-Dispose
        // races during window shutdown.
    }
}
