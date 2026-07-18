using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CodexMicro.Protocol;

public static class MicroRpcCodec
{
    public static IReadOnlyList<byte[]> EncodeHid(
        string key,
        int action,
        int? agent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (action is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        var parameters = new Dictionary<string, object?>
        {
            ["k"] = key,
            ["act"] = action,
        };
        if (agent.HasValue)
        {
            parameters["ag"] = agent.Value;
        }

        return EncodeNotification("v.oai.hid", parameters);
    }

    public static IReadOnlyList<byte[]> EncodeJoystick(
        double angle,
        double distance)
    {
        if (!double.IsFinite(angle) || !double.IsFinite(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(angle),
                "Micro joystick values must be finite.");
        }

        return EncodeNotification(
            "v.oai.rad",
            new Dictionary<string, object?>
            {
                ["a"] = Math.Clamp(angle, 0, 1),
                ["d"] = Math.Clamp(distance, 0, 1),
            });
    }

    public static IReadOnlyList<byte[]> EncodeNotification(
        string method,
        IReadOnlyDictionary<string, object?> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(parameters);

        var message = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["m"] = method,
            ["p"] = parameters,
        }) + "\n";

        return EncodeDeviceToHostMessage(message);
    }

    public static IReadOnlyList<byte[]> EncodeResponse(
        JsonElement id,
        object? result)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result);
            writer.WriteEndObject();
        }

        return EncodeDeviceToHostMessage(
            Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n");
    }

    public static IReadOnlyList<byte[]> EncodeError(
        JsonElement id,
        int code,
        string message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("error");
            writer.WriteStartObject();
            writer.WriteNumber("code", code);
            writer.WriteString("message", message);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return EncodeDeviceToHostMessage(
            Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n");
    }

    public static byte[] DecodePayload(
        IEnumerable<ReadOnlyMemory<byte>> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        using var payload = new MemoryStream();
        foreach (var reportMemory in reports)
        {
            var report = reportMemory.Span;
            ValidateWireReport(report);
            payload.Write(report[3..(3 + report[2])]);
        }

        return payload.ToArray();
    }

    public static void ValidateWireReport(ReadOnlySpan<byte> report)
    {
        if (report.Length != MicroProtocol.ReportLength)
        {
            throw new InvalidDataException(
                $"Micro report must contain {MicroProtocol.ReportLength} bytes.");
        }

        if (
            report[0] != MicroProtocol.ReportId ||
            report[1] != MicroProtocol.RpcChannel)
        {
            throw new InvalidDataException(
                "Micro report ID or RPC channel does not match the frozen ABI.");
        }

        if (report[2] > MicroProtocol.MaximumPayloadLength)
        {
            throw new InvalidDataException(
                "Micro report payload length is invalid.");
        }
    }

    private static IReadOnlyList<byte[]> EncodeDeviceToHostMessage(
        string message)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        if (payload.Length > MicroProtocol.MaximumMessageLength)
        {
            throw new InvalidDataException("Micro RPC message is too large.");
        }

        var reports = new List<byte[]>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var length = Math.Min(
                MicroProtocol.MaximumPayloadLength,
                payload.Length - offset);

            // The current host decodes each report independently before it
            // concatenates text. Never split a UTF-8 scalar at a report edge.
            while (
                offset + length < payload.Length &&
                IsUtf8Continuation(payload[offset + length]))
            {
                length--;
            }

            if (length <= 0)
            {
                throw new InvalidDataException(
                    "Unable to split the Micro UTF-8 payload safely.");
            }

            var report = new byte[MicroProtocol.ReportLength];
            report[0] = MicroProtocol.ReportId;
            report[1] = MicroProtocol.RpcChannel;
            report[2] = checked((byte)length);
            Buffer.BlockCopy(payload, offset, report, 3, length);
            reports.Add(report);
            offset += length;
        }

        return reports;
    }

    private static bool IsUtf8Continuation(byte value) =>
        (value & 0xC0) == 0x80;
}
