using System.Runtime.InteropServices;
using System.Windows.Automation;
using static CodexController.Services.CodexAutomationLocator;
using static CodexController.Services.CodexComposerDialProbe;

namespace CodexController.Services;

internal enum CodexMicroSurfaceKind
{
    None,
    Composer,
    Menu,
    Approval,
    Dialog,
}

internal sealed record CodexMicroReadback(
    CodexMicroSurfaceKind Surface,
    string SurfaceName,
    string ItemName,
    int Position,
    int Count,
    bool SelectionVerified,
    bool CanExpand = false,
    bool IsAdjustable = false)
{
    internal static CodexMicroReadback Closed { get; } = new(
        CodexMicroSurfaceKind.None,
        string.Empty,
        string.Empty,
        0,
        0,
        SelectionVerified: true);

    internal bool IsMenuOpen =>
        Surface is
            CodexMicroSurfaceKind.Menu or
            CodexMicroSurfaceKind.Approval or
            CodexMicroSurfaceKind.Dialog;

    internal string DisplayText => Position > 0 && Count > 0
        ? $"{Position} / {Count} · {ItemName}"
        : !string.IsNullOrWhiteSpace(ItemName)
            ? ItemName
            : SurfaceName;
}

/// <summary>
/// Reads the selection produced by Codex's official Micro bridge. This type
/// never focuses an element and never injects input; it is readback only.
/// </summary>
internal sealed class CodexMicroReadbackObserver
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(35),
        TimeSpan.FromMilliseconds(70),
        TimeSpan.FromMilliseconds(120),
    ];

    internal async Task<CodexMicroReadback> ObserveAsync(
        CancellationToken cancellationToken)
    {
        var last = CodexMicroReadback.Closed;
        foreach (var delay in RetryDelays)
        {
            await Task.Delay(delay, cancellationToken)
                .ConfigureAwait(false);
            last = await Task.Run(
                    TryObserve,
                    cancellationToken)
                .ConfigureAwait(false);
            if (
                last.SelectionVerified &&
                last.Surface != CodexMicroSurfaceKind.None)
            {
                return last;
            }
        }

        return last;
    }

    private static CodexMicroReadback TryObserve()
    {
        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    CodexMicroSurfaceKind.None,
                    string.Empty,
                    string.Empty,
                    0,
                    0,
                    SelectionVerified: false);
            }

            var window = context.Value.Window;
            var processId = context.Value.ProcessId;
            var composerRegion = TryGetComposerPopupRegion(window);

            var dialog = TryObserveDialog(window);
            if (dialog is not null)
            {
                return dialog;
            }

            var approval = TryObserveApproval(window);
            if (approval is not null)
            {
                return approval;
            }

            var menus = FindDialPopupRoots(window, processId)
                .Select(root =>
                    TryObserveMenu(root.Element, composerRegion))
                .Where(candidate => candidate is not null)
                .Cast<MenuCandidate>()
                .ToArray();
            var selectedMenu = menus
                .Where(candidate => candidate.Readback.SelectionVerified)
                .OrderBy(candidate => candidate.Area)
                .FirstOrDefault() ??
                menus
                    .OrderByDescending(candidate => candidate.Top)
                    .ThenBy(candidate => candidate.Area)
                    .FirstOrDefault();
            if (selectedMenu is not null)
            {
                return selectedMenu.Readback;
            }

            return TryObserveComposerFocus(
                    processId,
                    composerRegion) ??
                CodexMicroReadback.Closed;
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
                InvalidOperationException or
                COMException)
        {
            return new(
                CodexMicroSurfaceKind.None,
                string.Empty,
                string.Empty,
                0,
                0,
                SelectionVerified: false);
        }
    }

    private static MenuCandidate? TryObserveMenu(
        AutomationElement root,
        System.Windows.Rect? composerRegion)
    {
        var menus = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Menu));
        MenuCandidate? best = null;
        foreach (AutomationElement menu in menus)
        {
            var bounds = SafeRead(
                () => menu.Current.BoundingRectangle,
                System.Windows.Rect.Empty);
            if (!IsVisible(menu, bounds) ||
                composerRegion is { } region &&
                !region.Contains(Center(bounds)))
            {
                continue;
            }

            var descendants = menu.FindAll(
                TreeScope.Descendants,
                new OrCondition(
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.MenuItem),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.ListItem),
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.DataItem)));
            var items = descendants
                .Cast<AutomationElement>()
                .Select(element => ReadItem(element))
                .Where(item => item is not null)
                .Cast<ObservedItem>()
                .OrderBy(item => item.Top)
                .ThenBy(item => item.Left)
                .ToArray();
            if (items.Length == 0)
            {
                continue;
            }

            var selectedIndex = Array.FindIndex(
                items,
                item => item.HasKeyboardFocus);

            var selected = selectedIndex >= 0
                ? items[selectedIndex]
                : null;
            var readback = new CodexMicroReadback(
                CodexMicroSurfaceKind.Menu,
                SafeRead(() => menu.Current.Name, string.Empty),
                selected?.Name ?? string.Empty,
                selectedIndex + 1,
                items.Length,
                SelectionVerified: selected is not null,
                CanExpand: selected?.CanExpand == true,
                IsAdjustable: selected?.IsAdjustable == true);
            var candidate = new MenuCandidate(
                readback,
                bounds.Top,
                bounds.Width * bounds.Height);
            if (
                best is null ||
                readback.SelectionVerified &&
                !best.Readback.SelectionVerified ||
                readback.SelectionVerified ==
                    best.Readback.SelectionVerified &&
                candidate.Area < best.Area)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static CodexMicroReadback? TryObserveApproval(
        AutomationElement window)
    {
        var header = FindFirstVisibleByName(
            window,
            [
                "How should ChatGPT actions be approved?",
                "应如何批准 ChatGPT 操作？",
            ]);
        if (header is null)
        {
            return null;
        }

        var actionable = window.FindAll(
            TreeScope.Descendants,
            new OrCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Button),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.MenuItem),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.RadioButton),
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.ListItem)));
        var candidates = actionable
            .Cast<AutomationElement>()
            .Select(element => new
            {
                CanonicalName = CanonicalApprovalName(
                    SafeRead(
                        () => element.Current.Name,
                        string.Empty)),
                Item = ReadItem(element),
            })
            .Where(candidate =>
                candidate.CanonicalName is not null &&
                candidate.Item is not null)
            .ToArray();
        var ask = candidates
            .Where(candidate =>
                candidate.CanonicalName == "Ask for approval")
            .OrderBy(candidate => candidate.Item!.Top)
            .FirstOrDefault();
        var approve = candidates
            .Where(candidate =>
                candidate.CanonicalName == "Approve for me" &&
                candidate.Item!.Top > (ask?.Item?.Top ?? double.MaxValue))
            .OrderBy(candidate => candidate.Item!.Top)
            .FirstOrDefault();
        var full = candidates
            .Where(candidate =>
                candidate.CanonicalName == "Full access" &&
                candidate.Item!.Top >
                    (approve?.Item?.Top ?? double.MaxValue))
            .OrderBy(candidate => candidate.Item!.Top)
            .FirstOrDefault();
        if (ask is null || approve is null || full is null)
        {
            return null;
        }

        return CreateOrderedReadback(
            CodexMicroSurfaceKind.Approval,
            "Approval mode",
            [ask.Item!, approve.Item!, full.Item!]);
    }

    private static CodexMicroReadback? TryObserveDialog(
        AutomationElement window)
    {
        var confirmation = FindDialConfirmation(window);
        if (confirmation is null)
        {
            return null;
        }

        var items = new List<ObservedItem>(3);
        foreach (var names in new[]
                 {
                     new[] { "Learn more", "了解更多" },
                     new[] { "Cancel", "取消" },
                     new[] { "Confirm", "确认" },
                 })
        {
            var element = FindFirstVisibleByName(window, names);
            var item = element is null ? null : ReadItem(element);
            if (item is not null)
            {
                items.Add(item);
            }
        }

        return CreateOrderedReadback(
            CodexMicroSurfaceKind.Dialog,
            "Full access confirmation",
            items);
    }

    private static CodexMicroReadback? TryObserveComposerFocus(
        int processId,
        System.Windows.Rect? composerRegion)
    {
        var focused = AutomationElement.FocusedElement;
        if (focused is null)
        {
            return null;
        }

        var bounds = SafeRead(
            () => focused.Current.BoundingRectangle,
            System.Windows.Rect.Empty);
        var type = SafeRead(
            () => focused.Current.ControlType,
            ControlType.Custom);
        var name = SafeRead(
            () => focused.Current.Name?.Trim() ?? string.Empty,
            string.Empty);
        if (
            SafeRead(() => focused.Current.ProcessId, 0) != processId ||
            name.Length == 0 ||
            !IsVisible(focused, bounds) ||
            composerRegion is not { } region ||
            !region.Contains(Center(bounds)) ||
            type != ControlType.Button &&
            type != ControlType.Slider &&
            type != ControlType.Custom)
        {
            return null;
        }

        return new(
            CodexMicroSurfaceKind.Composer,
            "Composer",
            name,
            0,
            0,
            SelectionVerified: true,
            CanExpand: focused.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out _),
            IsAdjustable: focused.TryGetCurrentPattern(
                RangeValuePattern.Pattern,
                out _));
    }

    private static CodexMicroReadback? CreateOrderedReadback(
        CodexMicroSurfaceKind surface,
        string surfaceName,
        IReadOnlyCollection<ObservedItem> source)
    {
        var items = source
            .OrderBy(item => item.Top)
            .ThenBy(item => item.Left)
            .ToArray();
        if (items.Length < 2)
        {
            return null;
        }

        var selectedIndex = Array.FindIndex(
            items,
            item => item.HasKeyboardFocus);
        var selected = selectedIndex >= 0
            ? items[selectedIndex]
            : null;
        return new(
            surface,
            surfaceName,
            selected?.Name ?? string.Empty,
            selectedIndex + 1,
            items.Length,
            SelectionVerified: selected is not null,
            CanExpand: selected?.CanExpand == true,
            IsAdjustable: selected?.IsAdjustable == true);
    }

    private static ObservedItem? ReadItem(AutomationElement element)
    {
        var bounds = SafeRead(
            () => element.Current.BoundingRectangle,
            System.Windows.Rect.Empty);
        var name = SafeRead(
            () => element.Current.Name?.Trim() ?? string.Empty,
            string.Empty);
        if (name.Length == 0 || !IsVisible(element, bounds))
        {
            return null;
        }

        return new(
            name,
            SafeRead(
                () => element.Current.HasKeyboardFocus,
                false),
            bounds.Top,
            bounds.Left,
            element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out _),
            element.TryGetCurrentPattern(
                RangeValuePattern.Pattern,
                out _));
    }

    private static AutomationElement? FindFirstVisibleByName(
        AutomationElement root,
        IReadOnlyCollection<string> names)
    {
        foreach (var name in names)
        {
            var matches = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    name));
            foreach (AutomationElement match in matches)
            {
                var bounds = SafeRead(
                    () => match.Current.BoundingRectangle,
                    System.Windows.Rect.Empty);
                if (IsVisible(match, bounds))
                {
                    return match;
                }
            }
        }

        return null;
    }

    internal static string? CanonicalApprovalName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        foreach (var (canonical, aliases) in new[]
                 {
                     (
                         "Ask for approval",
                         new[] { "Ask for approval", "请求批准" }),
                     (
                         "Approve for me",
                         new[] { "Approve for me", "为我批准" }),
                     (
                         "Full access",
                         new[] { "Full access", "完全访问", "完整访问" }),
                 })
        {
            if (aliases.Any(alias =>
                    name.StartsWith(
                        alias,
                        StringComparison.OrdinalIgnoreCase)))
            {
                return canonical;
            }
        }

        return null;
    }

    private static bool IsVisible(
        AutomationElement element,
        System.Windows.Rect bounds) =>
        !bounds.IsEmpty &&
        bounds.Width > 0 &&
        bounds.Height > 0 &&
        !SafeRead(() => element.Current.IsOffscreen, true);

    private static System.Windows.Point Center(System.Windows.Rect bounds) =>
        new(
            bounds.Left + bounds.Width / 2,
            bounds.Top + bounds.Height / 2);

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
                InvalidOperationException or
                COMException)
        {
            return fallback;
        }
    }

    private sealed record MenuCandidate(
        CodexMicroReadback Readback,
        double Top,
        double Area);

    private sealed record ObservedItem(
        string Name,
        bool HasKeyboardFocus,
        double Top,
        double Left,
        bool CanExpand,
        bool IsAdjustable);
}
