using System.IO;
using System.Text;
using System.Text.Json;
using CodexController.Models;
using CodexController.Services;
using CodexController.Services.Micro;

namespace CodexController.Tests;

public sealed class MicroRpcCodecTests
{
    [Fact]
    public void EncodesFastCommandAsPrivateMicroHidNotification()
    {
        var reports = MicroRpcCodec.EncodeHid(
            "ACT06",
            action: 1);

        Assert.Single(reports);
        var report = reports[0];
        Assert.Equal(64, report.Length);
        Assert.Equal(0x06, report[0]);
        Assert.Equal(0x02, report[1]);
        Assert.All(
            report[(3 + report[2])..],
            value => Assert.Equal(0, value));

        using var json = JsonDocument.Parse(
            MicroRpcCodec.DecodePayload(
                reports.Select(report =>
                    (ReadOnlyMemory<byte>)report)));
        Assert.Equal(
            "v.oai.hid",
            json.RootElement.GetProperty("m").GetString());
        var parameters = json.RootElement.GetProperty("p");
        Assert.Equal("ACT06", parameters.GetProperty("k").GetString());
        Assert.Equal(1, parameters.GetProperty("act").GetInt32());
    }

    [Fact]
    public void AppendsRequiredNotificationNewline()
    {
        var reports = MicroRpcCodec.EncodeJoystick(0.75, 1);

        var payload = MicroRpcCodec.DecodePayload(
            reports.Select(report =>
                (ReadOnlyMemory<byte>)report));

        Assert.EndsWith("\n", Encoding.UTF8.GetString(payload));
    }

    [Fact]
    public void SplitsLongUtf8PayloadWithoutBreakingCodePoints()
    {
        var reports = MicroRpcCodec.EncodeNotification(
            "v.oai.test",
            new Dictionary<string, object?>
            {
                ["text"] = string.Concat(
                    Enumerable.Repeat("简易模式", 30)),
            });

        Assert.True(reports.Count > 1);
        foreach (var report in reports)
        {
            Assert.InRange(report[2], 1, 61);
            var chunk = report.AsSpan(3, report[2]);
            Assert.DoesNotContain(
                '\uFFFD',
                Encoding.UTF8.GetString(chunk));
        }

        var payload = MicroRpcCodec.DecodePayload(
            reports.Select(report =>
                (ReadOnlyMemory<byte>)report));
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(
            string.Concat(Enumerable.Repeat("简易模式", 30)),
            json.RootElement
                .GetProperty("p")
                .GetProperty("text")
                .GetString());
    }

    [Fact]
    public void RejectsMalformedReportLength()
    {
        Assert.Throws<InvalidDataException>(() =>
            MicroRpcCodec.DecodePayload(
                [new ReadOnlyMemory<byte>(new byte[63])]));
    }
}

public sealed class MicroInputServiceTests
{
    [Fact]
    public void MissingBrokerIsANormalNotSentResult()
    {
        using var transport = new NamedPipeMicroReportTransport(
            $"AgentController.Tests.Missing.{Guid.NewGuid():N}");

        var result = transport.Send(
            MicroRpcCodec.EncodeHid("ACT12", action: 1));

        Assert.Equal(MicroReportSendResult.NotSent, result);
        Assert.Equal(MicroTransportState.Unavailable, transport.State);
    }

    [Fact]
    public void FastUsesOfficialAct06PressAndRelease()
    {
        using var transport = new RecordingTransport();
        using var input = new MicroInputService(transport);

        Assert.True(input.TryToggleFast());

        Assert.Equal(
            [("ACT06", 1), ("ACT06", 0)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void ForkUsesOfficialAct09PressAndRelease()
    {
        using var transport = new RecordingTransport();
        using var input = new MicroInputService(transport);

        Assert.True(input.TryForkThread());

        Assert.Equal(
            [("ACT09", 1), ("ACT09", 0)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void PowerOpensReasoningThenSendsEncoderPulses()
    {
        using var transport = new RecordingTransport();
        using var input = new MicroInputService(transport);

        Assert.True(input.TryStepReasoning(2, openFirst: true));

        Assert.Equal(
            [("ENC", 1), ("ENC", 0), ("ENC_CW", 2), ("ENC_CW", 2)],
            DecodeHidEvents(transport.Reports));
    }

    private static IReadOnlyList<(string Key, int Action)> DecodeHidEvents(
        IReadOnlyList<byte[]> reports)
    {
        var events = new List<(string Key, int Action)>();
        foreach (var report in reports)
        {
            using var json = JsonDocument.Parse(
                MicroRpcCodec.DecodePayload(
                    [(ReadOnlyMemory<byte>)report]));
            var parameters = json.RootElement.GetProperty("p");
            events.Add((
                parameters.GetProperty("k").GetString()!,
                parameters.GetProperty("act").GetInt32()));
        }

        return events;
    }

    private sealed class RecordingTransport : IMicroReportTransport
    {
        public List<byte[]> Reports { get; } = [];

        public MicroReportSendResult Result { get; init; } =
            MicroReportSendResult.Accepted;

        public MicroTransportState State => MicroTransportState.Ready;

        public MicroReportSendResult Send(IReadOnlyList<byte[]> reports)
        {
            Reports.AddRange(reports);
            return Result;
        }

        public void Dispose()
        {
        }
    }
}
