using System.Text;
using AgentController.MicroBroker;
using CodexMicro.Protocol;
using Xunit;

namespace AgentController.MicroBroker.Tests;

public sealed class MicroInputBatchTests
{
    [Fact]
    public void CompleteInputNotificationsAreParsedInWireOrder()
    {
        var reports = new List<byte[]>();
        reports.AddRange(MicroRpcCodec.EncodeHid("ACT10", 1));
        reports.AddRange(MicroRpcCodec.EncodeJoystick(0.75, 1));

        var messages = MicroInputBatch.Parse(reports);

        Assert.Collection(
            messages,
            message =>
            {
                Assert.Equal(MicroInputMessageKind.Hid, message.Kind);
                Assert.Equal("ACT10", message.Key);
                Assert.Equal(1, message.Action);
            },
            message =>
            {
                Assert.Equal(MicroInputMessageKind.Analog, message.Kind);
                Assert.Equal(0.75, message.Angle);
                Assert.Equal(1, message.Distance);
            });
    }

    [Fact]
    public void IncompleteOrNonInputPayloadIsRejectedBeforeDriverSubmission()
    {
        var incomplete = MicroRpcCodec.EncodeHid("ACT10", 1)
            .Select(report => report.ToArray())
            .ToArray();
        incomplete[^1][2]--;
        var nonInput = MicroRpcCodec.EncodeNotification(
            "v.oai.thstatus",
            new Dictionary<string, object?>());

        Assert.Throws<InvalidDataException>(
            () => MicroInputBatch.Parse(incomplete));
        Assert.Throws<InvalidDataException>(
            () => MicroInputBatch.Parse(nonInput));
    }
}
