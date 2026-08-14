using System.Windows.Automation;

namespace CodexMicro.Desktop.Services;

internal enum CodexQuickModel
{
    Unknown,
    Sol,
    Terra,
    Luna,
}

internal sealed record CodexModelToggleResult(
    bool Succeeded,
    CodexQuickModel Previous,
    CodexQuickModel Current,
    string? Error = null);

/// <summary>
/// Selects an exact entry in Codex's official composer model picker. This
/// changes the current task's next turn and deliberately leaves the global
/// config.toml defaults untouched.
/// </summary>
internal sealed class CodexModelToggleService
{
    private const int PollAttempts = 24;
    private const int PollDelayMilliseconds = 60;

    internal Task<CodexQuickModel> ReadCurrentAsync(
        CancellationToken cancellationToken) =>
        Task.Run(
            () => ReadCurrentCore(cancellationToken),
            cancellationToken);

    internal Task<CodexModelToggleResult> ToggleAsync(
        CodexQuickModel first,
        CodexQuickModel second,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => ToggleCore(first, second, cancellationToken),
            cancellationToken);

    internal static CodexQuickModel ParseModelName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CodexQuickModel.Unknown;
        }

        if (value.Contains("Luna", StringComparison.OrdinalIgnoreCase))
        {
            return CodexQuickModel.Luna;
        }

        if (value.Contains("Terra", StringComparison.OrdinalIgnoreCase))
        {
            return CodexQuickModel.Terra;
        }

        return value.Contains("Sol", StringComparison.OrdinalIgnoreCase)
            ? CodexQuickModel.Sol
            : CodexQuickModel.Unknown;
    }

    internal static CodexQuickModel ResolveToggleTarget(
        CodexQuickModel current,
        CodexQuickModel first,
        CodexQuickModel second)
    {
        ValidatePair(first, second);
        return current == first ? second : first;
    }

    private static CodexQuickModel ReadCurrentCore(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = FindCodexWindow();
        if (context is null)
        {
            return CodexQuickModel.Unknown;
        }

        var button = FindComposerModelButton(context.Value.Window);
        return button is null
            ? CodexQuickModel.Unknown
            : ParseModelName(SafeName(button));
    }

    private static CodexModelToggleResult ToggleCore(
        CodexQuickModel first,
        CodexQuickModel second,
        CancellationToken cancellationToken)
    {
        ValidatePair(first, second);
        AutomationElement? composerButton = null;
        AutomationElement? modelCategory = null;
        var previous = CodexQuickModel.Unknown;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = FindCodexWindow();
            if (context is null)
            {
                return Failure(previous, "codex-window");
            }

            composerButton = FindComposerModelButton(context.Value.Window);
            if (composerButton is null)
            {
                return Failure(previous, "composer-model-button");
            }

            previous = ParseModelName(SafeName(composerButton));
            if (!TryExpand(composerButton))
            {
                return Failure(previous, "composer-model-button-expand");
            }

            modelCategory = WaitForModelCategory(
                context.Value.Window,
                context.Value.ProcessId,
                cancellationToken);
            if (modelCategory is null)
            {
                var advanced = WaitForAdvancedToggle(
                    context.Value.Window,
                    context.Value.ProcessId,
                    cancellationToken);
                if (advanced is null || !TryInvoke(advanced))
                {
                    return Failure(previous, "advanced-view");
                }

                modelCategory = WaitForModelCategory(
                    context.Value.Window,
                    context.Value.ProcessId,
                    cancellationToken);
            }

            if (modelCategory is null)
            {
                return Failure(previous, "model-category");
            }

            var categoryModel = ParseModelName(SafeName(modelCategory));
            if (categoryModel != CodexQuickModel.Unknown)
            {
                previous = categoryModel;
            }

            var target = ResolveToggleTarget(previous, first, second);
            if (!TryExpand(modelCategory))
            {
                return Failure(previous, "model-category-expand");
            }

            var option = WaitForModelOption(
                context.Value.Window,
                context.Value.ProcessId,
                target,
                cancellationToken);
            if (option is null || !TryInvoke(option))
            {
                return Failure(
                    previous,
                    $"model-option-{target.ToString().ToLowerInvariant()}");
            }

            for (var attempt = 0; attempt < PollAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(PollDelayMilliseconds);
                var refreshed = FindCodexWindow();
                var refreshedButton = refreshed is null
                    ? null
                    : FindComposerModelButton(refreshed.Value.Window);
                if (
                    refreshedButton is not null &&
                    ParseModelName(SafeName(refreshedButton)) == target)
                {
                    return new(true, previous, target);
                }
            }

            return Failure(previous, "model-readback");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ElementNotAvailableException)
        {
            return Failure(previous, "automation-stale");
        }
        catch
        {
            return Failure(previous, "automation-failed");
        }
        finally
        {
            TryCollapse(modelCategory);
            TryCollapse(composerButton);
        }
    }

    private static CodexModelToggleResult Failure(
        CodexQuickModel previous,
        string error) =>
        new(false, previous, previous, error);

    private static void ValidatePair(
        CodexQuickModel first,
        CodexQuickModel second)
    {
        if (
            first == CodexQuickModel.Unknown ||
            second == CodexQuickModel.Unknown ||
            first == second)
        {
            throw new ArgumentException(
                "Quick-model slots must contain two distinct known models.");
        }
    }

    private static (AutomationElement Window, int ProcessId)?
        FindCodexWindow()
    {
        if (
            !CodexWindowActivator.TryFindMainWindow(
                out var handle,
                out var processId) ||
            processId == 0)
        {
            return null;
        }

        var window = AutomationElement.FromHandle(handle);
        return window is null ? null : (window, processId);
    }

    private static AutomationElement? FindComposerModelButton(
        AutomationElement window)
    {
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            try
            {
                var current = button.Current;
                var name = current.Name?.Trim() ?? string.Empty;
                if (
                    name.Length > 0 &&
                    char.IsDigit(name[0]) &&
                    current.IsEnabled &&
                    !current.IsOffscreen &&
                    !current.BoundingRectangle.IsEmpty &&
                    HasClassToken(
                        current.ClassName,
                        "h-token-button-composer"))
                {
                    return button;
                }
            }
            catch (ElementNotAvailableException)
            {
                // Chromium replaced the button while its tree was read.
            }
        }

        return null;
    }

    private static AutomationElement? WaitForModelCategory(
        AutomationElement window,
        int processId,
        CancellationToken cancellationToken) =>
        WaitForElement(
            window,
            processId,
            element => IsModelCategory(SafeName(element)),
            cancellationToken,
            menuItemsOnly: true);

    private static AutomationElement? WaitForAdvancedToggle(
        AutomationElement window,
        int processId,
        CancellationToken cancellationToken) =>
        WaitForElement(
            window,
            processId,
            element =>
            {
                var name = SafeName(element);
                return
                    name.Equals("Advanced", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(
                        "Advanced ",
                        StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("高级", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(
                        "高级 ",
                        StringComparison.OrdinalIgnoreCase);
            },
            cancellationToken,
            menuItemsOnly: false);

    private static AutomationElement? WaitForModelOption(
        AutomationElement window,
        int processId,
        CodexQuickModel target,
        CancellationToken cancellationToken) =>
        WaitForElement(
            window,
            processId,
            element =>
                !IsModelCategory(SafeName(element)) &&
                ParseModelName(SafeName(element)) == target &&
                SupportsInvoke(element),
            cancellationToken,
            menuItemsOnly: true);

    private static AutomationElement? WaitForElement(
        AutomationElement window,
        int processId,
        Func<AutomationElement, bool> predicate,
        CancellationToken cancellationToken,
        bool menuItemsOnly)
    {
        for (var attempt = 0; attempt < PollAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var element in FindPickerElements(
                         window,
                         processId,
                         menuItemsOnly))
            {
                if (IsUsable(element) && predicate(element))
                {
                    return element;
                }
            }

            Thread.Sleep(PollDelayMilliseconds);
        }

        return null;
    }

    private static IEnumerable<AutomationElement> FindPickerElements(
        AutomationElement mainWindow,
        int processId,
        bool menuItemsOnly)
    {
        Condition condition = menuItemsOnly
            ? new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.MenuItem)
            : new OrCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.MenuItem),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Button),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Custom));
        var roots = new List<AutomationElement> { mainWindow };
        var processWindows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            new PropertyCondition(
                AutomationElement.ProcessIdProperty,
                processId));
        roots.AddRange(
            processWindows
                .Cast<AutomationElement>()
                .Where(element => !element.Equals(mainWindow)));

        foreach (var root in roots)
        {
            AutomationElementCollection elements;
            try
            {
                elements = root.FindAll(TreeScope.Descendants, condition);
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement element in elements)
            {
                yield return element;
            }
        }
    }

    private static bool IsModelCategory(string name) =>
        name.Equals("Model", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("Model ", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("模型", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("模型 ", StringComparison.OrdinalIgnoreCase);

    private static bool SupportsInvoke(AutomationElement element)
    {
        try
        {
            return element.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var pattern) &&
                pattern is InvokePattern;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryInvoke(AutomationElement element)
    {
        try
        {
            if (
                element.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var invokeObject) &&
                invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            if (
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var expandObject) &&
                expandObject is ExpandCollapsePattern expand)
            {
                if (
                    expand.Current.ExpandCollapseState ==
                    ExpandCollapseState.Expanded)
                {
                    expand.Collapse();
                }
                else
                {
                    expand.Expand();
                }

                return true;
            }
        }
        catch
        {
            // The caller reports the stable operation-level failure.
        }

        return false;
    }

    private static bool TryExpand(AutomationElement element)
    {
        try
        {
            if (
                !element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) ||
                patternObject is not ExpandCollapsePattern pattern)
            {
                return false;
            }

            if (
                pattern.Current.ExpandCollapseState !=
                ExpandCollapseState.Expanded)
            {
                pattern.Expand();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryCollapse(AutomationElement? element)
    {
        if (element is null)
        {
            return;
        }

        try
        {
            if (
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) &&
                patternObject is ExpandCollapsePattern pattern &&
                pattern.Current.ExpandCollapseState ==
                ExpandCollapseState.Expanded)
            {
                pattern.Collapse();
            }
        }
        catch
        {
            // Selecting an option commonly destroys the popup immediately.
        }
    }

    private static bool IsUsable(AutomationElement element)
    {
        try
        {
            var current = element.Current;
            return
                current.IsEnabled &&
                !current.IsOffscreen &&
                !current.BoundingRectangle.IsEmpty;
        }
        catch
        {
            return false;
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.Name?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool HasClassToken(string? className, string token) =>
        !string.IsNullOrWhiteSpace(className) &&
        className
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(token, StringComparer.Ordinal);
}
