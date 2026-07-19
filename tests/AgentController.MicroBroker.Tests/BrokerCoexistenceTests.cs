using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using AgentController.MicroBroker;
using CodexMicro.Protocol;
using Xunit;

namespace AgentController.MicroBroker.Tests;

public sealed class BrokerCoexistenceTests
{
    [Fact]
    public async Task TwoClientsShareOneDriverAndNeutralizeIndependently()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"AgentController.MicroBroker.Tests.{suffix}";
        var leasePath = Path.Combine(
            Path.GetTempPath(),
            "agent-controller-micro-broker-tests",
            $"{suffix}.lock");
        var driver = new FakeDriverEndpoint();
        using var host = new MicroBrokerHost(
            driver,
            pipeName,
            leasePath,
            TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var hostTask = host.RunAsync(cancellation.Token);
        var controller = new MicroBrokerClient(
            "controller",
            brokerExecutablePath: null,
            pipeName,
            launchEnabled: false);
        var simulator = new MicroBrokerClient(
            "simulator",
            brokerExecutablePath: null,
            pipeName,
            launchEnabled: false);

        var controllerInfo = controller.Connect();
        var simulatorInfo = simulator.Connect();
        Assert.Equal(
            controllerInfo.ConnectionEpoch,
            simulatorInfo.ConnectionEpoch);

        Assert.Equal(
            MicroSendDisposition.Accepted,
            controller.Submit(
                MicroRpcCodec.EncodeHid("ACT10", 1)).Disposition);
        Assert.Equal(
            MicroSendDisposition.Accepted,
            simulator.Submit(
                MicroRpcCodec.EncodeHid("ENC", 1)).Disposition);

        controller.Dispose();

        var afterControllerExit = driver.Messages;
        Assert.Contains(
            afterControllerExit,
            message =>
                message.Contains("\"k\":\"ACT10\"") &&
                message.Contains("\"act\":0"));
        Assert.DoesNotContain(
            afterControllerExit,
            message =>
                message.Contains("\"k\":\"ENC\"") &&
                message.Contains("\"act\":0"));

        simulator.Dispose();

        Assert.Contains(
            driver.Messages,
            message =>
                message.Contains("\"k\":\"ENC\"") &&
                message.Contains("\"act\":0"));
        Assert.Equal(1, driver.ConnectCount);

        cancellation.Cancel();
        Assert.Equal(0, await hostTask);
    }

    [Fact]
    public async Task DriverOutputHasOneReaderAndBroadcastsLightingToAllClients()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"AgentController.MicroBroker.Tests.{suffix}";
        var leasePath = Path.Combine(
            Path.GetTempPath(),
            "agent-controller-micro-broker-tests",
            $"{suffix}.lock");
        var driver = new FakeDriverEndpoint();
        using var host = new MicroBrokerHost(
            driver,
            pipeName,
            leasePath,
            TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var hostTask = host.RunAsync(cancellation.Token);
        using var controller = new MicroBrokerClient(
            "controller",
            brokerExecutablePath: null,
            pipeName,
            launchEnabled: false);
        using var simulator = new MicroBrokerClient(
            "simulator",
            brokerExecutablePath: null,
            pipeName,
            launchEnabled: false);
        var controllerLighting =
            new TaskCompletionSource<SlotLightingSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        var simulatorLighting =
            new TaskCompletionSource<SlotLightingSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        controller.SlotLightingObserved += (_, snapshot) =>
            controllerLighting.TrySetResult(snapshot);
        simulator.SlotLightingObserved += (_, snapshot) =>
            simulatorLighting.TrySetResult(snapshot);
        _ = controller.Connect();
        _ = simulator.Connect();

        driver.EnqueueHostRpc(
            "{\"id\":1,\"method\":\"v.oai.thstatus\",\"params\":[" +
            "{\"id\":0,\"c\":16711680,\"e\":1,\"b\":1," +
            "\"s\":0,\"sk\":1,\"sa\":0}]}");

        var controllerSnapshot = await controllerLighting.Task
            .WaitAsync(TimeSpan.FromSeconds(3));
        var simulatorSnapshot = await simulatorLighting.Task
            .WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, Assert.Single(controllerSnapshot.Slots).SlotId);
        Assert.Equal(0, Assert.Single(simulatorSnapshot.Slots).SlotId);
        Assert.Contains(
            driver.Messages,
            message => message.Contains("\"result\":true"));

        cancellation.Cancel();
        Assert.Equal(0, await hostTask);
    }

    [Fact]
    public async Task DisconnectDoesNotReleaseStateHeldByAnotherClient()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"AgentController.MicroBroker.Tests.{suffix}";
        var leasePath = Path.Combine(
            Path.GetTempPath(),
            "agent-controller-micro-broker-tests",
            $"{suffix}.lock");
        var driver = new FakeDriverEndpoint();
        using var host = new MicroBrokerHost(
            driver,
            pipeName,
            leasePath,
            TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var hostTask = host.RunAsync(cancellation.Token);
        var controller = new MicroBrokerClient(
            "controller",
            brokerExecutablePath: null,
            pipeName,
            launchEnabled: false);
        var simulator = new MicroBrokerClient(
            "simulator",
            brokerExecutablePath: null,
            pipeName,
            launchEnabled: false);
        _ = controller.Connect();
        _ = simulator.Connect();

        _ = controller.Submit(MicroRpcCodec.EncodeHid("ACT10", 1));
        _ = simulator.Submit(MicroRpcCodec.EncodeHid("ACT10", 1));
        _ = controller.Submit(MicroRpcCodec.EncodeJoystick(0.25, 1));
        _ = simulator.Submit(MicroRpcCodec.EncodeJoystick(0.75, 1));

        var beforeDisconnect = driver.Messages.Count;
        controller.Dispose();

        Assert.Equal(beforeDisconnect, driver.Messages.Count);

        simulator.Dispose();
        var finalMessages = driver.Messages.Skip(beforeDisconnect).ToArray();
        Assert.Contains(
            finalMessages,
            message =>
                message.Contains("\"k\":\"ACT10\"") &&
                message.Contains("\"act\":0"));
        Assert.Contains(
            finalMessages,
            message =>
                message.Contains("\"m\":\"v.oai.rad\"") &&
                message.Contains("\"d\":0"));

        cancellation.Cancel();
        Assert.Equal(0, await hostTask);
    }

    [Fact]
    public async Task DuplicateRequestIdReturnsCachedResponseWithoutResending()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"AgentController.MicroBroker.Tests.{suffix}";
        var leasePath = Path.Combine(
            Path.GetTempPath(),
            "agent-controller-micro-broker-tests",
            $"{suffix}.lock");
        var driver = new FakeDriverEndpoint();
        using var host = new MicroBrokerHost(
            driver,
            pipeName,
            leasePath,
            TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var hostTask = host.RunAsync(cancellation.Token);
        var clientId = Guid.NewGuid();
        _ = await SendRequestAsync(
            pipeName,
            new(
                MicroBrokerProtocol.Version,
                MicroBrokerProtocol.Hello,
                clientId,
                1,
                "raw-test-client"));
        var submit = new BrokerRequest(
            MicroBrokerProtocol.Version,
            MicroBrokerProtocol.Submit,
            clientId,
            2,
            "raw-test-client",
            Reports: MicroRpcCodec.EncodeHid("ENC_CW", 2)
                .Select(report => report.ToArray())
                .ToArray());

        var first = await SendRequestAsync(pipeName, submit);
        var duplicate = await SendRequestAsync(pipeName, submit);

        Assert.True(first.Succeeded);
        Assert.Equal(first, duplicate);
        Assert.Single(driver.Messages);

        var mismatched = await SendRequestAsync(
            pipeName,
            submit with
            {
                Reports = MicroRpcCodec.EncodeHid("ENC_CC", 2)
                    .Select(report => report.ToArray())
                    .ToArray(),
            });
        Assert.False(mismatched.Succeeded);
        Assert.Contains("different payload", mismatched.Error);
        Assert.Single(driver.Messages);

        _ = await SendRequestAsync(
            pipeName,
            new(
                MicroBrokerProtocol.Version,
                MicroBrokerProtocol.Disconnect,
                clientId,
                3,
                "raw-test-client"));
        cancellation.Cancel();
        Assert.Equal(0, await hostTask);
    }

    [Fact]
    public async Task BackgroundWarmupConnectsBeforeTheFirstInput()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var pipeName = $"AgentController.MicroBroker.Tests.{suffix}";
        var leasePath = Path.Combine(
            Path.GetTempPath(),
            "agent-controller-micro-broker-tests",
            $"{suffix}.lock");
        var driver = new FakeDriverEndpoint();
        using var host = new MicroBrokerHost(
            driver,
            pipeName,
            leasePath,
            TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        var hostTask = host.RunAsync(cancellation.Token);
        using var client = new MicroBrokerClient(
            "warmup-test-client",
            brokerExecutablePath: null,
            pipeName,
            launchEnabled: false);

        client.StartConnecting();
        var deadline = Environment.TickCount64 + 3_000;
        while (
            client.State != MicroBrokerClientState.Ready &&
            Environment.TickCount64 < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Equal(MicroBrokerClientState.Ready, client.State);
        Assert.Equal(
            MicroSendDisposition.Accepted,
            client.Submit(
                MicroRpcCodec.EncodeHid("ENC_CW", 2)).Disposition);
        Assert.Equal(1, driver.ConnectCount);

        client.Dispose();
        cancellation.Cancel();
        Assert.Equal(0, await hostTask);
    }

    private static async Task<BrokerResponse> SendRequestAsync(
        string pipeName,
        BrokerRequest request)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(3));
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await BrokerWire.WriteAsync(pipe, request, timeout.Token);
        return await BrokerWire.ReadAsync<BrokerResponse>(
            pipe,
            timeout.Token);
    }

    private sealed class FakeDriverEndpoint : IMicroDriverEndpoint
    {
        private readonly ConcurrentQueue<string> _messages = new();
        private readonly ConcurrentQueue<DriverOutputReport> _output = new();
        private long _outputSequence;

        public bool IsConnected { get; private set; }
        public int ConnectCount { get; private set; }
        public IReadOnlyList<string> Messages => _messages.ToArray();

        public BrokerDriverInfo Connect()
        {
            IsConnected = true;
            ConnectCount++;
            return new(
                0x1234,
                0,
                0,
                0,
                3,
                "fake-broker-driver");
        }

        public MicroSendResult Submit(IReadOnlyList<byte[]> reports)
        {
            var payload = MicroRpcCodec.DecodePayload(
                reports.Select(item => (ReadOnlyMemory<byte>)item));
            _messages.Enqueue(Encoding.UTF8.GetString(payload));
            return new(
                MicroSendDisposition.Accepted,
                reports.Count,
                reports.Count,
                0,
                "accepted");
        }

        public MicroSendResult TapKeyboard(
            BrokerKeyboardKey key,
            bool shift) =>
            new(
                MicroSendDisposition.Accepted,
                2,
                2,
                0,
                "accepted");

        public DriverOutputReport? TryReadOutput() =>
            _output.TryDequeue(out var report) ? report : null;

        public void EnqueueHostRpc(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            for (var offset = 0; offset < bytes.Length; offset += 61)
            {
                var count = Math.Min(61, bytes.Length - offset);
                var wire = new byte[MicroProtocol.ReportLength];
                wire[0] = MicroProtocol.ReportId;
                wire[1] = MicroProtocol.RpcChannel;
                wire[2] = checked((byte)count);
                Buffer.BlockCopy(bytes, offset, wire, 3, count);
                _output.Enqueue(new(
                    checked((ulong)Interlocked.Increment(
                        ref _outputSequence)),
                    0,
                    MicroProtocol.ReportLength,
                    1,
                    wire));
            }
        }

        public void Dispose()
        {
            IsConnected = false;
        }
    }
}
