using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NAudio.Wave;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace CodexMicro.Desktop.Services;

internal sealed record MicroVoiceInputSnapshot(
    string Phase,
    bool Active,
    string? SessionId,
    string Partial,
    string Message);

internal sealed record MicroVoiceStartResult(
    bool Success,
    bool SetupRequired,
    string Message);

internal sealed record MicroVoiceStopResult(
    bool Success,
    string Text,
    string Language,
    bool AutoSubmit,
    string Message);

internal sealed record MicroSpeechUpdate(
    string Phase,
    string Partial,
    string Message);

internal interface IMicroSpeechSession : IAsyncDisposable
{
    event EventHandler<MicroSpeechUpdate>? Updated;

    Task StartAsync(CancellationToken cancellationToken);

    Task<string> StopAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns microphone capture and speech recognition inside the keypad process.
/// DeepSeek receives only recognized text through <see cref="MicroHarnessRegistry"/>.
/// </summary>
internal sealed class MicroVoiceInputService : IAsyncDisposable
{
    private readonly MicroProfileSettings _profileSettings;
    private readonly IMicroVoiceCredentialStore _credentials;
    private readonly MicroLocalVoiceRuntime _localRuntime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMicroSpeechSession? _session;
    private MicroVoiceProfile? _activeSettings;
    private string? _activeSessionId;

    internal MicroVoiceInputService(
        MicroProfileSettings profileSettings,
        IMicroVoiceCredentialStore? credentials = null,
        MicroLocalVoiceRuntime? localRuntime = null)
    {
        _profileSettings = profileSettings ??
            throw new ArgumentNullException(nameof(profileSettings));
        _credentials = credentials ?? new MicroVoiceCredentialStore();
        _localRuntime = localRuntime ?? new MicroLocalVoiceRuntime();
    }

    internal event EventHandler<MicroVoiceInputSnapshot>? Changed;

    internal MicroVoiceInputSnapshot Current { get; private set; } = new(
        "idle",
        Active: false,
        SessionId: null,
        Partial: string.Empty,
        Message: "Voice input is idle.");

    internal async Task<MicroVoiceStartResult> StartAsync(
        string? sessionId,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null)
            {
                return new(
                    true,
                    false,
                    "The keypad voice session is already active.");
            }

            var settings = _profileSettings.Current.VoiceSettings;
            if (!settings.SetupCompleted)
            {
                Publish(new(
                    "error",
                    Active: false,
                    sessionId,
                    Partial: string.Empty,
                    Message: "VOICE_SETUP_REQUIRED: Configure voice in the Codex Micro keypad."));
                return new(
                    false,
                    true,
                    "VOICE_SETUP_REQUIRED: Configure voice in the Codex Micro keypad.");
            }

            Validate(settings);
            _activeSettings = settings;
            _activeSessionId = sessionId;
            Publish(new(
                "starting",
                Active: true,
                sessionId,
                Partial: string.Empty,
                Message: settings.Provider == MicroVoiceProviders.LocalQwen
                    ? "The keypad is preparing the local Qwen voice service."
                    : "The keypad is starting voice recognition."));
            if (settings.Provider == MicroVoiceProviders.LocalQwen)
            {
                await _localRuntime.EnsureReadyAsync(
                    settings,
                    cancellationToken);
            }
            var credential = settings.Provider == MicroVoiceProviders.RemoteWebSocket
                ? _credentials.Read(
                    _profileSettings.VoiceCredentialScope,
                    settings.Provider)
                : null;
            var speech = CreateSession(settings, credential, capture: true);
            speech.Updated += Speech_Updated;
            _session = speech;
            try
            {
                await speech.StartAsync(cancellationToken);
                Publish(new(
                    "listening",
                    Active: true,
                    sessionId,
                    Partial: string.Empty,
                    Message: "The keypad microphone is listening."));
                return new(true, false, "The keypad microphone is listening.");
            }
            catch
            {
                speech.Updated -= Speech_Updated;
                _session = null;
                _activeSettings = null;
                _activeSessionId = null;
                await speech.DisposeAsync();
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            _activeSettings = null;
            _activeSessionId = null;
            Publish(new(
                "idle",
                Active: false,
                sessionId,
                Partial: string.Empty,
                Message: "Keypad voice startup was cancelled."));
            throw;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            _activeSettings = null;
            _activeSessionId = null;
            Publish(new(
                "error",
                Active: false,
                sessionId,
                Partial: string.Empty,
                Message: exception.Message));
            return new(false, false, exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<MicroVoiceStopResult> StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var speech = _session;
            var settings = _activeSettings;
            if (speech is null || settings is null)
            {
                return new(
                    true,
                    string.Empty,
                    string.Empty,
                    false,
                    "The keypad voice session is already inactive.");
            }

            Publish(new(
                "stopping",
                Active: true,
                _activeSessionId,
                Current.Partial,
                "The keypad is finishing speech recognition."));
            try
            {
                var text = (await speech.StopAsync(cancellationToken)).Trim();
                Publish(new(
                    "idle",
                    Active: false,
                    _activeSessionId,
                    Partial: string.Empty,
                    Message: text.Length == 0
                        ? "Voice input ended without recognized text."
                        : "Voice transcription is ready."));
                return new(
                    true,
                    text,
                    settings.Language,
                    settings.AutoSubmit,
                    text.Length == 0
                        ? "Voice input ended without recognized text."
                        : "Voice transcription is ready.");
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                Publish(new(
                    "error",
                    Active: false,
                    _activeSessionId,
                    Partial: string.Empty,
                    Message: exception.Message));
                return new(
                    false,
                    string.Empty,
                    settings.Language,
                    settings.AutoSubmit,
                    exception.Message);
            }
            finally
            {
                speech.Updated -= Speech_Updated;
                _session = null;
                _activeSettings = null;
                _activeSessionId = null;
                await speech.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task TestAndSaveAsync(
        MicroVoiceProfile value,
        string? credential,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var candidate = value with { SetupCompleted = false };
        Validate(candidate);
        var candidateCredential = candidate.Provider ==
            MicroVoiceProviders.RemoteWebSocket
                ? !string.IsNullOrWhiteSpace(credential)
                    ? credential.Trim()
                    : _credentials.Read(
                        _profileSettings.VoiceCredentialScope,
                        candidate.Provider)
                : null;
        if (candidate.Provider == MicroVoiceProviders.LocalQwen)
        {
            await _localRuntime.EnsureReadyAsync(
                candidate,
                cancellationToken);
        }
        await using var probe = CreateSession(
            candidate,
            candidateCredential,
            capture: false);
        await probe.StartAsync(cancellationToken);
        await probe.StopAsync(cancellationToken);

        // Keep the last known-good profile and credential intact while a new
        // provider is being checked. Qwen can take minutes to load on first
        // use; closing the window or a failed probe must not permanently turn
        // a previously verified keypad into VOICE_SETUP_REQUIRED.
        if (candidate.Provider == MicroVoiceProviders.RemoteWebSocket &&
            !string.IsNullOrWhiteSpace(credential))
        {
            _credentials.Write(
                _profileSettings.VoiceCredentialScope,
                candidate.Provider,
                candidateCredential!);
        }
        _profileSettings.SetVoiceSettings(candidate with
        {
            SetupCompleted = true,
        });
    }

    internal bool HasCredential(string provider) =>
        provider == MicroVoiceProviders.RemoteWebSocket &&
        !string.IsNullOrEmpty(_credentials.Read(
            _profileSettings.VoiceCredentialScope,
            provider));

    internal void ClearCredential(string provider)
    {
        if (provider == MicroVoiceProviders.RemoteWebSocket)
        {
            _credentials.Delete(_profileSettings.VoiceCredentialScope, provider);
        }
    }

    internal Task WarmUpAsync(
        CancellationToken cancellationToken = default) =>
        _localRuntime.WarmUpAsync(
            _profileSettings.Current.VoiceSettings,
            cancellationToken);

    internal Task<bool> ProbeLocalReadyAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = _profileSettings.Current.VoiceSettings;
        return settings.Provider == MicroVoiceProviders.LocalQwen &&
            settings.SetupCompleted
                ? _localRuntime.ProbeReadyAsync(settings, cancellationToken)
                : Task.FromResult(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_session is not null)
            {
                _session.Updated -= Speech_Updated;
                await _session.DisposeAsync();
                _session = null;
            }
            await _localRuntime.DisposeAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    internal static void Validate(MicroVoiceProfile value)
    {
        if (!MicroVoiceProviders.IsKnown(value.Provider))
        {
            throw new InvalidOperationException("The selected voice provider is unsupported.");
        }
        if (value.Language.Length > 35)
        {
            throw new InvalidOperationException(
                "The voice language must be empty or a short BCP-47 tag.");
        }
        if (value.Provider == MicroVoiceProviders.LocalQwen)
        {
            ValidateStreamingUri(value.LocalStreamUrl, requireLoopback: true);
            MicroLocalVoiceRuntime.Validate(value);
            if (string.IsNullOrWhiteSpace(value.LocalModel))
            {
                throw new InvalidOperationException("The local ASR model is required.");
            }
        }
        if (value.Provider == MicroVoiceProviders.RemoteWebSocket)
        {
            ValidateStreamingUri(value.RemoteUrl, requireLoopback: false);
        }
    }

    internal static void ValidateStreamingUri(
        string value,
        bool requireLoopback)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Scheme != "wss" &&
                (uri.Scheme != "ws" || !uri.IsLoopback)) ||
            (requireLoopback && !uri.IsLoopback))
        {
            throw new InvalidOperationException(requireLoopback
                ? "Local ASR must use an unauthenticated loopback ws:// or wss:// address."
                : "Remote ASR must use wss://, or ws:// on loopback, without URL credentials.");
        }
    }

    private static IMicroSpeechSession CreateSession(
        MicroVoiceProfile settings,
        string? credential,
        bool capture) =>
        settings.Provider == MicroVoiceProviders.System
            ? new WindowsSpeechSession(settings.Language, capture)
            : new StreamingSpeechSession(
                settings.Provider == MicroVoiceProviders.LocalQwen
                    ? settings.LocalStreamUrl
                    : settings.RemoteUrl,
                settings.Provider == MicroVoiceProviders.LocalQwen
                    ? settings.LocalModel
                    : settings.RemoteModel,
                settings.Language,
                credential,
                capture);

    private void Speech_Updated(object? sender, MicroSpeechUpdate value) =>
        Publish(new(
            value.Phase,
            Active: true,
            _activeSessionId,
            value.Partial,
            value.Message));

    private void Publish(MicroVoiceInputSnapshot value)
    {
        Current = value;
        Changed?.Invoke(this, value);
    }
}

internal sealed class WindowsSpeechSession : IMicroSpeechSession
{
    private readonly string _language;
    private readonly bool _capture;
    private readonly List<string> _segments = [];
    private SpeechRecognizer? _recognizer;
    private bool _started;

    internal WindowsSpeechSession(string language, bool capture)
    {
        _language = language;
        _capture = capture;
    }

    public event EventHandler<MicroSpeechUpdate>? Updated;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _recognizer = string.IsNullOrWhiteSpace(_language)
            ? new SpeechRecognizer()
            : new SpeechRecognizer(new Language(_language));
        var compilation = await _recognizer.CompileConstraintsAsync();
        if (compilation.Status != SpeechRecognitionResultStatus.Success)
        {
            throw new InvalidOperationException(
                $"Windows speech recognition could not initialize ({compilation.Status}).");
        }
        if (!_capture)
        {
            return;
        }

        _recognizer.HypothesisGenerated += Recognizer_HypothesisGenerated;
        _recognizer.ContinuousRecognitionSession.ResultGenerated +=
            Session_ResultGenerated;
        await _recognizer.ContinuousRecognitionSession.StartAsync();
        _started = true;
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_recognizer is not null && _started)
        {
            await _recognizer.ContinuousRecognitionSession.StopAsync();
            _started = false;
        }
        return JoinSegments(_segments);
    }

    public async ValueTask DisposeAsync()
    {
        if (_recognizer is not null)
        {
            _recognizer.HypothesisGenerated -= Recognizer_HypothesisGenerated;
            _recognizer.ContinuousRecognitionSession.ResultGenerated -=
                Session_ResultGenerated;
            if (_started)
            {
                try
                {
                    await _recognizer.ContinuousRecognitionSession.CancelAsync();
                }
                catch
                {
                    // The session may already have completed after device loss.
                }
            }
            _recognizer.Dispose();
            _recognizer = null;
        }
    }

    private void Recognizer_HypothesisGenerated(
        SpeechRecognizer sender,
        SpeechRecognitionHypothesisGeneratedEventArgs args) =>
        Updated?.Invoke(this, new(
            "listening",
            args.Hypothesis.Text,
            "Windows speech recognition is listening."));

    private void Session_ResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Status == SpeechRecognitionResultStatus.Success &&
            !string.IsNullOrWhiteSpace(args.Result.Text))
        {
            _segments.Add(args.Result.Text.Trim());
            Updated?.Invoke(this, new(
                "listening",
                string.Empty,
                "Windows speech recognition produced text."));
        }
    }

    private static string JoinSegments(IEnumerable<string> values) =>
        string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}

internal sealed class StreamingSpeechSession : IMicroSpeechSession
{
    private const int MaximumMessageBytes = 64 * 1_024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    private readonly Uri _uri;
    private readonly string _model;
    private readonly string _language;
    private readonly string? _credential;
    private readonly bool _capture;
    private readonly ClientWebSocket _socket = new();
    private readonly Channel<byte[]> _audio = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(48)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly TaskCompletionSource<bool> _ready = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _done = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<string> _segments = [];
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _receiveTask;
    private Task? _sendTask;
    private WaveIn? _captureDevice;
    private bool _started;

    internal StreamingSpeechSession(
        string uri,
        string model,
        string language,
        string? credential,
        bool capture)
    {
        _uri = new Uri(uri);
        _model = model;
        _language = language;
        _credential = credential;
        _capture = capture;
    }

    public event EventHandler<MicroSpeechUpdate>? Updated;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_credential))
        {
            _socket.Options.SetRequestHeader(
                "Authorization",
                $"Bearer {_credential.Trim()}");
        }

        using var connect = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        connect.CancelAfter(ConnectTimeout);
        await _socket.ConnectAsync(_uri, connect.Token);
        _receiveTask = ReceiveAsync(_lifetime.Token);
        await SendTextAsync(JsonSerializer.Serialize(new
        {
            type = "start",
            protocol = "dsh-stream-v1",
            encoding = "pcm_s16le",
            sampleRate = 16_000,
            channels = 1,
            language = string.IsNullOrWhiteSpace(_language)
                ? null
                : _language,
            model = _model,
        }), cancellationToken);
        await _ready.Task.WaitAsync(ReadyTimeout, cancellationToken);
        _started = true;
        if (!_capture)
        {
            return;
        }

        _sendTask = SendAudioAsync(_lifetime.Token);
        _captureDevice = new WaveIn
        {
            DeviceNumber = 0,
            WaveFormat = new WaveFormat(16_000, 16, 1),
            BufferMilliseconds = 40,
            NumberOfBuffers = 4,
        };
        _captureDevice.DataAvailable += Capture_DataAvailable;
        _captureDevice.StartRecording();
    }

    public async Task<string> StopAsync(CancellationToken cancellationToken)
    {
        if (!_started)
        {
            return string.Empty;
        }

        StopCapture();
        _audio.Writer.TryComplete();
        if (_sendTask is not null)
        {
            await _sendTask.WaitAsync(cancellationToken);
        }
        if (_socket.State == WebSocketState.Open)
        {
            await SendTextAsync(
                JsonSerializer.Serialize(new { type = _capture ? "stop" : "cancel" }),
                cancellationToken);
        }
        if (_capture)
        {
            await _done.Task.WaitAsync(StopTimeout, cancellationToken);
        }
        return JoinSegments(_segments);
    }

    public async ValueTask DisposeAsync()
    {
        StopCapture();
        _audio.Writer.TryComplete();
        _lifetime.Cancel();
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseOutputAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "keypad voice complete",
                    CancellationToken.None);
            }
            catch
            {
                // Network teardown is best-effort during keypad disposal.
            }
        }
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask;
            }
            catch
            {
                // Any actionable receive failure was already surfaced.
            }
        }
        _socket.Dispose();
        _lifetime.Dispose();
    }

    private async Task SendAudioAsync(CancellationToken cancellationToken)
    {
        await foreach (var bytes in _audio.Reader.ReadAllAsync(cancellationToken))
        {
            if (_socket.State != WebSocketState.Open)
            {
                break;
            }
            await _socket.SendAsync(
                bytes,
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);
        }
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1_024];
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                _socket.State is (WebSocketState.Open or WebSocketState.CloseSent))
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (!_done.Task.IsCompleted)
                        {
                            _done.TrySetException(new InvalidOperationException(
                                "The ASR connection closed before transcription completed."));
                        }
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaximumMessageBytes)
                    {
                        throw new InvalidDataException("The ASR response is too large.");
                    }
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidDataException("The ASR returned a non-text control frame.");
                }
                HandleFrame(Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Expected during keypad shutdown.
        }
        catch (Exception exception)
        {
            _ready.TrySetException(exception);
            _done.TrySetException(exception);
            Updated?.Invoke(this, new("error", string.Empty, exception.Message));
        }
    }

    private void HandleFrame(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeValue) ||
            typeValue.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("The ASR returned a frame without a type.");
        }
        switch (typeValue.GetString())
        {
            case "ready":
                _ready.TrySetResult(true);
                return;
            case "partial":
                var partial = ReadText(root);
                Updated?.Invoke(this, new(
                    "listening",
                    partial,
                    "Streaming ASR is listening."));
                return;
            case "final":
                var final = ReadText(root).Trim();
                if (final.Length != 0)
                {
                    _segments.Add(final);
                }
                Updated?.Invoke(this, new(
                    "listening",
                    string.Empty,
                    "Streaming ASR produced text."));
                return;
            case "done":
                _done.TrySetResult(true);
                return;
            case "error":
                var message = root.TryGetProperty("message", out var errorValue) &&
                    errorValue.ValueKind == JsonValueKind.String
                        ? errorValue.GetString() ?? "ASR reported an error."
                        : "ASR reported an error.";
                var exception = new InvalidOperationException(message);
                _ready.TrySetException(exception);
                _done.TrySetException(exception);
                return;
            default:
                throw new InvalidDataException("The ASR returned an unsupported frame.");
        }
    }

    private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0)
        {
            return;
        }
        var copy = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, copy, 0, copy.Length);
        _audio.Writer.TryWrite(copy);
    }

    private async Task SendTextAsync(
        string value,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        await _socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }

    private void StopCapture()
    {
        var capture = Interlocked.Exchange(ref _captureDevice, null);
        if (capture is null)
        {
            return;
        }
        capture.DataAvailable -= Capture_DataAvailable;
        try
        {
            capture.StopRecording();
        }
        finally
        {
            capture.Dispose();
        }
    }

    private static string ReadText(JsonElement root) =>
        root.TryGetProperty("text", out var text) &&
        text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : throw new InvalidDataException("The ASR text frame is invalid.");

    private static string JoinSegments(IEnumerable<string> values) =>
        string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}
