using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CodexMicro.Protocol;
using Microsoft.Win32.SafeHandles;

namespace AgentController.MicroBroker;

internal interface IMicroDriverEndpoint : IDisposable
{
    bool IsConnected { get; }
    BrokerDriverInfo Connect();
    MicroSendResult Submit(IReadOnlyList<byte[]> reports);
    MicroSendResult TapKeyboard(BrokerKeyboardKey key, bool shift);
    DriverOutputReport? TryReadOutput();
}

internal sealed class VhfDriverEndpoint : IMicroDriverEndpoint
{
    private static readonly Guid DeviceInterfaceGuid =
        new("E2A7CB54-8420-4D51-9DD8-D6575B9251D1");

    private const uint Magic = 0x314D4356;
    private const ushort ContractVersion = 1;
    private const uint InfoFlagReady = 0x00000001;
    private const uint IoctlGetInfo =
        (0x22u << 16) | (3u << 14) | (0x800u << 2);
    private const uint IoctlSubmitInput =
        (0x22u << 16) | (3u << 14) | (0x801u << 2);
    private const uint IoctlReadOutput =
        (0x22u << 16) | (3u << 14) | (0x802u << 2);
    private const uint IoctlSubmitKeyboard =
        (0x22u << 16) | (3u << 14) | (0x803u << 2);
    private const int ErrorNoMoreItems = 259;

    private readonly object _sync = new();
    private SafeFileHandle? _handle;
    private ulong _nextSequence;
    private ulong _lastOutputSequence;

    public bool IsConnected =>
        _handle is { IsInvalid: false, IsClosed: false };

    public BrokerDriverInfo Connect()
    {
        lock (_sync)
        {
            DisposeHandle();
            var path = DeviceInterfaceEnumerator.FindFirst(
                DeviceInterfaceGuid) ??
                throw new InvalidOperationException(
                    "CodexMicroVhfUm device interface is not present.");
            _handle = CreateFile(
                path,
                desiredAccess: 0xC0000000,
                shareMode: 0,
                IntPtr.Zero,
                creationDisposition: 3,
                flagsAndAttributes: 0,
                IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                DisposeHandle();
                throw new Win32Exception(error);
            }

            var info = GetInfoCore();
            _nextSequence = info.LastBatchSequence;
            _lastOutputSequence = 0;
            return info;
        }
    }

    public MicroSendResult Submit(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count is 0 or > MicroBrokerProtocol.MaximumBatchReports)
        {
            return MicroSendResult.NotSent(
                "Batch report count is outside the bounded broker schema.");
        }

        foreach (var report in reports)
        {
            MicroRpcCodec.ValidateWireReport(report);
        }

        lock (_sync)
        {
            if (_nextSequence == ulong.MaxValue)
            {
                return new(
                    MicroSendDisposition.Rejected,
                    0,
                    reports.Count,
                    0,
                    "The driver input sequence is exhausted.");
            }

            var sequence = ++_nextSequence;
            var input = new byte[
                16 + reports.Count * MicroProtocol.ReportLength];
            BinaryPrimitives.WriteUInt32LittleEndian(input, Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(
                input.AsSpan(4),
                ContractVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(
                input.AsSpan(6),
                checked((ushort)reports.Count));
            BinaryPrimitives.WriteUInt64LittleEndian(
                input.AsSpan(8),
                sequence);
            for (var index = 0; index < reports.Count; index++)
            {
                Buffer.BlockCopy(
                    reports[index],
                    0,
                    input,
                    16 + index * MicroProtocol.ReportLength,
                    MicroProtocol.ReportLength);
            }

            return SubmitIoctlCore(
                IoctlSubmitInput,
                input,
                sequence,
                reports.Count);
        }
    }

    public MicroSendResult TapKeyboard(
        BrokerKeyboardKey key,
        bool shift)
    {
        lock (_sync)
        {
            if (_nextSequence == ulong.MaxValue)
            {
                return new(
                    MicroSendDisposition.Rejected,
                    0,
                    2,
                    0,
                    "The driver input sequence is exhausted.");
            }

            var sequence = ++_nextSequence;
            var input = new byte[24];
            BinaryPrimitives.WriteUInt32LittleEndian(input, Magic);
            BinaryPrimitives.WriteUInt16LittleEndian(
                input.AsSpan(4),
                ContractVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(6), 24);
            BinaryPrimitives.WriteUInt64LittleEndian(
                input.AsSpan(8),
                sequence);
            input[16] = key switch
            {
                BrokerKeyboardKey.Tab => 0x2B,
                BrokerKeyboardKey.Enter => 0x28,
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };
            input[17] = shift ? (byte)0x02 : (byte)0;
            if (key == BrokerKeyboardKey.Enter && shift)
            {
                return new(
                    MicroSendDisposition.Rejected,
                    0,
                    2,
                    0,
                    "Shift+Enter is outside the restricted keyboard contract.");
            }

            return SubmitIoctlCore(
                IoctlSubmitKeyboard,
                input,
                sequence,
                requestedReports: 2);
        }
    }

    public DriverOutputReport? TryReadOutput()
    {
        lock (_sync)
        {
            var output = new byte[96];
            if (!DeviceIoControl(
                    RequireHandle(),
                    IoctlReadOutput,
                    null,
                    0,
                    output,
                    output.Length,
                    out var returned,
                    IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreItems)
                {
                    return null;
                }

                throw new Win32Exception(error);
            }

            if (
                returned != output.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(output) != Magic ||
                BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(4)) !=
                    ContractVersion ||
                BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(6)) !=
                    output.Length)
            {
                throw new InvalidDataException(
                    "Driver output record did not match protocol v1.");
            }

            var sequence =
                BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8));
            if (
                sequence == 0 ||
                (_lastOutputSequence != 0 &&
                 sequence != _lastOutputSequence + 1))
            {
                throw new InvalidDataException(
                    "Driver output sequence is inconsistent.");
            }

            var wire = new byte[MicroProtocol.ReportLength];
            Buffer.BlockCopy(output, 32, wire, 0, wire.Length);
            MicroRpcCodec.ValidateWireReport(wire);
            _lastOutputSequence = sequence;
            return new(
                sequence,
                BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(16)),
                BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(24)),
                BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(28)),
                wire);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            DisposeHandle();
        }
    }

    private MicroSendResult SubmitIoctlCore(
        uint ioctl,
        byte[] input,
        ulong sequence,
        int requestedReports)
    {
        var output = new byte[32];
        if (!DeviceIoControl(
                RequireHandle(),
                ioctl,
                input,
                input.Length,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero))
        {
            return new(
                MicroSendDisposition.OutcomeUnknown,
                0,
                requestedReports,
                Marshal.GetLastWin32Error(),
                "Driver IOCTL completion is unknown; never retry automatically.");
        }

        if (
            returned != output.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(output) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(4)) !=
                ContractVersion ||
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8)) !=
                sequence)
        {
            return new(
                MicroSendDisposition.OutcomeUnknown,
                0,
                requestedReports,
                0,
                "Driver returned a malformed submit result.");
        }

        var disposition =
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(16));
        var accepted = checked((int)
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(20)));
        var nativeStatus =
            BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(24));
        return disposition switch
        {
            1 when accepted == requestedReports => new(
                MicroSendDisposition.Accepted,
                accepted,
                requestedReports,
                nativeStatus,
                "All reports were accepted by VHF."),
            3 when accepted == requestedReports => new(
                MicroSendDisposition.Accepted,
                accepted,
                requestedReports,
                nativeStatus,
                "The driver recognized an already accepted sequence."),
            0 when accepted == 0 => new(
                MicroSendDisposition.NotSent,
                0,
                requestedReports,
                nativeStatus,
                "No report reached VHF."),
            4 when accepted == 0 => new(
                MicroSendDisposition.Rejected,
                0,
                requestedReports,
                nativeStatus,
                "The driver rejected the batch."),
            _ => new(
                MicroSendDisposition.OutcomeUnknown,
                accepted,
                requestedReports,
                nativeStatus,
                "Only part of the batch was accepted; never retry automatically."),
        };
    }

    private BrokerDriverInfo GetInfoCore()
    {
        var output = new byte[40];
        if (!DeviceIoControl(
                RequireHandle(),
                IoctlGetInfo,
                null,
                0,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero) ||
            returned != output.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(output) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(4)) !=
                ContractVersion ||
            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(6)) !=
                output.Length ||
            (BinaryPrimitives.ReadUInt32LittleEndian(
                output.AsSpan(36)) & InfoFlagReady) == 0)
        {
            throw new InvalidDataException(
                "Installed driver does not implement ready protocol v1.");
        }

        return new(
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8)),
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(16)),
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(24)),
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(32)),
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(36)),
            "CodexMicroVhfUm / Broker");
    }

    private SafeFileHandle RequireHandle() =>
        _handle is { IsInvalid: false, IsClosed: false } handle
            ? handle
            : throw new InvalidOperationException(
                "CodexMicroVhfUm is not connected.");

    private void DisposeHandle()
    {
        _handle?.Dispose();
        _handle = null;
        _lastOutputSequence = 0;
    }

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[]? inputBuffer,
        int inputBufferSize,
        byte[]? outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}

internal sealed record DriverOutputReport(
    ulong Sequence,
    ulong PerformanceCounter,
    uint OriginalLength,
    uint Flags,
    byte[] WireReport);

internal static class DeviceInterfaceEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    internal static string? FindFirst(Guid interfaceGuid)
    {
        var set = SetupDiGetClassDevs(
            ref interfaceGuid,
            null,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var data = new SpDeviceInterfaceData
            {
                Size = Marshal.SizeOf<SpDeviceInterfaceData>(),
            };
            if (!SetupDiEnumDeviceInterfaces(
                    set,
                    IntPtr.Zero,
                    ref interfaceGuid,
                    0,
                    ref data))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreItems)
                {
                    return null;
                }

                throw new Win32Exception(error);
            }

            _ = SetupDiGetDeviceInterfaceDetail(
                set,
                ref data,
                IntPtr.Zero,
                0,
                out var requiredSize,
                IntPtr.Zero);
            var detailError = Marshal.GetLastWin32Error();
            var detailHeaderSize = IntPtr.Size == 8 ? 8u : 6u;
            if (
                detailError != ErrorInsufficientBuffer ||
                requiredSize < detailHeaderSize)
            {
                throw new Win32Exception(detailError);
            }

            var buffer = Marshal.AllocHGlobal(checked((int)requiredSize));
            try
            {
                Marshal.WriteInt32(buffer, checked((int)detailHeaderSize));
                if (!SetupDiGetDeviceInterfaceDetail(
                        set,
                        ref data,
                        buffer,
                        requiredSize,
                        out _,
                        IntPtr.Zero))
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error());
                }

                return Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = SetupDiDestroyDeviceInfoList(set);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [DllImport(
        "setupapi.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport(
        "setupapi.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(
        IntPtr deviceInfoSet);
}
