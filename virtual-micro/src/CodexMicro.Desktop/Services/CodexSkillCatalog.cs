using System.IO;
using System.Text.RegularExpressions;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexSkillDefinition(string Name, string SkillPath);

internal static class CodexSkillCatalog
{
    private const int MaximumSkills = 400;

    internal static IReadOnlyList<CodexSkillDefinition> ReadInstalled()
    {
        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(userProfile, ".codex", "skills"),
            Path.Combine(userProfile, ".agents", "skills"),
            Path.Combine(userProfile, ".codex", "plugins", "cache"),
        };
        var skills = new Dictionary<string, CodexSkillDefinition>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            foreach (var path in EnumerateSkillFiles(root))
            {
                var name = ReadName(path) ??
                    Path.GetFileName(Path.GetDirectoryName(path));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    skills.TryAdd(
                        path,
                        new CodexSkillDefinition(name.Trim(), path));
                }

                if (skills.Count >= MaximumSkills)
                {
                    break;
                }
            }

            if (skills.Count >= MaximumSkills)
            {
                break;
            }
        }

        return [.. skills.Values
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(skill => skill.SkillPath, StringComparer.OrdinalIgnoreCase)];
    }

    private static IEnumerable<string> EnumerateSkillFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory, "SKILL.md");
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var child in directories)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(child);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // A disappearing plugin-cache directory is skipped.
                }
            }
        }
    }

    private static string? ReadName(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path).Take(50))
            {
                var match = Regex.Match(
                    line,
                    "^\\s*name\\s*:\\s*[\"']?(?<name>[^\"'#]+)",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Groups["name"].Value.Trim();
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The directory name remains a useful fallback label.
        }

        return null;
    }
}
