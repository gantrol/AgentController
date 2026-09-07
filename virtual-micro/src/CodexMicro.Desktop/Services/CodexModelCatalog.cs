using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexModelDescriptor(
    string Id,
    string DisplayName,
    IReadOnlyList<string> SupportedEfforts,
    string? DefaultEffort,
    bool Hidden,
    int Priority)
{
    internal string Label => CodexModelCatalog.ModelLabel(DisplayName);
    internal string ShortLabel => Regex.Replace(Label, @"^\d+(?:\.\d+)*\s+", "");
}

internal sealed class CodexModelCatalog
{
    private static readonly object CacheLock = new();
    private static string? _cachedPath;
    private static DateTime _cachedModified;
    private static long _cachedLength;
    private static CodexModelCatalog? _cachedCatalog;
    private readonly DateTime _expiresAt;

    internal IReadOnlyList<CodexModelDescriptor> Models { get; }
    internal bool IsFresh => DateTime.UtcNow <= _expiresAt;

    private CodexModelCatalog(IReadOnlyList<CodexModelDescriptor> models, bool isFresh)
    {
        Models = models;
        _expiresAt = isFresh ? DateTime.UtcNow.AddMinutes(5) : DateTime.MinValue;
    }

    private CodexModelCatalog(IReadOnlyList<CodexModelDescriptor> models, DateTime expiresAt)
    {
        Models = models;
        _expiresAt = expiresAt;
    }

    internal CodexModelDescriptor? Find(string id) =>
        Models.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.Ordinal));

    internal static string DefaultCachePath => Path.Combine(
        Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
        "models_cache.json");

    internal static CodexModelCatalog Load(string? path = null)
    {
        try
        {
            path ??= DefaultCachePath;
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return new([], false);
            }

            lock (CacheLock)
            {
                if (_cachedPath == file.FullName && _cachedModified == file.LastWriteTimeUtc &&
                    _cachedLength == file.Length && _cachedCatalog is not null)
                {
                    return _cachedCatalog;
                }

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var parsed = Parse(document.RootElement);
                var modified = file.LastWriteTimeUtc;
                var length = file.Length;
                file.Refresh();
                if (!file.Exists || file.LastWriteTimeUtc != modified || file.Length != length)
                {
                    return new([], false);
                }

                _cachedPath = file.FullName;
                _cachedModified = modified;
                _cachedLength = length;
                _cachedCatalog = new(parsed.Models,
                    modified > DateTime.UtcNow ? DateTime.MinValue : modified.AddDays(1));
                return _cachedCatalog;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new([], false);
        }
    }

    internal static CodexModelCatalog Parse(JsonElement root, bool isFresh = true)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new([], false);
        }

        var fromServer = root.TryGetProperty("data", out var entries);
        if ((!fromServer && !root.TryGetProperty("models", out entries)) ||
            entries.ValueKind != JsonValueKind.Array)
        {
            return new([], false);
        }

        var models = new List<CodexModelDescriptor>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in entries.EnumerateArray())
        {
            var id = Text(item, fromServer ? "model" : "slug");
            if (id is null || !ids.Add(id))
            {
                continue;
            }

            var efforts = new List<string>();
            if (item.TryGetProperty(fromServer ? "supportedReasoningEfforts" : "supported_reasoning_levels", out var levels) &&
                levels.ValueKind == JsonValueKind.Array)
            {
                foreach (var level in levels.EnumerateArray())
                {
                    var effort = Text(level, fromServer ? "reasoningEffort" : "effort");
                    if (effort is not null && !efforts.Contains(effort, StringComparer.Ordinal))
                    {
                        efforts.Add(effort);
                    }
                }
            }

            var hidden = fromServer
                ? item.TryGetProperty("hidden", out var hiddenValue) && hiddenValue.ValueKind == JsonValueKind.True
                : Text(item, "visibility") != "list";
            var priority = item.TryGetProperty("priority", out var rank) &&
                rank.ValueKind == JsonValueKind.Number && rank.TryGetInt32(out var number)
                    ? number : int.MaxValue;
            models.Add(new(id,
                Text(item, fromServer ? "displayName" : "display_name") ?? id,
                efforts,
                Text(item, fromServer ? "defaultReasoningEffort" : "default_reasoning_level"),
                hidden, priority));
        }

        return new(models.OrderBy(model => model.Priority).ToArray(), isFresh);
    }

    internal string ResolveEffort(string modelId, string? requested)
    {
        if (!IsFresh || Find(modelId) is not { Hidden: false } model)
        {
            throw new CodexModelCapabilityException("model-catalog-unavailable");
        }

        var effort = string.IsNullOrWhiteSpace(requested) ? model.DefaultEffort : requested.Trim();
        if (effort is null || !model.SupportedEfforts.Contains(effort, StringComparer.Ordinal))
        {
            throw new CodexModelCapabilityException("model-effort-unavailable");
        }

        return effort;
    }

    internal CodexModelDescriptor? MatchLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = NormalizeLabel(value);
        var matches = Models.SelectMany(model => new[] { model.Id, model.DisplayName, model.Label }
                .Select(label => (Model: model, Label: NormalizeLabel(label)))
                .Append((Model: model, Label: NormalizeLabel(model.ShortLabel))))
            .Where(candidate => candidate.Label.Length > 0 &&
                Regex.IsMatch(normalized,
                    candidate.Label == NormalizeLabel(candidate.Model.ShortLabel)
                        ? $@"^(?:power\s+)?{Regex.Escape(candidate.Label)}(?![\p{{L}}\p{{N}}])"
                        : $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(candidate.Label)}(?![\p{{L}}\p{{N}}])"))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var longest = matches.Max(candidate => candidate.Label.Length);
        var best = matches.Where(candidate => candidate.Label.Length == longest)
            .Select(candidate => candidate.Model).DistinctBy(model => model.Id).ToArray();
        return best.Length == 1 ? best[0] : null;
    }

    internal static string ModelLabel(string name) =>
        (name.StartsWith("GPT-", StringComparison.OrdinalIgnoreCase) ? name[4..] : name).Replace('-', ' ');

    internal static string ShortLabel(string id)
    {
        var label = Regex.Replace(ModelLabel(id), @"^\d+(?:\.\d+)*\s+", "");
        return label.Length == 0 ? id : char.ToUpperInvariant(label[0]) + label[1..];
    }

    internal string? MatchEffort(string modelId, string value)
    {
        var model = Find(modelId);
        if (model is null)
        {
            return null;
        }

        var normalized = NormalizeLabel(value);
        var matches = model.SupportedEfforts.SelectMany(effort => EffortLabels(effort)
                .Select(label => (Effort: effort, Label: NormalizeLabel(label))))
            .Where(candidate => Regex.IsMatch(normalized,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(candidate.Label)}(?![\p{{L}}\p{{N}}])"))
            .ToArray();
        if (matches.Length == 0)
        {
            return null;
        }

        var longest = matches.Max(candidate => candidate.Label.Length);
        var best = matches.Where(candidate => candidate.Label.Length == longest)
            .Select(candidate => candidate.Effort).Distinct(StringComparer.Ordinal).ToArray();
        return best.Length == 1 ? best[0] : null;
    }

    private static IEnumerable<string> EffortLabels(string effort)
    {
        yield return effort;
        var alias = effort switch
        {
            "low" => "Light",
            "medium" => "Standard",
            "high" => "Extended",
            "xhigh" => "Extra High",
            _ => null,
        };
        if (alias is not null)
        {
            yield return alias;
        }
    }

    private static string NormalizeLabel(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant().Replace('-', ' '), @"\s+", " ");

    private static string? Text(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim() : null;
}

internal sealed class CodexModelCapabilityException(string error) : InvalidOperationException(error)
{
    internal string Error => Message;
}
