using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Updates only the Codex Micro TOML tables and preserves every unrelated
/// Codex setting. Writes use the same atomic replacement pattern observed by
/// <see cref="CodexMicroLayoutObserver"/>.
/// </summary>
internal sealed class CodexMicroConfigWriter
{
    private const string LayoutTable = "desktop.codex-micro-layout";

    private static readonly HashSet<string> SlotIds = new(
        [
            "ACT06",
            "ACT07",
            "ACT08",
            "ACT09",
            "ACT10",
            "ACT11",
            "ACT10_ACT11",
            "ACT12",
        ],
        StringComparer.Ordinal);

    private readonly string _configPath;

    internal CodexMicroConfigWriter(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        _configPath = configPath;
    }

    internal string ConfigPath => _configPath;

    internal bool SetSlot(
        string slotId,
        string keycapId,
        CodexMicroActionBinding? action)
        => SetSlotBinding(
            slotId,
            new CodexMicroSlotBinding(keycapId, null, action));

    internal bool SetSlotBinding(
        string slotId,
        CodexMicroSlotBinding binding)
    {
        if (!SlotIds.Contains(slotId))
        {
            throw new ArgumentOutOfRangeException(nameof(slotId));
        }

        ArgumentNullException.ThrowIfNull(binding);
        if (!CodexKeycapCatalog.IsKnown(binding.KeycapId))
        {
            throw new ArgumentOutOfRangeException(nameof(binding));
        }

        var actionValue = binding.Action switch
        {
            null => null,
            { Type: "command" } commandAction =>
                $"{{ type = \"command\", commandId = {TomlString(commandAction.Id)} }}",
            { Type: "skill", SkillPath: { Length: > 0 } path }
                skillAction =>
                $"{{ type = \"skill\", skillName = {TomlString(skillAction.Id)}, " +
                $"skillPath = {TomlString(path)} }}",
            _ => throw new ArgumentOutOfRangeException(nameof(binding)),
        };

        return Update(text => UpsertTable(
            text,
            $"{LayoutTable}.slots.{slotId}",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["keycapId"] = TomlString(binding.KeycapId),
                ["commandId"] = string.IsNullOrWhiteSpace(binding.CommandId)
                    ? null
                    : TomlString(binding.CommandId),
                ["action"] = actionValue,
            }));
    }

    internal bool SetEncoderMode(string mode)
    {
        if (mode is not (
            "composer-navigation" or
            "reasoning" or
            "conversation-scroll" or
            "custom"))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return SetLayoutValue("encoderMode", TomlString(mode));
    }

    internal bool SetVoiceButtonMode(string mode)
    {
        if (mode is not ("push-to-talk" or "realtime"))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        return SetLayoutValue("voiceButtonMode", TomlString(mode));
    }

    internal bool SetSeparateMicrophoneKeys(bool value) =>
        SetLayoutValue("separateMicrophoneKeys", value ? "true" : "false");

    internal bool ResetLayout() => Update(RemoveAndAppendDefaultLayout);

    private bool SetLayoutValue(string key, string value) =>
        Update(text => UpsertTable(
            text,
            LayoutTable,
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [key] = value,
            }));

    private bool Update(Func<string, string> update)
    {
        try
        {
            var source = File.Exists(_configPath)
                ? File.ReadAllText(_configPath)
                : string.Empty;
            var next = update(source);
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _configPath + ".micro.tmp";
            File.WriteAllText(
                temporaryPath,
                next,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, _configPath, overwrite: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
                UnauthorizedAccessException or
                JsonException)
        {
            return false;
        }
    }

    private static string UpsertTable(
        string source,
        string tableName,
        IReadOnlyDictionary<string, string?> updates)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var lines = NormalizeLines(source);
        var header = $"[{tableName}]";
        var start = lines.FindIndex(line =>
            string.Equals(line.Trim(), header, StringComparison.Ordinal));
        if (start < 0)
        {
            while (lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(header);
            foreach (var (key, value) in updates)
            {
                if (value is not null)
                {
                    lines.Add($"{key} = {value}");
                }
            }

            lines.Add(string.Empty);
            return JoinLines(lines, newline);
        }

        var end = FindNextHeader(lines, start + 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var replacement = new List<string> { lines[start] };
        for (var index = start + 1; index < end; index++)
        {
            var line = lines[index];
            var key = TryReadAssignmentKey(line);
            if (key is null || !updates.TryGetValue(key, out var value))
            {
                replacement.Add(line);
                continue;
            }

            if (seen.Add(key) && value is not null)
            {
                replacement.Add($"{key} = {value}");
            }
        }

        foreach (var (key, value) in updates)
        {
            if (value is not null && seen.Add(key))
            {
                replacement.Add($"{key} = {value}");
            }
        }

        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, replacement);
        return JoinLines(lines, newline);
    }

    private static string RemoveAndAppendDefaultLayout(string source)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var kept = new List<string>();
        var skipping = false;
        foreach (var line in NormalizeLines(source))
        {
            if (TryReadTableName(line) is { } table)
            {
                skipping = table == LayoutTable ||
                    table.StartsWith(LayoutTable + ".", StringComparison.Ordinal);
            }

            if (!skipping)
            {
                kept.Add(line);
            }
        }

        while (kept.Count > 0 && kept[^1].Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        if (kept.Count > 0)
        {
            kept.Add(string.Empty);
        }

        kept.AddRange(DefaultLayoutLines);
        kept.Add(string.Empty);
        return JoinLines(kept, newline);
    }

    private static readonly string[] DefaultLayoutLines =
    [
        "[desktop.codex-micro-layout]",
        "version = 1",
        "encoderMode = \"composer-navigation\"",
        "voiceButtonMode = \"push-to-talk\"",
        "separateMicrophoneKeys = false",
        "",
        "[desktop.codex-micro-layout.slots.ACT06]",
        "keycapId = \"FAST\"",
        "",
        "[desktop.codex-micro-layout.slots.ACT07]",
        "keycapId = \"APPR\"",
        "",
        "[desktop.codex-micro-layout.slots.ACT08]",
        "keycapId = \"REJ\"",
        "",
        "[desktop.codex-micro-layout.slots.ACT09]",
        "keycapId = \"SPLIT\"",
        "",
        "[desktop.codex-micro-layout.slots.ACT10]",
        "keycapId = \"MIC1\"",
        "",
        "[desktop.codex-micro-layout.slots.ACT11]",
        "keycapId = \"EMPT1\"",
        "",
        "[desktop.codex-micro-layout.slots.ACT10_ACT11]",
        "keycapId = \"MIC\"",
        "",
        "[desktop.codex-micro-layout.slots.ACT12]",
        "keycapId = \"CODEX\"",
        "",
        "[desktop.codex-micro-layout.analogStick.up]",
        "commandId = \"composer.togglePlanMode\"",
        "",
        "[desktop.codex-micro-layout.analogStick.right]",
        "commandId = \"navigateForward\"",
        "",
        "[desktop.codex-micro-layout.analogStick.down]",
        "commandId = \"toggleSidebar\"",
        "",
        "[desktop.codex-micro-layout.analogStick.left]",
        "commandId = \"navigateBack\"",
    ];

    private static List<string> NormalizeLines(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

    private static string JoinLines(List<string> lines, string newline) =>
        string.Join(newline, lines);

    private static int FindNextHeader(IReadOnlyList<string> lines, int start)
    {
        for (var index = start; index < lines.Count; index++)
        {
            if (TryReadTableName(lines[index]) is not null)
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static string? TryReadTableName(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= 3 && trimmed[0] == '[' && trimmed[^1] == ']'
            ? trimmed.Trim('[', ']').Trim()
            : null;
    }

    private static string? TryReadAssignmentKey(string line)
    {
        var match = Regex.Match(
            line,
            "^\\s*(?<key>[A-Za-z0-9_-]+)\\s*=",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["key"].Value : null;
    }

    private static string TomlString(string value) =>
        JsonSerializer.Serialize(value);
}
