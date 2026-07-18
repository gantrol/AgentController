using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodexMicro.Desktop.Driver;

internal static class DeviceInterfaceEnumerator
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const int ErrorNoMoreItems = 259;

    public static string? FindFirst(Guid interfaceGuid) =>
        FindAll(interfaceGuid).FirstOrDefault();

    public static IReadOnlyList<string> FindAll(Guid interfaceGuid)
    {
        var paths = new List<string>();
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
            for (uint index = 0; ; index++)
            {
                var data = new SpDeviceInterfaceData
                {
                    Size = Marshal.SizeOf<SpDeviceInterfaceData>(),
                };
                if (!SetupDiEnumDeviceInterfaces(
                    set,
                    IntPtr.Zero,
                    ref interfaceGuid,
                    index,
                    ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                    {
                        break;
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
                var buffer = Marshal.AllocHGlobal(checked((int)requiredSize));
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W has cbSize 8 on x64
                    // and 6 on x86, while DevicePath begins at byte offset 4.
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(
                        set,
                        ref data,
                        buffer,
                        requiredSize,
                        out _,
                        IntPtr.Zero))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    var path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return paths;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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
