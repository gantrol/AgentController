using System.IO;
using System.Text;
using System.Text.Json;
using CodexController.Models;

namespace CodexController.Services;

public sealed class SettingsService
{
    private const int CurrentVersion = 1;
    private const double MinimumDeadZone = 0.35;
    private const double MaximumDeadZone = 0.80;
    private const int MinimumRepeatDelayMs = 220;
    private const int MaximumRepeatDelayMs = 600;
    private const int MinimumRepeatIntervalMs = 140;
    private const int MaximumRepeatIntervalMs = 300;

    private readonly StartupRegistrationService _startupRegistration;
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
    {
        _startupRegistration = startupRegistration
            ?? throw new ArgumentNullException(nameof(startupRegistration));
    }

    public string SettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentController");

    public string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    private string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexController",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            var sourcePath = File.Exists(SettingsPath)
                ? SettingsPath
                : File.Exists(LegacySettingsPath)
                    ? LegacySettingsPath
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
        NormalizeForSave(settings);
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        WriteAtomically(json);
        _startupRegistration.Update(settings.StartWithWindows);
    }

    private static AppSettings NormalizeLoadedSettings(AppSettings? settings)
    {
        if (
            settings is null ||
            settings.Version is < 1 or > CurrentVersion)
        {
            return new AppSettings();
        }

        NormalizeValues(settings);
        return settings;
    }

    private static void NormalizeForSave(AppSettings settings)
    {
        settings.Version = CurrentVersion;
        NormalizeValues(settings);
    }

    private static void NormalizeValues(AppSettings settings)
    {
        var defaults = new AppSettings();
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

        settings.ReasoningDownShortcut ??= defaults.ReasoningDownShortcut;
        settings.ReasoningUpShortcut ??= defaults.ReasoningUpShortcut;
        settings.ModelPickerShortcut ??= defaults.ModelPickerShortcut;
        settings.FastToggleShortcut ??= defaults.FastToggleShortcut;
        settings.DictationShortcut ??= defaults.DictationShortcut;
        settings.SubmitShortcut ??= defaults.SubmitShortcut;
        settings.CancelShortcut ??= defaults.CancelShortcut;
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
