using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CodexMicro.Protocol;
using Microsoft.Win32.SafeHandles;

namespace CodexMicro.Desktop.Driver;

internal sealed class UmdfMicroDriverClient : IDisposable
{
    private const byte InjectFeatureId = 0xF0;
    private const byte OutputFeatureId = 0xF1;
    private const byte InfoFeatureId = 0xF2;
    private const int FeatureReportLength = 66;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;

    private readonly object _sync = new();
    private SafeFileHandle? _handle;
    private ulong _outputSequence;

    public bool IsConnected => _handle is { IsInvalid: false, IsClosed: false };

    public DriverInfo Connect()
    {
        lock (_sync)
        {
            DisposeHandle();
            HidD_GetHidGuid(out var hidGuid);
            foreach (var path in DeviceInterfaceEnumerator.FindAll(hidGuid))
            {
                var handle = CreateFile(
                    path,
                    GenericRead | GenericWrite,
                    ShareRead | ShareWrite,
                    IntPtr.Zero,
                    OpenExisting,
                    0,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    handle.Dispose();
                    continue;
                }

                var attributes = new HiddAttributes
                {
                    Size = Marshal.SizeOf<HiddAttributes>(),
                };
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    attributes.VendorId != MicroProtocol.VendorId ||
                    attributes.ProductId != MicroProtocol.ProductId)
                {
                    handle.Dispose();
                    continue;
                }

                var feature = new byte[FeatureReportLength];
                feature[0] = InfoFeatureId;
                if (!HidD_GetFeature(handle, feature, feature.Length) ||
                    Encoding.ASCII.GetString(feature, 1, 8) != "CMHIDUM2")
                {
                    handle.Dispose();
                    continue;
                }

                _handle = handle;
                _outputSequence = 0;
                return new DriverInfo(
                    BitConverter.ToUInt64(feature, 16),
                    0,
                    0,
                    BitConverter.ToUInt32(feature, 28),
                    1,
                    "UMDF2 HID");
            }

            throw new InvalidOperationException(
                "Codex Micro UMDF2 HID interface is not present.");
        }
    }

    public MicroSendResult Submit(IReadOnlyList<byte[]> reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        if (reports.Count is 0 or > DriverContract.MaximumBatchReports)
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
            var accepted = 0;
            foreach (var report in reports)
            {
                var feature = new byte[FeatureReportLength];
                feature[0] = InjectFeatureId;
                feature[1] = 1;
                Buffer.BlockCopy(
                    report,
                    0,
                    feature,
                    2,
                    DriverContract.WireReportLength);
                if (!HidD_SetFeature(RequireHandle(), feature, feature.Length))
                {
                    var error = Marshal.GetLastWin32Error();
                    return new MicroSendResult(
                        accepted == 0
                            ? MicroSendDisposition.NotSent
                            : MicroSendDisposition.OutcomeUnknown,
                        accepted,
                        reports.Count,
                        error,
                        accepted == 0
                            ? "The UMDF2 HID rejected the batch before delivery."
                            : "Only part of the batch reached the UMDF2 HID.");
                }

                accepted++;
            }

            return new MicroSendResult(
                MicroSendDisposition.Accepted,
                accepted,
                reports.Count,
                0,
                "All reports reached the Codex Micro UMDF2 HID.");
        }
    }

    public DriverOutputReport? TryReadOutput()
    {
        lock (_sync)
        {
            var feature = new byte[FeatureReportLength];
            feature[0] = OutputFeatureId;
            if (!HidD_GetFeature(RequireHandle(), feature, feature.Length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (feature[1] == 0)
            {
                return null;
            }

            var wire = new byte[DriverContract.WireReportLength];
            Buffer.BlockCopy(feature, 2, wire, 0, wire.Length);
            return new DriverOutputReport(
                ++_outputSequence,
                checked((ulong)Stopwatch.GetTimestamp()),
                DriverContract.WireReportLength,
                1,
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

    private SafeFileHandle RequireHandle() =>
        _handle is { IsInvalid: false, IsClosed: false } handle
            ? handle
            : throw new InvalidOperationException(
                "Codex Micro UMDF2 HID is not connected.");

    private void DisposeHandle()
    {
        _handle?.Dispose();
        _handle = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(
        SafeFileHandle hidDeviceObject,
        ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
