using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using CodexMicro.Protocol;
using Microsoft.Win32.SafeHandles;

namespace CodexMicro.Desktop.Driver;

internal sealed class VhfMicroDriverClient : IDisposable
{
    private const int ErrorNoMoreItems = 259;
    private readonly object _sync = new();
    private SafeFileHandle? _handle;
    private ulong _nextSequence;

    public bool IsConnected => _handle is { IsInvalid: false, IsClosed: false };

    public DriverInfo Connect()
    {
        lock (_sync)
        {
            DisposeHandle();
            var path = DeviceInterfaceEnumerator.FindFirst(
                DriverContract.DeviceInterfaceGuid);
            if (path is null)
            {
                throw new InvalidOperationException(
                    "CodexMicroVhf device interface is not present. " +
                    "The driver must be installed manually first.");
            }

            _handle = CreateFile(
                path,
                0xC0000000,
                0,
                IntPtr.Zero,
                3,
                0,
                IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                DisposeHandle();
                throw new Win32Exception(error);
            }

            var info = GetInfoCore();
            _nextSequence = info.LastBatchSequence;
            return info;
        }
    }

    public MicroSendResult Submit(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (
            reports.Count == 0 ||
            reports.Count > DriverContract.MaximumBatchReports)
        {
            return MicroSendResult.NotSent(
                "Batch report count is outside the bounded driver schema.");
        }

        foreach (var report in reports)
        {
            MicroRpcCodec.ValidateWireReport(report);
        }

        lock (_sync)
        {
            var handle = RequireHandle();
            var sequence = ++_nextSequence;
            var input = new byte[16 + reports.Count * DriverContract.WireReportLength];
            DriverContract.WriteHeader(
                input,
                checked((ushort)reports.Count),
                sequence);
            for (var index = 0; index < reports.Count; index++)
            {
                Buffer.BlockCopy(
                    reports[index],
                    0,
                    input,
                    16 + index * DriverContract.WireReportLength,
                    DriverContract.WireReportLength);
            }

            var output = new byte[32];
            if (!DeviceIoControl(
                handle,
                DriverContract.IoctlSubmitInput,
                input,
                input.Length,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                return new MicroSendResult(
                    MicroSendDisposition.OutcomeUnknown,
                    0,
                    reports.Count,
                    error,
                    "Driver IOCTL completion is unknown; the batch is never retried automatically.");
            }

            if (returned < 32 ||
                BinaryPrimitives.ReadUInt32LittleEndian(output) != DriverContract.Magic)
            {
                return new MicroSendResult(
                    MicroSendDisposition.OutcomeUnknown,
                    0,
                    reports.Count,
                    0,
                    "Driver returned a malformed submit result.");
            }

            var resultSequence =
                BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8));
            var disposition =
                BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(16));
            var accepted = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(20)));
            var nativeStatus =
                BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(24));

            if (resultSequence != sequence)
            {
                return new MicroSendResult(
                    MicroSendDisposition.OutcomeUnknown,
                    accepted,
                    reports.Count,
                    nativeStatus,
                    "Driver result sequence did not match the submitted batch.");
            }

            return disposition switch
            {
                1 => new MicroSendResult(
                    MicroSendDisposition.Accepted,
                    accepted,
                    reports.Count,
                    nativeStatus,
                    "All reports were accepted by VHF."),
                3 when accepted == reports.Count => new MicroSendResult(
                    MicroSendDisposition.Accepted,
                    accepted,
                    reports.Count,
                    nativeStatus,
                    "The driver recognized an already accepted sequence."),
                0 => new MicroSendResult(
                    MicroSendDisposition.NotSent,
                    accepted,
                    reports.Count,
                    nativeStatus,
                    "No report reached VHF."),
                4 => new MicroSendResult(
                    MicroSendDisposition.Rejected,
                    accepted,
                    reports.Count,
                    nativeStatus,
                    "The driver rejected a stale or invalid batch."),
                _ => new MicroSendResult(
                    MicroSendDisposition.OutcomeUnknown,
                    accepted,
                    reports.Count,
                    nativeStatus,
                    "Only part of the batch was accepted; do not retry automatically."),
            };
        }
    }

    public MicroSendResult TapKeyboardKey(VhfKeyboardKey key, bool shift)
    {
        lock (_sync)
        {
            var sequence = ++_nextSequence;
            var input = new byte[24];
            DriverContract.WriteKeyboardInput(input, key, shift, sequence);
            var output = new byte[32];
            if (!DeviceIoControl(
                RequireHandle(),
                DriverContract.IoctlSubmitKeyboard,
                input,
                input.Length,
                output,
                output.Length,
                out var returned,
                IntPtr.Zero))
            {
                return new MicroSendResult(
                    MicroSendDisposition.OutcomeUnknown,
                    0,
                    2,
                    Marshal.GetLastWin32Error(),
                    "Driver keyboard completion is unknown; the key is never retried automatically.");
            }

            if (returned < 32 ||
                BinaryPrimitives.ReadUInt32LittleEndian(output) != DriverContract.Magic ||
                BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8)) != sequence)
            {
                return new MicroSendResult(
                    MicroSendDisposition.OutcomeUnknown,
                    0,
                    2,
                    0,
                    "Driver returned a malformed keyboard result.");
            }

            var disposition =
                BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(16));
            var accepted = checked((int)
                BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(20)));
            var nativeStatus =
                BinaryPrimitives.ReadInt32LittleEndian(output.AsSpan(24));
            return disposition switch
            {
                1 => new MicroSendResult(
                    MicroSendDisposition.Accepted,
                    accepted,
                    2,
                    nativeStatus,
                    "The restricted VHF keyboard tap was accepted."),
                0 => new MicroSendResult(
                    MicroSendDisposition.NotSent,
                    accepted,
                    2,
                    nativeStatus,
                    "The restricted VHF keyboard tap did not reach VHF."),
                4 => new MicroSendResult(
                    MicroSendDisposition.Rejected,
                    accepted,
                    2,
                    nativeStatus,
                    "The restricted VHF keyboard tap was rejected."),
                _ => new MicroSendResult(
                    MicroSendDisposition.OutcomeUnknown,
                    accepted,
                    2,
                    nativeStatus,
                    "Only part of the restricted VHF keyboard tap was accepted."),
            };
        }
    }

    public DriverOutputReport? TryReadOutput()
    {
        lock (_sync)
        {
            var output = new byte[96];
            if (!DeviceIoControl(
                RequireHandle(),
                DriverContract.IoctlReadOutput,
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
                returned < 96 ||
                BinaryPrimitives.ReadUInt32LittleEndian(output) != DriverContract.Magic)
            {
                throw new InvalidDataException(
                    "Driver output record did not match protocol v1.");
            }

            var wire = new byte[DriverContract.WireReportLength];
            Buffer.BlockCopy(output, 32, wire, 0, wire.Length);
            return new DriverOutputReport(
                BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8)),
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

    private DriverInfo GetInfoCore()
    {
        var output = new byte[40];
        if (!DeviceIoControl(
            RequireHandle(),
            DriverContract.IoctlGetInfo,
            null,
            0,
            output,
            output.Length,
            out var returned,
            IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (
            returned < 40 ||
            BinaryPrimitives.ReadUInt32LittleEndian(output) != DriverContract.Magic ||
            BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(4)) !=
                DriverContract.Version)
        {
            throw new InvalidDataException(
                "Installed driver does not implement Virtual Micro protocol v1.");
        }

        return new DriverInfo(
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(8)),
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(16)),
            BinaryPrimitives.ReadUInt64LittleEndian(output.AsSpan(24)),
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(32)),
            BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(36)),
            "VHF");
    }

    private SafeFileHandle RequireHandle() =>
        _handle is { IsInvalid: false, IsClosed: false } handle
            ? handle
            : throw new InvalidOperationException("VHF driver is not connected.");

    private void DisposeHandle()
    {
        _handle?.Dispose();
        _handle = null;
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
