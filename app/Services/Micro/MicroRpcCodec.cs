using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexController.Services.Micro;

/// <summary>
/// Encodes the private Codex Micro device-to-host RPC ABI used by the
/// currently supported desktop package. Each returned buffer is one complete
/// HID input report, including report ID 0x06.
/// </summary>
public static class MicroRpcCodec
{
    public const byte ReportId = 0x06;
    public const byte DebugChannel = 0x01;
    public const byte RpcChannel = 0x02;
    public const int ReportLength = 64;
    public const int MaximumPayloadLength = 61;
    public const int MaximumMessageLength = 64 * 1024;
    public const string FirmwareVersion = "0.1.0-vhf-poc";

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
        return EncodeUtf8Payload(Encoding.UTF8.GetBytes(message));
    }

    public static IReadOnlyList<byte[]> EncodeResponse(
        JsonElement id,
        object? result)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            id.WriteTo(writer);
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, result);
            writer.WriteEndObject();
        }

        return EncodeUtf8PayloadWithNewline(buffer.WrittenSpan);
    }

    public static IReadOnlyList<byte[]> EncodeError(
        JsonElement id,
        int code,
        string message)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
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

        return EncodeUtf8PayloadWithNewline(buffer.WrittenSpan);
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

            var length = report[2];
            if (payload.Length + length > MaximumMessageLength)
            {
                throw new InvalidDataException(
                    "Micro RPC message is too large.");
            }

            payload.Write(report[3..(3 + length)]);
        }

        return payload.ToArray();
    }

    public static void ValidateWireReport(ReadOnlySpan<byte> report)
    {
        if (report.Length != ReportLength)
        {
            throw new InvalidDataException(
                $"Micro report must contain {ReportLength} bytes.");
        }

        if (report[0] != ReportId || report[1] != RpcChannel)
        {
            throw new InvalidDataException(
                "Micro report ID or channel does not match the ABI.");
        }

        if (report[2] > MaximumPayloadLength)
        {
            throw new InvalidDataException(
                "Micro report payload length is invalid.");
        }

        var padding = report[(3 + report[2])..];
        if (padding.IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidDataException(
                "Micro report padding must be zero-filled.");
        }
    }

    private static IReadOnlyList<byte[]> EncodeUtf8Payload(byte[] payload)
    {
        if (payload.Length == 0 || payload.Length > MaximumMessageLength)
        {
            if (payload.Length == 0)
            {
                return [];
            }

            throw new InvalidDataException("Micro RPC message is too large.");
        }

        var reports = new List<byte[]>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var length = Math.Min(
                MaximumPayloadLength,
                payload.Length - offset);

            // Codex decodes every report chunk separately before joining the
            // text. Keep a multi-byte UTF-8 scalar within one report.
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

            var report = new byte[ReportLength];
            report[0] = ReportId;
            report[1] = RpcChannel;
            report[2] = checked((byte)length);
            Buffer.BlockCopy(payload, offset, report, 3, length);
            reports.Add(report);
            offset += length;
        }

        return reports;
    }

    private static IReadOnlyList<byte[]> EncodeUtf8PayloadWithNewline(
        ReadOnlySpan<byte> payload)
    {
        var terminated = new byte[payload.Length + 1];
        payload.CopyTo(terminated);
        terminated[^1] = (byte)'\n';
        return EncodeUtf8Payload(terminated);
    }

    private static bool IsUtf8Continuation(byte value) =>
        (value & 0xC0) == 0x80;
}
