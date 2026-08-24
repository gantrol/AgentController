using System.Runtime.InteropServices;

namespace CodexController.Native;

/// <summary>
/// Raises a known top-level window even when activation is requested from a
/// worker thread. Windows normally prevents a background process from taking
/// the foreground, so temporarily join the relevant input queues before
/// transferring activation and keyboard focus.
/// </summary>
internal static class ForegroundWindowActivator
{
    private const int SwRestore = 9;

    internal static bool TryActivate(nint windowHandle, uint processId) =>
        TryActivate(windowHandle, processId, Win32Api.Instance);

    internal static bool TryActivate(
        nint windowHandle,
        uint processId,
        IForegroundWindowApi api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (windowHandle == nint.Zero || !api.IsWindowVisible(windowHandle))
        {
            return false;
        }

        if (api.GetForegroundWindow() == windowHandle &&
            !api.IsIconic(windowHandle))
        {
            return true;
        }

        // A Task.Run callback may not own a Win32 message queue yet.
        api.EnsureMessageQueue();
        _ = api.AllowSetForegroundWindow(processId);
        if (api.IsIconic(windowHandle))
        {
            _ = api.ShowWindow(windowHandle, SwRestore);
        }

        var currentThread = api.GetCurrentThreadId();
        var foreground = api.GetForegroundWindow();
        var foregroundThread = foreground == nint.Zero
            ? 0
            : api.GetWindowThreadProcessId(foreground, out _);
        var targetThread = api.GetWindowThreadProcessId(
            windowHandle,
            out _);
        if (targetThread == 0)
        {
            return false;
        }

        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            attachedForeground = AttachIfNeeded(
                api,
                currentThread,
                foregroundThread);
            attachedTarget = targetThread != foregroundThread &&
                AttachIfNeeded(api, currentThread, targetThread);
            _ = api.BringWindowToTop(windowHandle);
            _ = api.SetForegroundWindow(windowHandle);
            _ = api.SetActiveWindow(windowHandle);
            _ = api.SetFocus(windowHandle);
        }
        finally
        {
            DetachIfNeeded(
                api,
                currentThread,
                targetThread,
                attachedTarget);
            DetachIfNeeded(
                api,
                currentThread,
                foregroundThread,
                attachedForeground);
        }

        if (api.GetForegroundWindow() != windowHandle)
        {
            // Preserve maximize/fullscreen state while asking Windows for the
            // same final fallback used by the established Codex activator.
            api.SwitchToThisWindow(windowHandle, altTab: false);
        }

        return api.IsWindowVisible(windowHandle) &&
            !api.IsIconic(windowHandle) &&
            api.GetForegroundWindow() == windowHandle;
    }

    private static bool AttachIfNeeded(
        IForegroundWindowApi api,
        uint currentThread,
        uint otherThread) =>
        otherThread != 0 &&
        otherThread != currentThread &&
        api.AttachThreadInput(currentThread, otherThread, attach: true);

    private static void DetachIfNeeded(
        IForegroundWindowApi api,
        uint currentThread,
        uint otherThread,
        bool attached)
    {
        if (attached)
        {
            _ = api.AttachThreadInput(
                currentThread,
                otherThread,
                attach: false);
        }
    }

    private sealed class Win32Api : IForegroundWindowApi
    {
        internal static Win32Api Instance { get; } = new();

        private Win32Api()
        {
        }

        public void EnsureMessageQueue() =>
            _ = PeekMessage(
                out _,
                nint.Zero,
                0,
                0,
                0);

        public bool AllowSetForegroundWindow(uint processId) =>
            NativeAllowSetForegroundWindow(processId);

        public bool IsWindowVisible(nint windowHandle) =>
            NativeIsWindowVisible(windowHandle);

        public bool IsIconic(nint windowHandle) =>
            NativeIsIconic(windowHandle);

        public bool ShowWindow(nint windowHandle, int command) =>
            NativeShowWindow(windowHandle, command);

        public nint GetForegroundWindow() =>
            NativeGetForegroundWindow();

        public uint GetCurrentThreadId() =>
            NativeGetCurrentThreadId();

        public uint GetWindowThreadProcessId(
            nint windowHandle,
            out uint processId) =>
            NativeGetWindowThreadProcessId(windowHandle, out processId);

        public bool AttachThreadInput(
            uint attachThread,
            uint attachToThread,
            bool attach) =>
            NativeAttachThreadInput(
                attachThread,
                attachToThread,
                attach);

        public bool BringWindowToTop(nint windowHandle) =>
            NativeBringWindowToTop(windowHandle);

        public bool SetForegroundWindow(nint windowHandle) =>
            NativeSetForegroundWindow(windowHandle);

        public nint SetActiveWindow(nint windowHandle) =>
            NativeSetActiveWindow(windowHandle);

        public nint SetFocus(nint windowHandle) =>
            NativeSetFocus(windowHandle);

        public void SwitchToThisWindow(nint windowHandle, bool altTab) =>
            NativeSwitchToThisWindow(windowHandle, altTab);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public nint Window;
            public uint Message;
            public nuint WordParameter;
            public nint LongParameter;
            public uint Time;
            public NativePoint Point;
            public uint Private;
        }

        [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeIsWindowVisible(
            nint windowHandle);

        [DllImport("user32.dll", EntryPoint = "IsIconic")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeIsIconic(nint windowHandle);

        [DllImport("user32.dll", EntryPoint = "ShowWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeShowWindow(
            nint windowHandle,
            int command);

        [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
        private static extern nint NativeGetForegroundWindow();

        [DllImport("kernel32.dll", EntryPoint = "GetCurrentThreadId")]
        private static extern uint NativeGetCurrentThreadId();

        [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
        private static extern uint NativeGetWindowThreadProcessId(
            nint windowHandle,
            out uint processId);

        [DllImport("user32.dll", EntryPoint = "AttachThreadInput")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeAttachThreadInput(
            uint attachThread,
            uint attachToThread,
            [MarshalAs(UnmanagedType.Bool)] bool attach);

        [DllImport("user32.dll", EntryPoint = "BringWindowToTop")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeBringWindowToTop(
            nint windowHandle);

        [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeSetForegroundWindow(
            nint windowHandle);

        [DllImport("user32.dll", EntryPoint = "SetActiveWindow")]
        private static extern nint NativeSetActiveWindow(
            nint windowHandle);

        [DllImport("user32.dll", EntryPoint = "SetFocus")]
        private static extern nint NativeSetFocus(nint windowHandle);

        [DllImport("user32.dll", EntryPoint = "SwitchToThisWindow")]
        private static extern void NativeSwitchToThisWindow(
            nint windowHandle,
            [MarshalAs(UnmanagedType.Bool)] bool altTab);

        [DllImport("user32.dll", EntryPoint = "AllowSetForegroundWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool NativeAllowSetForegroundWindow(
            uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(
            out NativeMessage message,
            nint windowHandle,
            uint minimumMessage,
            uint maximumMessage,
            uint removeMessage);
    }
}

internal interface IForegroundWindowApi
{
    void EnsureMessageQueue();

    bool AllowSetForegroundWindow(uint processId);

    bool IsWindowVisible(nint windowHandle);

    bool IsIconic(nint windowHandle);

    bool ShowWindow(nint windowHandle, int command);

    nint GetForegroundWindow();

    uint GetCurrentThreadId();

    uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    bool AttachThreadInput(
        uint attachThread,
        uint attachToThread,
        bool attach);

    bool BringWindowToTop(nint windowHandle);

    bool SetForegroundWindow(nint windowHandle);

    nint SetActiveWindow(nint windowHandle);

    nint SetFocus(nint windowHandle);

    void SwitchToThisWindow(nint windowHandle, bool altTab);
}
