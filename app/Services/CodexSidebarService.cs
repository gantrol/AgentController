using System.Diagnostics;
using System.Windows.Automation;
using CodexController.Models;
using CodexController.Native;

namespace CodexController.Services;

public sealed record SidebarAutomationResult(
    bool Succeeded,
    string? Error = null);

public sealed class ProjectDisclosureLease
{
    public ProjectDisclosureLease(
        string projectName,
        bool projectIsPinned)
    {
        ProjectName = projectName;
        ProjectIsPinned = projectIsPinned;
    }

    public string ProjectName { get; }
    public bool ProjectIsPinned { get; }
    internal bool PinnedSectionInspected { get; set; }
    internal bool PinnedSectionExpandedByController { get; set; }
    internal bool ProjectSectionInspected { get; set; }
    internal bool ProjectSectionExpandedByController { get; set; }
    internal bool ProjectInspected { get; set; }
    internal bool ProjectExpandedByController { get; set; }
}

public sealed class CodexSidebarService
{
    private const double SidebarRightEdge = 540;

    public SidebarAutomationResult FocusEntry(
        SidebarEntry entry,
        string? projectName,
        AppSettings settings,
        CancellationToken cancellationToken,
        ProjectDisclosureLease? disclosureLease = null)
    {
        if (!settings.BridgeEnabled)
        {
            return new(false, "桥接处于安全预览");
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(false, "Codex 未在前台");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !settings.OnlyWhenCodexForeground &&
                !Win32Input.IsCodexForeground())
            {
                _ = Win32Input.FocusCodexAndWait(timeoutMs: 100);
            }

            var window = FindCodexWindow();
            if (window is null)
            {
                return new(false, "找不到 Codex 窗口");
            }

            var target = entry.Layer switch
            {
                SidebarLayer.Projects =>
                    FindProjectButton(
                        window,
                        entry.NativeTitle ?? entry.Title,
                        entry.ProjectIsPinned,
                        cancellationToken,
                        disclosureLease),
                SidebarLayer.Tasks =>
                    FindTaskRow(
                        window,
                        entry,
                        projectName,
                        entry.ProjectIsPinned,
                        cancellationToken,
                        disclosureLease),
                SidebarLayer.Pinned =>
                    FindPinnedTaskRow(
                        window,
                        entry.NativeTitle ?? entry.Title,
                        cancellationToken,
                        disclosureLease),
                _ => null,
            };
            if (target is null)
            {
                return new(false, $"Codex 侧边栏未显示：{entry.Title}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            TryScrollIntoView(target);
            Thread.Sleep(25);
            cancellationToken.ThrowIfCancellationRequested();
            target.SetFocus();
            Thread.Sleep(15);
            return HasKeyboardFocus(target)
                ? new(true)
                : new(false, "Codex 未接受侧边栏焦点");
        }
        catch (OperationCanceledException)
        {
            return new(false, "已取消");
        }
        catch (ElementNotAvailableException)
        {
            return new(false, "Codex 侧边栏已刷新");
        }
        catch (Exception exception)
        {
            return new(false, exception.Message);
        }
    }

    public string? TryGetCurrentThreadTitle()
    {
        try
        {
            var window = FindCodexWindow();
            if (window is null)
            {
                return null;
            }

            var texts = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Text));
            return texts
                .Cast<AutomationElement>()
                .Where(element => IsVisible(element))
                .Where(HasMainHeaderAncestors)
                .OrderByDescending(element => SafeRectangle(element).Height)
                .ThenBy(element => SafeRectangle(element).Top)
                .Select(SafeName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        }
        catch
        {
            return null;
        }
    }

    public SidebarAutomationResult GoBack(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(false, "桥接处于安全预览");
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(false, "Codex 未在前台");
        }

        try
        {
            var window = FindCodexWindow();
            if (window is null)
            {
                return new(false, "找不到 Codex 窗口");
            }

            var buttons = window.FindAll(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Button),
                    new PropertyCondition(
                        AutomationElement.NameProperty,
                        "Back")));
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    if (
                        !button.Current.IsEnabled ||
                        button.Current.IsOffscreen ||
                        !IsNavigationBackButton(window, button) ||
                        !button.TryGetCurrentPattern(
                            InvokePattern.Pattern,
                            out var patternObject) ||
                        patternObject is not InvokePattern pattern)
                    {
                        continue;
                    }

                    pattern.Invoke();
                    return new(true);
                }
                catch (ElementNotAvailableException)
                {
                    // Chromium may replace the header while querying it.
                }
            }

            return new(false, "Codex 当前没有可撤回的导航");
        }
        catch (Exception exception)
        {
            return new(false, exception.Message);
        }
    }

    public SidebarAutomationResult RestoreDisclosure(
        ProjectDisclosureLease lease)
    {
        try
        {
            var window = FindCodexWindow();
            if (window is null)
            {
                return new(false, "找不到 Codex 窗口");
            }

            if (lease.ProjectExpandedByController)
            {
                var project = FindProjectButtonCore(
                    window,
                    lease.ProjectName);
                TrySetExpanded(project, expanded: false);
            }

            var projectSection =
                lease.ProjectIsPinned ? "Pinned" : "Projects";
            if (
                lease.ProjectSectionExpandedByController &&
                !(projectSection == "Pinned" &&
                  lease.PinnedSectionExpandedByController))
            {
                TrySetExpanded(
                    FindSectionButton(window, projectSection),
                    expanded: false);
            }

            if (lease.PinnedSectionExpandedByController)
            {
                TrySetExpanded(
                    FindSectionButton(window, "Pinned"),
                    expanded: false);
            }

            return new(true);
        }
        catch (ElementNotAvailableException)
        {
            return new(false, "Codex 侧边栏已刷新");
        }
        catch (Exception exception)
        {
            return new(false, exception.Message);
        }
    }

    private static AutomationElement? FindCodexWindow()
    {
        nint selectedHandle = nint.Zero;
        var selectedStart = DateTime.MinValue;
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            try
            {
                var handle = process.MainWindowHandle;
                if (handle == nint.Zero)
                {
                    continue;
                }

                DateTime start;
                try
                {
                    start = process.StartTime;
                }
                catch
                {
                    start = DateTime.MinValue;
                }

                if (
                    selectedHandle == nint.Zero ||
                    start >= selectedStart)
                {
                    selectedHandle = handle;
                    selectedStart = start;
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        return selectedHandle == nint.Zero
            ? null
            : AutomationElement.FromHandle(selectedHandle);
    }

    private static AutomationElement? FindProjectButton(
        AutomationElement window,
        string title,
        bool projectIsPinned,
        CancellationToken cancellationToken,
        ProjectDisclosureLease? disclosureLease)
    {
        var sectionName = projectIsPinned ? "Pinned" : "Projects";
        EnsureSectionExpanded(
            window,
            sectionName,
            disclosureLease,
            isPinnedSection: projectIsPinned);
        return WaitForElement(
            () => FindProjectButtonCore(window, title),
            cancellationToken,
            timeoutMs: 700);
    }

    private static AutomationElement? FindProjectButtonCore(
        AutomationElement window,
        string title)
    {
        var candidates = FindExactNamedElements(
                window,
                ControlType.Button,
                title)
            .Where(element => IsInSidebar(window, element))
            .Where(element =>
                SafeClassName(element).Contains(
                    "group/folder-row",
                    StringComparison.OrdinalIgnoreCase))
            .OrderBy(element => SafeRectangle(element).Top)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static AutomationElement? FindTaskRow(
        AutomationElement window,
        SidebarEntry entry,
        string? projectName,
        bool projectIsPinned,
        CancellationToken cancellationToken,
        ProjectDisclosureLease? disclosureLease)
    {
        var title = entry.NativeTitle ?? entry.Title;
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var sectionName = projectIsPinned ? "Pinned" : "Projects";
            EnsureSectionExpanded(
                window,
                sectionName,
                disclosureLease,
                isPinnedSection: projectIsPinned);
            var projectRow = WaitForElement(
                () => FindTaskRowInProject(
                    window,
                    title,
                    projectName),
                cancellationToken,
                timeoutMs: 120);
            if (projectRow is not null)
            {
                return projectRow;
            }

            var projectButton = FindProjectButtonCore(
                window,
                projectName);
            if (EnsureProjectExpanded(
                    projectButton,
                    disclosureLease))
            {
                projectRow = WaitForElement(
                    () => FindTaskRowInProject(
                        window,
                        title,
                        projectName),
                    cancellationToken,
                    timeoutMs: 700);
                if (projectRow is not null)
                {
                    return projectRow;
                }
            }

            for (var page = 0; page < 24; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryInvokeShowMore(
                        window,
                        $"Scheduled tasks in {projectName}"))
                {
                    break;
                }

                projectRow = WaitForElement(
                    () => FindTaskRowInProject(
                        window,
                        title,
                        projectName),
                    cancellationToken,
                    timeoutMs: 550);
                if (projectRow is not null)
                {
                    return projectRow;
                }
            }

            return null;
        }

        if (entry.NativeListIndex is int nativeListIndex)
        {
            var row = WaitForElement(
                () => FindTaskRowAtIndex(window, nativeListIndex),
                cancellationToken,
                timeoutMs: 120);
            if (row is not null)
            {
                return row;
            }

            if (EnsureSectionExpanded(
                    window,
                    "Tasks",
                    disclosureLease: null,
                    isPinnedSection: false))
            {
                return WaitForElement(
                    () => FindTaskRowAtIndex(window, nativeListIndex),
                    cancellationToken,
                    timeoutMs: 700);
            }

            return null;
        }

        var fallbackRows = FindExactNamedElements(
                window,
                ControlType.ListItem,
                title)
            .Where(element => IsInSidebar(window, element))
            .Select(FindFocusableTaskRow)
            .Where(element => element is not null)
            .Cast<AutomationElement>()
            .OrderBy(element => SafeRectangle(element).Top)
            .ToList();
        return fallbackRows.Count == 1 ? fallbackRows[0] : null;
    }

    public int? TryGetBottomTaskCount()
    {
        try
        {
            var window = FindCodexWindow();
            if (window is null)
            {
                return null;
            }

            var taskLists = FindExactNamedElements(
                    window,
                    ControlType.List,
                    "Tasks")
                .Where(element => IsInSidebar(window, element))
                .ToList();
            if (taskLists.Count != 1)
            {
                return null;
            }

            return taskLists[0]
                .FindAll(
                    TreeScope.Children,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ListItem))
                .Cast<AutomationElement>()
                .Count(item =>
                    HasDescendantButtonPrefix(item, "Pin task") ||
                    HasDescendantButtonPrefix(item, "Unpin task"));
        }
        catch
        {
            return null;
        }
    }

    private static AutomationElement? FindTaskRowAtIndex(
        AutomationElement window,
        int index)
    {
        if (index < 0)
        {
            return null;
        }

        var taskLists = FindExactNamedElements(
                window,
                ControlType.List,
                "Tasks")
            .Where(element => IsInSidebar(window, element))
            .ToList();
        if (taskLists.Count != 1)
        {
            return null;
        }

        var rows = taskLists[0]
            .FindAll(
                TreeScope.Children,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.ListItem))
            .Cast<AutomationElement>()
            .Where(item =>
                HasDescendantButtonPrefix(item, "Pin task") ||
                HasDescendantButtonPrefix(item, "Unpin task"))
            .ToList();
        return index < rows.Count
            ? FindFocusableTaskRow(rows[index])
            : null;
    }

    private static AutomationElement? FindPinnedTaskRow(
        AutomationElement window,
        string title,
        CancellationToken cancellationToken,
        ProjectDisclosureLease? disclosureLease)
    {
        EnsureSectionExpanded(
            window,
            "Pinned",
            disclosureLease,
            isPinnedSection: true);
        var row = WaitForElement(
            () => FindPinnedTaskRowCore(window, title),
            cancellationToken,
            timeoutMs: 120);
        if (row is not null)
        {
            return row;
        }

        for (var page = 0; page < 24; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryInvokeShowMore(window, "Pinned"))
            {
                break;
            }

            row = WaitForElement(
                () => FindPinnedTaskRowCore(window, title),
                cancellationToken,
                timeoutMs: 550);
            if (row is not null)
            {
                return row;
            }
        }

        return null;
    }

    private static AutomationElement? FindPinnedTaskRowCore(
        AutomationElement window,
        string title)
    {
        var pinnedLists = FindExactNamedElements(
                window,
                ControlType.List,
                "Pinned")
            .Where(element => IsInSidebar(window, element));
        var candidates = pinnedLists
            .SelectMany(list =>
                FindExactNamedElements(
                    list,
                    ControlType.ListItem,
                    title))
            .Select(item => new
            {
                Row = FindFocusableTaskRow(item),
                IsPinned = HasDescendantButtonPrefix(item, "Unpin task"),
            })
            .Where(item => item.Row is not null)
            .OrderByDescending(item => item.IsPinned)
            .ThenBy(item => SafeRectangle(item.Row!).Top)
            .Select(item => item.Row)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static AutomationElement? FindTaskRowInProject(
        AutomationElement window,
        string title,
        string projectName)
    {
        var listName = $"Scheduled tasks in {projectName}";
        var projectLists = FindExactNamedElements(
            window,
            ControlType.List,
            listName);
        var rows = projectLists
            .SelectMany(list => FindExactNamedElements(
                    list,
                    ControlType.ListItem,
                    title))
            .Select(FindFocusableTaskRow)
            .Where(row => row is not null)
            .Cast<AutomationElement>()
            .ToList();
        return rows.Count == 1 ? rows[0] : null;
    }

    private static AutomationElement? FindFocusableTaskRow(
        AutomationElement listItem)
    {
        var buttons = listItem.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        return buttons
            .Cast<AutomationElement>()
            .Where(element =>
            {
                try
                {
                    return
                        element.Current.IsEnabled &&
                        element.Current.IsKeyboardFocusable;
                }
                catch
                {
                    return false;
                }
            })
            .OrderByDescending(element =>
            {
                var className = SafeClassName(element);
                if (
                    className.Contains(
                        "focus-visible",
                        StringComparison.OrdinalIgnoreCase) ||
                    className.Contains(
                        "cursor-interaction",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return 2;
                }

                return className.Contains(
                    "cursor-grab",
                    StringComparison.OrdinalIgnoreCase)
                    ? 0
                    : 1;
            })
            .ThenByDescending(element =>
            {
                var rectangle = SafeRectangle(element);
                return rectangle.Width * rectangle.Height;
            })
            .FirstOrDefault();
    }

    private static bool HasDescendantButtonPrefix(
        AutomationElement element,
        string prefix)
    {
        var buttons = element.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        return buttons
            .Cast<AutomationElement>()
            .Any(button =>
                SafeName(button).StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<AutomationElement> FindExactNamedElements(
        AutomationElement root,
        ControlType controlType,
        string name)
    {
        var condition = new AndCondition(
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                controlType),
            new PropertyCondition(
                AutomationElement.NameProperty,
                name));
        return root
            .FindAll(TreeScope.Descendants, condition)
            .Cast<AutomationElement>();
    }

    private static AutomationElement? FindSectionButton(
        AutomationElement window,
        string name)
    {
        return FindExactNamedElements(
                window,
                ControlType.Button,
                name)
            .Where(element => IsInSidebar(window, element))
            .Where(element =>
                !SafeClassName(element).Contains(
                    "group/folder-row",
                    StringComparison.OrdinalIgnoreCase))
            .Where(element =>
            {
                try
                {
                    return element.TryGetCurrentPattern(
                        ExpandCollapsePattern.Pattern,
                        out _);
                }
                catch
                {
                    return false;
                }
            })
            .OrderBy(element => SafeRectangle(element).Top)
            .FirstOrDefault();
    }

    private static bool EnsureSectionExpanded(
        AutomationElement window,
        string sectionName,
        ProjectDisclosureLease? disclosureLease,
        bool isPinnedSection)
    {
        var section = FindSectionButton(window, sectionName);
        if (section is null)
        {
            return false;
        }

        try
        {
            if (
                !section.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) ||
                patternObject is not ExpandCollapsePattern pattern)
            {
                return false;
            }

            var collapsed =
                pattern.Current.ExpandCollapseState ==
                ExpandCollapseState.Collapsed;
            if (disclosureLease is not null)
            {
                if (isPinnedSection)
                {
                    disclosureLease.PinnedSectionInspected = true;
                }
                else
                {
                    disclosureLease.ProjectSectionInspected = true;
                }
            }

            if (!collapsed)
            {
                return true;
            }

            pattern.Expand();
            if (disclosureLease is not null)
            {
                if (isPinnedSection)
                {
                    disclosureLease.PinnedSectionExpandedByController = true;
                }
                else
                {
                    disclosureLease.ProjectSectionExpandedByController = true;
                }
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TrySetExpanded(
        AutomationElement? element,
        bool expanded)
    {
        if (element is null)
        {
            return false;
        }

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

            var state = pattern.Current.ExpandCollapseState;
            if (
                expanded &&
                state == ExpandCollapseState.Collapsed)
            {
                pattern.Expand();
            }
            else if (
                !expanded &&
                state == ExpandCollapseState.Expanded)
            {
                pattern.Collapse();
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool EnsureProjectExpanded(
        AutomationElement? project,
        ProjectDisclosureLease? disclosureLease)
    {
        if (project is null)
        {
            return false;
        }

        try
        {
            if (
                !project.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) ||
                patternObject is not ExpandCollapsePattern pattern)
            {
                return false;
            }

            var collapsed =
                pattern.Current.ExpandCollapseState ==
                ExpandCollapseState.Collapsed;
            if (disclosureLease is not null)
            {
                disclosureLease.ProjectInspected = true;
            }

            if (!collapsed)
            {
                return true;
            }

            pattern.Expand();
            if (disclosureLease is not null)
            {
                disclosureLease.ProjectExpandedByController = true;
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryInvokeShowMore(
        AutomationElement window,
        string listName)
    {
        var lists = FindExactNamedElements(
                window,
                ControlType.List,
                listName)
            .Where(element => IsInSidebar(window, element))
            .ToList();
        if (lists.Count != 1)
        {
            return false;
        }

        var buttons = lists[0]
            .FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Button))
            .Cast<AutomationElement>()
            .Where(button =>
                SafeName(button).StartsWith(
                    "Show more",
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (buttons.Count != 1)
        {
            return false;
        }

        var button = buttons[0];
        try
        {
            TryScrollIntoView(button);
            if (
                button.Current.IsEnabled &&
                button.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var patternObject) &&
                patternObject is InvokePattern pattern)
            {
                pattern.Invoke();
                return true;
            }
        }
        catch (ElementNotAvailableException)
        {
            // React replaced the row while it was being materialized.
        }

        return false;
    }

    private static AutomationElement? WaitForElement(
        Func<AutomationElement?> finder,
        CancellationToken cancellationToken,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var element = finder();
                if (element is not null)
                {
                    return element;
                }
            }
            catch (ElementNotAvailableException)
            {
                // Re-query after Chromium finishes replacing the list.
            }

            Thread.Sleep(55);
        }
        while (Environment.TickCount64 < deadline);

        return null;
    }

    private static bool HasMainHeaderAncestors(AutomationElement element)
    {
        var hasHeaderTint = false;
        var hasMainSurface = false;
        var current = element;
        for (var depth = 0; depth < 10; depth++)
        {
            try
            {
                current = TreeWalker.RawViewWalker.GetParent(current);
                if (current is null)
                {
                    break;
                }

                var className = SafeClassName(current);
                hasHeaderTint |= className.Contains(
                    "app-header-tint",
                    StringComparison.OrdinalIgnoreCase);
                hasMainSurface |= className.Contains(
                    "main-surface",
                    StringComparison.OrdinalIgnoreCase);
                if (hasHeaderTint && hasMainSurface)
                {
                    return true;
                }
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
        }

        return false;
    }

    private static bool IsVisible(AutomationElement element)
    {
        try
        {
            return
                !element.Current.IsOffscreen &&
                !element.Current.BoundingRectangle.IsEmpty;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasKeyboardFocus(AutomationElement element)
    {
        try
        {
            return element.Current.HasKeyboardFocus;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInSidebar(
        AutomationElement window,
        AutomationElement element)
    {
        var windowRectangle = SafeRectangle(window);
        var rectangle = SafeRectangle(element);
        return
            !windowRectangle.IsEmpty &&
            !rectangle.IsEmpty &&
            rectangle.Left >= windowRectangle.Left - 1 &&
            rectangle.Right <= windowRectangle.Left + SidebarRightEdge;
    }

    private static bool IsNavigationBackButton(
        AutomationElement window,
        AutomationElement element)
    {
        var windowRectangle = SafeRectangle(window);
        var rectangle = SafeRectangle(element);
        return
            !windowRectangle.IsEmpty &&
            !rectangle.IsEmpty &&
            rectangle.Top <= windowRectangle.Top + 100 &&
            rectangle.Left >= windowRectangle.Left &&
            rectangle.Right <= windowRectangle.Left + SidebarRightEdge;
    }

    private static void TryScrollIntoView(AutomationElement element)
    {
        try
        {
            if (
                element.TryGetCurrentPattern(
                    ScrollItemPattern.Pattern,
                    out var patternObject) &&
                patternObject is ScrollItemPattern pattern)
            {
                pattern.ScrollIntoView();
            }
        }
        catch
        {
            // A visible or virtualized element can still accept focus directly.
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

    private static string SafeClassName(AutomationElement element)
    {
        try
        {
            return element.Current.ClassName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static System.Windows.Rect SafeRectangle(
        AutomationElement element)
    {
        try
        {
            return element.Current.BoundingRectangle;
        }
        catch
        {
            return System.Windows.Rect.Empty;
        }
    }
}
