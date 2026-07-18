using System.Text.Json;

namespace CodexMicro.Protocol;

public sealed class DeviceRpcHandler
{
    private long _slotSequence;

    public event EventHandler<SlotLightingSnapshot>? SlotLightingObserved;

    public event EventHandler<string>? RpcObserved;

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

        var method = methodElement.GetString()!;
        RpcObserved?.Invoke(this, method);
        return method switch
        {
            "sys.version" => MicroRpcCodec.EncodeResponse(
                id,
                new { version = MicroProtocol.FirmwareVersion }),
            "device.status" => MicroRpcCodec.EncodeResponse(
                id,
                new
                {
                    version = MicroProtocol.FirmwareVersion,
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
                "Method not supported by the Codex Micro virtual HID simulator."),
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
            return MicroRpcCodec.EncodeError(
                id,
                -32602,
                "v.oai.thstatus params must be an array.");
        }

        var slots = new List<SlotLighting>();
        var seen = new HashSet<int>();
        foreach (var element in parameters.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return InvalidThreadStatus(id);
            }

            if (
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

            var brightness = TryDouble(element, "b", out var b) ? b : 0;
            var speed = TryDouble(element, "s", out var s) ? s : 0;
            slots.Add(new SlotLighting(
                slotId,
                color,
                brightness,
                effect,
                speed,
                TryInt(element, "sk", out var sk) && sk != 0,
                TryInt(element, "sa", out var sa) && sa != 0,
                color == 0));
        }

        slots.Sort((left, right) => left.SlotId.CompareTo(right.SlotId));
        SlotLightingObserved?.Invoke(
            this,
            new SlotLightingSnapshot(
                Interlocked.Increment(ref _slotSequence),
                DateTimeOffset.UtcNow,
                slots));
        return MicroRpcCodec.EncodeResponse(id, true);
    }

    private static IReadOnlyList<byte[]> InvalidThreadStatus(JsonElement id) =>
        MicroRpcCodec.EncodeError(
            id,
            -32602,
            "v.oai.thstatus contains an invalid slot, color, or effect.");

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
