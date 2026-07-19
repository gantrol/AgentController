using AgentController.MicroBroker;
using Xunit;

namespace AgentController.MicroBroker.Tests;

public sealed class BrokerWireTests
{
    [Fact]
    public async Task FrameRoundTripsCorrelationAndClientIdentity()
    {
        var request = new BrokerRequest(
            MicroBrokerProtocol.Version,
            MicroBrokerProtocol.Hello,
            Guid.NewGuid(),
            17,
            "controller");
        await using var stream = new MemoryStream();

        await BrokerWire.WriteAsync(
            stream,
            request,
            CancellationToken.None);
        stream.Position = 0;
        var decoded = await BrokerWire.ReadAsync<BrokerRequest>(
            stream,
            CancellationToken.None);

        Assert.Equal(request, decoded);
    }

    [Fact]
    public async Task OversizedFrameIsRejectedBeforeAllocation()
    {
        await using var stream = new MemoryStream();
        var header = BitConverter.GetBytes(
            MicroBrokerProtocol.MaximumFrameLength + 1);
        await stream.WriteAsync(header);
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            BrokerWire.ReadAsync<BrokerRequest>(
                stream,
                CancellationToken.None));
    }
}
