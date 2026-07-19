using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace CodexController.Services.Micro;

/// <summary>
/// Full-duplex client for the bounded virtual-Micro VHF driver contract.
/// Input batches are submitted through the private device interface while a
/// single background reader services Codex-to-device RPC and slot lighting.
/// </summary>
public sealed class VhfMicroReportTransport : IMicroReportTransport
{
    private static readonly Guid DeviceInterfaceGuid =
        new("E2A7CB54-8420-4D51-9DD8-D6575B9251D1");

    private const uint Magic = 0x314D4356;
    private const ushort ContractVersion = 1;
    private const int MaximumBatchReports = 64;
    private const uint InfoFlagReady = 0x00000001;
    private const uint OutputFlagIncludedReportId = 0x00000001;
    private const uint OutputFlagExcludedReportId = 0x00000002;
    private const int ErrorNoMoreItems = 259;
    private const int RetryBackoffMs = 1_000;
    private const uint IoctlGetInfo =
        (0x22u << 16) | (3u << 14) | (0x800u << 2);
    private const uint IoctlSubmitInput =
        (0x22u << 16) | (3u << 14) | (0x801u << 2);
    private const uint IoctlReadOutput =
        (0x22u << 16) | (3u << 14) | (0x802u << 2);

    private readonly object _sync = new();
    private readonly MicroDeviceRpcHandler _rpc = new();
    private SafeFileHandle? _handle;
    private CancellationTokenSource? _outputCancellation;
    private Task? _outputTask;
    private ulong _nextSequence;
    private ulong _lastOutputSequence;
    private long _connectionGeneration;
    private long _retryAfter;
    private MicroTransportState _state = MicroTransportState.Unavailable;
    private bool _disposed;

    public VhfMicroReportTransport()
    {
        _rpc.SlotLightingObserved += (_, snapshot) =>
            PublishSlotLighting(snapshot);
    }

    public event EventHandler<MicroSlotLightingSnapshot>?
        SlotLightingObserved;

    public MicroTransportState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public MicroReportSendResult Send(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count is 0 or > MaximumBatchReports)
        {
            return MicroReportSendResult.NotSent;
        }

        foreach (var report in reports)
        {
            MicroRpcCodec.ValidateWireReport(report);
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!EnsureConnectedCore())
            {
                return MicroReportSendResult.NotSent;
            }

            return SubmitCore(reports);
        }
    }

    public void Dispose()
    {
        Task? outputTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _outputCancellation?.Cancel();
            outputTask = _outputTask;
            DisposeHandleCore();
            _state = MicroTransportState.Unavailable;
        }

        try
        {
            outputTask?.Wait(TimeSpan.FromMilliseconds(250));
        }
        catch (AggregateException)
        {
        }

        lock (_sync)
        {
            _outputCancellation?.Dispose();
            _outputCancellation = null;
            _outputTask = null;
        }
    }

    private bool EnsureConnectedCore()
    {
        if (_handle is { IsInvalid: false, IsClosed: false })
        {
            return true;
        }

        if (Environment.TickCount64 < _retryAfter)
        {
            return false;
        }

        try
        {
            var path = MicroDeviceInterfaceEnumerator.FindFirst(
                DeviceInterfaceGuid);
            if (path is null)
            {
                MarkUnavailableCore();
                return false;
            }

            var handle = CreateFile(
                path,
                desiredAccess: 0xC0000000,
                shareMode: 0,
                IntPtr.Zero,
                creationDisposition: 3,
                flagsAndAttributes: 0,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                MarkUnavailableCore();
                return false;
            }

            _handle = handle;
            var output = new byte[40];
            if (!DeviceIoControl(
                    handle,
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
                DisposeHandleCore();
                _state = MicroTransportState.Unavailable;
                _retryAfter = Environment.TickCount64 + RetryBackoffMs;
                return false;
            }

            _nextSequence =
                BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(16));
            _lastOutputSequence = 0;
            var generation = ++_connectionGeneration;
            _state = MicroTransportState.Ready;
            StartOutputLoopCore(generation);
            return true;
        }
        catch (Exception exception) when (
            exception is Win32Exception or
                InvalidDataException or
                InvalidOperationException or
                UnauthorizedAccessException)
        {
            DisposeHandleCore();
            _state = MicroTransportState.Faulted;
            _retryAfter = Environment.TickCount64 + RetryBackoffMs;
            return false;
        }
    }

    private MicroReportSendResult SubmitCore(
        IReadOnlyList<byte[]> reports)
    {
        if (reports.Count is 0 or > MaximumBatchReports)
        {
            return MicroReportSendResult.Rejected;
        }

        foreach (var report in reports)
        {
            MicroRpcCodec.ValidateWireReport(report);
        }

        var sequence = ++_nextSequence;
        var input = new byte[
            16 + reports.Count * MicroRpcCodec.ReportLength];
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
                16 + index * MicroRpcCodec.ReportLength,
                MicroRpcCodec.ReportLength);
        }

        var output = new byte[32];
        if (!DeviceIoControl(
            RequireHandleCore(),
            IoctlSubmitInput,
            input,
            input.Length,
            output,
            output.Length,
            out var returned,
            IntPtr.Zero))
        {
            FaultConnectionCore();
            return MicroReportSendResult.OutcomeUnknown;
        }

        if (
            returned != output.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(output) != Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(4)) !=
                ContractVersion ||
            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(6)) !=
                output.Length ||
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(28)) != 0 ||
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8)) !=
                sequence)
        {
            FaultConnectionCore();
            return MicroReportSendResult.OutcomeUnknown;
        }

        var disposition =
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(16));
        var accepted =
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(20));
        if (accepted > reports.Count)
        {
            FaultConnectionCore();
            return MicroReportSendResult.OutcomeUnknown;
        }

        return disposition switch
        {
            1 when accepted == reports.Count =>
                MicroReportSendResult.Accepted,
            3 when accepted == reports.Count =>
                MicroReportSendResult.Accepted,
            0 when accepted == 0 => MicroReportSendResult.NotSent,
            4 when accepted == 0 => MicroReportSendResult.Rejected,
            _ => MicroReportSendResult.OutcomeUnknown,
        };
    }

    private void StartOutputLoopCore(long generation)
    {
        var previousCancellation = _outputCancellation;
        var previousTask = _outputTask;
        previousCancellation?.Cancel();
        if (previousCancellation is not null)
        {
            if (previousTask is null || previousTask.IsCompleted)
            {
                previousCancellation.Dispose();
            }
            else
            {
                _ = previousTask.ContinueWith(
                    _ => previousCancellation.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        var cancellation = new CancellationTokenSource();
        _outputCancellation = cancellation;
        _outputTask = Task.Run(
            () => PollOutputAsync(generation, cancellation.Token),
            cancellation.Token);
    }

    private async Task PollOutputAsync(
        long generation,
        CancellationToken cancellationToken)
    {
        var assembler = new MicroHostRpcAssembler();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                byte[]? wireReport;
                lock (_sync)
                {
                    if (
                        _disposed ||
                        generation != _connectionGeneration ||
                        _handle is not { IsInvalid: false, IsClosed: false })
                    {
                        return;
                    }

                    wireReport = TryReadOutputCore();
                }

                if (wireReport is null)
                {
                    await Task.Delay(12, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (wireReport[1] == MicroRpcCodec.DebugChannel)
                {
                    continue;
                }

                var json = assembler.Append(
                    wireReport,
                    DateTimeOffset.UtcNow);
                if (json is null)
                {
                    continue;
                }

                var response = _rpc.Handle(json);
                if (response.Count is 0 or > MaximumBatchReports)
                {
                    throw new InvalidDataException(
                        "Host RPC response exceeded the driver batch limit.");
                }

                lock (_sync)
                {
                    if (
                        _disposed ||
                        cancellationToken.IsCancellationRequested ||
                        generation != _connectionGeneration ||
                        _handle is not { IsInvalid: false, IsClosed: false })
                    {
                        return;
                    }

                    var result = SubmitCore(response);
                    if (result != MicroReportSendResult.Accepted)
                    {
                        FaultConnectionCore(generation);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (
                exception is IOException or
                    InvalidDataException or
                    InvalidOperationException or
                    Win32Exception or
                    JsonException)
            {
                lock (_sync)
                {
                    assembler.Reset();
                    FaultConnectionCore(generation);
                }

                return;
            }
            catch (Exception)
            {
                lock (_sync)
                {
                    assembler.Reset();
                    FaultConnectionCore(generation);
                }

                return;
            }
        }
    }

    private byte[]? TryReadOutputCore()
    {
        var output = new byte[96];
        if (!DeviceIoControl(
            RequireHandleCore(),
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
        var originalLength =
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(24));
        var flags =
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(28));
        if (
            sequence == 0 ||
            (_lastOutputSequence != 0 &&
             sequence != _lastOutputSequence + 1) ||
            flags is not (
                OutputFlagIncludedReportId or
                OutputFlagExcludedReportId) ||
            (flags == OutputFlagIncludedReportId &&
             originalLength != MicroRpcCodec.ReportLength) ||
            (flags == OutputFlagExcludedReportId &&
             originalLength is 0 or >= MicroRpcCodec.ReportLength))
        {
            throw new InvalidDataException(
                "Driver output metadata is inconsistent.");
        }

        var wire = new byte[MicroRpcCodec.ReportLength];
        Buffer.BlockCopy(output, 32, wire, 0, wire.Length);
        ValidateHostWireReport(wire);
        _lastOutputSequence = sequence;
        return wire;
    }

    private static void ValidateHostWireReport(ReadOnlySpan<byte> report)
    {
        if (
            report.Length != MicroRpcCodec.ReportLength ||
            report[0] != MicroRpcCodec.ReportId ||
            report[1] is not (
                MicroRpcCodec.DebugChannel or
                MicroRpcCodec.RpcChannel) ||
            report[2] > MicroRpcCodec.MaximumPayloadLength ||
            report[(3 + report[2])..].IndexOfAnyExcept((byte)0) >= 0)
        {
            throw new InvalidDataException(
                "Driver output wire report is invalid.");
        }
    }

    private SafeFileHandle RequireHandleCore() =>
        _handle is { IsInvalid: false, IsClosed: false } handle
            ? handle
            : throw new InvalidOperationException(
                "Codex Micro VHF driver is not connected.");

    private void MarkUnavailableCore()
    {
        DisposeHandleCore();
        _state = MicroTransportState.Unavailable;
        _retryAfter = Environment.TickCount64 + RetryBackoffMs;
    }

    private void FaultConnectionCore()
    {
        DisposeHandleCore();
        _state = MicroTransportState.Faulted;
        _retryAfter = Environment.TickCount64 + RetryBackoffMs;
    }

    private void FaultConnectionCore(long generation)
    {
        if (generation != _connectionGeneration)
        {
            return;
        }

        FaultConnectionCore();
    }

    private void DisposeHandleCore()
    {
        _handle?.Dispose();
        _handle = null;
        _lastOutputSequence = 0;
        _connectionGeneration++;
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
                // Lighting is observational; a UI subscriber must not stop
                // the device RPC loop.
            }
        }
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

internal static class MicroDeviceInterfaceEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorNoMoreItems = 259;
    private const int ErrorInsufficientBuffer = 122;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static string? FindFirst(Guid interfaceGuid)
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

            var detailProbe = SetupDiGetDeviceInterfaceDetail(
                set,
                ref data,
                IntPtr.Zero,
                0,
                out var requiredSize,
                IntPtr.Zero);
            var detailError = Marshal.GetLastWin32Error();
            var detailHeaderSize = IntPtr.Size == 8 ? 8u : 6u;
            if (
                detailProbe ||
                detailError != ErrorInsufficientBuffer ||
                requiredSize < detailHeaderSize)
            {
                throw new Win32Exception(
                    detailError,
                    "Unable to size the Micro device interface path.");
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
