using System.Text;
using System.Text.Json;
using CodexMicro.Protocol;

namespace AgentController.MicroBroker;

internal enum MicroInputMessageKind
{
    Hid,
    Analog,
}

internal readonly record struct MicroInputMessage(
    MicroInputMessageKind Kind,
    string? Key = null,
    int Action = 0,
    int? Agent = null,
    double Angle = 0,
    double Distance = 0)
{
    internal IReadOnlyList<byte[]> Encode() => Kind switch
    {
        MicroInputMessageKind.Hid => MicroRpcCodec.EncodeHid(
            Key ?? throw new InvalidDataException(
                "Micro HID message has no key."),
            Action,
            Agent),
        MicroInputMessageKind.Analog =>
            MicroRpcCodec.EncodeJoystick(Angle, Distance),
        _ => throw new InvalidDataException(
            "Micro input message kind is unsupported."),
    };
}

internal static class MicroInputBatch
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static IReadOnlyList<MicroInputMessage> Parse(
        IReadOnlyList<byte[]> reports)
    {
        var payload = MicroRpcCodec.DecodePayload(
            reports.Select(report => (ReadOnlyMemory<byte>)report));
        if (payload.Length == 0 || payload[^1] != (byte)'\n')
        {
            throw new InvalidDataException(
                "Broker input batches must contain complete LF-terminated messages.");
        }

        var text = StrictUtf8.GetString(payload);
        var lines = text.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            throw new InvalidDataException(
                "Broker input batch contains no messages.");
        }

        var messages = new List<MicroInputMessage>(lines.Length);
        foreach (var line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (
                !root.TryGetProperty("m", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("p", out var parameters) ||
                parameters.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    "Broker input message has an invalid RPC envelope.");
            }

            messages.Add(methodElement.GetString() switch
            {
                "v.oai.hid" => ParseHid(parameters),
                "v.oai.rad" => ParseAnalog(parameters),
                _ => throw new InvalidDataException(
                    "Broker clients may submit only Micro HID or analog notifications."),
            });
        }

        return messages;
    }

    private static MicroInputMessage ParseHid(JsonElement parameters)
    {
        if (
            !parameters.TryGetProperty("k", out var keyElement) ||
            keyElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(keyElement.GetString()) ||
            !parameters.TryGetProperty("act", out var actionElement) ||
            !actionElement.TryGetInt32(out var action) ||
            action is < 0 or > 2)
        {
            throw new InvalidDataException(
                "Broker HID notification is invalid.");
        }

        int? agent = null;
        if (parameters.TryGetProperty("ag", out var agentElement))
        {
            if (!agentElement.TryGetInt32(out var parsedAgent))
            {
                throw new InvalidDataException(
                    "Broker HID agent index is invalid.");
            }

            agent = parsedAgent;
        }

        return new(
            MicroInputMessageKind.Hid,
            keyElement.GetString(),
            action,
            agent);
    }

    private static MicroInputMessage ParseAnalog(JsonElement parameters)
    {
        if (
            !parameters.TryGetProperty("a", out var angleElement) ||
            !angleElement.TryGetDouble(out var angle) ||
            !double.IsFinite(angle) ||
            !parameters.TryGetProperty("d", out var distanceElement) ||
            !distanceElement.TryGetDouble(out var distance) ||
            !double.IsFinite(distance))
        {
            throw new InvalidDataException(
                "Broker analog notification is invalid.");
        }

        return new(
            MicroInputMessageKind.Analog,
            Angle: Math.Clamp(angle, 0, 1),
            Distance: Math.Clamp(distance, 0, 1));
    }
}
