using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexController.Services.Micro;

internal sealed record CodexMicroSlotBinding(
    string KeycapId,
    string? CommandId,
    bool IsVerified = true);

internal sealed record CodexMicroLayoutSnapshot(
    IReadOnlyDictionary<string, CodexMicroSlotBinding> Slots,
    string EncoderMode,
    string Source)
{
    public CodexMicroSlotBinding GetSlot(string slotId) =>
        Slots.TryGetValue(slotId, out var slot)
            ? slot
            : new(string.Empty, null, IsVerified: false);
}

/// <summary>
/// Read-only resolver for Codex's persisted Micro layout. Semantic actions
/// are delivered to a physical ACT slot only when its effective command can
/// be proven; unknown or partially parsed custom mappings fail closed.
/// </summary>
internal sealed class CodexMicroLayoutResolver
{
    internal const string ComposerNavigationMode = "composer-navigation";
    internal const string ReasoningMode = "reasoning";

    internal static readonly IReadOnlyDictionary<
        string,
        CodexMicroSlotBinding> DefaultSlots =
        new Dictionary<string, CodexMicroSlotBinding>(StringComparer.Ordinal)
        {
            ["ACT06"] = new("FAST", null),
            ["ACT07"] = new("APPR", null),
            ["ACT08"] = new("REJ", null),
            ["ACT09"] = new("SPLIT", null),
            ["ACT10_ACT11"] = new("MIC", null),
            ["ACT12"] = new("CODEX", null),
        };

    private static readonly IReadOnlyDictionary<string, string>
        KeycapActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FAST"] = "composer.toggleFastMode",
            ["APPR"] = "approval.approve",
            ["REJ"] = "approval.decline",
            ["SPLIT"] = "forkThread",
            ["MIC"] = "dictation.pushToTalk",
            ["CODEX"] = "composer.submit",
            ["TERM"] = "toggleTerminal",
            ["DWN"] = "copyConversationMarkdown",
            ["DEL"] = "archiveThread",
            ["NEW"] = "newTask",
            ["NAV"] = "openBrowserTab",
            ["MAGIC"] = "toggleThreadPin",
            ["DIFF"] = "toggleReviewTab",
            ["GIT"] = "git.commit",
            ["PR"] = "git.createPullRequest",
            ["PAINT"] = "composer.addPhotos",
            ["LAB"] = "settings",
            ["PARTY"] = "openSideChat",
            ["TIME"] = "manageTasks",
            ["MIND+"] = "composer.increaseReasoningEffort",
            ["MIND-"] = "composer.decreaseReasoningEffort",
            ["FOLD"] = "openFolder",
            ["UPL"] = "composer.addFiles",
            ["APPS"] = "openSkills",
        };

    private static readonly HashSet<string> EncoderModes = new(
        [ComposerNavigationMode, ReasoningMode],
        StringComparer.Ordinal);

    private readonly object _sync = new();
    private readonly string _configPath;
    private DateTime _lastWriteUtc;
    private long _lastLength = -1;
    private CodexMicroLayoutSnapshot _current;

    public CodexMicroLayoutResolver(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");
        _current = CreateDefault("Codex default layout");
    }

    public string EncoderMode => Current().EncoderMode;

    public bool AllowsCommand(
        string slotId,
        string expectedCommandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommandId);
        var binding = Current().GetSlot(slotId);
        if (!binding.IsVerified)
        {
            return false;
        }

        var effectiveCommand =
            !string.IsNullOrWhiteSpace(binding.CommandId)
                ? binding.CommandId
                : KeycapActions.GetValueOrDefault(binding.KeycapId);
        return string.Equals(
            effectiveCommand,
            expectedCommandId,
            StringComparison.Ordinal);
    }

    internal CodexMicroLayoutSnapshot Current()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    _lastWriteUtc = default;
                    _lastLength = -1;
                    _current = CreateDefault("Codex default layout");
                    return _current;
                }

                var info = new FileInfo(_configPath);
                if (
                    info.LastWriteTimeUtc == _lastWriteUtc &&
                    info.Length == _lastLength)
                {
                    return _current;
                }

                using var stream = new FileStream(
                    _configPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(
                    stream,
                    detectEncodingFromByteOrderMarks: true);
                _current = Parse(reader.ReadToEnd(), _configPath);
                _lastWriteUtc = info.LastWriteTimeUtc;
                _lastLength = info.Length;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                _current = CreateUnverified(
                    $"Unreadable Codex layout: {exception.GetType().Name}");
            }

            return _current;
        }
    }

    internal static CodexMicroLayoutSnapshot Parse(
        string text,
        string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        var slots = DefaultSlots.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal);
        var encoderMode = ComposerNavigationMode;

        foreach (var slotId in DefaultSlots.Keys)
        {
            var parsed = ParseSlot(text, slotId);
            if (parsed is not null)
            {
                slots[slotId] = parsed;
            }
        }

        if (
            TryFindProperty(text, "encoderMode", out var parsedMode) &&
            EncoderModes.Contains(parsedMode))
        {
            encoderMode = parsedMode;
        }

        return new(slots, encoderMode, source);
    }

    private static CodexMicroSlotBinding? ParseSlot(
        string text,
        string slotId)
    {
        var slotMentioned = Regex.IsMatch(
            text,
            $@"(?i)(?:\b|[\""']){Regex.Escape(slotId)}(?:\b|[\""'])");
        if (!slotMentioned)
        {
            return null;
        }

        var body = FindTableBody(text, slotId) ??
                   FindInlineBody(text, slotId);
        if (body is null)
        {
            return DefaultSlots[slotId] with { IsVerified = false };
        }

        var current = DefaultSlots[slotId];
        var recognized = false;
        if (TryFindProperty(body, "keycapId", out var keycapId))
        {
            current = current with { KeycapId = keycapId };
            recognized = true;
        }

        if (TryFindProperty(body, "commandId", out var commandId))
        {
            current = current with { CommandId = commandId };
            recognized = true;
        }

        return recognized
            ? current
            : current with { IsVerified = false };
    }

    private static string? FindTableBody(
        string text,
        string slotId)
    {
        var match = Regex.Match(
            text,
            $@"(?ims)^\s*\[\s*desktop\.codex-micro-layout\.slots\.(?:\""|')?{Regex.Escape(slotId)}(?:\""|')?\s*\]\s*(?<body>.*?)(?=^\s*\[|\z)");
        return match.Success ? match.Groups["body"].Value : null;
    }

    private static string? FindInlineBody(
        string text,
        string slotId)
    {
        var match = Regex.Match(
            text,
            $@"(?is)(?:\b|[\""']){Regex.Escape(slotId)}(?:\b|[\""'])\s*[:=]\s*\{{(?<body>[^}}]*)\}}");
        return match.Success ? match.Groups["body"].Value : null;
    }

    private static bool TryFindProperty(
        string text,
        string property,
        out string value)
    {
        value = string.Empty;
        var match = Regex.Match(
            text,
            $@"(?is)(?:\b|[\""']){Regex.Escape(property)}(?:\b|[\""'])\s*[:=]\s*(?<value>\""(?:\\.|[^\""\\])*\""|'[^']*')");
        return match.Success &&
               TryReadString(match.Groups["value"].Value, out value);
    }

    private static bool TryReadString(
        string text,
        out string value)
    {
        value = string.Empty;
        var trimmed = text.Trim().TrimEnd(',');
        if (trimmed.Length < 2)
        {
            return false;
        }

        if (trimmed[0] == '\'' && trimmed[^1] == '\'')
        {
            value = trimmed[1..^1];
            return true;
        }

        if (trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<string>(trimmed) ??
                    string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CodexMicroLayoutSnapshot CreateDefault(
        string source) =>
        new(
            DefaultSlots.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal),
            ComposerNavigationMode,
            source);

    private static CodexMicroLayoutSnapshot CreateUnverified(
        string source) =>
        new(
            DefaultSlots.ToDictionary(
                item => item.Key,
                item => item.Value with { IsVerified = false },
                StringComparer.Ordinal),
            ComposerNavigationMode,
            source);
}
