using System.Globalization;
using System.IO;
using System.Text.Json;
using CodexMicro.Desktop.Services;

namespace CodexMicro.DesktopHost;

internal sealed class MicroLanguageSettings
{
    private readonly string _settingsPath;
    private readonly string[] _agentControllerSettingsPaths;

    internal MicroLanguageSettings()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(
            localAppData,
            "CodexMicro",
            "settings.json");
        _agentControllerSettingsPaths =
        [
            Path.Combine(localAppData, "AgentController", "settings.json"),
            Path.Combine(localAppData, "CodexController", "settings.json"),
        ];
    }

    internal MicroLocalization CreateLocalization()
    {
        return new MicroLocalization(
            LoadPreference(),
            ReadAgentControllerLanguage,
            () => CultureInfo.CurrentUICulture);
    }

    internal void Save(MicroLanguage language)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(
                new StoredSettings
                {
                    Language = MicroLocalization.ToSettingValue(language),
                },
                new JsonSerializerOptions { WriteIndented = true });
            var temporaryPath = _settingsPath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        catch
        {
            // A read-only profile must not prevent an in-memory language switch.
        }
    }

    private MicroLanguage LoadPreference()
    {
        return ReadLanguage(_settingsPath) ?? MicroLanguage.Auto;
    }

    private MicroLanguage? ReadAgentControllerLanguage()
    {
        foreach (var path in _agentControllerSettingsPaths)
        {
            var language = ReadLanguage(path);
            if (language is MicroLanguage.ZhCn or MicroLanguage.EnUs)
            {
                return language;
            }

            if (File.Exists(path))
            {
                return null;
            }
        }

        return null;
    }

    private static MicroLanguage? ReadLanguage(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Name.Equals(
                        "Language",
                        StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return MicroLocalization.Parse(property.Value.GetString());
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private sealed class StoredSettings
    {
        public string Language { get; set; } = "auto";
    }
}
