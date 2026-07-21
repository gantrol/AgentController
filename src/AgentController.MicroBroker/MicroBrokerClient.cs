using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using CodexMicro.Protocol;

namespace AgentController.MicroBroker;

public enum MicroBrokerClientState
{
    Unavailable,
    Ready,
    Faulted,
}

public sealed class MicroBrokerClient : IDisposable
{
    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromMilliseconds(900);
    private const int ConnectFailureBackoffMs = 2_000;

    private readonly Guid _clientId = Guid.NewGuid();
    private readonly string _clientName;
    private readonly string? _brokerExecutablePath;
    private readonly string _pipeName;
    private readonly bool _launchEnabled;
    private readonly object _connectionSync = new();
    private readonly object _requestSync = new();
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private Task? _backgroundConnectTask;
    private long _requestId;
    private long _eventCursor;
    private long _retryAfter;
    private int _backgroundConnectRunning;
    private volatile bool _connected;
    private bool _disposed;
    private int _state = (int)MicroBrokerClientState.Unavailable;

    public MicroBrokerClient(
        string clientName,
        string? brokerExecutablePath = null)
        : this(
            clientName,
            brokerExecutablePath ?? Environment.ProcessPath,
            MicroBrokerProtocol.PipeName,
            launchEnabled: true)
    {
    }

    internal MicroBrokerClient(
        string clientName,
        string? brokerExecutablePath,
        string pipeName,
        bool launchEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _clientName = clientName;
        _brokerExecutablePath = brokerExecutablePath;
        _pipeName = pipeName;
        _launchEnabled = launchEnabled;
    }

    public event EventHandler<SlotLightingSnapshot>?
        SlotLightingObserved;

    public MicroBrokerClientState State
    {
        get => (MicroBrokerClientState)Volatile.Read(ref _state);
        private set => Volatile.Write(ref _state, (int)value);
    }

    public BrokerDriverInfo Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_connectionSync)
        {
            if (_connected)
            {
                try
                {
                    var status = SendRequest(
                        MicroBrokerProtocol.Hello,
                        allowLaunch: false);
                    if (status.Succeeded && status.Driver is not null)
                    {
                        UpdateDriverState(status.Driver);
                        return status.Driver;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or
                        TimeoutException or
                        InvalidDataException)
                {
                }

                _connected = false;
            }

            var response = TryConnectExisting();
            if (response is null)
            {
                LaunchBroker();
                response = WaitForBroker();
            }

            if (
                response is null ||
                !response.Succeeded ||
                response.Driver is null)
            {
                Volatile.Write(
                    ref _retryAfter,
                    Environment.TickCount64 + ConnectFailureBackoffMs);
                State = MicroBrokerClientState.Unavailable;
                throw new InvalidOperationException(
                    response?.Error ??
                    "AgentController Micro Broker is unavailable.");
            }

            ObjectDisposedException.ThrowIf(_disposed, this);
            _connected = true;
            Volatile.Write(ref _retryAfter, 0);
            _eventCursor = 0;
            UpdateDriverState(response.Driver);
            StartPollLoop();
            return response.Driver;
        }
    }

    public void StartConnecting()
    {
        if (
            _disposed ||
            _connected ||
            Environment.TickCount64 < Volatile.Read(ref _retryAfter) ||
            Interlocked.CompareExchange(
                ref _backgroundConnectRunning,
                1,
                0) != 0)
        {
            return;
        }

        _backgroundConnectTask = Task.Run(() =>
        {
            try
            {
                _ = Connect();
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                    ObjectDisposedException)
            {
            }
            finally
            {
                Volatile.Write(ref _backgroundConnectRunning, 0);
            }
        });
    }

    public MicroSendResult Submit(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count is 0 or > MicroBrokerProtocol.MaximumBatchReports)
        {
            return MicroSendResult.NotSent(
                "Batch report count is outside the bounded broker schema.");
        }

        foreach (var report in reports)
        {
            MicroRpcCodec.ValidateWireReport(report);
        }

        if (!EnsureConnected())
        {
            return MicroSendResult.NotSent(
                "AgentController Micro Broker is unavailable.");
        }

        if (State != MicroBrokerClientState.Ready)
        {
            return MicroSendResult.NotSent(
                "Codex has not completed a Micro HID handshake; use the " +
                "non-Micro fallback until runtime capability is observed.");
        }

        try
        {
            var response = SendRequest(
                MicroBrokerProtocol.Submit,
                reports: reports.Select(item => item.ToArray()).ToArray());
            return ResolveSend(response, reports.Count);
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                InvalidDataException)
        {
            MarkDisconnected();
            return MicroSendResult.NotSent(exception.Message);
        }
    }

    public MicroSendResult TapKeyboard(
        BrokerKeyboardKey key,
        bool shift)
    {
        if (!EnsureConnected())
        {
            return MicroSendResult.NotSent(
                "AgentController Micro Broker is unavailable.");
        }

        if (State != MicroBrokerClientState.Ready)
        {
            return MicroSendResult.NotSent(
                "Codex has not completed a Micro HID handshake.");
        }

        try
        {
            var response = SendRequest(
                MicroBrokerProtocol.Keyboard,
                keyboardKey: key,
                shift: shift);
            return ResolveSend(response, requestedReports: 2);
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                InvalidDataException)
        {
            MarkDisconnected();
            return MicroSendResult.NotSent(exception.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _backgroundConnectTask?.Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
        }

        _backgroundConnectTask = null;
        var cancellation = _pollCancellation;
        _pollCancellation = null;
        cancellation?.Cancel();
        try
        {
            _pollTask?.Wait(TimeSpan.FromMilliseconds(1_000));
        }
        catch (AggregateException)
        {
        }

        _pollTask = null;
        if (_connected)
        {
            try
            {
                _ = SendRequest(
                    MicroBrokerProtocol.Disconnect,
                    allowLaunch: false);
            }
            catch
            {
            }
        }

        _connected = false;
        State = MicroBrokerClientState.Unavailable;
        cancellation?.Dispose();
    }

    private bool EnsureConnected()
    {
        if (_disposed)
        {
            return false;
        }

        if (_connected)
        {
            return true;
        }

        if (
            Volatile.Read(ref _backgroundConnectRunning) != 0 ||
            Environment.TickCount64 < Volatile.Read(ref _retryAfter))
        {
            return false;
        }

        try
        {
            _ = Connect();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private BrokerResponse? TryConnectExisting()
    {
        try
        {
            var response = SendRequest(
                MicroBrokerProtocol.Hello,
                allowLaunch: false);
            return response.Succeeded ? response : null;
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                OperationCanceledException)
        {
            return null;
        }
    }

    private BrokerResponse? WaitForBroker()
    {
        var deadline = Environment.TickCount64 + 2_000;
        do
        {
            Thread.Sleep(75);
            var response = TryConnectExisting();
            if (response is not null)
            {
                return response;
            }
        }
        while (Environment.TickCount64 < deadline);

        return null;
    }

    private void LaunchBroker()
    {
        if (
            !_launchEnabled ||
            string.IsNullOrWhiteSpace(_brokerExecutablePath))
        {
            return;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = _brokerExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            start.ArgumentList.Add("--micro-broker");
            Process.Start(start)?.Dispose();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception or
                System.Security.SecurityException)
        {
        }
    }

    private void StartPollLoop()
    {
        if (_pollTask is { IsCompleted: false })
        {
            return;
        }

        _pollCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _pollCancellation = cancellation;
        _pollTask = Task.Run(
            () => PollAsync(cancellation.Token),
            cancellation.Token);
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, cancellationToken)
                    .ConfigureAwait(false);
                if (!_connected)
                {
                    continue;
                }

                var response = SendRequest(
                    MicroBrokerProtocol.Poll,
                    eventCursor: Interlocked.Read(ref _eventCursor),
                    allowLaunch: false);
                if (!response.Succeeded)
                {
                    MarkDisconnected();
                    continue;
                }

                if (response.Driver is { } driver)
                {
                    UpdateDriverState(driver);
                }

                if (response.EventCursor <
                    Interlocked.Read(ref _eventCursor))
                {
                    Interlocked.Exchange(ref _eventCursor, 0);
                }

                foreach (var item in response.Events ?? [])
                {
                    if (item.SlotLighting is { } lighting)
                    {
                        PublishSlotLighting(lighting);
                    }
                }

                Interlocked.Exchange(
                    ref _eventCursor,
                    response.EventCursor);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (
                exception is IOException or
                    TimeoutException or
                    InvalidDataException)
            {
                MarkDisconnected();
            }
        }
    }

    private BrokerResponse SendRequest(
        string operation,
        byte[][]? reports = null,
        BrokerKeyboardKey? keyboardKey = null,
        bool shift = false,
        long eventCursor = 0,
        bool allowLaunch = true)
    {
        lock (_requestSync)
        {
            return SendRequestCore(
                operation,
                reports,
                keyboardKey,
                shift,
                eventCursor,
                allowLaunch);
        }
    }

    private BrokerResponse SendRequestCore(
        string operation,
        byte[][]? reports,
        BrokerKeyboardKey? keyboardKey,
        bool shift,
        long eventCursor,
        bool allowLaunch)
    {
        using var timeout = new CancellationTokenSource(RequestTimeout);
        using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            pipe.ConnectAsync(timeout.Token)
                .GetAwaiter()
                .GetResult();
            var requestId = Interlocked.Increment(ref _requestId);
            var request = new BrokerRequest(
                MicroBrokerProtocol.Version,
                operation,
                _clientId,
                requestId,
                _clientName,
                reports,
                keyboardKey,
                shift,
                eventCursor);
            BrokerWire.WriteAsync(pipe, request, timeout.Token)
                .GetAwaiter()
                .GetResult();
            var response = BrokerWire.ReadAsync<BrokerResponse>(
                    pipe,
                    timeout.Token)
                .GetAwaiter()
                .GetResult();
            if (
                response.Version != MicroBrokerProtocol.Version ||
                response.RequestId != requestId)
            {
                throw new InvalidDataException(
                    "Micro Broker response correlation failed.");
            }

            return response;
        }
        catch (OperationCanceledException exception)
        {
            if (allowLaunch)
            {
                MarkDisconnected();
            }

            throw new TimeoutException(
                "Micro Broker request timed out.",
                exception);
        }
        catch (IOException)
        {
            if (allowLaunch)
            {
                MarkDisconnected();
            }

            throw;
        }
    }

    private MicroSendResult ResolveSend(
        BrokerResponse response,
        int requestedReports)
    {
        if (!response.Succeeded || response.Send is null)
        {
            MarkDisconnected();
            return new(
                MicroSendDisposition.NotSent,
                0,
                requestedReports,
                0,
                response.Error ?? "Micro Broker did not accept the request.");
        }

        if (response.Driver is { } driver)
        {
            UpdateDriverState(driver);
        }
        return response.Send.Value;
    }

    private void UpdateDriverState(BrokerDriverInfo driver) =>
        State = driver.CodexLinkObserved
            ? MicroBrokerClientState.Ready
            : MicroBrokerClientState.Unavailable;

    private void MarkDisconnected()
    {
        _connected = false;
        Volatile.Write(
            ref _retryAfter,
            Environment.TickCount64 + ConnectFailureBackoffMs);
        State = MicroBrokerClientState.Faulted;
    }

    private void PublishSlotLighting(SlotLightingSnapshot snapshot)
    {
        var handlers = SlotLightingObserved;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<SlotLightingSnapshot> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch
            {
            }
        }
    }
}
