using System.Buffers.Binary;
using System.Collections.Concurrent;
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
    string? Error = null);

internal sealed record CodexThreadModelState(
    string ThreadId,
    string ModelId,
    string? Effort);

/// <summary>
/// Changes the next-turn settings on the App Server already owned by Codex
/// Desktop. The bridge uses Codex's versioned cross-window IPC protocol; it
/// never opens or drives the model picker and never guesses from recent tasks.
/// </summary>
internal sealed class CodexModelToggleService : IAsyncDisposable
{
    private sealed record SnapshotWaiter(
        string OwnerClientId,
        TaskCompletionSource<CodexThreadModelState> Completion);

    private const string PipeName = "codex-ipc";
    private const string LocalHostId = "local";
    private const string InitialClientId = "initializing-client";
    private const int MaximumFrameBytes = 256 * 1024 * 1024;
    private static readonly TimeSpan ConnectTimeout =
        TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CurrentThreadTimeout =
        TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan SnapshotTimeout =
        TimeSpan.FromSeconds(2);

    private readonly object _stateSync = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _toggleGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>
        _pendingRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _visibleThreadByClient =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, SnapshotWaiter>
        _snapshotWaiters = new(StringComparer.Ordinal);
    private readonly CodexThreadModelEffortStore _effortStore;
    private TaskCompletionSource<bool> _visibleThreadChanged = NewSignal();
    private NamedPipeClientStream? _pipe;
    private Task? _readerTask;
    private string? _clientId;
    private int _disposed;

    internal CodexModelToggleService(
        CodexThreadModelEffortStore? effortStore = null)
    {
        _effortStore = effortStore ?? new CodexThreadModelEffortStore();
    }

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
                InvalidDataException)
        {
            return false;
        }
    }

    internal async Task<CodexModelToggleResult> ToggleAsync(
        CodexQuickModel first,
        CodexQuickModel second,
        CancellationToken cancellationToken)
    {
        ValidatePair(first, second);
        await _toggleGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureConnectedAsync(cancellationToken);
            var currentThread = await WaitForSingleVisibleThreadAsync(
                cancellationToken);
            if (currentThread.Error is not null)
            {
                return Failure(currentThread.Error);
            }

            var threadId = currentThread.ThreadId!;
            var owner = await DiscoverOwnerAsync(threadId, cancellationToken);
            if (owner is null)
            {
                return Failure("thread-owner-unavailable", threadId);
            }

            var state = await ReadThreadStateAsync(
                threadId,
                owner,
                cancellationToken);
            if (state is null)
            {
                return Failure("thread-state-unavailable", threadId);
            }

            var previous = ParseModelId(state.ModelId);
            var previousEffort = ResolveTargetEffort(
                state.ModelId,
                state.Effort);
            var visibilityError = ValidateSelectedThreadIsStillVisible(threadId);
            if (visibilityError is not null)
            {
                return Failure(
                    visibilityError,
                    threadId,
                    previous,
                    previousEffort);
            }

            var target = ResolveToggleTarget(previous, first, second);
            var targetModelId = ToModelId(target);
            RememberEffort(
                state.ThreadId,
                state.ModelId,
                previousEffort);
            var targetEffort = ResolveTargetEffort(
                targetModelId,
                RecallEffort(state.ThreadId, targetModelId));

            var response = await SendRequestAsync(
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
                owner,
                RequestTimeout,
                cancellationToken);
            if (!IsSuccessfulUpdate(response))
            {
                return Failure(
                    "thread-settings-rejected",
                    threadId,
                    previous,
                    previousEffort);
            }

            RememberEffort(threadId, targetModelId, targetEffort);
            return new(
                true,
                previous,
                target,
                threadId,
                previousEffort,
                targetEffort);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException)
        {
            return Failure("ipc-timeout");
        }
        catch (Exception exception) when (
            exception is IOException or
                InvalidDataException or
                InvalidOperationException or
                JsonException or
                ObjectDisposedException)
        {
            return Failure("ipc-unavailable");
        }
        finally
        {
            _toggleGate.Release();
        }
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
        try
        {
            var path = modelsCachePath ?? Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".codex",
                "models_cache.json");
            if (!File.Exists(path))
            {
                return fallback;
            }

            using var cache = JsonDocument.Parse(File.ReadAllText(path));
            if (!cache.RootElement.TryGetProperty("models", out var models) ||
                models.ValueKind != JsonValueKind.Array)
            {
                return fallback;
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
                if (!string.IsNullOrWhiteSpace(rememberedEffort) &&
                    supported.Contains(rememberedEffort))
                {
                    return rememberedEffort;
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
            // A stale or partially-written cache falls back to known defaults.
        }

        return fallback;
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

            lock (_stateSync)
            {
                _pipe?.Dispose();
                _pipe = pipe;
                _clientId = null;
                _visibleThreadByClient.Clear();
                PulseVisibleThreadChangedLocked();
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
                }
            }
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
                ProcessMessage(message.RootElement);
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
                lock (_stateSync)
                {
                    _visibleThreadByClient.Clear();
                    PulseVisibleThreadChangedLocked();
                }
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
        lock (_stateSync)
        {
            if (following)
            {
                _visibleThreadByClient[sourceClientId] = threadId;
            }
            else if (_visibleThreadByClient.TryGetValue(
                         sourceClientId,
                         out var current) &&
                     current == threadId)
            {
                _visibleThreadByClient.Remove(sourceClientId);
            }

            PulseVisibleThreadChangedLocked();
        }
    }

    private void ProcessThreadStateChanged(JsonElement message)
    {
        if (!TryReadString(message, "sourceClientId", out var sourceClientId) ||
            !message.TryGetProperty("params", out var parameters) ||
            !TryReadString(parameters, "hostId", out var hostId) ||
            hostId != LocalHostId ||
            !TryReadString(parameters, "conversationId", out var threadId) ||
            !parameters.TryGetProperty("change", out var change) ||
            !TryReadString(change, "type", out var changeType) ||
            changeType != "snapshot" ||
            !change.TryGetProperty("conversationState", out var state) ||
            !TryReadString(state, "latestModel", out var modelId))
        {
            return;
        }

        var effort = TryReadString(
            state,
            "latestReasoningEffort",
            out var effortValue)
                ? effortValue
                : null;
        SnapshotWaiter? waiter;
        lock (_stateSync)
        {
            if (!_snapshotWaiters.TryGetValue(threadId, out waiter) ||
                waiter.OwnerClientId != sourceClientId)
            {
                return;
            }

            _snapshotWaiters.Remove(threadId);
        }

        waiter.Completion.TrySetResult(new(threadId, modelId, effort));
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

        lock (_stateSync)
        {
            if (_visibleThreadByClient.Remove(clientId))
            {
                PulseVisibleThreadChangedLocked();
            }
        }
    }

    private async Task<(string? ThreadId, string? Error)>
        WaitForSingleVisibleThreadAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + CurrentThreadTimeout;
        while (true)
        {
            Task signal;
            lock (_stateSync)
            {
                var threadIds = _visibleThreadByClient.Values
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (threadIds.Length == 1)
                {
                    return (threadIds[0], null);
                }

                if (threadIds.Length > 1)
                {
                    return (null, "multiple-visible-threads");
                }

                signal = _visibleThreadChanged.Task;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return (null, "no-visible-thread");
            }

            try
            {
                await signal.WaitAsync(remaining, cancellationToken);
            }
            catch (TimeoutException)
            {
                return (null, "no-visible-thread");
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

    private string? ValidateSelectedThreadIsStillVisible(string threadId)
    {
        string[] visibleThreadIds;
        lock (_stateSync)
        {
            visibleThreadIds = _visibleThreadByClient.Values.ToArray();
        }

        return ValidateVisibleThreadSelection(visibleThreadIds, threadId);
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

    private async Task<CodexThreadModelState?> ReadThreadStateAsync(
        string threadId,
        string ownerClientId,
        CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource<CodexThreadModelState>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_stateSync)
        {
            if (_snapshotWaiters.Remove(threadId, out var replaced))
            {
                replaced.Completion.TrySetCanceled();
            }

            _snapshotWaiters[threadId] = new(ownerClientId, waiter);
        }

        try
        {
            await SendFollowingAsync(
                threadId,
                ownerClientId,
                following: true,
                cancellationToken);
            try
            {
                return await waiter.Task.WaitAsync(
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
                    ReferenceEquals(pending.Completion, waiter))
                {
                    _snapshotWaiters.Remove(threadId);
                }
            }

            try
            {
                await SendFollowingAsync(
                    threadId,
                    ownerClientId,
                    following: false,
                    CancellationToken.None);
            }
            catch
            {
                // Closing the follower lease is best-effort on disconnect.
            }
        }
    }

    private Task SendFollowingAsync(
        string threadId,
        string ownerClientId,
        bool following,
        CancellationToken cancellationToken) =>
        SendBroadcastAsync(
            "thread-stream-following-changed",
            version: 1,
            new
            {
                conversationId = threadId,
                hostId = LocalHostId,
                following,
            },
            [ownerClientId],
            cancellationToken);

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
        lock (_stateSync)
        {
            if (!ReferenceEquals(_pipe, pipe))
            {
                return;
            }

            _pipe = null;
            _clientId = null;
            _visibleThreadByClient.Clear();
            PulseVisibleThreadChangedLocked();
            foreach (var waiter in _snapshotWaiters.Values)
            {
                waiter.Completion.TrySetException(
                    failure ?? new IOException("Codex IPC disconnected."));
            }

            _snapshotWaiters.Clear();
        }

        pipe.Dispose();
        var disconnected = failure ?? new IOException("Codex IPC disconnected.");
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
        string? previousEffort = null) =>
        new(
            false,
            previous,
            previous,
            threadId,
            previousEffort,
            previousEffort,
            error);

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
        lock (_stateSync)
        {
            pipe = _pipe;
            _pipe = null;
            reader = _readerTask;
            _readerTask = null;
            _clientId = null;
            _visibleThreadByClient.Clear();
            PulseVisibleThreadChangedLocked();
        }

        pipe?.Dispose();
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

        _lifetime.Dispose();
        _connectGate.Dispose();
        _writeGate.Dispose();
        _toggleGate.Dispose();
    }
}
