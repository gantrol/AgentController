using System.Text;
using AgentController.MicroBroker;
using CodexMicro.Protocol;
using Xunit;

namespace AgentController.MicroBroker.Tests;

public sealed class ClientInputStateTests
{
    [Fact]
    public void TapDoesNotLeaveAClientHeld()
    {
        var state = new ClientInputState();
        var reports = new List<byte[]>();
        reports.AddRange(MicroRpcCodec.EncodeHid("ENC", 1));
        reports.AddRange(MicroRpcCodec.EncodeHid("ENC", 0));

        state.Observe(reports);

        Assert.Empty(state.HeldKeys);
        Assert.Empty(state.BuildNeutralReports());
    }

    [Fact]
    public void DisconnectNeutralizesOnlyThisClientsHeldState()
    {
        var controller = new ClientInputState();
        var simulator = new ClientInputState();
        controller.Observe(MicroRpcCodec.EncodeHid("ACT10", 1));
        simulator.Observe(MicroRpcCodec.EncodeHid("ENC", 1));

        var controllerNeutral = controller.BuildNeutralReports();

        var json = Decode(controllerNeutral);
        Assert.Contains("\"k\":\"ACT10\"", json);
        Assert.Contains("\"act\":0", json);
        Assert.DoesNotContain("\"k\":\"ENC\"", json);
        Assert.Contains("ENC", simulator.HeldKeys);
    }

    [Fact]
    public void DisconnectAddsAnalogNeutralForOwningClient()
    {
        var state = new ClientInputState();
        state.Observe(MicroRpcCodec.EncodeJoystick(0.75, 1));

        var neutral = Decode(state.BuildNeutralReports());

        Assert.Contains("\"m\":\"v.oai.rad\"", neutral);
        Assert.Contains("\"d\":0", neutral);
    }

    [Fact]
    public void FragmentedHeldMessageIsTrackedAfterTerminatorArrives()
    {
        var key = new string('K', 80);
        var reports = MicroRpcCodec.EncodeHid(key, 1);
        Assert.True(reports.Count > 1);
        var state = new ClientInputState();

        state.Observe([reports[0]]);
        Assert.Empty(state.HeldKeys);

        state.Observe(reports.Skip(1).ToArray());
        Assert.Contains(key, state.HeldKeys);
    }

    private static string Decode(IReadOnlyList<byte[]> reports) =>
        Encoding.UTF8.GetString(
            MicroRpcCodec.DecodePayload(
                reports.Select(item => (ReadOnlyMemory<byte>)item)));
}
