using System.IO;
using System.Text;
using System.Text.Json;
using CodexController.Localization;
using CodexController.Models;

namespace CodexController.Services;

public sealed class SettingsService
{
    private const int CurrentVersion = 3;
    private const double MinimumDeadZone = 0.35;
    private const double MaximumDeadZone = 0.80;
    private const int MinimumRepeatDelayMs = 220;
    private const int MaximumRepeatDelayMs = 600;
    private const int MinimumRepeatIntervalMs = 140;
    private const int MaximumRepeatIntervalMs = 300;

    private readonly string _legacySettingsPath;
    private readonly Action<bool> _updateStartupRegistration;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
    };

    public SettingsService()
        : this(new StartupRegistrationService())
    {
    }

    public SettingsService(
        StartupRegistrationService startupRegistration)
        : this(
            DefaultSettingsDirectory,
            DefaultLegacySettingsPath,
            (startupRegistration ??
                throw new ArgumentNullException(
                    nameof(startupRegistration))).Update)
    {
    }

    public SettingsService(
        string settingsDirectory,
        string legacySettingsPath,
        Action<bool> updateStartupRegistration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacySettingsPath);

        SettingsDirectory = Path.GetFullPath(settingsDirectory);
        _legacySettingsPath = Path.GetFullPath(legacySettingsPath);
        _updateStartupRegistration = updateStartupRegistration
            ?? throw new ArgumentNullException(
                nameof(updateStartupRegistration));
    }

    private static string DefaultSettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentController");

    private static string DefaultLegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexController",
        "settings.json");

    public string SettingsDirectory { get; }

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            var sourcePath = File.Exists(SettingsPath)
                ? SettingsPath
                : File.Exists(_legacySettingsPath)
                    ? _legacySettingsPath
                    : null;
            if (sourcePath is null)
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(sourcePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return NormalizeLoadedSettings(settings);
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalizedSettings = CreateNormalizedCopy(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(
            normalizedSettings,
            _jsonOptions);
        WriteAtomically(json);
        _updateStartupRegistration(
            normalizedSettings.StartWithWindows);
    }

    private static AppSettings NormalizeLoadedSettings(AppSettings? settings)
    {
        if (
            settings is null ||
            settings.Version is < 1 or > CurrentVersion)
        {
            return new AppSettings();
        }

        if (settings.Version <= 2)
        {
            // v1 originally defaulted to Advanced. The first Simple-default
            // migration shipped as v2, but an already-running older binary
            // could immediately persist Advanced as v2 and bypass it. Reset
            // both legacy versions once; v3+ then preserves an explicit user
            // choice between Simple and Advanced.
            settings.ComposerDialMode = ComposerDialModes.Simple;
            settings.Version = CurrentVersion;
        }

        NormalizeValues(settings);
        return settings;
    }

    private static AppSettings CreateNormalizedCopy(
        AppSettings settings)
    {
        var copy = new AppSettings
        {
            Version = CurrentVersion,
            Language = settings.Language,
            TextSize = settings.TextSize,
            ActiveAgentId = settings.ActiveAgentId,
            OnlyWhenCodexForeground =
                settings.OnlyWhenCodexForeground,
            HapticFeedback = settings.HapticFeedback,
            ShowOverlay = settings.ShowOverlay,
            RadialMenuMode = settings.RadialMenuMode,
            ComposerDialMode = settings.ComposerDialMode,
            StartWithWindows = settings.StartWithWindows,
            MinimizeToTray = settings.MinimizeToTray,
            DeadZone = settings.DeadZone,
            RepeatDelayMs = settings.RepeatDelayMs,
            RepeatIntervalMs = settings.RepeatIntervalMs,
            ReasoningDownShortcut =
                settings.ReasoningDownShortcut,
            ReasoningUpShortcut =
                settings.ReasoningUpShortcut,
            PlanToggleShortcut =
                settings.PlanToggleShortcut,
            ModelPickerShortcut =
                settings.ModelPickerShortcut,
            FastToggleShortcut =
                settings.FastToggleShortcut,
            ForkShortcut = settings.ForkShortcut,
            DictationShortcut = settings.DictationShortcut,
            CancelShortcut = settings.CancelShortcut,
        };

        NormalizeValues(copy);
        return copy;
    }

    private static void NormalizeValues(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.Language =
            AppLanguageParser.Parse(settings.Language).ToSettingValue();
        settings.TextSize = UiTextSizes.Normalize(settings.TextSize);
        settings.RadialMenuMode =
            RadialMenuModes.Normalize(settings.RadialMenuMode);
        settings.ComposerDialMode =
            ComposerDialModes.Normalize(settings.ComposerDialMode);
        settings.DeadZone =
            double.IsFinite(settings.DeadZone)
                ? Math.Clamp(
                    settings.DeadZone,
                    MinimumDeadZone,
                    MaximumDeadZone)
                : defaults.DeadZone;
        settings.RepeatDelayMs = Math.Clamp(
            settings.RepeatDelayMs,
            MinimumRepeatDelayMs,
            MaximumRepeatDelayMs);
        settings.RepeatIntervalMs = Math.Clamp(
            settings.RepeatIntervalMs,
            MinimumRepeatIntervalMs,
            MaximumRepeatIntervalMs);

        settings.ReasoningDownShortcut = NormalizeShortcut(
            settings.ReasoningDownShortcut,
            defaults.ReasoningDownShortcut);
        settings.ReasoningUpShortcut = NormalizeShortcut(
            settings.ReasoningUpShortcut,
            defaults.ReasoningUpShortcut);
        settings.PlanToggleShortcut = NormalizeShortcut(
            settings.PlanToggleShortcut,
            defaults.PlanToggleShortcut);
        settings.ModelPickerShortcut = NormalizeShortcut(
            settings.ModelPickerShortcut,
            defaults.ModelPickerShortcut);
        settings.FastToggleShortcut = NormalizeShortcut(
            settings.FastToggleShortcut,
            defaults.FastToggleShortcut);
        settings.ForkShortcut = NormalizeShortcut(
            settings.ForkShortcut,
            defaults.ForkShortcut);
        settings.DictationShortcut = NormalizeShortcut(
            settings.DictationShortcut,
            defaults.DictationShortcut);
        settings.CancelShortcut = NormalizeShortcut(
            settings.CancelShortcut,
            defaults.CancelShortcut);
        settings.ActiveAgentId =
            string.IsNullOrWhiteSpace(settings.ActiveAgentId)
                ? defaults.ActiveAgentId
                : settings.ActiveAgentId.Trim().ToLowerInvariant();
    }

    private static string NormalizeShortcut(
        string? shortcut,
        string fallback)
    {
        return string.IsNullOrWhiteSpace(shortcut)
            ? fallback
            : shortcut.Trim();
    }

    private void WriteAtomically(string json)
    {
        var temporaryPath = Path.Combine(
            SettingsDirectory,
            $".settings.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (!File.Exists(SettingsPath))
            {
                File.Move(temporaryPath, SettingsPath);
                return;
            }

            try
            {
                File.Replace(
                    temporaryPath,
                    SettingsPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            catch (IOException)
            {
                File.Move(temporaryPath, SettingsPath, overwrite: true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(temporaryPath, SettingsPath, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // Cleanup must not hide the original persistence failure.
                }
            }
        }
    }
}
