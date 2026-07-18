using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace CodexMicro.Desktop.Services;

internal enum CodexSelectionSurface
{
    Menu,
    Dialog,
}

internal readonly record struct CodexMenuSelection(
    string MenuName,
    string ItemName,
    int Position,
    int Count,
    CodexSelectionSurface Surface = CodexSelectionSurface.Menu)
{
    public string DisplayText => CodexMenuSelectionObserver.Format(this);
}

/// <summary>
/// Reads Codex's accessibility tree only to mirror the selection made by the
/// official Micro bridge. Input continues to travel exclusively through VHF.
/// </summary>
internal sealed class CodexMenuSelectionObserver
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(35),
        TimeSpan.FromMilliseconds(70),
        TimeSpan.FromMilliseconds(120),
    ];

    public async Task<CodexMenuSelection?> ObserveAsync(
        string? packageRoot,
        CancellationToken cancellationToken = default)
    {
        CodexMenuSelection? lastResult = null;
        foreach (var delay in RetryDelays)
        {
            await Task.Delay(delay, cancellationToken);
            lastResult = await Task.Run(
                () => TryObserve(packageRoot),
                cancellationToken);
            if (lastResult is { Position: > 0 })
            {
                return lastResult;
            }
        }

        return lastResult;
    }

    public Task<CodexMenuSelection?> ObserveCurrentAsync(
        string? packageRoot,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => TryObserve(packageRoot), cancellationToken);

    internal static string Format(CodexMenuSelection selection)
    {
        var item = NormalizeLabel(selection.ItemName);
        if (selection.Position > 0 && selection.Count > 0)
        {
            return $"{selection.Position} / {selection.Count}  ·  {item}";
        }

        var menu = NormalizeLabel(selection.MenuName);
        if (selection.Surface == CodexSelectionSurface.Dialog)
        {
            return "确认权限  ·  转动选择";
        }

        return string.IsNullOrWhiteSpace(menu)
            ? "转动旋钮选择"
            : $"转动选择  ·  {menu}";
    }

    private static CodexMenuSelection? TryObserve(string? packageRoot)
    {
        try
        {
            var desktop = AutomationElement.RootElement;
            var windows = desktop.FindAll(
                TreeScope.Children,
                Condition.TrueCondition);
            var processCache = new Dictionary<int, bool>();
            var candidates = new List<MenuCandidate>();

            for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
            {
                var window = windows[windowIndex];
                var processId = SafeRead(() => window.Current.ProcessId, 0);
                if (processId == 0 ||
                    !IsCodexProcess(processId, packageRoot, processCache))
                {
                    continue;
                }

                var menuCondition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Menu);
                var menus = window.FindAll(
                    TreeScope.Descendants,
                    menuCondition);
                for (var menuIndex = 0; menuIndex < menus.Count; menuIndex++)
                {
                    if (TryReadMenu(menus[menuIndex]) is { } candidate)
                    {
                        candidates.Add(candidate);
                    }
                }

                if (TryReadDialog(window) is { } dialog)
                {
                    candidates.Add(dialog);
                }
            }

            var dialogCandidate = candidates
                .Where(candidate =>
                    candidate.Selection.Surface == CodexSelectionSurface.Dialog)
                .OrderByDescending(candidate => candidate.Selection.Position > 0)
                .ThenBy(candidate => candidate.Area)
                .FirstOrDefault();
            if (dialogCandidate is not null)
            {
                return dialogCandidate.Selection;
            }

            var focused = candidates
                .Where(candidate => candidate.Selection.Position > 0)
                .OrderBy(candidate => candidate.Area)
                .FirstOrDefault();
            if (focused is not null)
            {
                return focused.Selection;
            }

            return candidates
                .OrderByDescending(candidate => candidate.Top)
                .ThenBy(candidate => candidate.Area)
                .Select(candidate => (CodexMenuSelection?)candidate.Selection)
                .FirstOrDefault();
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
                InvalidOperationException or
                COMException or
                Win32Exception)
        {
            return null;
        }
    }

    private static MenuCandidate? TryReadMenu(AutomationElement menu)
    {
        var rectangle = SafeRead(
            () => menu.Current.BoundingRectangle,
            System.Windows.Rect.Empty);
        if (rectangle.IsEmpty ||
            rectangle.Width <= 0 ||
            rectangle.Height <= 0 ||
            SafeRead(() => menu.Current.IsOffscreen, true))
        {
            return null;
        }

        var menuItemCondition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            ControlType.MenuItem);
        var descendants = menu.FindAll(
            TreeScope.Descendants,
            menuItemCondition);
        var items = new List<MenuItemCandidate>(descendants.Count);
        for (var itemIndex = 0; itemIndex < descendants.Count; itemIndex++)
        {
            var item = descendants[itemIndex];
            var itemRectangle = SafeRead(
                () => item.Current.BoundingRectangle,
                System.Windows.Rect.Empty);
            var name = SafeRead(() => item.Current.Name, string.Empty);
            if (itemRectangle.IsEmpty ||
                itemRectangle.Width <= 0 ||
                itemRectangle.Height <= 0 ||
                string.IsNullOrWhiteSpace(name) ||
                SafeRead(() => item.Current.IsOffscreen, true))
            {
                continue;
            }

            items.Add(new MenuItemCandidate(
                name,
                SafeRead(() => item.Current.HasKeyboardFocus, false),
                itemRectangle.Top,
                itemRectangle.Left));
        }

        items.Sort(static (left, right) =>
        {
            var top = left.Top.CompareTo(right.Top);
            return top != 0 ? top : left.Left.CompareTo(right.Left);
        });

        var focusedIndex = items.FindIndex(item => item.HasKeyboardFocus);
        var menuName = SafeRead(() => menu.Current.Name, string.Empty);
        var selection = focusedIndex >= 0
            ? new CodexMenuSelection(
                menuName,
                items[focusedIndex].Name,
                focusedIndex + 1,
                items.Count)
            : new CodexMenuSelection(
                menuName,
                string.Empty,
                0,
                items.Count);
        return new MenuCandidate(
            selection,
            rectangle.Top,
            rectangle.Width * rectangle.Height);
    }

    private static MenuCandidate? TryReadDialog(AutomationElement window)
    {
        var items = new List<MenuItemCandidate>(3);
        foreach (var name in new[] { "Learn more", "Cancel", "Confirm" })
        {
            var condition = new AndCondition(
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Button),
                new PropertyCondition(
                    AutomationElement.NameProperty,
                    name));
            var button = window.FindFirst(TreeScope.Descendants, condition);
            if (button is null)
            {
                continue;
            }

            var rectangle = SafeRead(
                () => button.Current.BoundingRectangle,
                System.Windows.Rect.Empty);
            if (rectangle.IsEmpty ||
                rectangle.Width <= 0 ||
                rectangle.Height <= 0 ||
                SafeRead(() => button.Current.IsOffscreen, true))
            {
                continue;
            }

            items.Add(new MenuItemCandidate(
                name,
                SafeRead(() => button.Current.HasKeyboardFocus, false),
                rectangle.Top,
                rectangle.Left));
        }

        if (!items.Any(item => item.Name == "Cancel") ||
            !items.Any(item => item.Name == "Confirm"))
        {
            return null;
        }

        items.Sort(static (left, right) =>
        {
            var top = left.Top.CompareTo(right.Top);
            return top != 0 ? top : left.Left.CompareTo(right.Left);
        });
        var focusedIndex = items.FindIndex(item => item.HasKeyboardFocus);
        var selection = focusedIndex >= 0
            ? new CodexMenuSelection(
                "Full access confirmation",
                items[focusedIndex].Name,
                focusedIndex + 1,
                items.Count,
                CodexSelectionSurface.Dialog)
            : new CodexMenuSelection(
                "Full access confirmation",
                string.Empty,
                0,
                items.Count,
                CodexSelectionSurface.Dialog);
        var top = items.Min(item => item.Top);
        var left = items.Min(item => item.Left);
        var right = items.Max(item => item.Left);
        return new MenuCandidate(
            selection,
            top,
            Math.Max(1, right - left));
    }

    private static bool IsCodexProcess(
        int processId,
        string? packageRoot,
        IDictionary<int, bool> cache)
    {
        if (cache.TryGetValue(processId, out var cached))
        {
            return cached;
        }

        var isCodex = false;
        try
        {
            using var process = Process.GetProcessById(processId);
            var path = process.MainModule?.FileName;
            isCodex = !string.IsNullOrWhiteSpace(path) &&
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
            isCodex = false;
        }

        cache[processId] = isCodex;
        return isCodex;
    }

    private static string NormalizeLabel(string value)
    {
        var normalized = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
        return normalized.Length <= 52
            ? normalized
            : $"{normalized[..49]}…";
    }

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
        CodexMenuSelection Selection,
        double Top,
        double Area);

    private sealed record MenuItemCandidate(
        string Name,
        bool HasKeyboardFocus,
        double Top,
        double Left);
}
