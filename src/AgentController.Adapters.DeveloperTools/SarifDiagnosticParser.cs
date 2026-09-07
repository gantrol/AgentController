using System.Text.Json;
using AgentController.Application.Diagnostics;

namespace AgentController.Adapters.DeveloperTools;

internal sealed record SarifParseResult(
    IReadOnlyList<DeveloperDiagnosticObservation> Diagnostics,
    string? FailureCode = null);

internal static class SarifDiagnosticParser
{
    internal static async Task<SarifParseResult> ParseAsync(
        string sarifPath,
        string workspaceRoot,
        string buildId,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sarifPath))
        {
            return new([]);
        }

        var file = new FileInfo(sarifPath);
        if (file.Length > maxBytes)
        {
            return new([], "developer-diagnostics.sarif-too-large");
        }

        try
        {
            await using var stream = new FileStream(
                sarifPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return ParseDocument(
                document.RootElement,
                sarifPath,
                workspaceRoot,
                buildId);
        }
        catch (JsonException)
        {
            return new([], "developer-diagnostics.sarif-invalid");
        }
        catch (IOException)
        {
            return new([], "developer-diagnostics.sarif-unreadable");
        }
        catch (UnauthorizedAccessException)
        {
            return new([], "developer-diagnostics.sarif-unreadable");
        }
    }

    private static SarifParseResult ParseDocument(
        JsonElement root,
        string sarifPath,
        string workspaceRoot,
        string buildId)
    {
        if (!root.TryGetProperty("runs", out var runs) ||
            runs.ValueKind != JsonValueKind.Array)
        {
            return new([], "developer-diagnostics.sarif-invalid");
        }

        var diagnostics = new List<DeveloperDiagnosticObservation>();
        foreach (var run in runs.EnumerateArray())
        {
            var (toolName, toolVersion) = ReadTool(run);
            if (!run.TryGetProperty("results", out var results) ||
                results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                var message = ReadMessage(result);
                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                var code = ReadString(result, "ruleId") ?? "SARIF";
                var location = ReadLocation(
                    result,
                    run,
                    workspaceRoot);
                diagnostics.Add(new(
                    ReadSeverity(result),
                    code,
                    message,
                    location,
                    ReadProject(result),
                    new(
                        $"dotnet-build.sarif:{toolName}",
                        toolVersion,
                        buildId,
                        sarifPath)));
            }
        }

        return new(diagnostics);
    }

    private static (string Name, string? Version) ReadTool(JsonElement run)
    {
        if (!run.TryGetProperty("tool", out var tool) ||
            !tool.TryGetProperty("driver", out var driver))
        {
            return ("unknown", null);
        }

        return (
            ReadString(driver, "name") ?? "unknown",
            ReadString(driver, "semanticVersion") ??
            ReadString(driver, "version"));
    }

    private static string? ReadMessage(JsonElement result)
    {
        if (!result.TryGetProperty("message", out var message))
        {
            return null;
        }

        return ReadString(message, "text") ??
            ReadString(message, "markdown");
    }

    private static DeveloperDiagnosticSeverity ReadSeverity(
        JsonElement result) =>
        ReadString(result, "level")?.ToLowerInvariant() switch
        {
            "error" => DeveloperDiagnosticSeverity.Error,
            "warning" => DeveloperDiagnosticSeverity.Warning,
            _ => DeveloperDiagnosticSeverity.Information,
        };

    private static DeveloperDiagnosticLocation ReadLocation(
        JsonElement result,
        JsonElement run,
        string workspaceRoot)
    {
        if (!result.TryGetProperty("locations", out var locations) ||
            locations.ValueKind != JsonValueKind.Array)
        {
            return new(null);
        }

        var locationEnumerator = locations.EnumerateArray();
        if (!locationEnumerator.MoveNext() ||
            !locationEnumerator.Current.TryGetProperty(
                "physicalLocation",
                out var physicalLocation))
        {
            return new(null);
        }

        string? filePath = null;
        if (physicalLocation.TryGetProperty(
                "artifactLocation",
                out var artifactLocation))
        {
            var uri = ReadString(artifactLocation, "uri");
            var uriBaseId = ReadString(artifactLocation, "uriBaseId");
            filePath = ResolveArtifactPath(
                uri,
                uriBaseId,
                run,
                workspaceRoot);
        }

        int? startLine = null;
        int? startColumn = null;
        int? endLine = null;
        int? endColumn = null;
        if (physicalLocation.TryGetProperty("region", out var region))
        {
            startLine = ReadInt32(region, "startLine");
            startColumn = ReadInt32(region, "startColumn");
            endLine = ReadInt32(region, "endLine");
            endColumn = ReadInt32(region, "endColumn");
        }

        return new(
            filePath,
            startLine,
            startColumn,
            endLine,
            endColumn,
            filePath is not null &&
            DiagnosticPath.IsWithin(workspaceRoot, filePath));
    }

    private static string? ResolveArtifactPath(
        string? value,
        string? uriBaseId,
        JsonElement run,
        string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var baseValue = ReadUriBase(run, uriBaseId);
        if (baseValue is not null &&
            Uri.TryCreate(baseValue, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, value, out var combinedUri))
        {
            return DiagnosticPath.ResolveWorkspacePath(
                combinedUri.ToString(),
                workspaceRoot);
        }

        if (!string.IsNullOrWhiteSpace(baseValue))
        {
            var basePath = DiagnosticPath.ResolveWorkspacePath(
                baseValue,
                workspaceRoot);
            if (basePath is not null)
            {
                return DiagnosticPath.ResolveWorkspacePath(
                    Path.Combine(basePath, value),
                    workspaceRoot);
            }
        }

        return DiagnosticPath.ResolveWorkspacePath(value, workspaceRoot);
    }

    private static string? ReadUriBase(
        JsonElement run,
        string? uriBaseId)
    {
        if (string.IsNullOrWhiteSpace(uriBaseId) ||
            !run.TryGetProperty("originalUriBaseIds", out var bases) ||
            bases.ValueKind != JsonValueKind.Object ||
            !bases.TryGetProperty(uriBaseId, out var entry))
        {
            return null;
        }

        return ReadString(entry, "uri");
    }

    private static string? ReadProject(JsonElement result)
    {
        if (!result.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(properties, "project") ??
            ReadString(properties, "projectFile");
    }

    private static string? ReadString(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ReadInt32(
        JsonElement element,
        string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.TryGetInt32(out var value)
            ? value
            : null;
}
