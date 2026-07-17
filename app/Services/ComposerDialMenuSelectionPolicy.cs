namespace CodexController.Services;

/// <summary>
/// A popup surface is one visual Radix menu/listbox layer. Multiple lists
/// inside one picker (for example projects plus footer actions) must share
/// the same surface key; a nested submenu gets its own child surface key.
/// </summary>
internal readonly record struct ComposerDialMenuSurfaceSnapshot(
    string Key,
    string? ParentKey,
    int Depth,
    long MountSequence,
    bool IsOwnedByDialSession,
    bool ContainsKeyboardFocus,
    bool ContainsActiveOption);

internal readonly record struct ComposerDialMenuOptionSnapshot(
    string Key,
    string SurfaceKey,
    string Name,
    int VisualOrder,
    bool HasKeyboardFocus,
    bool IsActiveDescendant,
    bool IsSelected);

internal readonly record struct ComposerDialMenuTarget(
    string SurfaceKey,
    string OptionKey,
    string Name,
    int VisualIndex);

internal readonly record struct ComposerDialMenuContainerGeometry(
    double Left,
    double Top,
    double Width,
    double Height);

internal readonly record struct ComposerDialMenuSurfaceGeometrySnapshot(
    string Key,
    ComposerDialMenuContainerGeometry Bounds);

/// <summary>
/// Resolves the logical dial cursor independently from transient Chromium
/// keyboard focus. The UI Automation adapter remains responsible for
/// collecting stable keys and applying the resulting focus/highlight.
/// </summary>
internal static class ComposerDialMenuSelectionPolicy
{
    private const double TriggerAssociationDistance = 180;
    private const double SurfaceAssociationDistance = 120;

    public static string? ResolveActiveSurface(
        IReadOnlyList<ComposerDialMenuSurfaceSnapshot> surfaces,
        string? previousSurfaceKey)
    {
        var owned = surfaces
            .Where(surface =>
                surface.IsOwnedByDialSession &&
                !string.IsNullOrWhiteSpace(surface.Key))
            .ToArray();
        if (owned.Length == 0)
        {
            return null;
        }

        var focused = owned
            .Where(surface =>
                surface.ContainsKeyboardFocus ||
                surface.ContainsActiveOption)
            .OrderByDescending(surface => surface.Depth)
            .ThenByDescending(surface => surface.MountSequence)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(focused.Key))
        {
            return focused.Key;
        }

        if (!string.IsNullOrWhiteSpace(previousSurfaceKey))
        {
            var descendants = owned
                .Where(surface =>
                    IsDescendantOf(
                        surface,
                        previousSurfaceKey,
                        owned))
                .OrderByDescending(surface => surface.Depth)
                .ThenByDescending(surface => surface.MountSequence)
                .ToArray();
            if (descendants.Length > 0)
            {
                return descendants[0].Key;
            }

            if (owned.Any(surface =>
                    string.Equals(
                        surface.Key,
                        previousSurfaceKey,
                        StringComparison.Ordinal)))
            {
                return previousSurfaceKey;
            }
        }

        return owned
            .OrderBy(surface => surface.Depth)
            .ThenBy(surface => surface.MountSequence)
            .First()
            .Key;
    }

    public static ComposerDialMenuTarget? ResolveTarget(
        IReadOnlyList<ComposerDialMenuOptionSnapshot> options,
        string? activeSurfaceKey,
        string? previousOptionKey,
        int delta)
    {
        if (
            string.IsNullOrWhiteSpace(activeSurfaceKey) ||
            delta == 0)
        {
            return null;
        }

        var activeOptions = options
            .Where(option =>
                string.Equals(
                    option.SurfaceKey,
                    activeSurfaceKey,
                    StringComparison.Ordinal))
            .OrderBy(option => option.VisualOrder)
            .ThenBy(option => option.Key, StringComparer.Ordinal)
            .ToArray();
        if (activeOptions.Length == 0)
        {
            return null;
        }

        var currentIndex = FindCurrentIndex(
            activeOptions,
            previousOptionKey);
        var targetIndex = ComposerDialPolicy.ResolveVisualOptionIndex(
            activeOptions.Length,
            currentIndex,
            delta);
        if (targetIndex < 0)
        {
            return null;
        }

        var target = activeOptions[targetIndex];
        return new(
            target.SurfaceKey,
            target.Key,
            target.Name,
            targetIndex);
    }

    public static ComposerDialMenuTarget? ResolveInitialTarget(
        IReadOnlyList<ComposerDialMenuOptionSnapshot> options,
        string? activeSurfaceKey,
        string? parentSelectionName = null,
        bool preferFirst = false)
    {
        if (string.IsNullOrWhiteSpace(activeSurfaceKey))
        {
            return null;
        }

        var activeOptions = options
            .Where(option =>
                string.Equals(
                    option.SurfaceKey,
                    activeSurfaceKey,
                    StringComparison.Ordinal))
            .OrderBy(option => option.VisualOrder)
            .ThenBy(option => option.Key, StringComparer.Ordinal)
            .ToArray();
        if (activeOptions.Length == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(parentSelectionName))
        {
            var normalizedParent =
                NormalizeLabel(parentSelectionName);
            var suffixMatch = activeOptions
                .Select(option => new
                {
                    Option = option,
                    Normalized = NormalizeLabel(option.Name),
                })
                .Where(candidate =>
                    candidate.Normalized.Length > 0 &&
                    normalizedParent.EndsWith(
                        candidate.Normalized,
                        StringComparison.Ordinal))
                .OrderByDescending(candidate =>
                    candidate.Normalized.Length)
                .ThenBy(candidate =>
                    candidate.Option.VisualOrder)
                .Select(candidate => candidate.Option)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(suffixMatch.Key))
            {
                return ToTarget(suffixMatch, activeOptions);
            }
        }

        var target = preferFirst
            ? activeOptions[0]
            : activeOptions.FirstOrDefault(option =>
                  option.HasKeyboardFocus ||
                  option.IsActiveDescendant)
              is { Key.Length: > 0 } focused
                ? focused
                : activeOptions.FirstOrDefault(option =>
                      option.IsSelected)
                  is { Key.Length: > 0 } selected
                    ? selected
                    : activeOptions[0];
        return ToTarget(target, activeOptions);
    }

    public static bool SharesVisualSurface(
        ComposerDialMenuContainerGeometry left,
        ComposerDialMenuContainerGeometry right)
    {
        if (
            left.Width <= 0 ||
            left.Height <= 0 ||
            right.Width <= 0 ||
            right.Height <= 0)
        {
            return false;
        }

        var overlap =
            Math.Min(
                left.Left + left.Width,
                right.Left + right.Width) -
            Math.Max(left.Left, right.Left);
        var overlapRatio =
            Math.Max(0, overlap) /
            Math.Min(left.Width, right.Width);
        var leftCenter = left.Left + left.Width / 2;
        var rightCenter = right.Left + right.Width / 2;
        var centerTolerance =
            Math.Max(left.Width, right.Width) * 0.22;
        if (
            overlapRatio < 0.72 ||
            Math.Abs(leftCenter - rightCenter) > centerTolerance)
        {
            return false;
        }

        var verticalGap = Math.Max(
            0,
            Math.Max(left.Top, right.Top) -
            Math.Min(
                left.Top + left.Height,
                right.Top + right.Height));
        return verticalGap <= 96;
    }

    public static bool LooksLikeSelectionMarker(
        ComposerDialMenuContainerGeometry item,
        ComposerDialMenuContainerGeometry marker,
        string? markerClassName)
    {
        if (
            !IsValidGeometry(item) ||
            !IsValidGeometry(marker) ||
            markerClassName?.Contains(
                "input-placeholder",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        var markerCenter =
            marker.Left + marker.Width / 2;
        return markerCenter >=
            item.Left +
            item.Width -
            Math.Max(64, item.Width * 0.16);
    }

    public static IReadOnlySet<string> ResolveAssociatedSurfaceKeys(
        IReadOnlyList<ComposerDialMenuSurfaceGeometrySnapshot> surfaces,
        IReadOnlyList<ComposerDialMenuContainerGeometry> triggers)
    {
        var validSurfaces = surfaces
            .Where(surface =>
                !string.IsNullOrWhiteSpace(surface.Key) &&
                IsValidGeometry(surface.Bounds))
            .ToArray();
        var validTriggers = triggers
            .Where(IsValidGeometry)
            .ToArray();
        var associated = new HashSet<string>(StringComparer.Ordinal);
        if (
            validSurfaces.Length == 0 ||
            validTriggers.Length == 0)
        {
            return associated;
        }

        foreach (var surface in validSurfaces)
        {
            if (validTriggers.Any(trigger =>
                    DistanceBetween(
                        surface.Bounds,
                        trigger) <=
                    TriggerAssociationDistance))
            {
                associated.Add(surface.Key);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            var activeBounds = validSurfaces
                .Where(surface =>
                    associated.Contains(surface.Key))
                .Select(surface => surface.Bounds)
                .ToArray();
            foreach (var candidate in validSurfaces)
            {
                if (
                    associated.Contains(candidate.Key) ||
                    !activeBounds.Any(active =>
                        DistanceBetween(
                            candidate.Bounds,
                            active) <=
                        SurfaceAssociationDistance))
                {
                    continue;
                }

                associated.Add(candidate.Key);
                changed = true;
            }
        }

        return associated;
    }

    public static bool IsTargetStillConfirmable(
        ComposerDialMenuTarget target,
        IReadOnlyList<ComposerDialMenuSurfaceSnapshot> surfaces,
        IReadOnlyList<ComposerDialMenuOptionSnapshot> options)
    {
        var ownsSurface = surfaces.Any(surface =>
            surface.IsOwnedByDialSession &&
            string.Equals(
                surface.Key,
                target.SurfaceKey,
                StringComparison.Ordinal));
        if (!ownsSurface)
        {
            return false;
        }

        return options.Any(option =>
            string.Equals(
                option.SurfaceKey,
                target.SurfaceKey,
                StringComparison.Ordinal) &&
            string.Equals(
                option.Key,
                target.OptionKey,
                StringComparison.Ordinal));
    }

    private static int FindCurrentIndex(
        IReadOnlyList<ComposerDialMenuOptionSnapshot> options,
        string? previousOptionKey)
    {
        for (var index = 0; index < options.Count; index++)
        {
            if (
                options[index].HasKeyboardFocus ||
                options[index].IsActiveDescendant)
            {
                return index;
            }
        }

        if (!string.IsNullOrWhiteSpace(previousOptionKey))
        {
            for (var index = 0; index < options.Count; index++)
            {
                if (string.Equals(
                        options[index].Key,
                        previousOptionKey,
                        StringComparison.Ordinal))
                {
                    return index;
                }
            }
        }

        for (var index = 0; index < options.Count; index++)
        {
            if (options[index].IsSelected)
            {
                return index;
            }
        }

        return -1;
    }

    private static ComposerDialMenuTarget ToTarget(
        ComposerDialMenuOptionSnapshot target,
        IReadOnlyList<ComposerDialMenuOptionSnapshot> orderedOptions)
    {
        var visualIndex = -1;
        for (var index = 0; index < orderedOptions.Count; index++)
        {
            if (string.Equals(
                    orderedOptions[index].Key,
                    target.Key,
                    StringComparison.Ordinal))
            {
                visualIndex = index;
                break;
            }
        }

        return new(
            target.SurfaceKey,
            target.Key,
            target.Name,
            visualIndex);
    }

    private static string NormalizeLabel(string value)
    {
        return new string(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static bool IsValidGeometry(
        ComposerDialMenuContainerGeometry value)
    {
        return
            double.IsFinite(value.Left) &&
            double.IsFinite(value.Top) &&
            double.IsFinite(value.Width) &&
            double.IsFinite(value.Height) &&
            value.Width > 0 &&
            value.Height > 0;
    }

    private static double DistanceBetween(
        ComposerDialMenuContainerGeometry left,
        ComposerDialMenuContainerGeometry right)
    {
        var horizontalGap = Math.Max(
            0,
            Math.Max(left.Left, right.Left) -
            Math.Min(
                left.Left + left.Width,
                right.Left + right.Width));
        var verticalGap = Math.Max(
            0,
            Math.Max(left.Top, right.Top) -
            Math.Min(
                left.Top + left.Height,
                right.Top + right.Height));
        return Math.Sqrt(
            horizontalGap * horizontalGap +
            verticalGap * verticalGap);
    }

    private static bool IsDescendantOf(
        ComposerDialMenuSurfaceSnapshot candidate,
        string ancestorKey,
        IReadOnlyList<ComposerDialMenuSurfaceSnapshot> surfaces)
    {
        var parentKey = candidate.ParentKey;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (
            !string.IsNullOrWhiteSpace(parentKey) &&
            visited.Add(parentKey))
        {
            if (string.Equals(
                    parentKey,
                    ancestorKey,
                    StringComparison.Ordinal))
            {
                return true;
            }

            var parent = surfaces.FirstOrDefault(surface =>
                string.Equals(
                    surface.Key,
                    parentKey,
                    StringComparison.Ordinal));
            parentKey = parent.ParentKey;
        }

        return false;
    }
}
