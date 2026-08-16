using System.IO;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed record MicroProfileSnapshot(
    CodexQuickModel QuickModelA,
    CodexQuickModel QuickModelB,
    string ActiveHarnessId = "codex",
    string AgentSource = "recent",
    bool SingleTapAgentKeys = false,
    string? KeypadName = null,
    double? WindowLeft = null,
    double? WindowTop = null,
    bool WindowTopmost = true);

/// <summary>
/// Owns settings that extend the official Codex Micro surface. The file is
/// deliberately separate from the host's language settings so either side can
/// evolve without erasing fields owned by the other.
/// </summary>
internal sealed class MicroProfileSettings
{
    private static readonly MicroProfileSnapshot DefaultSnapshot =
        new(CodexQuickModel.Sol, CodexQuickModel.Luna);

    private readonly string? _settingsPath;
    private readonly MicroProfileSnapshot _defaultSnapshot;

    internal MicroProfileSettings(string? settingsPath = null)
    {
        _defaultSnapshot = settingsPath is null
            ? MicroDistributionPreset.TryLoad()?.Apply(DefaultSnapshot) ??
                DefaultSnapshot
            : DefaultSnapshot;
        _settingsPath = settingsPath ?? GetDefaultPath();
        Current = Read(_settingsPath) ?? _defaultSnapshot;
    }

    private MicroProfileSettings(
        string settingsPath,
        MicroProfileSnapshot fallback,
        string persistentKeypadId)
    {
        _settingsPath = settingsPath;
        PersistentKeypadId = persistentKeypadId;
        _defaultSnapshot = Normalize(fallback);
        var existing = Read(settingsPath);
        Current = existing ?? _defaultSnapshot;
        LastSaveSucceeded = existing is not null || Persist(Current);
    }

    private MicroProfileSettings(MicroProfileSnapshot snapshot)
    {
        _defaultSnapshot = Normalize(snapshot);
        Current = _defaultSnapshot;
        LastSaveSucceeded = true;
    }

    internal event EventHandler? Changed;

    internal MicroProfileSnapshot Current { get; private set; }

    internal string? PersistentKeypadId { get; }

    internal bool LastSaveSucceeded { get; private set; } = true;

    internal static MicroProfileSettings CreateTransient(
        MicroProfileSnapshot? snapshot = null) =>
        new(snapshot ?? DefaultSnapshot);

    internal static MicroProfileSettings CreateForKeypad(
        string keypadId,
        MicroProfileSnapshot initialSnapshot)
    {
        var normalizedId = NormalizeKeypadId(keypadId);
        return new MicroProfileSettings(
            GetKeypadPath(normalizedId),
            initialSnapshot,
            normalizedId);
    }

    internal static IReadOnlyList<MicroProfileSettings> LoadAdditionalKeypads()
    {
        var directory = GetKeypadDirectory();
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var result = new List<MicroProfileSettings>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json")
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var id = Path.GetFileNameWithoutExtension(path);
            if (!Guid.TryParseExact(id, "N", out _))
            {
                continue;
            }

            result.Add(new MicroProfileSettings(
                path,
                DefaultSnapshot,
                id.ToLowerInvariant()));
        }

        return result;
    }

    internal void SetQuickModelA(CodexQuickModel model)
    {
        ValidateKnown(model);
        var current = Current;
        Update(model == current.QuickModelB
            ? current with
            {
                QuickModelA = model,
                QuickModelB = current.QuickModelA,
            }
            : current with { QuickModelA = model });
    }

    internal void SetQuickModelB(CodexQuickModel model)
    {
        ValidateKnown(model);
        var current = Current;
        Update(model == current.QuickModelA
            ? current with
            {
                QuickModelA = current.QuickModelB,
                QuickModelB = model,
            }
            : current with { QuickModelB = model });
    }

    internal void SetActiveHarness(string harnessId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(harnessId);
        Update(Current with { ActiveHarnessId = harnessId.Trim() });
    }

    internal void SetAgentSource(string source)
    {
        if (!IsAgentSource(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        Update(Current with { AgentSource = source });
    }

    internal void SetSingleTapAgentKeys(bool value) =>
        Update(Current with { SingleTapAgentKeys = value });

    internal void SetKeypadName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
        Update(Current with { KeypadName = name });
    }

    internal void SetWindowPlacement(
        double left,
        double top,
        bool topmost)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        Update(Current with
        {
            WindowLeft = left,
            WindowTop = top,
            WindowTopmost = topmost,
        });
    }

    internal bool DeletePersistentKeypad()
    {
        if (PersistentKeypadId is null || _settingsPath is null)
        {
            return false;
        }

        try
        {
            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    internal void Reset() => Update(_defaultSnapshot);

    private void Update(MicroProfileSnapshot snapshot)
    {
        var normalized = Normalize(snapshot);
        if (normalized == Current)
        {
            return;
        }

        Current = normalized;
        LastSaveSucceeded = Persist(normalized);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool Persist(MicroProfileSnapshot snapshot)
    {
        if (_settingsPath is null)
        {
            return true;
        }

        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(
                new StoredSettings
                {
                    QuickModelA = ToSettingValue(snapshot.QuickModelA),
                    QuickModelB = ToSettingValue(snapshot.QuickModelB),
                    ActiveHarnessId = snapshot.ActiveHarnessId,
                    AgentSource = snapshot.AgentSource,
                    SingleTapAgentKeys = snapshot.SingleTapAgentKeys,
                    KeypadName = snapshot.KeypadName,
                    WindowLeft = snapshot.WindowLeft,
                    WindowTop = snapshot.WindowTop,
                    WindowTopmost = snapshot.WindowTopmost,
                },
                new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MicroProfileSnapshot? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredSettings>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            if (stored is null)
            {
                return null;
            }

            return Normalize(new(
                ParseModel(stored.QuickModelA, DefaultSnapshot.QuickModelA),
                ParseModel(stored.QuickModelB, DefaultSnapshot.QuickModelB),
                stored.ActiveHarnessId,
                stored.AgentSource,
                stored.SingleTapAgentKeys,
                stored.KeypadName,
                stored.WindowLeft,
                stored.WindowTop,
                stored.WindowTopmost));
        }
        catch
        {
            return null;
        }
    }

    private static MicroProfileSnapshot Normalize(
        MicroProfileSnapshot snapshot)
    {
        var first = IsKnown(snapshot.QuickModelA)
            ? snapshot.QuickModelA
            : DefaultSnapshot.QuickModelA;
        var second = IsKnown(snapshot.QuickModelB)
            ? snapshot.QuickModelB
            : DefaultSnapshot.QuickModelB;
        if (first == second)
        {
            first = DefaultSnapshot.QuickModelA;
            second = DefaultSnapshot.QuickModelB;
        }

        var harnessId = string.IsNullOrWhiteSpace(snapshot.ActiveHarnessId)
            ? DefaultSnapshot.ActiveHarnessId
            : snapshot.ActiveHarnessId.Trim();
        var agentSource = IsAgentSource(snapshot.AgentSource)
            ? snapshot.AgentSource
            : DefaultSnapshot.AgentSource;
        var keypadName = string.IsNullOrWhiteSpace(snapshot.KeypadName)
            ? null
            : snapshot.KeypadName.Trim();
        var left = snapshot.WindowLeft is { } windowLeft &&
            double.IsFinite(windowLeft)
                ? (double?)windowLeft
                : null;
        var top = snapshot.WindowTop is { } windowTop &&
            double.IsFinite(windowTop)
                ? (double?)windowTop
                : null;
        return new(
            first,
            second,
            harnessId,
            agentSource,
            snapshot.SingleTapAgentKeys,
            keypadName,
            left,
            top,
            snapshot.WindowTopmost);
    }

    private static CodexQuickModel ParseModel(
        string? value,
        CodexQuickModel fallback) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "sol" => CodexQuickModel.Sol,
            "terra" => CodexQuickModel.Terra,
            "luna" => CodexQuickModel.Luna,
            _ => fallback,
        };

    private static string ToSettingValue(CodexQuickModel model) =>
        model.ToString().ToLowerInvariant();

    private static bool IsKnown(CodexQuickModel model) =>
        model is CodexQuickModel.Sol or
            CodexQuickModel.Terra or
            CodexQuickModel.Luna;

    private static bool IsAgentSource(string? value) =>
        value is "recent" or "pinned" or "priority" or "custom";

    private static void ValidateKnown(CodexQuickModel model)
    {
        if (!IsKnown(model))
        {
            throw new ArgumentOutOfRangeException(nameof(model));
        }
    }

    private static string GetDefaultPath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CodexMicro", "micro-profile.json");
    }

    private static string GetKeypadDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "CodexMicro", "keypads");
    }

    private static string GetKeypadPath(string keypadId) =>
        Path.Combine(GetKeypadDirectory(), keypadId + ".json");

    private static string NormalizeKeypadId(string keypadId)
    {
        if (!Guid.TryParseExact(keypadId, "N", out var parsed))
        {
            throw new ArgumentException(
                "A keypad id must be a GUID in N format.",
                nameof(keypadId));
        }

        return parsed.ToString("N");
    }

    private sealed class StoredSettings
    {
        public string QuickModelA { get; set; } = "sol";

        public string QuickModelB { get; set; } = "luna";

        public string ActiveHarnessId { get; set; } = "codex";

        public string AgentSource { get; set; } = "recent";

        public bool SingleTapAgentKeys { get; set; }

        public string? KeypadName { get; set; }

        public double? WindowLeft { get; set; }

        public double? WindowTop { get; set; }

        public bool WindowTopmost { get; set; } = true;
    }
}
