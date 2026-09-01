using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace AgentController.MicroBroker;

public enum MicroKeypadControlCommand
{
    Ping,
    Show,
    Restart,
#if DEBUG
    E2eNewTask,
    E2eToggleQuickModel,
#endif
}

public enum MicroKeypadControlState
{
    Ready,
    Restarting,
    Busy,
    Rejected,
}

public sealed record MicroKeypadControlResponse(
    int Version,
    bool Accepted,
    MicroKeypadControlState State,
    string InstanceId,
    string? Detail = null);

public static class MicroKeypadControlClient
{
    public const int ProtocolVersion = 1;

    internal static string DefaultPipeName { get; } =
        BuildDefaultPipeName();

    public static Task<MicroKeypadControlResponse?> TrySendAsync(
        MicroKeypadControlCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default) =>
        TrySendAsync(
            DefaultPipeName,
            command,
            timeout,
            cancellationToken);

    internal static async Task<MicroKeypadControlResponse?> TrySendAsync(
        string pipeName,
        MicroKeypadControlCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        using var timeoutCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutCancellation.Token)
                .ConfigureAwait(false);

            var request = new MicroKeypadControlRequest(
                ProtocolVersion,
                command,
                Guid.NewGuid().ToString("N"));
            using var reader = new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true)
            {
                AutoFlush = true,
            };
            await writer.WriteLineAsync(
                    JsonSerializer.Serialize(request))
                .WaitAsync(timeoutCancellation.Token)
                .ConfigureAwait(false);
            var responseLine = await reader.ReadLineAsync(
                    timeoutCancellation.Token)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return null;
            }

            var response = JsonSerializer.Deserialize<
                MicroKeypadControlResponse>(responseLine);
            return response is { Version: ProtocolVersion }
                ? response
                : null;
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return null;
        }
    }

    internal sealed record MicroKeypadControlRequest(
        int Version,
        MicroKeypadControlCommand Command,
        string RequestId);

    private static string BuildDefaultPipeName()
    {
        using var process = Process.GetCurrentProcess();
        return "CodexMicro.Keypad.Control." +
            "1C01985F-1A5E-47DB-8E70-240EBA2F4D76." +
            process.SessionId;
    }
}

public sealed class MicroKeypadControlServer : IAsyncDisposable
{
    private readonly string _pipeName;
    private readonly Func<
        MicroKeypadControlCommand,
        CancellationToken,
        Task<MicroKeypadControlResponse>> _handler;
    private readonly Func<
        MicroKeypadControlCommand,
        CancellationToken,
        Task>? _afterResponse;
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _listener;
    private int _disposeStarted;

    public MicroKeypadControlServer(
        Func<
            MicroKeypadControlCommand,
            CancellationToken,
            Task<MicroKeypadControlResponse>> handler,
        Func<
            MicroKeypadControlCommand,
            CancellationToken,
            Task>? afterResponse = null)
        : this(
            MicroKeypadControlClient.DefaultPipeName,
            handler,
            afterResponse)
    {
    }

    internal MicroKeypadControlServer(
        string pipeName,
        Func<
            MicroKeypadControlCommand,
            CancellationToken,
            Task<MicroKeypadControlResponse>> handler,
        Func<
            MicroKeypadControlCommand,
            CancellationToken,
            Task>? afterResponse = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(handler);
        _pipeName = pipeName;
        _handler = handler;
        _afterResponse = afterResponse;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeStarted) != 0,
            this);
        _listener ??= ListenAsync(_lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        if (_listener is not null)
        {
            try
            {
                await _listener.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Disposal owns cancellation of the listener.
            }
            catch (Exception) when (_lifetime.IsCancellationRequested)
            {
                // A listener that faulted before disposal must not prevent
                // the keypad from completing its own shutdown.
            }
        }

        _lifetime.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous |
                    PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken)
                    .ConfigureAwait(false);
                await HandleConnectionAsync(pipe, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException)
            {
                // A client can disconnect between connect and reply. The
                // next listener instance remains available.
            }
        }
    }

    private async Task HandleConnectionAsync(
        Stream pipe,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            pipe,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        await using var writer = new StreamWriter(
            pipe,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            leaveOpen: true)
        {
            AutoFlush = true,
        };

        MicroKeypadControlResponse response;
        MicroKeypadControlCommand? requestCommand = null;
        try
        {
            var requestLine = await reader.ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            var request = string.IsNullOrWhiteSpace(requestLine)
                ? null
                : JsonSerializer.Deserialize<
                    MicroKeypadControlClient.MicroKeypadControlRequest>(
                        requestLine);
            if (request is
                {
                    Version: MicroKeypadControlClient.ProtocolVersion,
                    RequestId.Length: > 0,
                })
            {
                requestCommand = request.Command;
                response = await _handler(
                        request.Command,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                response = new MicroKeypadControlResponse(
                    MicroKeypadControlClient.ProtocolVersion,
                    Accepted: false,
                    MicroKeypadControlState.Rejected,
                    InstanceId: string.Empty,
                    Detail: "Unsupported keypad control request.");
            }
        }
        catch (JsonException)
        {
            response = new MicroKeypadControlResponse(
                MicroKeypadControlClient.ProtocolVersion,
                Accepted: false,
                MicroKeypadControlState.Rejected,
                InstanceId: string.Empty,
                Detail: "Invalid keypad control request.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            response = new MicroKeypadControlResponse(
                MicroKeypadControlClient.ProtocolVersion,
                Accepted: false,
                MicroKeypadControlState.Rejected,
                InstanceId: string.Empty,
                Detail: exception.Message);
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(response))
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (response.Accepted &&
            requestCommand is { } command &&
            _afterResponse is not null)
        {
            await _afterResponse(command, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
