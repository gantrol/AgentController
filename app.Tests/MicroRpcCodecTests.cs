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

    [Fact]
    public void RejectsUnsupportedHidAction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MicroRpcCodec.EncodeHid("ENC", action: 3));
    }

    [Fact]
    public void RejectsNonZeroReportPadding()
    {
        var report = MicroRpcCodec.EncodeHid("ENC", action: 1)[0];
        report[^1] = 1;

        Assert.Throws<InvalidDataException>(() =>
            MicroRpcCodec.ValidateWireReport(report));
    }
}

public sealed class MicroInputServiceTests
{
    [Fact]
    public void FastUsesOfficialAct06PressAndRelease()
    {
        using var transport = new RecordingTransport();
        using var input = CreateInput(transport);

        Assert.True(input.TryToggleFast());

        Assert.Equal(
            [("ACT06", 1), ("ACT06", 0)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void ForkUsesOfficialAct09PressAndRelease()
    {
        using var transport = new RecordingTransport();
        using var input = CreateInput(transport);

        Assert.True(input.TryForkThread());

        Assert.Equal(
            [("ACT09", 1), ("ACT09", 0)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void EncoderDefaultsToComposerNavigationAndSendsDetents()
    {
        using var transport = new RecordingTransport();
        using var input = CreateInput(transport);

        Assert.False(input.TryStepReasoning(2, openFirst: true));
        Assert.True(input.TryStepEncoder(2));

        Assert.Equal(
            [("ENC_CW", 2), ("ENC_CW", 2)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void PushToTalkKeepsAct10PressedUntilRelease()
    {
        using var transport = new RecordingTransport();
        using var input = CreateInput(transport);

        Assert.True(input.TrySetPushToTalk(pressed: true));
        Assert.True(input.TrySetPushToTalk(pressed: false));

        Assert.Equal(
            [("ACT10", 1), ("ACT10", 0)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void PushToTalkRetriesUnconfirmedReleaseDuringDispose()
    {
        using var transport = new RecordingTransport();
        transport.Results.Enqueue(MicroReportSendResult.Accepted);
        transport.Results.Enqueue(MicroReportSendResult.NotSent);
        transport.Results.Enqueue(MicroReportSendResult.Accepted);
        var input = CreateInput(transport);

        Assert.Equal(
            MicroReportSendResult.Accepted,
            input.SendPushToTalk(pressed: true));
        Assert.Equal(
            MicroReportSendResult.NotSent,
            input.SendPushToTalk(pressed: false));

        input.Dispose();

        Assert.Equal(
            [("ACT10", 1), ("ACT10", 0), ("ACT10", 0)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void NextPressRearmsAnUnconfirmedPushToTalkRelease()
    {
        using var transport = new RecordingTransport();
        transport.Results.Enqueue(MicroReportSendResult.Accepted);
        transport.Results.Enqueue(MicroReportSendResult.OutcomeUnknown);
        transport.Results.Enqueue(MicroReportSendResult.Accepted);
        using var input = CreateInput(transport);

        Assert.Equal(
            MicroReportSendResult.Accepted,
            input.SendPushToTalk(pressed: true));
        Assert.Equal(
            MicroReportSendResult.OutcomeUnknown,
            input.SendPushToTalk(pressed: false));
        Assert.Equal(
            MicroReportSendResult.Accepted,
            input.SendPushToTalk(pressed: true));

        Assert.Equal(
            [
                ("ACT10", 1),
                ("ACT10", 0),
                ("ACT10", 0),
                ("ACT10", 1),
            ],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void AgentZeroIsARealSlotAndNeverEscape()
    {
        using var transport = new RecordingTransport();
        using var input = CreateInput(transport);

        Assert.True(input.TryTapAgentSlot(0));

        Assert.Equal(
            [("AG00", 1), ("AG00", 0)],
            DecodeHidEvents(transport.Reports));
    }

    [Fact]
    public void CustomSubmitMappingFailsClosedBeforeTransport()
    {
        var layout = CodexMicroLayoutResolver.Parse(
            """
            [desktop.codex-micro-layout.slots.ACT12]
            keycapId = "TERM"
            commandId = "toggleTerminal"
            """,
            "test");

        Assert.True(layout.GetSlot("ACT12").IsVerified);
        Assert.Equal("toggleTerminal", layout.GetSlot("ACT12").CommandId);

        var path = Path.Combine(
            Path.GetTempPath(),
            $"agent-controller-layout-{Guid.NewGuid():N}.toml");
        try
        {
            File.WriteAllText(
                path,
                """
                [desktop.codex-micro-layout.slots.ACT12]
                keycapId = "TERM"
                commandId = "toggleTerminal"
                """);
            using var transport = new RecordingTransport();
            using var input = new MicroInputService(
                transport,
                new CodexMicroLayoutResolver(path));

            Assert.Equal(
                MicroReportSendResult.NotSent,
                input.SendSubmit());
            Assert.Empty(transport.Reports);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MicroInputService CreateInput(
        IMicroReportTransport transport) =>
        new(
            transport,
            new CodexMicroLayoutResolver(Path.Combine(
                Path.GetTempPath(),
                $"agent-controller-missing-{Guid.NewGuid():N}.toml")));

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

        public Queue<MicroReportSendResult> Results { get; } = [];

        public MicroReportSendResult Result { get; init; } =
            MicroReportSendResult.Accepted;

        public MicroTransportState State => MicroTransportState.Ready;

        public MicroReportSendResult Send(IReadOnlyList<byte[]> reports)
        {
            Reports.AddRange(reports);
            return Results.TryDequeue(out var result)
                ? result
                : Result;
        }

        public void Dispose()
        {
        }
    }
}
