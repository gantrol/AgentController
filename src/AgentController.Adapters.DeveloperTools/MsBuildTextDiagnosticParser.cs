using System.Text.RegularExpressions;
using AgentController.Application.Diagnostics;

namespace AgentController.Adapters.DeveloperTools;

internal static partial class MsBuildTextDiagnosticParser
{
    internal static IReadOnlyList<DeveloperDiagnosticObservation> Parse(
        string? output,
        string workspaceRoot,
        string buildId,
        string? artifactPath)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        var diagnostics = new List<DeveloperDiagnosticObservation>();
        using var reader = new StringReader(output);
        while (reader.ReadLine() is { } line)
        {
            var match = DiagnosticLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var origin = match.Groups["origin"].Value.Trim();
            var (filePath, startLine, startColumn) = ReadLocation(
                origin,
                workspaceRoot);
            diagnostics.Add(new(
                ReadSeverity(match.Groups["severity"].Value),
                match.Groups["code"].Value,
                match.Groups["message"].Value.Trim(),
                new(
                    filePath,
                    startLine,
                    startColumn,
                    IsWithinWorkspace: filePath is not null &&
                        DiagnosticPath.IsWithin(workspaceRoot, filePath)),
                NullIfEmpty(match.Groups["project"].Value),
                new(
                    "dotnet-build.console",
                    null,
                    buildId,
                    artifactPath)));
        }

        return diagnostics;
    }

    private static (
        string? FilePath,
        int? StartLine,
        int? StartColumn) ReadLocation(
            string origin,
            string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(origin))
        {
            return (null, null, null);
        }

        var match = FileLocation().Match(origin);
        if (!match.Success)
        {
            return (
                DiagnosticPath.ResolveWorkspacePath(origin, workspaceRoot),
                null,
                null);
        }

        return (
            DiagnosticPath.ResolveWorkspacePath(
                match.Groups["file"].Value,
                workspaceRoot),
            ParseInt32(match.Groups["line"].Value),
            ParseInt32(match.Groups["column"].Value));
    }

    private static DeveloperDiagnosticSeverity ReadSeverity(string value) =>
        value.Equals("error", StringComparison.OrdinalIgnoreCase)
            ? DeveloperDiagnosticSeverity.Error
            : DeveloperDiagnosticSeverity.Warning;

    private static int? ParseInt32(string value) =>
        int.TryParse(value, out var result) ? result : null;

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(
        "^(?:(?<origin>.*?)\\s*:\\s*)?(?<severity>error|warning)\\s+(?<code>[A-Za-z]+[0-9]+)\\s*:\\s*(?<message>.*?)(?:\\s+\\[(?<project>[^\\[\\]]+\\.(?:cs|fs|vb)?proj)\\])?\\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(
        "^(?<file>.*)\\((?<line>[0-9]+)(?:,(?<column>[0-9]+)(?:,[0-9]+,[0-9]+)?)?\\)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FileLocation();
}
