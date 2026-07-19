using System.Windows.Automation;
using CodexController.Agents;
using CodexController.Models;
using CodexController.Native;
using static CodexController.Services.CodexAutomationLocator;
using static CodexController.Services.CodexComposerStateVerifier;

namespace CodexController.Services;

internal sealed class CodexComposerAutomationExecutor
{
    private readonly Func<string, bool> _sendShortcut;

    internal CodexComposerAutomationExecutor(
        Func<string, bool> sendShortcut)
    {
        _sendShortcut = sendShortcut ??
            throw new ArgumentNullException(nameof(sendShortcut));
    }

    internal Task<ComposerAutomationResult> ScrollConversationAsync(
        ConversationBoundary boundary,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => ScrollConversationCore(
                boundary,
                settings,
                cancellationToken),
            cancellationToken);
    }

    internal string? TryReadDispatchButtonName()
    {
        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return null;
            }

            return FindVisibleNamedButton(
                    context.Value.Window,
                    [
                        "Steer",
                        "Steer current turn",
                        "Queue",
                        "Queue next turn",
                        "Send",
                        "Send message",
                        "Submit",
                        "Submit prompt",
                        "加入当前运行",
                        "排到下一轮",
                        "发送",
                        "提交",
                    ])
                ?.Current.Name;
        }
        catch
        {
            return null;
        }
    }

    internal bool IsComposerActionAvailable(params string[] actionNames)
    {
        try
        {
            var context = FindCodexWindow();
            return
                context is not null &&
                FindVisibleNamedButton(
                    context.Value.Window,
                    actionNames) is not null;
        }
        catch
        {
            return false;
        }
    }

    internal ComposerAutomationResult InvokeComposerAction(
        AppSettings settings,
        params string[] actionNames)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var targets = actionNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                AutomationElement? button;
                try
                {
                    button = context.Value.Window.FindFirst(
                        TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(
                                AutomationElement.ControlTypeProperty,
                                ControlType.Button),
                            new PropertyCondition(
                                AutomationElement.NameProperty,
                                target,
                                PropertyConditionFlags.IgnoreCase)));
                    if (button is null)
                    {
                        continue;
                    }

                    if (
                        !button.Current.IsEnabled ||
                        button.Current.IsOffscreen ||
                        button.Current.BoundingRectangle.IsEmpty)
                    {
                        continue;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                if (
                    !button.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out var patternObject) ||
                    patternObject is not InvokePattern pattern)
                {
                    continue;
                }

                pattern.Invoke();
                return ComposerAutomationResults.UiAutomationSucceeded();
            }

            return new(
                false,
                AgentAutomationErrorCodes.ElementNotFound,
                $"action:{string.Join("|", actionNames)}");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    internal Task<ComposerAutomationResult> InvokeComposerActionAsync(
        AppSettings settings,
        int timeoutMs,
        CancellationToken cancellationToken,
        params string[] actionNames)
    {
        return Task.Run(() =>
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            ComposerAutomationResult result;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = InvokeComposerAction(settings, actionNames);
                if (result.Succeeded)
                {
                    return result;
                }

                if (AgentAutomationErrorCodes.IsImmediateFailure(
                        result.Error))
                {
                    return result;
                }

                Thread.Sleep(45);
            }
            while (Environment.TickCount64 < deadline);

            return result;
        }, cancellationToken);
    }

    private static ComposerAutomationResult ScrollConversationCore(
        ConversationBoundary boundary,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var editor = FindComposerEditor(context.Value.Window);
            var scroll = FindConversationScrollPattern(
                context.Value.Window,
                editor);
            if (scroll is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    "conversation-scroll");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (
                !scroll.Value.Pattern.Current.VerticallyScrollable ||
                scroll.Value.Pattern.Current.VerticalViewSize >= 99.5)
            {
                return ComposerAutomationResults.UiAutomationSucceeded(stateVerified: true);
            }

            var target = boundary == ConversationBoundary.Top
                ? 0d
                : 100d;
            scroll.Value.Pattern.SetScrollPercent(
                ScrollPattern.NoScroll,
                target);
            var deadline = Environment.TickCount64 + 500;
            do
            {
                Thread.Sleep(30);
                cancellationToken.ThrowIfCancellationRequested();
                var current =
                    scroll.Value.Pattern.Current.VerticalScrollPercent;
                if (
                    current == ScrollPattern.NoScroll ||
                    Math.Abs(current - target) <= 1.5)
                {
                    return ComposerAutomationResults.UiAutomationSucceeded(stateVerified: true);
                }
            }
            while (Environment.TickCount64 < deadline);

            return new(
                false,
                AgentAutomationErrorCodes.ElementUnsupported,
                "conversation-scroll-not-verified");
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.OperationCanceled);
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.AutomationStale,
                "conversation-scroll");
        }
        catch (InvalidOperationException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.ElementUnsupported,
                "conversation-scroll");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    private static (AutomationElement Element, ScrollPattern Pattern)?
        FindConversationScrollPattern(
            AutomationElement window,
            AutomationElement? editor)
    {
        System.Windows.Rect? editorBounds = null;
        try
        {
            if (editor is not null)
            {
                editorBounds = editor.Current.BoundingRectangle;
            }
        }
        catch (ElementNotAvailableException)
        {
            editorBounds = null;
        }

        (AutomationElement Element, ScrollPattern Pattern)? best = null;
        var bestScore = double.NegativeInfinity;
        var candidates = window.FindAll(
            TreeScope.Descendants,
            Condition.TrueCondition);
        foreach (AutomationElement candidate in candidates)
        {
            try
            {
                var bounds = candidate.Current.BoundingRectangle;
                if (
                    candidate.Current.IsOffscreen ||
                    bounds.IsEmpty ||
                    bounds.Width < 280 ||
                    bounds.Height < 180 ||
                    !candidate.TryGetCurrentPattern(
                        ScrollPattern.Pattern,
                        out var patternObject) ||
                    patternObject is not ScrollPattern pattern)
                {
                    continue;
                }

                var score = bounds.Height;
                if (
                    editorBounds is { } composer &&
                    !composer.IsEmpty)
                {
                    var overlap = Math.Max(
                        0,
                        Math.Min(bounds.Right, composer.Right) -
                        Math.Max(bounds.Left, composer.Left));
                    var overlapRatio = overlap /
                        Math.Max(1, Math.Min(bounds.Width, composer.Width));
                    if (
                        overlapRatio < 0.45 ||
                        bounds.Top >= composer.Top)
                    {
                        continue;
                    }

                    score += overlapRatio * 1200;
                    score -= Math.Abs(bounds.Bottom - composer.Top) * 0.08;
                }

                if (pattern.Current.VerticallyScrollable)
                {
                    score += 1600;
                }

                if (score <= bestScore)
                {
                    continue;
                }

                best = (candidate, pattern);
                bestScore = score;
            }
            catch (ElementNotAvailableException)
            {
                // Continue with the remaining live accessibility elements.
            }
        }

        return best;
    }

    internal ComposerAutomationResult SubmitComposer(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            // Codex 26.707.12708.0 maps Mod-Enter directly to the composer
            // submit command for every composerEnterBehavior value. On
            // Windows, Mod is Control. This intentionally bypasses UIA,
            // custom keybindings, and the optional Micro bridge.
            if (!_sendShortcut(CodexComposerService.NativeSubmitShortcut))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.InputInjectionFailed,
                    CodexComposerService.NativeSubmitShortcut);
            }

            return new(
                true,
                Channel: ComposerAutomationChannel.KeyboardInput);
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    internal ComposerAutomationResult ClearComposer(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var editor = FindComposerEditor(context.Value.Window);
            if (editor is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    "composer-editor");
            }

            var text = ReadComposerText(editor);
            if (text is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementUnsupported,
                    "composer-text-read");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ComposerEmpty);
            }

            if (
                !Win32Input.IsCodexForeground() &&
                !Win32Input.FocusCodexAndWait())
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.FocusRejected,
                    "composer-editor");
            }

            editor.SetFocus();
            Thread.Sleep(35);
            var clearedThroughValuePattern = false;
            if (
                editor.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out var valueObject) &&
                valueObject is ValuePattern valuePattern &&
                !valuePattern.Current.IsReadOnly)
            {
                try
                {
                    valuePattern.SetValue(string.Empty);
                    clearedThroughValuePattern = true;
                }
                catch (InvalidOperationException)
                {
                    // Chromium contenteditable nodes commonly expose a
                    // read-like ValuePattern while rejecting SetValue.
                }
            }

            if (
                !clearedThroughValuePattern &&
                (
                    !Win32Input.SendShortcut("Ctrl+A") ||
                    !Win32Input.SendKey(0x08)
                ))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.InputInjectionFailed,
                    "Ctrl+A,Backspace");
            }

            return WaitForComposerTextToClear(editor, timeoutMs: 280)
                ? ComposerAutomationResults.UiAutomationSucceeded(stateVerified: true)
                : new(
                    false,
                    AgentAutomationErrorCodes.ElementUnsupported,
                    "composer-clear-not-verified");
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.AutomationStale,
                "composer-editor");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    internal ComposerAutomationResult StopCurrentTurn(AppSettings settings) =>
        InvokeComposerAction(
            settings,
            "Stop",
            "Cancel",
            "Cancel request");

    internal ComposerAutomationResult CancelComposer(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var namedCancel = InvokeNamedButton(
                context.Value.Window,
                ["Stop", "Cancel", "Cancel request"]);
            if (namedCancel.Succeeded)
            {
                return namedCancel;
            }

            var editor = FindComposerEditor(context.Value.Window);
            if (editor is not null)
            {
                editor.SetFocus();
                Thread.Sleep(45);
            }
            else if (!Win32Input.FocusCodexAndWait())
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.FocusRejected,
                    "composer-editor");
            }

            return Win32Input.SendKey(0x1B)
                ? new(
                    true,
                    Channel: ComposerAutomationChannel.KeyboardInput)
                : new(
                    false,
                    AgentAutomationErrorCodes.InputInjectionFailed,
                    "Escape");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    private static ComposerAutomationResult InvokeNamedButton(
        AutomationElement window,
        IReadOnlyCollection<string> actionNames)
    {
        var button = FindVisibleNamedButton(window, actionNames);
        if (
            button is not null &&
            button.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out var patternObject) &&
            patternObject is InvokePattern pattern)
        {
            try
            {
                pattern.Invoke();
                return ComposerAutomationResults.UiAutomationSucceeded();
            }
            catch (ElementNotAvailableException)
            {
                // Chromium replaced the button between discovery and invoke.
            }
        }

        return new(
            false,
            AgentAutomationErrorCodes.ElementNotFound,
            $"action:{string.Join("|", actionNames)}");
    }
}
