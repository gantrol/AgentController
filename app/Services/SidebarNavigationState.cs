using CodexController.Models;

namespace CodexController.Services;

/// <summary>
/// Keeps Agent Controller's sidebar cursor independent from Codex UIA focus.
/// Root sections live in one continuous list; section jumps only accelerate
/// movement and never change which entries exist.
/// </summary>
public sealed class SidebarNavigationState
{
    private readonly List<SidebarEntry> _frozenEntries = [];

    public int SelectedIndex { get; private set; } = -1;

    public IReadOnlyList<SidebarEntry> FrozenEntries => _frozenEntries;

    public SidebarEntry? SelectedEntry(
        IReadOnlyList<SidebarEntry> entries) =>
        SelectedIndex >= 0 && SelectedIndex < entries.Count
            ? entries[SelectedIndex]
            : null;

    /// <summary>
    /// Reconciles a fresh data snapshot with the controller-owned directory.
    /// Metadata is refreshed by stable key, while surviving entries keep their
    /// frozen relative order. A forced rebuild is reserved for an explicit
    /// reorder/reset action, never an automatic data refresh.
    /// </summary>
    public SidebarNavigationSyncResult Synchronize(
        IReadOnlyList<SidebarEntry> candidates,
        string? preferredId,
        int fallbackIndex = 0,
        bool forceRebuild = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        SidebarNavigationKey? previousKey =
            SelectedEntry(_frozenEntries) is { } previous
            ? Key(previous)
            : null;
        var previousIndex = SelectedIndex;
        var candidateMap = candidates.ToDictionary(
            Key,
            entry => entry,
            SidebarNavigationKeyComparer.Instance);
        var previousKeys = _frozenEntries
            .Select(Key)
            .ToHashSet(SidebarNavigationKeyComparer.Instance);
        var candidateKeys = candidateMap.Keys.ToHashSet(
            SidebarNavigationKeyComparer.Instance);
        var structureChanged = !previousKeys.SetEquals(candidateKeys);

        IReadOnlyList<SidebarEntry> synchronized;
        if (
            forceRebuild ||
            _frozenEntries.Count == 0 ||
            candidates.Count == 0)
        {
            synchronized = candidates.ToList();
        }
        else if (!structureChanged)
        {
            synchronized = _frozenEntries
                .Select(entry => candidateMap[Key(entry)])
                .ToList();
        }
        else
        {
            synchronized = MergeStructure(candidates, candidateMap);
        }

        var orderChanged = !_frozenEntries
            .Select(Key)
            .SequenceEqual(
                synchronized.Select(Key),
                SidebarNavigationKeyComparer.Instance);
        _frozenEntries.Clear();
        _frozenEntries.AddRange(synchronized);

        if (_frozenEntries.Count == 0)
        {
            SelectedIndex = -1;
            return new(
                StructureChanged: structureChanged,
                OrderChanged: orderChanged,
                SelectionChanged: previousIndex != -1);
        }

        var selectedIndex = previousKey is not null
            ? FindByKey(_frozenEntries, previousKey.Value)
            : -1;
        if (selectedIndex < 0 && !string.IsNullOrWhiteSpace(preferredId))
        {
            selectedIndex = FindById(_frozenEntries, preferredId);
        }

        if (selectedIndex < 0)
        {
            var effectiveFallback = previousIndex >= 0
                ? previousIndex
                : fallbackIndex;
            selectedIndex = Math.Clamp(
                effectiveFallback,
                0,
                _frozenEntries.Count - 1);
        }

        SelectedIndex = selectedIndex;
        return new(
            StructureChanged: structureChanged,
            OrderChanged: orderChanged,
            SelectionChanged: previousIndex != SelectedIndex);
    }

    public int Restore(
        IReadOnlyList<SidebarEntry> entries,
        string? preferredId,
        int fallbackIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            SelectedIndex = -1;
            return SelectedIndex;
        }

        var restored = string.IsNullOrWhiteSpace(preferredId)
            ? -1
            : FindById(entries, preferredId);
        SelectedIndex = restored >= 0
            ? restored
            : Math.Clamp(fallbackIndex, 0, entries.Count - 1);
        return SelectedIndex;
    }

    public int Select(
        IReadOnlyList<SidebarEntry> entries,
        int index)
    {
        ArgumentNullException.ThrowIfNull(entries);
        SelectedIndex = entries.Count == 0
            ? -1
            : Math.Clamp(index, 0, entries.Count - 1);
        return SelectedIndex;
    }

    public bool TryMove(
        IReadOnlyList<SidebarEntry> entries,
        int direction,
        out SidebarEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        entry = null;
        if (entries.Count == 0 || direction == 0)
        {
            return false;
        }

        var current = SelectedIndex < 0
            ? 0
            : SelectedIndex;
        var next = Math.Clamp(
            current + Math.Sign(direction),
            0,
            entries.Count - 1);
        if (next == SelectedIndex)
        {
            return false;
        }

        SelectedIndex = next;
        entry = entries[next];
        return true;
    }

    public bool TryJumpToScope(
        IReadOnlyList<SidebarEntry> entries,
        SidebarScope scope,
        string? preferredId,
        out SidebarEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(entries);
        entry = null;

        var preferred = string.IsNullOrWhiteSpace(preferredId)
            ? -1
            : entries
                .Select((candidate, index) => (candidate, index))
                .Where(item =>
                    item.candidate.NavigationScope == scope)
                .Where(item =>
                    item.candidate.Id.Equals(
                        preferredId,
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        var next = preferred >= 0
            ? preferred
            : entries
                .Select((candidate, index) => (candidate, index))
                .Where(item =>
                    item.candidate.NavigationScope == scope)
                .Select(item => item.index)
                .DefaultIfEmpty(-1)
                .First();
        if (next < 0)
        {
            return false;
        }

        SelectedIndex = next;
        entry = entries[next];
        return true;
    }

    public void Clear()
    {
        _frozenEntries.Clear();
        SelectedIndex = -1;
    }

    public SidebarNavigationMenuState? BuildMenuState(
        IReadOnlyList<SidebarEntry> entries,
        Func<SidebarScope, string> scopeLabel)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(scopeLabel);
        var current = SelectedEntry(entries);
        if (current is null)
        {
            return null;
        }

        SidebarNavigationMenuItem CreateItem(
            SidebarEntry entry,
            bool crossesBoundary) =>
            new(
                entry.Title,
                entry.NavigationScope,
                scopeLabel(entry.NavigationScope),
                crossesBoundary);

        var previous = SelectedIndex > 0
            ? entries[SelectedIndex - 1]
            : null;
        var next = SelectedIndex + 1 < entries.Count
            ? entries[SelectedIndex + 1]
            : null;
        return new(
            previous is null
                ? null
                : CreateItem(
                    previous,
                    previous.NavigationScope != current.NavigationScope),
            CreateItem(current, crossesBoundary: false),
            next is null
                ? null
                : CreateItem(
                    next,
                    next.NavigationScope != current.NavigationScope),
            SelectedIndex + 1,
            entries.Count);
    }

    public static CodexThread? FindCurrentThread(
        CodexSnapshot snapshot,
        string? currentTitle)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(currentTitle))
        {
            return null;
        }

        var matches = snapshot.Threads
            .Where(thread =>
                string.Equals(
                    thread.NativeTitle,
                    currentTitle,
                    StringComparison.Ordinal) ||
                string.Equals(
                    thread.Title,
                    currentTitle,
                    StringComparison.Ordinal))
            .Take(2)
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static int FindById(
        IReadOnlyList<SidebarEntry> entries,
        string id)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Id.Equals(
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private IReadOnlyList<SidebarEntry> MergeStructure(
        IReadOnlyList<SidebarEntry> candidates,
        IReadOnlyDictionary<SidebarNavigationKey, SidebarEntry>
            candidateMap)
    {
        var result = new List<SidebarEntry>(candidates.Count);
        var emitted = new HashSet<SidebarNavigationKey>(
            SidebarNavigationKeyComparer.Instance);
        var scopeOrder = candidates
            .Select(entry => entry.NavigationScope)
            .Distinct()
            .ToList();

        foreach (var scope in scopeOrder)
        {
            foreach (var existing in _frozenEntries.Where(entry =>
                         entry.NavigationScope == scope))
            {
                var key = Key(existing);
                if (
                    candidateMap.TryGetValue(key, out var updated) &&
                    emitted.Add(key))
                {
                    result.Add(updated);
                }
            }

            foreach (var candidate in candidates.Where(entry =>
                         entry.NavigationScope == scope))
            {
                var key = Key(candidate);
                if (emitted.Add(key))
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    private static int FindByKey(
        IReadOnlyList<SidebarEntry> entries,
        SidebarNavigationKey key)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (SidebarNavigationKeyComparer.Instance.Equals(
                    Key(entries[index]),
                    key))
            {
                return index;
            }
        }

        return -1;
    }

    private static SidebarNavigationKey Key(SidebarEntry entry) =>
        new(entry.NavigationScope, entry.Id);

    private readonly record struct SidebarNavigationKey(
        SidebarScope Scope,
        string Id);

    private sealed class SidebarNavigationKeyComparer :
        IEqualityComparer<SidebarNavigationKey>
    {
        public static SidebarNavigationKeyComparer Instance { get; } =
            new();

        public bool Equals(
            SidebarNavigationKey x,
            SidebarNavigationKey y) =>
            x.Scope == y.Scope &&
            string.Equals(
                x.Id,
                y.Id,
                StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(SidebarNavigationKey obj) =>
            HashCode.Combine(
                obj.Scope,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id));
    }
}

public readonly record struct SidebarNavigationSyncResult(
    bool StructureChanged,
    bool OrderChanged,
    bool SelectionChanged);
