using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace CodexMicro.Desktop.Services;

public enum CodexRequestCardCancellationResult
{
    NotPresent,
    Cancelled,
    Blocked,
    Failed,
}

internal enum CodexRequestCardPresence
{
    NotPresent,
    Present,
    Blocked,
    Failed,
}

internal readonly record struct CodexRequestCardSnapshot(
    bool IsVisible,
    bool HasRequestCardClass,
    int DismissButtonCount,
    int SkipButtonCount,
    int RadioButtonCount,
    int EditCount)
{
    internal bool HasRequestControls =>
        DismissButtonCount > 0 &&
        SkipButtonCount > 0 &&
        (RadioButtonCount > 0 || EditCount > 0);
}

/// <summary>
/// Narrow compatibility shim for the Codex request-navigation card. The
/// official Micro bridge currently maps AG00 to Escape for menus/listboxes,
/// but not for this card. Detection is read-only; a verified card receives one
/// native Escape gesture and never an additional AG00 report.
/// </summary>
public static class CodexRequestCardCancellation
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const ushort EscapeKey = 0x1B;
    private const string RequestCardClassMarker = "@container/request-card";

    internal static CodexRequestCardPresence InspectForegroundRequestCard(
        string? packageRoot = null)
    {
        var inspection = Inspect(packageRoot);
        return inspection.Presence;
    }

    public static CodexRequestCardCancellationResult
        TryCancelForegroundRequestCard(string? packageRoot = null)
    {
        var inspection = Inspect(packageRoot);
        switch (inspection.Presence)
        {
            case CodexRequestCardPresence.NotPresent:
                return CodexRequestCardCancellationResult.NotPresent;
            case CodexRequestCardPresence.Blocked:
                return CodexRequestCardCancellationResult.Blocked;
            case CodexRequestCardPresence.Failed:
                return CodexRequestCardCancellationResult.Failed;
        }

        try
        {
            if (inspection.RequestCard is null ||
                inspection.RequestCard.Current.IsOffscreen ||
                !(inspection.RequestCard.Current.ClassName ?? string.Empty)
                    .Contains(
                        RequestCardClassMarker,
                        StringComparison.Ordinal))
            {
                return CodexRequestCardCancellationResult.Failed;
            }
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
                InvalidOperationException or
                COMException)
        {
            return CodexRequestCardCancellationResult.Failed;
        }

        // The accessibility walk can take long enough for focus to change.
        // Revalidate both HWND and PID immediately before input injection.
        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero ||
            foreground != inspection.ForegroundWindow)
        {
            return CodexRequestCardCancellationResult.Failed;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0 || processId != inspection.ProcessId)
        {
            return CodexRequestCardCancellationResult.Failed;
        }

        var inputs = new[]
        {
            CreateKeyInput(EscapeKey, keyUp: false),
            CreateKeyInput(EscapeKey, keyUp: true),
        };
        return SendInput(
                   (uint)inputs.Length,
                   inputs,
                   Marshal.SizeOf<NativeInput>()) == inputs.Length
            ? CodexRequestCardCancellationResult.Cancelled
            : CodexRequestCardCancellationResult.Failed;
    }

    internal static CodexRequestCardPresence Classify(
        IReadOnlyList<CodexRequestCardSnapshot> candidates)
    {
        var verified = candidates.Count(candidate =>
            candidate.IsVisible &&
            candidate.HasRequestCardClass &&
            candidate.HasRequestControls);
        var marked = candidates.Count(candidate =>
            candidate.IsVisible && candidate.HasRequestCardClass);
        if (verified == 1 && marked == 1)
        {
            return CodexRequestCardPresence.Present;
        }

        if (verified > 1 || marked > 1 || candidates.Any(candidate =>
                candidate.IsVisible &&
                ((candidate.HasRequestCardClass &&
                  !candidate.HasRequestControls) ||
                 (!candidate.HasRequestCardClass &&
                  candidate.HasRequestControls))))
        {
            return CodexRequestCardPresence.Blocked;
        }

        return CodexRequestCardPresence.NotPresent;
    }

    private static ForegroundInspection Inspect(string? packageRoot)
    {
        var foreground = GetForegroundWindow();
        if (foreground == nint.Zero)
        {
            return new(
                CodexRequestCardPresence.Failed,
                nint.Zero,
                0);
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0 || !IsCodexProcess(processId, packageRoot))
        {
            return new(
                CodexRequestCardPresence.Failed,
                foreground,
                processId);
        }

        try
        {
            var window = AutomationElement.FromHandle(foreground);
            if (window is null)
            {
                return new(
                    CodexRequestCardPresence.Failed,
                    foreground,
                    processId);
            }

            var groups = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Group));
            var snapshots = new List<CodexRequestCardSnapshot>(groups.Count);
            AutomationElement? verifiedRequestCard = null;
            for (var index = 0; index < groups.Count; index++)
            {
                var group = groups[index];
                var className = group.Current.ClassName ?? string.Empty;
                var descendants = group.FindAll(
                    TreeScope.Descendants,
                    new OrCondition(
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.Button),
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.RadioButton),
                        new PropertyCondition(
                            AutomationElement.ControlTypeProperty,
                            ControlType.Edit)));

                var dismissCount = 0;
                var skipCount = 0;
                var radioCount = 0;
                var editCount = 0;
                for (var childIndex = 0;
                     childIndex < descendants.Count;
                     childIndex++)
                {
                    var child = descendants[childIndex];
                    var type = child.Current.ControlType;
                    var name = child.Current.Name ?? string.Empty;
                    if (type == ControlType.Button &&
                        name.Equals("Dismiss", StringComparison.OrdinalIgnoreCase))
                    {
                        dismissCount++;
                    }
                    else if (type == ControlType.Button &&
                             name.Equals("Skip", StringComparison.OrdinalIgnoreCase))
                    {
                        skipCount++;
                    }
                    else if (type == ControlType.RadioButton)
                    {
                        radioCount++;
                    }
                    else if (type == ControlType.Edit)
                    {
                        editCount++;
                    }
                }

                var rectangle = group.Current.BoundingRectangle;
                var snapshot = new CodexRequestCardSnapshot(
                    !group.Current.IsOffscreen &&
                    rectangle != Rect.Empty &&
                    rectangle.Width > 0 &&
                    rectangle.Height > 0,
                    className.Contains(
                        RequestCardClassMarker,
                        StringComparison.Ordinal),
                    dismissCount,
                    skipCount,
                    radioCount,
                    editCount);
                snapshots.Add(snapshot);
                if (snapshot.IsVisible &&
                    snapshot.HasRequestCardClass &&
                    snapshot.HasRequestControls)
                {
                    verifiedRequestCard = verifiedRequestCard is null
                        ? group
                        : null;
                }
            }

            var presence = Classify(snapshots);
            return new(
                presence,
                foreground,
                processId,
                presence == CodexRequestCardPresence.Present
                    ? verifiedRequestCard
                    : null);
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
                ArgumentException or
                InvalidOperationException or
                COMException or
                Win32Exception or
                UnauthorizedAccessException or
                System.Security.SecurityException)
        {
            return new(
                CodexRequestCardPresence.Failed,
                foreground,
                processId);
        }
    }

    private static bool IsCodexProcess(uint processId, string? packageRoot)
    {
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var path = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(path) &&
                ((!string.IsNullOrWhiteSpace(packageRoot) &&
                  path.StartsWith(
                      packageRoot,
                      StringComparison.OrdinalIgnoreCase)) ||
                 path.Contains(
                     @"\WindowsApps\OpenAI.Codex_",
                     StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                InvalidOperationException or
                Win32Exception)
        {
            return false;
        }
    }

    private static NativeInput CreateKeyInput(ushort key, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = key,
                Flags = keyUp ? KeyEventKeyUp : 0,
            },
        },
    };

    private readonly record struct ForegroundInspection(
        CodexRequestCardPresence Presence,
        nint ForegroundWindow,
        uint ProcessId,
        AutomationElement? RequestCard = null);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;

        // Preserve the native INPUT union size on x64.
        [FieldOffset(0)]
        public NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        nint window,
        out uint processId);

    [DllImport("user32.dll")]
    private static extern uint SendInput(
        uint count,
        [In] NativeInput[] inputs,
        int size);
}
