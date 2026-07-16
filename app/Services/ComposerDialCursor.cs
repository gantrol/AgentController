namespace CodexController.Services;

/// <summary>
/// Keeps a virtual dial anchored to a stable control identity while the
/// surrounding composer controls are rediscovered from UI Automation.
/// </summary>
public sealed class ComposerDialCursor
{
    private string? _selectedKey;

    public string? SelectedKey => _selectedKey;

    public int Move(
        IReadOnlyList<string> keys,
        int delta)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0 || delta == 0)
        {
            return -1;
        }

        var direction = Math.Sign(delta);
        var current = FindSelectedIndex(keys);
        var next = current < 0
            ? direction > 0 ? 0 : keys.Count - 1
            : (current + direction + keys.Count) % keys.Count;
        _selectedKey = keys[next];
        return next;
    }

    public int FindSelectedIndex(IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (string.IsNullOrWhiteSpace(_selectedKey))
        {
            return -1;
        }

        for (var index = 0; index < keys.Count; index++)
        {
            if (string.Equals(
                    keys[index],
                    _selectedKey,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    public void Select(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _selectedKey = key;
    }

    public void Reset()
    {
        _selectedKey = null;
    }
}
