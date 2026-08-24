using CodexController.Native;

namespace CodexController.Tests;

public sealed class ForegroundWindowActivatorTests
{
    private static readonly nint ForegroundHandle = new(10);
    private static readonly nint TargetHandle = new(20);

    [Fact]
    public void WorkerThreadJoinsInputQueuesBeforeTransferringFocus()
    {
        var api = new FakeForegroundWindowApi
        {
            Foreground = ForegroundHandle,
            PromoteOnSetForeground = true,
        };

        var activated = ForegroundWindowActivator.TryActivate(
            TargetHandle,
            processId: 42,
            api);

        Assert.True(activated);
        Assert.Equal(
            [
                "queue",
                "allow:42",
                "attach:100:200:True",
                "attach:100:300:True",
                "top:20",
                "foreground:20",
                "active:20",
                "focus:20",
                "attach:100:300:False",
                "attach:100:200:False",
            ],
            api.Calls);
    }

    [Fact]
    public void FailedForegroundTransferUsesExistingNonAltTabFallback()
    {
        var api = new FakeForegroundWindowApi
        {
            Foreground = ForegroundHandle,
            PromoteOnSwitch = true,
        };

        var activated = ForegroundWindowActivator.TryActivate(
            TargetHandle,
            processId: 42,
            api);

        Assert.True(activated);
        Assert.Contains("switch:20:False", api.Calls);
    }

    [Fact]
    public void MinimizedTargetIsRestoredWithSwRestore()
    {
        var api = new FakeForegroundWindowApi
        {
            Foreground = ForegroundHandle,
            IsMinimized = true,
            PromoteOnSetForeground = true,
        };

        var activated = ForegroundWindowActivator.TryActivate(
            TargetHandle,
            processId: 42,
            api);

        Assert.True(activated);
        Assert.Contains("show:20:9", api.Calls);
    }

    private sealed class FakeForegroundWindowApi : IForegroundWindowApi
    {
        internal List<string> Calls { get; } = [];

        internal nint Foreground { get; set; }

        internal bool IsMinimized { get; set; }

        internal bool PromoteOnSetForeground { get; init; }

        internal bool PromoteOnSwitch { get; init; }

        public void EnsureMessageQueue() =>
            Calls.Add("queue");

        public bool AllowSetForegroundWindow(uint processId)
        {
            Calls.Add($"allow:{processId}");
            return true;
        }

        public bool IsWindowVisible(nint windowHandle) =>
            windowHandle == TargetHandle;

        public bool IsIconic(nint windowHandle) =>
            windowHandle == TargetHandle && IsMinimized;

        public bool ShowWindow(nint windowHandle, int command)
        {
            Calls.Add($"show:{windowHandle}:{command}");
            IsMinimized = false;
            return true;
        }

        public nint GetForegroundWindow() => Foreground;

        public uint GetCurrentThreadId() => 100;

        public uint GetWindowThreadProcessId(
            nint windowHandle,
            out uint processId)
        {
            processId = windowHandle == TargetHandle ? 42u : 7u;
            return windowHandle == TargetHandle ? 300u : 200u;
        }

        public bool AttachThreadInput(
            uint attachThread,
            uint attachToThread,
            bool attach)
        {
            Calls.Add($"attach:{attachThread}:{attachToThread}:{attach}");
            return true;
        }

        public bool BringWindowToTop(nint windowHandle)
        {
            Calls.Add($"top:{windowHandle}");
            return true;
        }

        public bool SetForegroundWindow(nint windowHandle)
        {
            Calls.Add($"foreground:{windowHandle}");
            if (PromoteOnSetForeground)
            {
                Foreground = windowHandle;
            }

            return PromoteOnSetForeground;
        }

        public nint SetActiveWindow(nint windowHandle)
        {
            Calls.Add($"active:{windowHandle}");
            return windowHandle;
        }

        public nint SetFocus(nint windowHandle)
        {
            Calls.Add($"focus:{windowHandle}");
            return windowHandle;
        }

        public void SwitchToThisWindow(nint windowHandle, bool altTab)
        {
            Calls.Add($"switch:{windowHandle}:{altTab}");
            if (PromoteOnSwitch)
            {
                Foreground = windowHandle;
            }
        }
    }
}
