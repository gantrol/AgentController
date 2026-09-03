using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace CodexMicro.Desktop.Services;

internal static class WindowsProcessImage
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int ErrorInsufficientBuffer = 122;
    private const int InitialPathCapacity = 512;
    private const int MaximumPathCapacity = 32_768;

    internal static bool TryGetPath(uint processId, out string path)
    {
        path = string.Empty;
        if (processId == 0)
        {
            return false;
        }

        using var process = OpenProcess(
            ProcessQueryLimitedInformation,
            inheritHandle: false,
            processId);
        if (process.IsInvalid)
        {
            return false;
        }

        if (TryQueryPath(process, InitialPathCapacity, out path))
        {
            return true;
        }

        return Marshal.GetLastWin32Error() == ErrorInsufficientBuffer &&
            TryQueryPath(process, MaximumPathCapacity, out path);
    }

    internal static bool TryGetFileNameWithoutExtension(
        uint processId,
        out string fileName)
    {
        fileName = string.Empty;
        if (!TryGetPath(processId, out var path))
        {
            return false;
        }

        var start = Math.Max(
                path.LastIndexOf('\\'),
                path.LastIndexOf('/')) + 1;
        var end = path.LastIndexOf('.');
        if (end <= start)
        {
            end = path.Length;
        }

        if (start >= end)
        {
            return false;
        }

        fileName = path[start..end];
        return true;
    }

    private static bool TryQueryPath(
        SafeProcessHandle process,
        int capacity,
        out string path)
    {
        var buffer = new StringBuilder(capacity);
        var length = buffer.Capacity;
        if (!QueryFullProcessImageName(
                process,
                flags: 0,
                buffer,
                ref length) ||
            length <= 0)
        {
            path = string.Empty;
            return false;
        }

        path = buffer.ToString(0, length);
        return path.Length > 0;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process,
        uint flags,
        StringBuilder executableName,
        ref int size);
}
