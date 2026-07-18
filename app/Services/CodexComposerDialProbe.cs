using System.Windows.Automation;
using static CodexController.Services.CodexAutomationLocator;

namespace CodexController.Services;

internal static class CodexComposerDialProbe
{
    private const int DialPopupPollIntervalMs = 24;

    internal static IReadOnlyList<DialControl> FindDialControls(
        AutomationElement window)
    {
        var editor = FindComposerEditor(window);
        if (editor is null)
        {
            return [];
        }

        System.Windows.Rect editorBounds;
        try
        {
            editorBounds = editor.Current.BoundingRectangle;
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }

        var composerRegion = new System.Windows.Rect(
            editorBounds.Left - 80,
            editorBounds.Top - 180,
            editorBounds.Width + 160,
            editorBounds.Height + 360);
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        var controls = new List<DialControl>();
        foreach (AutomationElement button in buttons)
        {
            try
            {
                var name = button.Current.Name?.Trim() ?? string.Empty;
                var className = button.Current.ClassName ?? string.Empty;
                var bounds = button.Current.BoundingRectangle;
                if (
                    name.Length == 0 ||
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    bounds.IsEmpty ||
                    !composerRegion.IntersectsWith(bounds) ||
                    !ComposerDialPolicy.HasComposerButtonClassToken(
                        className) ||
                    ComposerDialPolicy.HasClassToken(
                        className,
                        "aspect-square") ||
                    !ComposerDialActionPolicy.IsPickerControl(name) ||
                    !CanOpenDialControl(
                        button,
                        className,
                        out var allowInvoke,
                        out var supportsExpandCollapse))
                {
                    continue;
                }

                var automationId =
                    button.Current.AutomationId?.Trim() ?? string.Empty;
                var stableKey = automationId.Length > 0
                    ? $"id:{automationId}"
                    : $"name:{ComposerChoiceNormalizer.Normalize(name)}";
                controls.Add(new DialControl(
                    stableKey,
                    name,
                    bounds.Left,
                    bounds.Top,
                    bounds.Width,
                    bounds.Height,
                    ComposerDialActionPolicy.PickerControlPriority(
                        supportsExpandCollapse,
                        allowInvoke),
                    allowInvoke,
                    button));
            }
            catch (ElementNotAvailableException)
            {
                // Chromium may replace one composer button mid-query.
            }
        }

        return controls
            .OrderBy(control => control.Priority)
            .ThenBy(control => control.Left)
            .ThenBy(control => control.Top)
            .ToArray();
    }

    private static bool CanOpenDialControl(
        AutomationElement element,
        string className,
        out bool allowInvoke,
        out bool supportsExpandCollapse)
    {
        allowInvoke = false;
        supportsExpandCollapse = false;
        if (
            element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out _))
        {
            supportsExpandCollapse = true;
            return true;
        }

        bool isKeyboardFocusable;
        try
        {
            isKeyboardFocusable =
                element.Current.IsKeyboardFocusable;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        allowInvoke =
            ComposerDialPolicy.IsConservativeInvokeTrigger(
                className,
                isKeyboardFocusable) &&
            element.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out _);
        return allowInvoke;
    }

    internal static bool TryOpenDialControl(
        AutomationElement element,
        bool allowInvoke)
    {
        if (
            element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out var expandObject) &&
            expandObject is ExpandCollapsePattern expand)
        {
            if (
                expand.Current.ExpandCollapseState !=
                ExpandCollapseState.Expanded)
            {
                expand.Expand();
            }

            return true;
        }

        if (
            allowInvoke &&
            element.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out var invokeObject) &&
            invokeObject is InvokePattern invoke)
        {
            invoke.Invoke();
            return true;
        }

        return false;
    }

    internal static DialPopupProbe ProbeDialPopup(
        AutomationElement window,
        int processId,
        System.Windows.Rect? knownComposerRegion = null,
        ComposerDialMenuContainerGeometry? knownTrigger = null)
    {
        var composerRegion =
            knownComposerRegion ??
            TryGetComposerPopupRegion(window);
        var popupOptions = new List<DialPopupOptionCandidate>();
        foreach (var root in FindDialPopupRoots(window, processId))
        {
            var rootKey = StableAutomationKey(
                root.Element,
                root.IsPopupWindow
                    ? "popup-window"
                    : "main-window");
            var elements = new List<AutomationElement>();
            try
            {
                elements.Add(root.Element);
                elements.AddRange(
                    root.Element.FindAll(
                            TreeScope.Descendants,
                            new OrCondition(
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.Menu),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.List),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.MenuItem),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.ListItem),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.DataItem),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.Edit)))
                        .Cast<AutomationElement>());
            }
            catch
            {
                continue;
            }

            foreach (var element in elements)
            {
                try
                {
                    var current = element.Current;
                    var bounds = current.BoundingRectangle;
                    var kind = DialPopupKind(current.ControlType);
                    if (kind is null)
                    {
                        continue;
                    }

                    var nearComposer =
                        composerRegion is { } region &&
                        region.Contains(
                            new System.Windows.Point(
                                bounds.Left + bounds.Width / 2,
                                bounds.Top + bounds.Height / 2));
                    var hasPopupAncestor =
                        HasPopupAncestor(
                            element,
                            root.Element,
                            includeList:
                                kind is
                                    ComposerDialPopupElementKind.Edit or
                                    ComposerDialPopupElementKind.OptionItem);
                    var semanticSearch =
                        kind == ComposerDialPopupElementKind.Edit &&
                        ComposerDialPolicy.LooksLikeSearchEdit(
                            current.Name,
                            current.AutomationId,
                            current.ClassName,
                            current.HelpText);
                    var evidence = new ComposerDialPopupEvidence(
                        kind.Value,
                        current.IsEnabled,
                        current.IsOffscreen,
                        bounds.IsEmpty,
                        nearComposer,
                        current.HasKeyboardFocus,
                        current.IsKeyboardFocusable,
                        root.IsPopupWindow,
                        hasPopupAncestor,
                        semanticSearch,
                        element.TryGetCurrentPattern(
                            SelectionPattern.Pattern,
                            out _) ||
                        element.TryGetCurrentPattern(
                            SelectionItemPattern.Pattern,
                            out _));
                    if (!ComposerDialPolicy.IsPopupEvidence(evidence))
                    {
                        continue;
                    }

                    var name = current.Name?.Trim() ?? string.Empty;
                    if (
                        (
                            kind is
                                ComposerDialPopupElementKind.MenuItem or
                                ComposerDialPopupElementKind.OptionItem
                        ) &&
                        name.Length > 0 &&
                        ComposerDialActionPolicy.IsPickerControl(name) &&
                        current.IsKeyboardFocusable &&
                        TryGetDialPopupContainer(
                            element,
                            root.Element,
                            out var containerKey,
                            out var containerBounds))
                    {
                        var canExpand =
                            element.TryGetCurrentPattern(
                                ExpandCollapsePattern.Pattern,
                                out _);
                        var optionKey = StableAutomationKey(
                            element,
                            string.Join(
                                ':',
                                containerKey,
                                kind.Value,
                                ComposerChoiceNormalizer.Normalize(name),
                                Math.Round(bounds.Left),
                                Math.Round(bounds.Top)));
                        popupOptions.Add(
                            new DialPopupOptionCandidate(
                                optionKey,
                                name,
                                rootKey,
                                containerKey,
                                containerBounds.Left,
                                containerBounds.Top,
                                containerBounds.Width,
                                containerBounds.Height,
                                bounds.Left,
                                bounds.Top,
                                bounds.Width,
                                bounds.Height,
                                current.HasKeyboardFocus,
                                IsDialPopupOptionSelected(element),
                                canExpand,
                                element));
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // Chromium can replace popup nodes while they mount.
                }
            }
        }

        var allSurfaces =
            BuildDialPopupSurfaces(popupOptions);
        var triggerGeometries =
            knownTrigger is { } trigger
                ? [trigger]
                : FindDialControls(window)
                    .Select(control =>
                        new ComposerDialMenuContainerGeometry(
                            control.Left,
                            control.Top,
                            control.Width,
                            control.Height))
                    .ToArray();
        var associatedSurfaceKeys =
            ComposerDialMenuSelectionPolicy
                .ResolveAssociatedSurfaceKeys(
                    allSurfaces
                        .Select(surface =>
                            new ComposerDialMenuSurfaceGeometrySnapshot(
                                surface.Key,
                                ToContainerGeometry(
                                    surface.Bounds)))
                        .ToArray(),
                    triggerGeometries);
        var surfaces = allSurfaces
            .Where(surface =>
                associatedSurfaceKeys.Contains(surface.Key))
            .ToArray();
        var options = surfaces
            .SelectMany(surface => surface.Options)
            .ToArray();
        var focusedOption = options
            .FirstOrDefault(option =>
                option.HasKeyboardFocus);
        var focusedName =
            focusedOption?.Name ??
            TryReadFocusedPopupName(
                processId,
                composerRegion);
        if (
            focusedOption is null &&
            !string.IsNullOrWhiteSpace(focusedName))
        {
            var normalizedFocusedName =
                ComposerChoiceNormalizer.Normalize(focusedName);
            var nameMatches = options
                .Where(option =>
                    string.Equals(
                        ComposerChoiceNormalizer.Normalize(option.Name),
                        normalizedFocusedName,
                        StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            if (nameMatches.Length == 1)
            {
                focusedOption = nameMatches[0];
            }
        }

        var isOpen = surfaces.Length > 0;
        return new DialPopupProbe(
            isOpen,
            focusedOption?.Name ?? focusedName,
            focusedOption?.Key,
            string.Join(
                '|',
                surfaces
                    .Select(surface =>
                        string.Join(
                            ':',
                            surface.Key,
                            Math.Round(surface.Bounds.Left),
                            Math.Round(surface.Bounds.Top),
                            Math.Round(surface.Bounds.Width),
                            Math.Round(surface.Bounds.Height)))
                    .Concat(options.Select(option =>
                        string.Join(
                            ':',
                            option.Key,
                            option.SurfaceKey,
                            ComposerChoiceNormalizer.Normalize(option.Name))))
                    .Order(StringComparer.Ordinal)),
            surfaces,
            options,
            composerRegion);
    }

    private static IReadOnlyList<DialPopupSurface>
        BuildDialPopupSurfaces(
            IReadOnlyList<DialPopupOptionCandidate> candidates)
    {
        var surfaces = new List<DialPopupSurface>();
        foreach (var rootGroup in candidates
                     .GroupBy(
                         candidate => candidate.RootKey,
                         StringComparer.Ordinal))
        {
            var containers = rootGroup
                .GroupBy(
                    candidate => candidate.ContainerKey,
                    StringComparer.Ordinal)
                .Select(group =>
                {
                    var first = group.First();
                    return new DialPopupContainer(
                        group.Key,
                        new(
                            first.ContainerLeft,
                            first.ContainerTop,
                            first.ContainerWidth,
                            first.ContainerHeight),
                        group.ToArray());
                })
                .OrderBy(container => container.Bounds.Top)
                .ThenBy(container => container.Bounds.Left)
                .ToArray();
            var clusters = new List<List<DialPopupContainer>>();
            foreach (var container in containers)
            {
                var cluster = clusters.FirstOrDefault(candidate =>
                    candidate.Any(existing =>
                        ComposerDialMenuSelectionPolicy
                            .SharesVisualSurface(
                                ToContainerGeometry(
                                    existing.Bounds),
                                ToContainerGeometry(
                                    container.Bounds))));
                if (cluster is null)
                {
                    cluster = [];
                    clusters.Add(cluster);
                }

                cluster.Add(container);
            }

            foreach (var cluster in clusters)
            {
                var orderedContainers = cluster
                    .OrderBy(container =>
                        container.Bounds.Top)
                    .ThenBy(container =>
                        container.Bounds.Left)
                    .ToArray();
                var surfaceKey = string.Join(
                    ':',
                    "surface",
                    rootGroup.Key,
                    orderedContainers[0].Key);
                var options = orderedContainers
                    .SelectMany(container =>
                        container.Options)
                    .GroupBy(
                        option => option.Key,
                        StringComparer.Ordinal)
                    .Select(group =>
                        group
                            .OrderByDescending(option =>
                                option.HasKeyboardFocus)
                            .ThenByDescending(option =>
                                option.IsSelected)
                            .First())
                    .OrderBy(option => option.Top)
                    .ThenBy(option => option.Left)
                    .Select(option =>
                        new DialPopupOption(
                            option.Key,
                            option.Name,
                            option.ContainerKey,
                            surfaceKey,
                            option.Left,
                            option.Top,
                            option.Width,
                            option.Height,
                            option.HasKeyboardFocus,
                            option.IsSelected,
                            option.CanExpand,
                            option.Element))
                    .ToArray();
                surfaces.Add(
                    new(
                        surfaceKey,
                        UnionBounds(
                            orderedContainers.Select(
                                container =>
                                    container.Bounds)),
                        options));
            }
        }

        return surfaces
            .OrderBy(surface => surface.Bounds.Top)
            .ThenBy(surface => surface.Bounds.Left)
            .ToArray();
    }

    private static ComposerDialMenuContainerGeometry
        ToContainerGeometry(System.Windows.Rect bounds)
    {
        return new(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height);
    }

    private static System.Windows.Rect UnionBounds(
        IEnumerable<System.Windows.Rect> bounds)
    {
        var result = System.Windows.Rect.Empty;
        foreach (var item in bounds)
        {
            result = result.IsEmpty
                ? item
                : System.Windows.Rect.Union(result, item);
        }

        return result;
    }

    private static string StableAutomationKey(
        AutomationElement element,
        string fallback)
    {
        try
        {
            var runtimeId = element.GetRuntimeId();
            if (runtimeId is { Length: > 0 })
            {
                return $"rid:{string.Join('.', runtimeId)}";
            }
        }
        catch
        {
            // The geometry fallback is scoped to one mounted popup.
        }

        return $"fallback:{fallback}";
    }

    internal static bool IsDialPopupOptionSelected(
        AutomationElement element)
    {
        try
        {
            if (
                element.TryGetCurrentPattern(
                    SelectionItemPattern.Pattern,
                    out var selectionObject) &&
                selectionObject is SelectionItemPattern selection &&
                selection.Current.IsSelected)
            {
                return true;
            }

            var itemBounds = element.Current.BoundingRectangle;
            if (itemBounds.IsEmpty)
            {
                return false;
            }

            var images = element.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Image));
            foreach (AutomationElement image in images)
            {
                var bounds = image.Current.BoundingRectangle;
                var className =
                    image.Current.ClassName ?? string.Empty;
                if (ComposerDialMenuSelectionPolicy
                    .LooksLikeSelectionMarker(
                        ToContainerGeometry(itemBounds),
                        ToContainerGeometry(bounds),
                        className))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal static bool TryGetDialPopupContainer(
        AutomationElement element,
        AutomationElement root,
        out string key,
        out System.Windows.Rect bounds)
    {
        key = string.Empty;
        bounds = System.Windows.Rect.Empty;
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = walker.GetParent(element);
            for (
                var depth = 0;
                current is not null &&
                depth < 16;
                depth++)
            {
                var controlType = current.Current.ControlType;
                if (
                    controlType == ControlType.Menu ||
                    controlType == ControlType.List)
                {
                    bounds = current.Current.BoundingRectangle;
                    if (bounds.IsEmpty)
                    {
                        return false;
                    }

                    var runtimeId = current.GetRuntimeId();
                    key = runtimeId is { Length: > 0 }
                        ? string.Join('.', runtimeId)
                        : string.Join(
                            ':',
                            controlType.ProgrammaticName,
                            current.Current.Name,
                            Math.Round(bounds.Left),
                            Math.Round(bounds.Top));
                    return true;
                }

                if (current.Equals(root))
                {
                    break;
                }

                current = walker.GetParent(current);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    internal static System.Windows.Rect? TryGetComposerPopupRegion(
        AutomationElement window)
    {
        var editor = FindComposerEditor(window);
        if (editor is null)
        {
            return null;
        }

        try
        {
            var bounds = editor.Current.BoundingRectangle;
            return new System.Windows.Rect(
                bounds.Left - 160,
                bounds.Top - 720,
                bounds.Width + 320,
                bounds.Height + 900);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<DialPopupRoot> FindDialPopupRoots(
        AutomationElement mainWindow,
        int processId)
    {
        var roots = new List<DialPopupRoot>
        {
            new(mainWindow, IsPopupWindow: false),
        };
        try
        {
            var processWindows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(
                    AutomationElement.ProcessIdProperty,
                    processId));
            foreach (AutomationElement processWindow in processWindows)
            {
                if (!processWindow.Equals(mainWindow))
                {
                    roots.Add(
                        new(
                            processWindow,
                            IsPopupWindow: true));
                }
            }
        }
        catch
        {
            // The main window still provides the normal Electron popup tree.
        }

        return roots;
    }

    private static bool HasPopupAncestor(
        AutomationElement element,
        AutomationElement root,
        bool includeList)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = walker.GetParent(element);
            for (
                var depth = 0;
                current is not null &&
                depth < 16 &&
                !current.Equals(root);
                depth++)
            {
                var type = current.Current.ControlType;
                if (
                    type == ControlType.Menu ||
                    type == ControlType.MenuItem ||
                    (includeList && type == ControlType.List))
                {
                    return true;
                }

                current = walker.GetParent(current);
            }
        }
        catch
        {
            // A focused localized Search edit is sufficient without ancestry.
        }

        return false;
    }

    private static string? TryReadFocusedPopupName(
        int processId,
        System.Windows.Rect? composerRegion)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (
                focused is null ||
                focused.Current.ProcessId != processId ||
                focused.Current.IsOffscreen ||
                focused.Current.BoundingRectangle.IsEmpty)
            {
                return null;
            }

            if (DialPopupKind(focused.Current.ControlType) is null)
            {
                return null;
            }

            var bounds = focused.Current.BoundingRectangle;
            if (
                composerRegion is not { } region ||
                !region.Contains(
                    new System.Windows.Point(
                        bounds.Left + bounds.Width / 2,
                        bounds.Top + bounds.Height / 2)))
            {
                return null;
            }

            var name = focused.Current.Name?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    internal static DialConfirmationDialog? FindDialConfirmation(
        AutomationElement window)
    {
        AutomationElementCollection dialogs;
        try
        {
            dialogs = window.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Window));
        }
        catch
        {
            return null;
        }

        foreach (AutomationElement dialog in dialogs)
        {
            try
            {
                var className =
                    dialog.Current.ClassName ?? string.Empty;
                if (
                    dialog.Current.IsOffscreen ||
                    dialog.Current.BoundingRectangle.IsEmpty ||
                    !className.Contains(
                        "codex-dialog",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var confirmButton = FindVisibleNamedButton(
                    dialog,
                    ["Confirm", "确认"]);
                var cancelButton = FindVisibleNamedButton(
                    dialog,
                    ["Cancel", "取消"]);
                if (
                    confirmButton is null ||
                    cancelButton is null)
                {
                    continue;
                }

                var texts = dialog.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(
                        AutomationElement.ControlTypeProperty,
                        ControlType.Text));
                foreach (AutomationElement text in texts)
                {
                    string title;
                    try
                    {
                        if (
                            text.Current.IsOffscreen ||
                            text.Current.BoundingRectangle.IsEmpty)
                        {
                            continue;
                        }

                        title = text.Current.Name?.Trim() ??
                            string.Empty;
                    }
                    catch (ElementNotAvailableException)
                    {
                        continue;
                    }

                    if (IsFullAccessConfirmationTitle(title))
                    {
                        return new(
                            title,
                            confirmButton,
                            cancelButton);
                    }
                }
            }
            catch (ElementNotAvailableException)
            {
                // Chromium may replace the dialog while it is being queried.
            }
        }

        return null;
    }

    private static bool IsFullAccessConfirmationTitle(string title)
    {
        return
            title.Contains(
                "Full Access",
                StringComparison.OrdinalIgnoreCase) ||
            title.Contains(
                "完全访问",
                StringComparison.Ordinal) ||
            title.Contains(
                "完整访问",
                StringComparison.Ordinal);
    }

    internal static bool TryInvokeDialConfirmationButton(
        AutomationElement button)
    {
        try
        {
            if (
                !button.Current.IsEnabled ||
                !button.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var patternObject) ||
                patternObject is not InvokePattern pattern)
            {
                return false;
            }

            pattern.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool WaitForDialConfirmationClosed(
        AutomationElement window,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        do
        {
            if (FindDialConfirmation(window) is null)
            {
                return true;
            }

            Thread.Sleep(DialPopupPollIntervalMs);
        }
        while (Environment.TickCount64 < deadline);

        return FindDialConfirmation(window) is null;
    }

    internal static ComposerDialPopupElementKind? DialPopupKind(
        ControlType type)
    {
        if (type == ControlType.Menu)
        {
            return ComposerDialPopupElementKind.Menu;
        }

        if (type == ControlType.List)
        {
            return ComposerDialPopupElementKind.ListBox;
        }

        if (type == ControlType.MenuItem)
        {
            return ComposerDialPopupElementKind.MenuItem;
        }

        if (
            type == ControlType.ListItem ||
            type == ControlType.DataItem)
        {
            return ComposerDialPopupElementKind.OptionItem;
        }

        if (type == ControlType.Edit)
        {
            return ComposerDialPopupElementKind.Edit;
        }

        return null;
    }
}

internal sealed record DialControl(
    string Key,
    string Name,
    double Left,
    double Top,
    double Width,
    double Height,
    int Priority,
    bool AllowInvoke,
    AutomationElement Element);

internal sealed record DialPopupRoot(
    AutomationElement Element,
    bool IsPopupWindow);

internal sealed record DialPopupContainer(
    string Key,
    System.Windows.Rect Bounds,
    IReadOnlyList<DialPopupOptionCandidate> Options);

internal sealed record DialPopupOptionCandidate(
    string Key,
    string Name,
    string RootKey,
    string ContainerKey,
    double ContainerLeft,
    double ContainerTop,
    double ContainerWidth,
    double ContainerHeight,
    double Left,
    double Top,
    double Width,
    double Height,
    bool HasKeyboardFocus,
    bool IsSelected,
    bool CanExpand,
    AutomationElement Element);

internal sealed record DialPopupOption(
    string Key,
    string Name,
    string ContainerKey,
    string SurfaceKey,
    double Left,
    double Top,
    double Width,
    double Height,
    bool HasKeyboardFocus,
    bool IsSelected,
    bool CanExpand,
    AutomationElement Element);

internal sealed record DialConfirmationDialog(
    string Title,
    AutomationElement ConfirmButton,
    AutomationElement CancelButton);

internal sealed record DialPopupSurface(
    string Key,
    System.Windows.Rect Bounds,
    IReadOnlyList<DialPopupOption> Options);

internal sealed record DialPopupProbe(
    bool IsOpen,
    string? FocusedName,
    string? FocusedOptionKey,
    string Signature,
    IReadOnlyList<DialPopupSurface> Surfaces,
    IReadOnlyList<DialPopupOption> Options,
    System.Windows.Rect? ComposerRegion);
