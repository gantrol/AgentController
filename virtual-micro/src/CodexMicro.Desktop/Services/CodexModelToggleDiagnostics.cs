#if DEBUG
using System.IO;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

/// <summary>
/// Keeps a small local JSONL trail for intermittent model-toggle failures.
/// It records only task/model identifiers and timing, never prompts or output.
/// </summary>
internal static class CodexModelToggleDiagnostics
{
    private static readonly object Sync = new();

    internal static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexMicro",
        "logs",
        "model-toggle.jsonl");

    internal static void Record(
        CodexModelToggleResult result,
        TimeSpan elapsed)
    {
        try
        {
            var entry = JsonSerializer.Serialize(new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                result.Succeeded,
                result.Error,
                result.Detail,
                result.ThreadId,
                previousModel = result.Previous.ToString(),
                currentModel = result.Current.ToString(),
                result.PreviousEffort,
                result.CurrentEffort,
                elapsedMilliseconds = Math.Round(elapsed.TotalMilliseconds, 1),
            });
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, entry + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never turn a successful toggle into a failure.
        }
    }
}
#endif
