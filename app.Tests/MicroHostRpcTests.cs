using System.Text;
using System.Text.Json;
using CodexController.Services.Micro;

namespace CodexController.Tests;

public sealed class MicroHostRpcAssemblerTests
{
    [Fact]
    public void ReassemblesFragmentedHostRequest()
    {
        const string request =
            "{\"id\":17,\"method\":\"sys.version\"}";
        var reports = EncodeHostReports(request, payloadLength: 9);
        var assembler = new MicroHostRpcAssembler();
        var observedAt = DateTimeOffset.UtcNow;

        for (var index = 0; index < reports.Count - 1; index++)
        {
            Assert.Null(assembler.Append(reports[index], observedAt));
        }

        Assert.Equal(
            request,
            assembler.Append(reports[^1], observedAt));
        Assert.Equal(0, assembler.BufferedLength);
    }

    [Fact]
    public void TimedOutFragmentCannotCorruptNextRequest()
    {
        var assembler = new MicroHostRpcAssembler(
            TimeSpan.FromMilliseconds(100));
        var observedAt = DateTimeOffset.UtcNow;

        Assert.Null(assembler.Append(
            EncodeHostReports("{\"id\":1,", payloadLength: 61)[0],
            observedAt));

        const string nextRequest =
            "{\"id\":2,\"method\":\"device.status\"}";
        var completed = assembler.Append(
            EncodeHostReports(nextRequest, payloadLength: 61)[0],
            observedAt.AddMilliseconds(101));

        Assert.Equal(nextRequest, completed);
        Assert.Equal(0, assembler.BufferedLength);
    }

    internal static IReadOnlyList<byte[]> EncodeHostReports(
        string json,
        int payloadLength)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var reports = new List<byte[]>();
        for (var offset = 0; offset < payload.Length; offset += payloadLength)
        {
            var length = Math.Min(payloadLength, payload.Length - offset);
            var report = new byte[MicroRpcCodec.ReportLength];
            report[0] = MicroRpcCodec.ReportId;
            report[1] = MicroRpcCodec.RpcChannel;
            report[2] = checked((byte)length);
            Buffer.BlockCopy(payload, offset, report, 3, length);
            reports.Add(report);
        }

        return reports;
    }
}

public sealed class MicroDeviceRpcHandlerTests
{
    [Fact]
    public void VersionRequestPreservesIdAndReturnsFirmwareVersion()
    {
        var handler = new MicroDeviceRpcHandler();

        using var response = DecodeResponse(handler.Handle(
            "{\"id\":42,\"method\":\"sys.version\"}"));

        Assert.Equal(42, response.RootElement.GetProperty("id").GetInt32());
        Assert.Equal(
            MicroRpcCodec.FirmwareVersion,
            response.RootElement
                .GetProperty("result")
                .GetProperty("version")
                .GetString());
    }

    [Fact]
    public void ThreadStatusPublishesSortedSlotOnlyLighting()
    {
        var handler = new MicroDeviceRpcHandler();
        MicroSlotLightingSnapshot? observed = null;
        handler.SlotLightingObserved += (_, snapshot) => observed = snapshot;

        using var response = DecodeResponse(handler.Handle(
            """
            {"id":"lighting-1","method":"v.oai.thstatus","params":[
              {"id":4,"c":16711935,"b":0.6,"e":2,"s":1.5,"sk":1,"sa":0},
              {"id":1,"c":0,"e":0}
            ]}
            """));

        Assert.True(response.RootElement.GetProperty("result").GetBoolean());
        Assert.NotNull(observed);
        Assert.Equal(1, observed.Sequence);
        Assert.Equal([1, 4], observed.Slots.Select(slot => slot.SlotId));

        var unlit = observed.Slots[0];
        Assert.True(unlit.LightingAmbiguous);
        Assert.Equal(0, unlit.Brightness);

        var lit = observed.Slots[1];
        Assert.Equal(0xFF00FF, lit.Color);
        Assert.Equal(0.6, lit.Brightness);
        Assert.Equal(2, lit.Effect);
        Assert.Equal(1.5, lit.Speed);
        Assert.True(lit.SyncKeysLighting);
        Assert.False(lit.SyncAmbientLighting);
        Assert.False(lit.LightingAmbiguous);
    }

    [Fact]
    public void InvalidThreadStatusReturnsInvalidParamsWithoutPublishing()
    {
        var handler = new MicroDeviceRpcHandler();
        var observations = 0;
        handler.SlotLightingObserved += (_, _) => observations++;

        using var response = DecodeResponse(handler.Handle(
            """
            {"id":9,"method":"v.oai.thstatus","params":[
              {"id":6,"c":1,"e":0}
            ]}
            """));

        Assert.Equal(0, observations);
        Assert.Equal(
            -32602,
            response.RootElement
                .GetProperty("error")
                .GetProperty("code")
                .GetInt32());
    }

    private static JsonDocument DecodeResponse(
        IReadOnlyList<byte[]> reports) =>
        JsonDocument.Parse(MicroRpcCodec.DecodePayload(
            reports.Select(report => (ReadOnlyMemory<byte>)report)));
}
