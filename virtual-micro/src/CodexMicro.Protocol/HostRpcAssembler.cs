using System.Text;
using System.Text.Json;

namespace CodexMicro.Protocol;

/// <summary>
/// Reassembles Codex-to-device output reports. This direction deliberately
/// has no newline terminator: after each fragment the observed host protocol
/// attempts to parse one complete JSON value.
/// </summary>
public sealed class HostRpcAssembler
{
    private readonly MemoryStream _buffer = new();
    private readonly TimeSpan _messageTimeout;
    private DateTimeOffset? _startedAt;

    public HostRpcAssembler(TimeSpan? messageTimeout = null)
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
        if (_buffer.Length + payloadLength > MicroProtocol.MaximumMessageLength)
        {
            Reset();
            throw new InvalidDataException(
                "Codex-to-device RPC exceeded the bounded message size.");
        }

        _buffer.Write(wireReport[3..(3 + payloadLength)]);
        var bytes = _buffer.GetBuffer().AsMemory(0, checked((int)_buffer.Length));
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
