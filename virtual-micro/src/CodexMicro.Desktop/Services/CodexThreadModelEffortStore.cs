using System.IO;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Persists the last confirmed effort independently for each task and model.
/// Values are committed only after they came from a semantic snapshot or a
/// confirmed settings response.
/// </summary>
internal sealed class CodexThreadModelEffortStore
{
    private const int MaximumEntries = 256;
    private static readonly object FileSync = new();
    private readonly string _path;
    private Dictionary<string, StoredEffort> _entries;

    internal CodexThreadModelEffortStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
        _entries = Read(_path);
    }

    internal string? Recall(string threadId, string modelId)
    {
        var key = Key(threadId, modelId);
        lock (FileSync)
        {
            return _entries.TryGetValue(key, out var entry)
                ? entry.Effort
                : null;
        }
    }

    internal void Remember(string threadId, string modelId, string? effort)
    {
        if (!IsValidPart(threadId, 128) ||
            !IsValidPart(modelId, 128) ||
            !IsValidPart(effort, 32))
        {
            return;
        }

        lock (FileSync)
        {
            // Merge first so two keypad windows in this process cannot erase
            // entries that the other window wrote after construction.
            var merged = Read(_path);
            foreach (var pair in _entries)
            {
                if (!merged.TryGetValue(pair.Key, out var existing) ||
                    existing.UpdatedAtUtc < pair.Value.UpdatedAtUtc)
                {
                    merged[pair.Key] = pair.Value;
                }
            }

            merged[Key(threadId, modelId)] = new(
                threadId,
                modelId,
                effort!,
                DateTimeOffset.UtcNow);
            _entries = merged.Values
                .OrderByDescending(entry => entry.UpdatedAtUtc)
                .Take(MaximumEntries)
                .ToDictionary(
                    entry => Key(entry.ThreadId, entry.ModelId),
                    StringComparer.Ordinal);
            Persist();
        }
    }

    private void Persist()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(
                new StoredFile
                {
                    Entries = _entries.Values
                        .OrderByDescending(entry => entry.UpdatedAtUtc)
                        .ToList(),
                },
                new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The in-memory value remains useful if persistence is unavailable.
        }
    }

    private static Dictionary<string, StoredEffort> Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new(StringComparer.Ordinal);
            }

            var stored = JsonSerializer.Deserialize<StoredFile>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            return stored?.Entries
                .Where(entry =>
                    IsValidPart(entry.ThreadId, 128) &&
                    IsValidPart(entry.ModelId, 128) &&
                    IsValidPart(entry.Effort, 32))
                .OrderByDescending(entry => entry.UpdatedAtUtc)
                .Take(MaximumEntries)
                .ToDictionary(
                    entry => Key(entry.ThreadId, entry.ModelId),
                    StringComparer.Ordinal) ??
                new(StringComparer.Ordinal);
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException or
                ArgumentException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    private static string Key(string threadId, string modelId) =>
        $"{threadId}\0{modelId.ToLowerInvariant()}";

    private static bool IsValidPart(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value.All(character => !char.IsControl(character));

    private static string GetDefaultPath() =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CodexMicro",
            "thread-model-efforts.json");

    private sealed class StoredFile
    {
        public List<StoredEffort> Entries { get; set; } = [];
    }

    private sealed record StoredEffort(
        string ThreadId,
        string ModelId,
        string Effort,
        DateTimeOffset UpdatedAtUtc);
}
