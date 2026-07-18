using System.Text;
using System.Text.Json;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Protocol.Tests;

public sealed class MicroProtocolTests
{
    [Fact]
    public void EncodesHidPressWithFrozenWireHeader()
    {
        var reports = MicroRpcCodec.EncodeHid("ACT12", 1);

        var report = Assert.Single(reports);
        Assert.Equal(64, report.Length);
        Assert.Equal(0x06, report[0]);
        Assert.Equal(0x02, report[1]);
        Assert.All(
            report[(3 + report[2])..],
            value => Assert.Equal(0, value));

        using var json = JsonDocument.Parse(
            MicroRpcCodec.DecodePayload(
                reports.Select(value => (ReadOnlyMemory<byte>)value)));
        Assert.Equal(
            "v.oai.hid",
            json.RootElement.GetProperty("m").GetString());
        Assert.Equal(
            "ACT12",
            json.RootElement.GetProperty("p").GetProperty("k").GetString());
    }

    [Fact]
    public void DeviceMessagesEndWithNewlineAndDoNotSplitUtf8Scalars()
    {
        var expected = string.Concat(Enumerable.Repeat("虚拟小键盘", 40));
        var reports = MicroRpcCodec.EncodeNotification(
            "v.oai.test",
            new Dictionary<string, object?> { ["text"] = expected });

        foreach (var report in reports)
        {
            var chunk = report.AsSpan(3, report[2]);
            Assert.DoesNotContain('\uFFFD', Encoding.UTF8.GetString(chunk));
        }

        var payload = Encoding.UTF8.GetString(
            MicroRpcCodec.DecodePayload(
                reports.Select(value => (ReadOnlyMemory<byte>)value)));
        Assert.EndsWith("\n", payload);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(
            expected,
            json.RootElement.GetProperty("p").GetProperty("text").GetString());
    }

    [Fact]
    public void ReassemblesHostJsonWithoutNewline()
    {
        var request =
            "{\"method\":\"device.status\",\"params\":null,\"id\":42}";
        var bytes = Encoding.UTF8.GetBytes(request);
        var reports = CreateHostReports(bytes, 17);
        var assembler = new HostRpcAssembler();
        string? completed = null;

        foreach (var report in reports)
        {
            completed = assembler.Append(report, DateTimeOffset.UtcNow);
        }

        Assert.Equal(request, completed);
        Assert.Equal(0, assembler.BufferedLength);
    }

    [Fact]
    public void RespondsToThreadLightingAndPublishesSlotOnlyObservation()
    {
        var handler = new DeviceRpcHandler();
        SlotLightingSnapshot? snapshot = null;
        handler.SlotLightingObserved += (_, value) => snapshot = value;

        var reports = handler.Handle(
            "{\"method\":\"v.oai.thstatus\",\"params\":[" +
            "{\"id\":0,\"c\":3166206,\"b\":1,\"e\":4,\"s\":0.4," +
            "\"sk\":0,\"sa\":0}],\"id\":7}");

        Assert.NotNull(snapshot);
        Assert.Equal("SlotOnly", snapshot.MappingKind);
        Assert.Equal(0x304FFE, Assert.Single(snapshot.Slots).Color);
        using var response = JsonDocument.Parse(
            MicroRpcCodec.DecodePayload(
                reports.Select(value => (ReadOnlyMemory<byte>)value)));
        Assert.Equal(7, response.RootElement.GetProperty("id").GetInt32());
        Assert.True(response.RootElement.GetProperty("result").GetBoolean());
    }

    [Fact]
    public void RejectsDuplicateOrOutOfRangeThreadSlots()
    {
        var handler = new DeviceRpcHandler();

        var reports = handler.Handle(
            "{\"method\":\"v.oai.thstatus\",\"params\":[" +
            "{\"id\":6,\"c\":0,\"e\":0}],\"id\":9}");

        using var response = JsonDocument.Parse(
            MicroRpcCodec.DecodePayload(
                reports.Select(value => (ReadOnlyMemory<byte>)value)));
        Assert.Equal(
            -32602,
            response.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    private static IReadOnlyList<byte[]> CreateHostReports(
        byte[] payload,
        int chunkSize)
    {
        var reports = new List<byte[]>();
        for (var offset = 0; offset < payload.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, payload.Length - offset);
            var report = new byte[MicroProtocol.ReportLength];
            report[0] = MicroProtocol.ReportId;
            report[1] = MicroProtocol.RpcChannel;
            report[2] = checked((byte)length);
            Buffer.BlockCopy(payload, offset, report, 3, length);
            reports.Add(report);
        }

        return reports;
    }
}
