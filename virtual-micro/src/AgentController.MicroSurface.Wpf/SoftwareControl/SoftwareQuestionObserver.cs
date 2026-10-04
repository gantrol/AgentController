using CodexMicro.Codex;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexMicro.Desktop.Services;

namespace AgentController.MicroSurface.Wpf.SoftwareControl;

internal sealed class SoftwareQuestionObserver : IDisposable
{
    private readonly CodexPeerClient _peer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _gate = new(1);
    private readonly object _sync = new();
    private readonly Dictionary<string, CodexThreadModelStateAccumulator> _streams = new(StringComparer.Ordinal);
    private readonly HashSet<string> _resync = new(StringComparer.Ordinal);
    private bool _disposed;

    internal event Action<string, IReadOnlyList<string>>? AnswersAccepted;

    internal SoftwareQuestionObserver(CodexPeerClient? peer = null)
    {
        _peer = peer ?? new();
        _peer.Broadcast += OnBroadcast;
        _peer.Disconnected += () => { lock (_sync) { _streams.Clear(); _resync.Clear(); } };
    }

    internal async Task RefreshAsync(IEnumerable<string> pendingThreads)
    {
        if (_disposed || !await _gate.WaitAsync(0)) return;
        try
        {
            var wanted = pendingThreads.ToHashSet(StringComparer.Ordinal);
            CodexThreadModelStateAccumulator[] removed;
            lock (_sync)
            {
                removed = _streams.Values.Where(stream => !wanted.Contains(stream.ThreadId) ||
                    _resync.Contains(stream.ThreadId)).ToArray();
                foreach (var stream in removed) { _streams.Remove(stream.ThreadId); _resync.Remove(stream.ThreadId); }
            }
            if (wanted.Count == 0 && removed.Length == 0) return;
            var token = _lifetime.Token;
            await _peer.ConnectAsync(token);
            foreach (var stream in removed)
                await _peer.FollowAsync(stream.ThreadId, stream.OwnerClientId, false, token);
            foreach (var threadId in wanted)
            {
                lock (_sync) { if (_streams.ContainsKey(threadId)) continue; }
                var owner = (await _peer.RequestAsync("thread-owner-discovery", 1,
                    new { hostId = "local", conversationId = threadId }, null, token)).Required("handledByClientId");
                lock (_sync) _streams[threadId] = new(threadId, owner);
                try { await _peer.FollowAsync(threadId, owner, true, token); }
                catch { lock (_sync) _streams.Remove(threadId); throw; }
            }
        }
        catch (Exception error) when (error is IOException or TimeoutException or OperationCanceledException or ObjectDisposedException)
        {
            SoftwareControlDiagnostics.Write("question-observer-unavailable", error);
        }
        finally { _gate.Release(); }
    }

    private void OnBroadcast(JsonObject message)
    {
        if (message.Text("method") == "client-status-changed" &&
            message["params"]?.Text("status") == "disconnected")
        {
            lock (_sync)
                foreach (var stream in _streams.Values.Where(stream =>
                    stream.OwnerClientId == message["params"]?.Text("clientId")))
                    _resync.Add(stream.ThreadId);
            return;
        }
        if (message.Text("method") != "thread-stream-state-changed" ||
            message["version"]?.GetValue<int>() != 11 ||
            message["params"]?.Text("hostId") != "local" ||
            message["params"]?.Text("conversationId") is not { } threadId ||
            message["params"]?["change"] is not { } change) return;
        string[] replies;
        lock (_sync)
        {
            if (!_streams.TryGetValue(threadId, out var stream) ||
                stream.OwnerClientId != message.Text("sourceClientId")) return;
            using var document = JsonDocument.Parse(change.ToJsonString());
            var result = stream.ApplyChange(document.RootElement);
            if (result.RequiresSnapshot) _resync.Add(threadId);
            replies = result.Applied ? stream.QuestionAnswers.DrainAcceptedReplies() : [];
        }
        if (replies.Length > 0) AnswersAccepted?.Invoke(threadId, replies);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _ = DisposeAsync();
    }

    private async Task DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await _peer.DisposeAsync(); }
        finally { _lifetime.Dispose(); _gate.Release(); }
    }
}
