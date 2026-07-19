using System.Diagnostics;
using System.Windows.Automation;

namespace CodexController.Services;

internal static class CodexAutomationLocator
{
    internal static (AutomationElement Window, int ProcessId)? FindCodexWindow()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            if (process.MainWindowHandle == nint.Zero)
            {
                process.Dispose();
                continue;
            }

            var processId = process.Id;
            var handle = process.MainWindowHandle;
            process.Dispose();
            var window = AutomationElement.FromHandle(handle);
            if (window is not null)
            {
                return (window, processId);
            }
        }

        return null;
    }

    internal static AutomationElement? FindComposerButton(
        AutomationElement window)
    {
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            string name;
            string className;
            try
            {
                name = button.Current.Name?.Trim() ?? string.Empty;
                className = button.Current.ClassName ?? string.Empty;
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
                name.Length > 0 &&
                char.IsDigit(name[0]) &&
                ComposerDialPolicy.HasComposerButtonClassToken(
                    className))
            {
                return button;
            }
        }

        return null;
    }

    internal static AutomationElement? FindVisibleNamedButton(
        AutomationElement window,
        IReadOnlyCollection<string> actionNames)
    {
        var targets = actionNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(ComposerChoiceNormalizer.Normalize)
            .ToHashSet(StringComparer.Ordinal);
        if (targets.Count == 0)
        {
            return null;
        }

        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            try
            {
                if (
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    button.Current.BoundingRectangle.IsEmpty ||
                    !targets.Contains(ComposerChoiceNormalizer.Normalize(button.Current.Name)))
                {
                    continue;
                }

                return button;
            }
            catch (ElementNotAvailableException)
            {
                // Continue looking if Chromium replaced a button mid-query.
            }
        }

        return null;
    }

    internal static AutomationElement? FindVisibleNamedButtonNearComposer(
        AutomationElement window,
        IReadOnlyCollection<string> actionNames,
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

        var targets = actionNames
            .Select(ComposerChoiceNormalizer.Normalize)
            .ToHashSet(StringComparer.Ordinal);
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            try
            {
                var bounds = button.Current.BoundingRectangle;
                if (
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    bounds.IsEmpty ||
                    !targets.Contains(
                        ComposerChoiceNormalizer.Normalize(button.Current.Name)))
                {
                    continue;
                }

                if (
                    editorBounds is not { } composer ||
                    IsNearComposer(bounds, composer))
                {
                    return button;
                }
            }
            catch (ElementNotAvailableException)
            {
                // Continue if Chromium replaced the indicator mid-query.
            }
        }

        return null;
    }

    internal static bool IsNearComposer(
        System.Windows.Rect candidate,
        System.Windows.Rect composer)
    {
        var horizontalOverlap =
            Math.Min(candidate.Right, composer.Right) -
            Math.Max(candidate.Left, composer.Left);
        return
            horizontalOverlap > 0 &&
            candidate.Top >= composer.Top - 16 &&
            candidate.Top <= composer.Bottom + 120;
    }

    internal static AutomationElement? FindComposerEditor(
        AutomationElement window)
    {
        var groups = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Group));
        foreach (AutomationElement group in groups)
        {
            try
            {
                var className = group.Current.ClassName ?? string.Empty;
                if (
                    group.Current.IsEnabled &&
                    !group.Current.IsOffscreen &&
                    !group.Current.BoundingRectangle.IsEmpty &&
                    className
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains("ProseMirror", StringComparer.Ordinal))
                {
                    return group;
                }
            }
            catch (ElementNotAvailableException)
            {
                // Continue looking for the live editor.
            }
        }

        return null;
    }
}
