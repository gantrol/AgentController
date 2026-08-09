using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AgentController.MicroBroker;

internal static class BrokerWire
{
    internal static Task WriteAsync(
        Stream stream,
        BrokerRequest message,
        CancellationToken cancellationToken) =>
        WriteAsync(
            stream,
            message,
            BrokerJsonContext.Default.BrokerRequest,
            cancellationToken);

    internal static Task WriteAsync(
        Stream stream,
        BrokerResponse message,
        CancellationToken cancellationToken) =>
        WriteAsync(
            stream,
            message,
            BrokerJsonContext.Default.BrokerResponse,
            cancellationToken);

    private static async Task WriteAsync<T>(
        Stream stream,
        T message,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            typeInfo);
        if (payload.Length is <= 0 or > MicroBrokerProtocol.MaximumFrameLength)
        {
            throw new InvalidDataException(
                "Micro Broker frame is outside the bounded schema.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken)
            .ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken)
            .ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    internal static Task<BrokerRequest> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        ReadAsync(
            stream,
            BrokerJsonContext.Default.BrokerRequest,
            cancellationToken);

    internal static Task<BrokerResponse> ReadResponseAsync(
        Stream stream,
        CancellationToken cancellationToken) =>
        ReadAsync(
            stream,
            BrokerJsonContext.Default.BrokerResponse,
            cancellationToken);

    private static async Task<T> ReadAsync<T>(
        Stream stream,
        JsonTypeInfo<T> typeInfo,
        CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, header, cancellationToken)
            .ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MicroBrokerProtocol.MaximumFrameLength)
        {
            throw new InvalidDataException(
                "Micro Broker frame length is invalid.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.Deserialize(payload, typeInfo) ??
            throw new InvalidDataException(
                "Micro Broker frame payload is invalid.");
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var count = await stream.ReadAsync(
                    destination[read..],
                    cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                throw new EndOfStreamException(
                    "Micro Broker pipe closed before the frame completed.");
            }

            read += count;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(BrokerRequest))]
[JsonSerializable(typeof(BrokerResponse))]
internal sealed partial class BrokerJsonContext : JsonSerializerContext;
