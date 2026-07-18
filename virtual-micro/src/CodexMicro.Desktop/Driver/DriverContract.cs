using System.Buffers.Binary;

namespace CodexMicro.Desktop.Driver;

internal static class DriverContract
{
    public static readonly Guid DeviceInterfaceGuid =
        new("E2A7CB54-8420-4D51-9DD8-D6575B9251D1");

    public const uint Magic = 0x314D4356; // "VCM1" as little-endian bytes.
    public const ushort Version = 1;
    public const int WireReportLength = 64;
    public const int MaximumBatchReports = 64;
    public const uint InfoFlagReady = 0x00000001;
    public const uint InfoFlagDialogKeyboard = 0x00000002;

    public const uint IoctlGetInfo =
        (0x22u << 16) | (3u << 14) | (0x800u << 2);
    public const uint IoctlSubmitInput =
        (0x22u << 16) | (3u << 14) | (0x801u << 2);
    public const uint IoctlReadOutput =
        (0x22u << 16) | (3u << 14) | (0x802u << 2);
    public const uint IoctlSubmitKeyboard =
        (0x22u << 16) | (3u << 14) | (0x803u << 2);

    public static void WriteHeader(
        Span<byte> destination,
        ushort reportCount,
        ulong sequence)
    {
        if (destination.Length < 16)
        {
            throw new ArgumentException("Driver batch header is truncated.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], reportCount);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], sequence);
    }

    public static void WriteKeyboardInput(
        Span<byte> destination,
        VhfKeyboardKey key,
        bool shift,
        ulong sequence)
    {
        if (destination.Length < 24)
        {
            throw new ArgumentException("Driver keyboard input is truncated.");
        }

        destination[..24].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], 24);
        BinaryPrimitives.WriteUInt64LittleEndian(destination[8..], sequence);
        destination[16] = (byte)key;
        destination[17] = shift ? (byte)0x02 : (byte)0;
    }
}

internal enum VhfKeyboardKey : byte
{
    Enter = 0x28,
    Tab = 0x2B,
}

internal sealed record DriverInfo(
    ulong ConnectionEpoch,
    ulong LastBatchSequence,
    ulong OutputSequence,
    uint DroppedOutputReports,
    uint Flags,
    string TransportName);

internal sealed record DriverOutputReport(
    ulong Sequence,
    ulong PerformanceCounter,
    uint OriginalLength,
    uint Flags,
    byte[] WireReport);
