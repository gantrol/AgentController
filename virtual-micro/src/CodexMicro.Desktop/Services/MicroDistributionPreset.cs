using System.IO;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Optional first-run defaults shipped beside CodexMicro.exe. A preset is
/// consulted only when no user profile exists; it never overwrites an
/// existing user's Agent, model or voice choices.
/// </summary>
internal sealed record MicroDistributionPreset(
    string Id,
    string DefaultHarnessId,
    string VoiceDefaultProvider,
    string SurfaceTheme)
{
    internal static MicroDistributionPreset? TryLoad(
        string? explicitPath = null)
    {
        try
        {
            var path = explicitPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable))
                {
                    return null;
                }

                path = Path.Combine(
                    Path.GetDirectoryName(executable)!,
                    "distribution-preset.json");
            }

            if (!File.Exists(path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<StoredPreset>(
                File.ReadAllText(path),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
            if (stored is null ||
                stored.SchemaVersion != 1 ||
                string.IsNullOrWhiteSpace(stored.Id) ||
                string.IsNullOrWhiteSpace(stored.DefaultHarnessId))
            {
                return null;
            }

            return new MicroDistributionPreset(
                stored.Id.Trim(),
                stored.DefaultHarnessId.Trim(),
                stored.Voice?.DefaultProvider?.Trim() ?? "system",
                stored.Surface?.Theme?.Trim() ?? string.Empty);
        }
        catch
        {
            return null;
        }
    }

    internal MicroProfileSnapshot Apply(MicroProfileSnapshot fallback) =>
        fallback with { ActiveHarnessId = DefaultHarnessId };

    private sealed class StoredPreset
    {
        public int SchemaVersion { get; set; }

        public string Id { get; set; } = string.Empty;

        public string DefaultHarnessId { get; set; } = string.Empty;

        public StoredVoice? Voice { get; set; }

        public StoredSurface? Surface { get; set; }
    }

    private sealed class StoredVoice
    {
        public string? DefaultProvider { get; set; }
    }

    private sealed class StoredSurface
    {
        public string? Theme { get; set; }
    }
}
