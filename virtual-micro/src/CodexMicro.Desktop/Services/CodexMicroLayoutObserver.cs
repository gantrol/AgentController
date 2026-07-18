using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexMicroSlotBinding(
    string KeycapId,
    string? CommandId);

internal sealed record CodexMicroLayoutSnapshot(
    IReadOnlyDictionary<string, CodexMicroSlotBinding> Slots,
    string EncoderMode,
    IReadOnlyDictionary<string, string> AnalogActions,
    string Source)
{
    public CodexMicroSlotBinding GetSlot(string slotId) =>
        Slots.TryGetValue(slotId, out var slot)
            ? slot
            : CodexMicroLayoutObserver.DefaultSlots[slotId];
}

/// <summary>
/// Observes the same persisted setting used by the Codex settings surface.
/// This is deliberately read-only: Codex remains the sole owner/writer of
/// ~/.codex/config.toml and the simulator only mirrors keycap presentation.
/// </summary>
internal sealed partial class CodexMicroLayoutObserver : IDisposable
{
    internal static readonly IReadOnlyDictionary<string, CodexMicroSlotBinding>
        DefaultSlots = new Dictionary<string, CodexMicroSlotBinding>(
            StringComparer.Ordinal)
        {
            ["ACT06"] = new("FAST", null),
            ["ACT07"] = new("APPR", null),
            ["ACT08"] = new("REJ", null),
            ["ACT09"] = new("SPLIT", null),
            ["ACT10_ACT11"] = new("MIC", null),
            ["ACT12"] = new("CODEX", null),
        };

    private static readonly string[] SlotIds = [
        "ACT06",
        "ACT07",
        "ACT08",
        "ACT09",
        "ACT10_ACT11",
        "ACT12",
    ];

    private static readonly HashSet<string> EncoderModes = new(
        ["composer-navigation", "reasoning"],
        StringComparer.Ordinal);

    private readonly string _configPath;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private bool _started;
    private bool _disposed;

    public CodexMicroLayoutObserver(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex",
            "config.toml");
        Current = CreateDefault("Codex default layout");
    }

    public event EventHandler<CodexMicroLayoutSnapshot>? LayoutChanged;

    public CodexMicroLayoutSnapshot Current { get; private set; }

    public string ConfigPath => _configPath;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
            {
                return;
            }

            _started = true;
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                _watcher = new FileSystemWatcher(directory, Path.GetFileName(_configPath))
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += ConfigChanged;
                _watcher.Created += ConfigChanged;
                _watcher.Deleted += ConfigChanged;
                _watcher.Renamed += ConfigRenamed;
            }
        }

        Reload();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            _reloadTimer?.Dispose();
            _reloadTimer = null;
        }
    }

    internal static CodexMicroLayoutSnapshot Parse(string text, string source)
    {
        var slots = DefaultSlots.ToDictionary(
            item => item.Key,
            item => item.Value,
            StringComparer.Ordinal);
        var analog = new Dictionary<string, string>(StringComparer.Ordinal);
        var encoderMode = "composer-navigation";
        string[] section = [];

        foreach (var rawLine in text.Split(['\r', '\n']))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseHeader(line, out var parsedSection))
            {
                section = parsedSection;
                continue;
            }

            if (!TryParseAssignment(line, out var key, out var value))
            {
                continue;
            }

            if (SectionIs(section, "desktop", "codex-micro-layout"))
            {
                if (key == "encoderMode" &&
                    TryReadString(value, out var parsedMode) &&
                    EncoderModes.Contains(parsedMode))
                {
                    encoderMode = parsedMode;
                }

                continue;
            }

            if (section.Length == 4 &&
                SectionStartsWith(section, "desktop", "codex-micro-layout", "slots") &&
                slots.TryGetValue(section[3], out var binding))
            {
                if (key == "keycapId" &&
                    TryReadString(value, out var keycapId) &&
                    CodexKeycapCatalog.IsKnown(keycapId))
                {
                    slots[section[3]] = binding with { KeycapId = keycapId };
                }
                else if (key == "commandId" && TryReadString(value, out var commandId))
                {
                    slots[section[3]] = binding with { CommandId = commandId };
                }

                continue;
            }

            if (section.Length == 4 &&
                SectionStartsWith(section, "desktop", "codex-micro-layout", "analogStick") &&
                key is "commandId" or "skillName" &&
                TryReadString(value, out var analogAction))
            {
                analog[section[3]] = analogAction;
            }
        }

        // The config writer normally expands objects into TOML tables. These
        // fallbacks also cover a compact inline table and older JSON-like data.
        foreach (var slotId in SlotIds)
        {
            var block = Regex.Match(
                text,
                $@"(?is)(?:\b|[\""']){Regex.Escape(slotId)}(?:\b|[\""'])\s*[:=]\s*\{{(?<body>[^}}]*)\}}");
            if (!block.Success)
            {
                continue;
            }

            var current = slots[slotId];
            if (TryFindProperty(block.Groups["body"].Value, "keycapId", out var keycapId) &&
                CodexKeycapCatalog.IsKnown(keycapId))
            {
                current = current with { KeycapId = keycapId };
            }

            if (TryFindProperty(block.Groups["body"].Value, "commandId", out var commandId))
            {
                current = current with { CommandId = commandId };
            }

            slots[slotId] = current;
        }

        if (TryFindProperty(text, "encoderMode", out var inlineMode) &&
            EncoderModes.Contains(inlineMode))
        {
            encoderMode = inlineMode;
        }

        return new CodexMicroLayoutSnapshot(slots, encoderMode, analog, source);
    }

    private void ConfigChanged(object sender, FileSystemEventArgs e) =>
        ScheduleReload();

    private void ConfigRenamed(object sender, RenamedEventArgs e) =>
        ScheduleReload();

    private void ScheduleReload()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _reloadTimer ??= new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            _reloadTimer.Change(180, Timeout.Infinite);
        }
    }

    private void Reload()
    {
        CodexMicroLayoutSnapshot next;
        try
        {
            if (!File.Exists(_configPath))
            {
                next = CreateDefault("Codex default layout");
            }
            else
            {
                using var stream = new FileStream(
                    _configPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                next = Parse(reader.ReadToEnd(), _configPath);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Codex writes config.toml atomically. A transient replacement race
            // is retried; the last complete layout remains visible meanwhile.
            ScheduleReload();
            return;
        }

        if (Equivalent(Current, next))
        {
            return;
        }

        Current = next;
        LayoutChanged?.Invoke(this, next);
    }

    private static CodexMicroLayoutSnapshot CreateDefault(string source) =>
        new(
            DefaultSlots.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal),
            "composer-navigation",
            new Dictionary<string, string>(StringComparer.Ordinal),
            source);

    private static bool Equivalent(
        CodexMicroLayoutSnapshot left,
        CodexMicroLayoutSnapshot right) =>
        left.EncoderMode == right.EncoderMode &&
        left.Slots.Count == right.Slots.Count &&
        left.Slots.All(item =>
            right.Slots.TryGetValue(item.Key, out var value) && value == item.Value) &&
        left.AnalogActions.Count == right.AnalogActions.Count &&
        left.AnalogActions.All(item =>
            right.AnalogActions.TryGetValue(item.Key, out var value) && value == item.Value);

    private static bool TryParseHeader(string line, out string[] section)
    {
        section = [];
        if (line.Length < 3 || line[0] != '[' || line[^1] != ']')
        {
            return false;
        }

        var content = line.Trim('[', ']').Trim();
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        char quote = '\0';
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    current.Append(character);
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '.')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(character);
            }
        }

        parts.Add(current.ToString().Trim());
        section = [.. parts.Where(part => part.Length > 0)];
        return section.Length > 0;
    }

    private static bool TryParseAssignment(
        string line,
        out string key,
        out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var match = AssignmentRegex().Match(line);
        if (!match.Success)
        {
            return false;
        }

        key = match.Groups["quotedKey"].Success
            ? match.Groups["quotedKey"].Value
            : match.Groups["bareKey"].Value;
        value = match.Groups["value"].Value.Trim();
        return true;
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
        return match.Success && TryReadString(match.Groups["value"].Value, out value);
    }

    private static bool TryReadString(string text, out string value)
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
            value = JsonSerializer.Deserialize<string>(trimmed) ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string StripComment(string line)
    {
        char quote = '\0';
        var escaped = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (quote == '"' && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = quote == '\0' ? character : quote == character ? '\0' : quote;
                continue;
            }

            if (character == '#' && quote == '\0')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static bool SectionIs(string[] section, params string[] expected) =>
        section.Length == expected.Length &&
        SectionStartsWith(section, expected);

    private static bool SectionStartsWith(string[] section, params string[] expected) =>
        section.Length >= expected.Length &&
        expected.Select((value, index) => section[index] == value).All(match => match);

    [GeneratedRegex(
        "^\\s*(?:\"(?<quotedKey>[^\"]+)\"|(?<bareKey>[A-Za-z0-9_-]+))\\s*=\\s*(?<value>.+?)\\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex AssignmentRegex();
}
