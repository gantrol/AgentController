using System.IO;
using System.Text.RegularExpressions;
using CodexMicro.Desktop.Services;

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
        if (modelIndex < 0 || modelIndex >= Models.Count)
        {
            return [];
        }

        return Models[modelIndex].Efforts;
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
        var catalog = CodexModelCatalog.Load(Path.Combine(codexHome, "models_cache.json"));
        var models = catalog.Models.Where(model => !model.Hidden)
            .Select(model => new ComposerModelOption(model.Id, model.Label,
                catalog.IsFresh ? model.SupportedEfforts.Select(EffortLabel).ToArray() : []))
            .ToArray();
        var preferences = ReadConfig(Path.Combine(codexHome, "config.toml"));
        var buttonName = _readComposerButtonName();
        if (models.Length == 0)
        {
            return new ComposerCatalog
            {
                Models = [],
                InitialModelIndex = -1,
                InitialEffort = string.Empty,
                InitialSpeed = FindSpeed(
                    buttonName,
                    preferences.ServiceTier),
            };
        }

        var selected = catalog.MatchLabel(buttonName);
        var modelIndex = selected is null ? -1 : Array.FindIndex(models, model => model.Slug == selected.Id);
        var effort = modelIndex >= 0 && catalog.IsFresh && buttonName is not null
            ? EffortLabel(catalog.MatchEffort(models[modelIndex].Slug, buttonName))
            : string.Empty;

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

    internal static string ModelLabel(string displayName) => CodexModelCatalog.ModelLabel(displayName);

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
