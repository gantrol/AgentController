using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO.Pipes;
using System.Text.Json;
using CodexMicro.Protocol;

namespace AgentController.MicroBroker;

public sealed class MicroBrokerHost : IDisposable
{
    private static readonly TimeSpan OutputPollInterval =
        TimeSpan.FromMilliseconds(12);
    private static readonly TimeSpan OutputFaultBackoff =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LeaseSweepInterval =
        TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultIdleExitDelay =
        TimeSpan.FromSeconds(15);
    private const int MaximumConcurrentConnections = 16;

    private readonly IMicroDriverEndpoint _driver;
    private readonly string _pipeName;
    private readonly string _instanceLeasePath;
    private readonly TimeSpan _idleExitDelay;
    private readonly DeviceRpcHandler _rpc = new();
    private readonly ConcurrentDictionary<Guid, ClientLease> _clients = new();
    private readonly object _inputSync = new();
    private readonly object _eventSync = new();
    private readonly Queue<BrokerEvent> _events = new();
    private BrokerDriverInfo? _driverInfo;
    private long _eventSequence;
    private long _lastActivity = Environment.TickCount64;
    private bool _disposed;

    public MicroBrokerHost()
        : this(
            new VhfDriverEndpoint(),
            MicroBrokerProtocol.PipeName,
            DefaultInstanceLeasePath(),
            DefaultIdleExitDelay)
    {
    }

    internal MicroBrokerHost(
        IMicroDriverEndpoint driver,
        string pipeName,
        string instanceLeasePath,
        TimeSpan idleExitDelay)
    {
        _driver = driver ?? throw new ArgumentNullException(nameof(driver));
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceLeasePath);
        if (idleExitDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleExitDelay));
        }

        _pipeName = pipeName;
        _instanceLeasePath = instanceLeasePath;
        _idleExitDelay = idleExitDelay;
        _rpc.SlotLightingObserved += (_, snapshot) =>
            PublishEvent("slot-lighting", snapshot);
    }

    public static bool IsBrokerArgument(IEnumerable<string> args) =>
        args.Contains(
            "--micro-broker",
            StringComparer.OrdinalIgnoreCase);

    public static int RunFromCommandLine()
    {
        using var host = new MicroBrokerHost();
        return host.RunAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var instanceLease = TryAcquireInstanceLease(
            _instanceLeasePath);
        if (instanceLease is null)
        {
            return 0;
        }

        try
        {
            _driverInfo = _driver.Connect();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                InvalidDataException or
                Win32Exception or
                UnauthorizedAccessException)
        {
            return 2;
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        var output = PumpOutputAsync(lifetime.Token);
        var leases = SweepLeasesAsync(lifetime);
        var handlers = new List<Task>();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                handlers.RemoveAll(task => task.IsCompleted);
                if (handlers.Count >= MaximumConcurrentConnections)
                {
                    await Task.WhenAny(handlers).ConfigureAwait(false);
                    continue;
                }

                var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    MaximumConcurrentConnections,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await server.WaitForConnectionAsync(lifetime.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    server.Dispose();
                    throw;
                }

                handlers.Add(Task.Run(
                    () => HandleConnectionAsync(server, lifetime.Token),
                    CancellationToken.None));
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lifetime.Cancel();
            await IgnoreCancellation(output).ConfigureAwait(false);
            await IgnoreCancellation(leases).ConfigureAwait(false);
            await Task.WhenAll(handlers.Select(IgnoreCancellation))
                .ConfigureAwait(false);
            NeutralizeAllClients();
        }

        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NeutralizeAllClients();
        _driver.Dispose();
    }

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await using (pipe.ConfigureAwait(false))
        {
            BrokerRequest? request = null;
            try
            {
                request = await BrokerWire.ReadAsync<BrokerRequest>(
                        pipe,
                        cancellationToken)
                    .ConfigureAwait(false);
                var response = HandleRequest(request);
                await BrokerWire.WriteAsync(
                        pipe,
                        response,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                    EndOfStreamException or
                    IOException or
                    JsonException or
                    ArgumentException)
            {
                if (pipe.IsConnected)
                {
                    var failure = new BrokerResponse(
                        MicroBrokerProtocol.Version,
                        request?.RequestId ?? 0,
                        false,
                        Error: exception.Message);
                    try
                    {
                        await BrokerWire.WriteAsync(
                                pipe,
                                failure,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private BrokerResponse HandleRequest(BrokerRequest request)
    {
        if (
            request.Version != MicroBrokerProtocol.Version ||
            request.ClientId == Guid.Empty ||
            request.RequestId <= 0 ||
            string.IsNullOrWhiteSpace(request.ClientName))
        {
            return Failure(request, "Broker request header is invalid.");
        }

        Volatile.Write(ref _lastActivity, Environment.TickCount64);
        var client = _clients.GetOrAdd(
            request.ClientId,
            id => new ClientLease(id, request.ClientName));
        client.Touch(request.ClientName);

        return request.Operation switch
        {
            MicroBrokerProtocol.Hello => Success(request, client),
            MicroBrokerProtocol.Submit => Submit(request, client),
            MicroBrokerProtocol.Keyboard => Keyboard(request, client),
            MicroBrokerProtocol.Poll => Poll(request, client),
            MicroBrokerProtocol.Disconnect => Disconnect(request, client),
            _ => Failure(request, "Broker operation is not supported."),
        };
    }

    private BrokerResponse Submit(
        BrokerRequest request,
        ClientLease client)
    {
        if (request.Reports is not { Length: > 0 } reports ||
            reports.Length > MicroBrokerProtocol.MaximumBatchReports)
        {
            return Failure(request, "Broker report batch is invalid.");
        }

        MicroSendResult result;
        lock (_inputSync)
        {
            try
            {
                result = _driver.Submit(reports);
                if (result.WasPossiblySent)
                {
                    client.Observe(reports);
                }
            }
            catch (Exception exception) when (
                exception is InvalidDataException or
                    InvalidOperationException or
                    ArgumentException)
            {
                return Failure(request, exception.Message);
            }
        }

        return Success(request, client) with { Send = result };
    }

    private BrokerResponse Keyboard(
        BrokerRequest request,
        ClientLease client)
    {
        if (request.KeyboardKey is not { } key)
        {
            return Failure(request, "Broker keyboard request is invalid.");
        }

        var result = _driver.TapKeyboard(key, request.Shift);
        return Success(request, client) with { Send = result };
    }

    private BrokerResponse Poll(
        BrokerRequest request,
        ClientLease client)
    {
        BrokerEvent[] events;
        long cursor;
        lock (_eventSync)
        {
            events = _events
                .Where(item => item.Sequence > request.EventCursor)
                .ToArray();
            cursor = _eventSequence;
        }

        return Success(request, client) with
        {
            Events = events,
            EventCursor = cursor,
        };
    }

    private BrokerResponse Disconnect(
        BrokerRequest request,
        ClientLease client)
    {
        _clients.TryRemove(client.ClientId, out _);
        Neutralize(client);
        return Success(request, client);
    }

    private BrokerResponse Success(
        BrokerRequest request,
        ClientLease client) =>
        new(
            MicroBrokerProtocol.Version,
            request.RequestId,
            true,
            Driver: _driverInfo,
            EventCursor: Volatile.Read(ref _eventSequence),
            Role: BrokerConnectionRole.Client);

    private static BrokerResponse Failure(
        BrokerRequest request,
        string error) =>
        new(
            MicroBrokerProtocol.Version,
            request.RequestId,
            false,
            Error: error);

    private async Task PumpOutputAsync(CancellationToken cancellationToken)
    {
        var assembler = new HostRpcAssembler();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var output = _driver.TryReadOutput();
                if (output is null)
                {
                    await Task.Delay(OutputPollInterval, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (output.WireReport[1] == MicroProtocol.DebugChannel)
                {
                    continue;
                }

                var json = assembler.Append(
                    output.WireReport,
                    DateTimeOffset.UtcNow);
                if (json is null)
                {
                    continue;
                }

                var response = _rpc.Handle(json);
                var result = _driver.Submit(response);
                if (result.Disposition != MicroSendDisposition.Accepted)
                {
                    assembler.Reset();
                    PublishEvent(
                        "rpc-fault",
                        detail: result.Detail);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (
                exception is IOException or
                    InvalidDataException or
                    InvalidOperationException or
                    Win32Exception or
                    JsonException)
            {
                assembler.Reset();
                PublishEvent("rpc-fault", detail: exception.Message);
                await Task.Delay(OutputFaultBackoff, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task SweepLeasesAsync(CancellationTokenSource lifetime)
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LeaseSweepInterval, lifetime.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var now = Environment.TickCount64;
            foreach (var pair in _clients)
            {
                if (
                    now - pair.Value.LastSeen <=
                    MicroBrokerProtocol.ClientLeaseTimeoutMs ||
                    !_clients.TryRemove(pair.Key, out var expired))
                {
                    continue;
                }

                Neutralize(expired);
                PublishEvent(
                    "client-expired",
                    detail: expired.ClientName);
            }

            if (
                _clients.IsEmpty &&
                now - Volatile.Read(ref _lastActivity) >=
                    _idleExitDelay.TotalMilliseconds)
            {
                lifetime.Cancel();
                return;
            }
        }
    }

    private void NeutralizeAllClients()
    {
        foreach (var pair in _clients.ToArray())
        {
            if (_clients.TryRemove(pair.Key, out var client))
            {
                Neutralize(client);
            }
        }
    }

    private void Neutralize(ClientLease client)
    {
        lock (_inputSync)
        {
            var releaseAnalog = !_clients.Values.Any(
                other => other.ClientId != client.ClientId &&
                    other.HasAnalog);
            var reports = client.TakeNeutralReports(
                key => !_clients.Values.Any(
                    other => other.ClientId != client.ClientId &&
                        other.HoldsKey(key)),
                releaseAnalog);
            if (reports.Count == 0)
            {
                return;
            }

            try
            {
                _ = _driver.Submit(reports);
            }
            catch
            {
                // The lease is gone even if best-effort device neutralization
                // cannot complete. Never release another client's state.
            }
        }
    }

    private void PublishEvent(
        string kind,
        SlotLightingSnapshot? lighting = null,
        string? detail = null)
    {
        lock (_eventSync)
        {
            var item = new BrokerEvent(
                ++_eventSequence,
                kind,
                lighting,
                detail);
            _events.Enqueue(item);
            while (_events.Count > 64)
            {
                _events.Dequeue();
            }
        }
    }

    private static FileStream? TryAcquireInstanceLease(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            return new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string DefaultInstanceLeasePath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "OpenAI",
            "CodexMicro",
            "broker-v1.lock");

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed class ClientLease
    {
        private readonly object _sync = new();
        private readonly ClientInputState _input = new();
        private long _lastSeen;

        internal ClientLease(Guid clientId, string clientName)
        {
            ClientId = clientId;
            ClientName = clientName;
            _lastSeen = Environment.TickCount64;
        }

        internal Guid ClientId { get; }
        internal string ClientName { get; private set; }
        internal long LastSeen => Volatile.Read(ref _lastSeen);

        internal void Touch(string clientName)
        {
            lock (_sync)
            {
                ClientName = clientName;
                Volatile.Write(ref _lastSeen, Environment.TickCount64);
            }
        }

        internal void Observe(IReadOnlyList<byte[]> reports)
        {
            lock (_sync)
            {
                _input.Observe(reports);
            }
        }

        internal bool HoldsKey(string key)
        {
            lock (_sync)
            {
                return _input.HoldsKey(key);
            }
        }

        internal bool HasAnalog
        {
            get
            {
                lock (_sync)
                {
                    return _input.HasAnalog;
                }
            }
        }

        internal IReadOnlyList<byte[]> TakeNeutralReports(
            Func<string, bool> shouldReleaseKey,
            bool releaseAnalog)
        {
            lock (_sync)
            {
                var reports = _input.BuildNeutralReports(
                    shouldReleaseKey,
                    releaseAnalog);
                _input.Clear();
                return reports;
            }
        }
    }
}
