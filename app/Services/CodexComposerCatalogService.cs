using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexController.Services;

public sealed record ComposerModelOption(
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Efforts);

public sealed class ComposerCatalog
{
    public required IReadOnlyList<ComposerModelOption> Models { get; init; }
    public required int InitialModelIndex { get; init; }
    public required string InitialEffort { get; init; }
    public required string InitialSpeed { get; init; }

    public IReadOnlyList<string> EffortsForModel(int modelIndex)
    {
        if (Models.Count == 0)
        {
            return [];
        }

        var safeIndex = Math.Clamp(modelIndex, 0, Models.Count - 1);
        return Models[safeIndex].Efforts;
    }
}

internal sealed class CodexComposerCatalogService
{
    private readonly Func<string?> _readComposerButtonName;
    private readonly Func<string> _resolveCodexHome;

    internal CodexComposerCatalogService(
        Func<string?> readComposerButtonName,
        Func<string>? resolveCodexHome = null)
    {
        _readComposerButtonName = readComposerButtonName ??
            throw new ArgumentNullException(nameof(readComposerButtonName));
        _resolveCodexHome = resolveCodexHome ?? ResolveCodexHome;
    }

    internal ComposerCatalog LoadCatalog()
    {
        var codexHome = _resolveCodexHome();
        var models = LoadModels(Path.Combine(codexHome, "models_cache.json"));
        var preferences = ReadConfig(Path.Combine(codexHome, "config.toml"));
        var buttonName = _readComposerButtonName();
        if (models.Count == 0)
        {
            return new ComposerCatalog
            {
                Models = [],
                InitialModelIndex = 0,
                InitialEffort = string.Empty,
                InitialSpeed = FindSpeed(
                    buttonName,
                    preferences.ServiceTier),
            };
        }

        var modelIndex = FindModelIndex(
            models,
            buttonName,
            preferences.ModelSlug);
        var modelEfforts = models[modelIndex].Efforts;
        var effort = FindEffort(
            modelEfforts,
            buttonName,
            EffortLabel(preferences.Effort));

        return new ComposerCatalog
        {
            Models = models,
            InitialModelIndex = modelIndex,
            InitialEffort = effort,
            InitialSpeed = FindSpeed(
                buttonName,
                preferences.ServiceTier),
        };
    }

    private static IReadOnlyList<ComposerModelOption> LoadModels(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (
                !document.RootElement.TryGetProperty(
                    "models",
                    out var modelsElement) ||
                modelsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var models = new List<(
                ComposerModelOption Option,
                int Priority,
                int SourceOrder)>();
            var sourceOrder = 0;
            foreach (var model in modelsElement.EnumerateArray())
            {
                if (
                    GetString(model, "visibility") != "list" ||
                    GetString(model, "slug") is not { Length: > 0 } slug ||
                    GetString(model, "display_name") is not { Length: > 0 }
                        displayName)
                {
                    continue;
                }

                var efforts = new List<string>();
                if (
                    model.TryGetProperty(
                        "supported_reasoning_levels",
                        out var effortElement) &&
                    effortElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var effort in effortElement.EnumerateArray())
                    {
                        var label = EffortLabel(GetString(effort, "effort"));
                        if (
                            label.Length > 0 &&
                            !efforts.Contains(
                                label,
                                StringComparer.OrdinalIgnoreCase))
                        {
                            efforts.Add(label);
                        }
                    }
                }

                models.Add((
                    new ComposerModelOption(
                        slug,
                        ModelLabel(displayName),
                        efforts),
                    GetInt(model, "priority") ?? int.MaxValue,
                    sourceOrder++));
            }

            return models
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.SourceOrder)
                .Select(item => item.Option)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static ConfigPreferences ReadConfig(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return new(
                MatchTomlString(text, "model"),
                MatchTomlString(text, "model_reasoning_effort"),
                MatchTomlString(text, "service_tier"));
        }
        catch
        {
            return new(null, null, null);
        }
    }

    private static string? MatchTomlString(string text, string key)
    {
        var match = Regex.Match(
            text,
            $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*[""']([^""']+)[""']");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int FindModelIndex(
        IReadOnlyList<ComposerModelOption> models,
        string? buttonName,
        string? configuredSlug)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalizedButton =
                ComposerChoiceNormalizer.Normalize(buttonName);
            var match = models
                .Select((model, index) => new
                {
                    Index = index,
                    Length = ComposerChoiceNormalizer.Normalize(
                        model.DisplayName).Length,
                    Matches = normalizedButton.StartsWith(
                        ComposerChoiceNormalizer.Normalize(
                            model.DisplayName),
                        StringComparison.Ordinal),
                })
                .Where(item => item.Matches)
                .OrderByDescending(item => item.Length)
                .FirstOrDefault();
            if (match is not null)
            {
                return match.Index;
            }
        }

        var configuredIndex = models
            .Select((model, index) => new { model, index })
            .FirstOrDefault(item =>
                string.Equals(
                    item.model.Slug,
                    configuredSlug,
                    StringComparison.OrdinalIgnoreCase))?.index;
        return configuredIndex ?? 0;
    }

    private static string FindEffort(
        IReadOnlyList<string> efforts,
        string? buttonName,
        string configuredEffort)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalizedButton =
                ComposerChoiceNormalizer.Normalize(buttonName);
            var fromButton = efforts
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value =>
                    normalizedButton.EndsWith(
                        ComposerChoiceNormalizer.Normalize(value),
                        StringComparison.Ordinal));
            if (fromButton is not null)
            {
                return fromButton;
            }
        }

        return efforts.FirstOrDefault(value =>
                   string.Equals(
                       value,
                       configuredEffort,
                       StringComparison.OrdinalIgnoreCase))
               ?? efforts.FirstOrDefault()
               ?? string.Empty;
    }

    private static string FindSpeed(
        string? buttonName,
        string? configuredServiceTier)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalized = ComposerChoiceNormalizer.Normalize(buttonName);
            if (normalized.EndsWith("standard", StringComparison.Ordinal))
            {
                return "Standard";
            }

            if (normalized.EndsWith("fast", StringComparison.Ordinal))
            {
                return "Fast";
            }
        }

        return string.Equals(
            configuredServiceTier,
            "priority",
            StringComparison.OrdinalIgnoreCase)
            ? "Fast"
            : "Standard";
    }

    internal static string ModelLabel(string displayName)
    {
        var value = displayName.StartsWith(
            "GPT-",
            StringComparison.OrdinalIgnoreCase)
            ? displayName[4..]
            : displayName;
        return value.Replace('-', ' ');
    }

    private static string EffortLabel(string? effort)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return string.Empty;
        }

        var raw = effort.Trim();
        return raw.ToLowerInvariant() switch
        {
            "low" => "Light",
            "medium" => "Medium",
            "high" => "High",
            "xhigh" => "Extra High",
            _ => string.Join(
                ' ',
                raw.Split(
                        ['_', '-'],
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(part =>
                        char.ToUpperInvariant(part[0]) +
                        part[1..].ToLowerInvariant())),
        };
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".codex");
    }

    private sealed record ConfigPreferences(
        string? ModelSlug,
        string? Effort,
        string? ServiceTier);
}
