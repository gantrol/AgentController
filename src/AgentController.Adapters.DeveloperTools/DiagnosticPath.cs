namespace AgentController.Adapters.DeveloperTools;

internal static class DiagnosticPath
{
    internal static bool IsWithin(string rootPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(rootPath, candidatePath);
        return !Path.IsPathRooted(relative) &&
            !string.Equals(relative, "..", StringComparison.Ordinal) &&
            !relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) &&
            !relative.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                StringComparison.Ordinal);
    }

    internal static string? ResolveWorkspacePath(
        string? path,
        string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            IsToolOrigin(path))
        {
            return null;
        }

        try
        {
            var decoded = DecodeFilePath(path.Trim());
            return Path.IsPathFullyQualified(decoded)
                ? Path.GetFullPath(decoded)
                : Path.GetFullPath(decoded, workspaceRoot);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException or
            UriFormatException)
        {
            return null;
        }
    }

    private static string DecodeFilePath(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.IsFile)
        {
            return uri.LocalPath;
        }

        return Uri.UnescapeDataString(value)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsToolOrigin(string value) =>
        value.Equals("MSBUILD", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("CSC", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("VBC", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("FSC", StringComparison.OrdinalIgnoreCase);
}
