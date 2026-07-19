using System.Windows.Automation;
using CodexController.Agents;
using CodexController.Native;
using CodexController.Services.Micro;
using static CodexController.Services.CodexAutomationLocator;

namespace CodexController.Services;

internal enum CurrentControlAction
{
    None,
    EncoderPress,
    NativeLeft,
    NativeRight,
    Escape,
}

internal static class CurrentControlActionPolicy
{
    internal static CurrentControlAction Resolve(
        CodexMicroReadback readback,
        ComposerDialNavigation navigation)
    {
        if (navigation is not (
                ComposerDialNavigation.Left or
                ComposerDialNavigation.Right))
        {
            return CurrentControlAction.None;
        }

        if (
            navigation == ComposerDialNavigation.Left &&
            readback.IsMenuOpen)
        {
            return CurrentControlAction.Escape;
        }

        if (!readback.SelectionVerified)
        {
            return CurrentControlAction.None;
        }

        if (readback.Surface == CodexMicroSurfaceKind.Dialog)
        {
            return CurrentControlAction.None;
        }

        if (readback.IsAdjustable)
        {
            return navigation == ComposerDialNavigation.Left
                ? CurrentControlAction.NativeLeft
                : CurrentControlAction.NativeRight;
        }

        if (
            navigation == ComposerDialNavigation.Right &&
            readback.CanExpand)
        {
            return CurrentControlAction.EncoderPress;
        }

        if (readback.Surface == CodexMicroSurfaceKind.Composer)
        {
            return navigation == ComposerDialNavigation.Left
                ? CurrentControlAction.NativeLeft
                : CurrentControlAction.NativeRight;
        }

        return CurrentControlAction.None;
    }
}

/// <summary>
/// Executes the gamepad-only horizontal axis against the last verified
/// official-Micro selection. UI Automation is used only to verify that the
/// focused target is still the same control and to read back range changes.
/// </summary>
internal sealed class CodexCurrentControlExecutor
{
    private const int ValueReadbackTimeoutMs = 180;
    private const int ValueReadbackPollMs = 18;

    private readonly MicroInputService _microInput;
    private readonly Func<ushort, bool> _sendKey;

    internal CodexCurrentControlExecutor(
        MicroInputService microInput,
        Func<ushort, bool>? sendKey = null)
    {
        _microInput = microInput ??
            throw new ArgumentNullException(nameof(microInput));
        _sendKey = sendKey ?? Win32Input.SendKey;
    }

    internal ComposerDialResult Execute(
        CodexMicroReadback readback,
        ComposerDialNavigation navigation)
    {
        var action = CurrentControlActionPolicy.Resolve(
            readback,
            navigation);
        if (action == CurrentControlAction.None)
        {
            return Failure(
                readback,
                "dial-current-control-unverified");
        }

        if (action == CurrentControlAction.EncoderPress)
        {
            var micro = _microInput.SendEncoderPress();
            if (micro is
                MicroReportSendResult.Accepted or
                MicroReportSendResult.OutcomeUnknown)
            {
                return new(
                    true,
                    readback.ItemName,
                    IsMenuOpen: readback.IsMenuOpen,
                    MenuWasPresent: readback.IsMenuOpen,
                    StateVerified: false);
            }

            if (micro == MicroReportSendResult.Rejected)
            {
                return new(
                    false,
                    readback.ItemName,
                    IsMenuOpen: readback.IsMenuOpen,
                    Error: AgentAutomationErrorCodes.InputInjectionFailed,
                    ErrorDetail: "micro.encoder-rejected",
                    MenuWasPresent: readback.IsMenuOpen);
            }

            // Right-arrow fallback is allowed only after confirmed NotSent.
            action = CurrentControlAction.NativeRight;
        }

        return ExecuteNative(readback, action);
    }

    private ComposerDialResult ExecuteNative(
        CodexMicroReadback readback,
        CurrentControlAction action)
    {
        try
        {
            var context = FindCodexWindow();
            if (
                context is null ||
                !Win32Input.IsProcessForeground(
                    context.Value.ProcessId))
            {
                return Failure(
                    readback,
                    "dial-current-control-focus");
            }

            AutomationElement? focused = null;
            double? beforeValue = null;
            if (action != CurrentControlAction.Escape)
            {
                focused = AutomationElement.FocusedElement;
                if (!MatchesExpectedFocus(
                        focused,
                        context.Value.ProcessId,
                        readback.ItemName))
                {
                    return Failure(
                        readback,
                        "dial-current-control-focus");
                }

                beforeValue = TryReadRangeValue(focused!);
            }

            var key = action switch
            {
                CurrentControlAction.NativeLeft =>
                    ComposerDialNativeInputPolicy.LeftKey,
                CurrentControlAction.NativeRight =>
                    ComposerDialNativeInputPolicy.RightKey,
                CurrentControlAction.Escape =>
                    ComposerDialNativeInputPolicy.EscapeKey,
                _ => (ushort)0,
            };
            if (key == 0 || !_sendKey(key))
            {
                return Failure(
                    readback,
                    "dial-native-input");
            }

            if (beforeValue is { } before)
            {
                var expectedDirection =
                    action == CurrentControlAction.NativeLeft ? -1 : 1;
                if (!WaitForRangeChange(
                        focused!,
                        before,
                        expectedDirection))
                {
                    return Failure(
                        readback,
                        "dial-current-control-no-change");
                }

                return new(
                    true,
                    readback.ItemName,
                    IsMenuOpen: readback.IsMenuOpen,
                    MenuWasPresent: readback.IsMenuOpen,
                    StateVerified: true);
            }

            return new(
                true,
                readback.ItemName,
                IsMenuOpen: readback.IsMenuOpen,
                MenuWasPresent: readback.IsMenuOpen,
                StateVerified: false);
        }
        catch (ElementNotAvailableException)
        {
            return Failure(
                readback,
                "dial-current-control-focus");
        }
        catch (InvalidOperationException)
        {
            return Failure(
                readback,
                "dial-current-control-focus");
        }
    }

    private static bool MatchesExpectedFocus(
        AutomationElement? focused,
        int processId,
        string expectedName)
    {
        if (
            focused is null ||
            string.IsNullOrWhiteSpace(expectedName))
        {
            return false;
        }

        return
            focused.Current.ProcessId == processId &&
            focused.Current.HasKeyboardFocus &&
            string.Equals(
                ComposerChoiceNormalizer.Normalize(
                    focused.Current.Name),
                ComposerChoiceNormalizer.Normalize(expectedName),
                StringComparison.Ordinal);
    }

    private static double? TryReadRangeValue(
        AutomationElement element)
    {
        if (
            element.TryGetCurrentPattern(
                RangeValuePattern.Pattern,
                out var patternObject) &&
            patternObject is RangeValuePattern pattern)
        {
            return pattern.Current.Value;
        }

        return null;
    }

    private static bool WaitForRangeChange(
        AutomationElement element,
        double before,
        int expectedDirection)
    {
        var deadline =
            Environment.TickCount64 + ValueReadbackTimeoutMs;
        do
        {
            var current = TryReadRangeValue(element);
            if (
                current is { } value &&
                Math.Sign(value - before) == expectedDirection)
            {
                return true;
            }

            Thread.Sleep(ValueReadbackPollMs);
        }
        while (Environment.TickCount64 < deadline);

        return false;
    }

    private static ComposerDialResult Failure(
        CodexMicroReadback readback,
        string detail) =>
        new(
            false,
            readback.ItemName,
            IsMenuOpen: readback.IsMenuOpen,
            Error: AgentAutomationErrorCodes.ElementUnsupported,
            ErrorDetail: detail,
            MenuWasPresent: readback.IsMenuOpen);
}
