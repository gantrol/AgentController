using System.IO;

namespace CodexMicro.Desktop.Services;

internal static class CodexExecutableResolver
{
    internal static string? Resolve()
    {
        const string executableName = "codex.exe";
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            foreach (var binRoot in new[]
                     {
                         Path.Combine(localAppData, "OpenAI", "Codex", "bin"),
                         Path.Combine(localAppData, "Programs", "OpenAI", "Codex", "bin"),
                     })
            {
                try
                {
                    if (!Directory.Exists(binRoot))
                    {
                        continue;
                    }

                    var installed = Directory.EnumerateDirectories(binRoot)
                        .Select(directory => Path.Combine(directory, executableName))
                        .Append(Path.Combine(binRoot, executableName))
                        .Where(File.Exists)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    if (installed is not null)
                    {
                        return installed;
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ??
                     string.Empty).Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), executableName);
                if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            catch (Exception exception) when (
                exception is ArgumentException or
                    NotSupportedException or
                    PathTooLongException)
            {
            }
        }

        return null;
    }
}
