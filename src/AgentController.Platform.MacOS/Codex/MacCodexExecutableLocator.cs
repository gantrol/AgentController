namespace AgentController.Platform.MacOS.Codex;

public sealed record CodexExecutableProbe(
    bool IsFound,
    string? ExecutablePath);

public static class MacCodexExecutableLocator
{
    public static CodexExecutableProbe Locate() => Locate(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        File.Exists);

    internal static CodexExecutableProbe Locate(
        string? pathEnvironment,
        string? homeDirectory,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(pathEnvironment))
        {
            foreach (var directory in pathEnvironment.Split(
                         [Path.PathSeparator, ':', ';'],
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                candidates.Add(Path.Combine(directory, "codex"));
            }
        }

        candidates.Add("/opt/homebrew/bin/codex");
        candidates.Add("/usr/local/bin/codex");
        candidates.Add(
            "/Applications/Codex.app/Contents/Resources/codex");
        if (!string.IsNullOrWhiteSpace(homeDirectory))
        {
            candidates.Add(Path.Combine(
                homeDirectory,
                ".local",
                "bin",
                "codex"));
            candidates.Add(Path.Combine(
                homeDirectory,
                ".codex",
                "bin",
                "codex"));
        }

        var executable = candidates
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(fileExists);
        return new CodexExecutableProbe(
            executable is not null,
            executable);
    }
}
