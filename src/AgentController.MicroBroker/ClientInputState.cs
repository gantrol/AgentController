using System.Text.Json;
using CodexMicro.Protocol;

namespace AgentController.MicroBroker;

internal sealed class ClientInputState
{
    private readonly MemoryStream _message = new();
    private readonly HashSet<string> _heldKeys = new(StringComparer.Ordinal);
    private AnalogInputState? _analog;

    internal IReadOnlyCollection<string> HeldKeys => _heldKeys;
    internal AnalogInputState? Analog => _analog;
    internal bool HoldsKey(string key) => _heldKeys.Contains(key);

    internal void Observe(IReadOnlyList<byte[]> reports)
    {
        foreach (var report in reports)
        {
            MicroRpcCodec.ValidateWireReport(report);
            var payload = report.AsSpan(3, report[2]);
            foreach (var value in payload)
            {
                if (value == (byte)'\n')
                {
                    ApplyMessage();
                    _message.SetLength(0);
                    continue;
                }

                if (_message.Length >= MicroProtocol.MaximumMessageLength)
                {
                    _message.SetLength(0);
                    throw new InvalidDataException(
                        "Client Micro message exceeded the bounded size.");
                }

                _message.WriteByte(value);
            }
        }
    }

    internal IReadOnlyList<byte[]> BuildNeutralReports(
        Func<string, bool>? shouldReleaseKey = null,
        bool releaseAnalog = true)
    {
        var reports = new List<byte[]>();
        foreach (var key in _heldKeys.Order(StringComparer.Ordinal))
        {
            if (shouldReleaseKey?.Invoke(key) ?? true)
            {
                reports.AddRange(MicroRpcCodec.EncodeHid(key, action: 0));
            }
        }

        if (releaseAnalog && _analog is { } analog)
        {
            reports.AddRange(MicroRpcCodec.EncodeJoystick(
                analog.Angle,
                0));
        }

        return reports;
    }

    internal void Clear()
    {
        _heldKeys.Clear();
        _analog = null;
        _message.SetLength(0);
    }

    private void ApplyMessage()
    {
        if (_message.Length == 0)
        {
            return;
        }

        using var document = JsonDocument.Parse(
            _message.GetBuffer().AsMemory(0, checked((int)_message.Length)));
        var root = document.RootElement;
        if (
            !root.TryGetProperty("m", out var method) ||
            method.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("p", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (method.GetString())
        {
            case "v.oai.hid":
                ObserveHid(parameters);
                break;
            case "v.oai.rad":
                ObserveAnalog(parameters);
                break;
        }
    }

    private void ObserveHid(JsonElement parameters)
    {
        if (
            !parameters.TryGetProperty("k", out var keyElement) ||
            keyElement.ValueKind != JsonValueKind.String ||
            !parameters.TryGetProperty("act", out var actionElement) ||
            !actionElement.TryGetInt32(out var action))
        {
            return;
        }

        var key = keyElement.GetString();
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (action == 1)
        {
            _heldKeys.Add(key);
        }
        else if (action == 0)
        {
            _heldKeys.Remove(key);
        }
    }

    private void ObserveAnalog(JsonElement parameters)
    {
        if (
            !parameters.TryGetProperty("a", out var angleElement) ||
            !angleElement.TryGetDouble(out var angle) ||
            !parameters.TryGetProperty("d", out var distanceElement) ||
            !distanceElement.TryGetDouble(out var distance))
        {
            return;
        }

        _analog = distance > 0
            ? new(
                Math.Clamp(angle, 0, 1),
                Math.Clamp(distance, 0, 1))
            : null;
    }
}

internal readonly record struct AnalogInputState(
    double Angle,
    double Distance);
