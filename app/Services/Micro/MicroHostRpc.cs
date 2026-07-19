using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexController.Services.Micro;

public sealed record MicroSlotLighting(
    int SlotId,
    int Color,
    double Brightness,
    int Effect,
    double Speed,
    bool SyncKeysLighting,
    bool SyncAmbientLighting,
    bool LightingAmbiguous);

public sealed record MicroSlotLightingSnapshot(
    long Sequence,
    DateTimeOffset ObservedAt,
    IReadOnlyList<MicroSlotLighting> Slots);

/// <summary>
/// Reassembles Codex-to-device output reports. Unlike device notifications,
/// this direction has no line terminator: each appended fragment is tested as
/// one complete JSON value.
/// </summary>
internal sealed class MicroHostRpcAssembler
{
    private readonly MemoryStream _buffer = new();
    private readonly TimeSpan _messageTimeout;
    private DateTimeOffset? _startedAt;

    public MicroHostRpcAssembler(TimeSpan? messageTimeout = null)
    {
        _messageTimeout = messageTimeout ?? TimeSpan.FromSeconds(1);
    }

    public int BufferedLength => checked((int)_buffer.Length);

    public string? Append(
        ReadOnlySpan<byte> wireReport,
        DateTimeOffset observedAt)
    {
        MicroRpcCodec.ValidateWireReport(wireReport);
        if (
            _startedAt.HasValue &&
            observedAt - _startedAt.Value > _messageTimeout)
        {
            Reset();
        }

        _startedAt ??= observedAt;
        var payloadLength = wireReport[2];
        if (
            _buffer.Length + payloadLength >
            MicroRpcCodec.MaximumMessageLength)
        {
            Reset();
            throw new InvalidDataException(
                "Codex-to-device RPC exceeded the bounded message size.");
        }

        _buffer.Write(wireReport[3..(3 + payloadLength)]);
        var bytes = _buffer.GetBuffer().AsMemory(
            0,
            checked((int)_buffer.Length));
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var json = Encoding.UTF8.GetString(bytes.Span);
            Reset();
            return json;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Reset()
    {
        _buffer.SetLength(0);
        _startedAt = null;
    }
}

/// <summary>
/// Implements the minimum RPC surface Codex expects from a Micro device and
/// publishes slot-only lighting. Slot reports intentionally carry no thread
/// identity, so consumers must not attach them to task titles without a
/// separately proven roster.
/// </summary>
internal sealed class MicroDeviceRpcHandler
{
    private long _slotSequence;

    public event EventHandler<MicroSlotLightingSnapshot>?
        SlotLightingObserved;

    public IReadOnlyList<byte[]> Handle(string requestJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestJson);
        using var document = JsonDocument.Parse(requestJson);
        var root = document.RootElement;
        if (
            root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("id", out var id) ||
            !root.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                "Host RPC request is missing id or method.");
        }

        return methodElement.GetString() switch
        {
            "sys.version" => MicroRpcCodec.EncodeResponse(
                id,
                new { version = MicroRpcCodec.FirmwareVersion }),
            "device.status" => MicroRpcCodec.EncodeResponse(
                id,
                new
                {
                    version = MicroRpcCodec.FirmwareVersion,
                    profile_index = 0,
                    layer_index = 0,
                    battery = 100,
                    is_charging = true,
                }),
            "v.oai.rgbcfg" => MicroRpcCodec.EncodeResponse(id, true),
            "v.oai.thstatus" => HandleThreadStatus(root, id),
            _ => MicroRpcCodec.EncodeError(
                id,
                -32601,
                "Method not supported by the Agent Controller Micro device."),
        };
    }

    private IReadOnlyList<byte[]> HandleThreadStatus(
        JsonElement request,
        JsonElement id)
    {
        if (
            !request.TryGetProperty("params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Array)
        {
            return InvalidThreadStatus(id);
        }

        var slots = new List<MicroSlotLighting>();
        var seen = new HashSet<int>();
        foreach (var element in parameters.EnumerateArray())
        {
            if (
                element.ValueKind != JsonValueKind.Object ||
                !TryInt(element, "id", out var slotId) ||
                slotId is < 0 or > 5 ||
                !seen.Add(slotId) ||
                !TryInt(element, "c", out var color) ||
                color is < 0 or > 0xFFFFFF ||
                !TryInt(element, "e", out var effect) ||
                effect is < 0 or > 6)
            {
                return InvalidThreadStatus(id);
            }

            slots.Add(new MicroSlotLighting(
                slotId,
                color,
                TryDouble(element, "b", out var brightness)
                    ? brightness
                    : 0,
                effect,
                TryDouble(element, "s", out var speed) ? speed : 0,
                TryInt(element, "sk", out var syncKeys) && syncKeys != 0,
                TryInt(element, "sa", out var syncAmbient) &&
                    syncAmbient != 0,
                color == 0));
        }

        slots.Sort((left, right) =>
            left.SlotId.CompareTo(right.SlotId));
        var response = MicroRpcCodec.EncodeResponse(id, true);
        PublishSlotLighting(new MicroSlotLightingSnapshot(
            Interlocked.Increment(ref _slotSequence),
            DateTimeOffset.UtcNow,
            slots));
        return response;
    }

    private void PublishSlotLighting(MicroSlotLightingSnapshot snapshot)
    {
        var handlers = SlotLightingObserved;
        if (handlers is null)
        {
            return;
        }

        foreach (EventHandler<MicroSlotLightingSnapshot> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(this, snapshot);
            }
            catch
            {
                // Lighting observers are presentation-only and must never
                // break the host RPC acknowledgement loop.
            }
        }
    }

    private static IReadOnlyList<byte[]> InvalidThreadStatus(
        JsonElement id) =>
        MicroRpcCodec.EncodeError(
            id,
            -32602,
            "v.oai.thstatus contains invalid slot lighting data.");

    private static bool TryInt(
        JsonElement element,
        string name,
        out int value)
    {
        value = default;
        return
            element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool TryDouble(
        JsonElement element,
        string name,
        out double value)
    {
        value = default;
        return
            element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value) &&
            double.IsFinite(value);
    }
}
