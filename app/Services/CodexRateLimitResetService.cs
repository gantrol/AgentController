using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexController.Services;

internal sealed record CodexRateLimitResetCredit(
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt);

internal sealed class CodexRateLimitResetService
{
    private static readonly TimeSpan QueryTimeout =
        TimeSpan.FromSeconds(8);

    public async Task<IReadOnlyList<CodexRateLimitResetCredit>>
        ReadAvailableFullResetsAsync(
            CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ResolveCodexExecutable(),
                Arguments = "app-server --stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        try
        {
            if (!process.Start())
            {
                return [];
            }

            process.BeginErrorReadLine();
            await WriteRequestAsync(
                process,
                """
                {"method":"initialize","id":1,"params":{"clientInfo":{"name":"agent-controller","title":"Agent Controller","version":"1.1.0"}}}
                """,
                timeout.Token);
            _ = await ReadResponseAsync(
                process,
                responseId: 1,
                timeout.Token);

            await WriteRequestAsync(
                process,
                """{"method":"initialized","params":{}}""",
                timeout.Token);
            await WriteRequestAsync(
                process,
                """{"method":"account/rateLimits/read","id":2}""",
                timeout.Token);
            var response = await ReadResponseAsync(
                process,
                responseId: 2,
                timeout.Token);

            return ParseAvailableFullResetCredits(response);
        }
        catch (Exception exception)
            when (
                exception is IOException or
                InvalidOperationException or
                Win32Exception or
                JsonException or
                OperationCanceledException)
        {
            return [];
        }
        finally
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // The process never started or exited during cleanup.
            }
        }
    }

    internal static IReadOnlyList<CodexRateLimitResetCredit>
        ParseAvailableFullResetCredits(string response)
    {
        using var document = JsonDocument.Parse(response);
        if (
            !document.RootElement.TryGetProperty(
                "result",
                out var result) ||
            !result.TryGetProperty(
                "rateLimitResetCredits",
                out var resetCredits) ||
            !resetCredits.TryGetProperty(
                "credits",
                out var credits) ||
            credits.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var available = new List<CodexRateLimitResetCredit>();
        foreach (var credit in credits.EnumerateArray())
        {
            if (
                !HasStringValue(credit, "title", "Full reset") ||
                !HasStringValue(credit, "status", "available") ||
                !TryReadUnixSeconds(
                    credit,
                    "grantedAt",
                    out var grantedAt) ||
                !TryReadUnixSeconds(
                    credit,
                    "expiresAt",
                    out var expiresAt))
            {
                continue;
            }

            available.Add(new CodexRateLimitResetCredit(
                grantedAt,
                expiresAt));
        }

        return available
            .OrderBy(credit => credit.ExpiresAt)
            .ToArray();
    }

    internal static string ResolveCodexExecutable()
    {
        const string executableName = "codex.exe";
        var candidates = new List<string>();
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(
                localAppData,
                "Programs",
                "OpenAI",
                "Codex",
                "bin",
                executableName));
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var directory in path.Split(
                         Path.PathSeparator,
                         StringSplitOptions.RemoveEmptyEntries |
                         StringSplitOptions.TrimEntries))
            {
                candidates.Add(Path.Combine(directory, executableName));
            }
        }

        return candidates.FirstOrDefault(File.Exists) ?? executableName;
    }

    private static async Task WriteRequestAsync(
        Process process,
        string request,
        CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(
            request.AsMemory(),
            cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadResponseAsync(
        Process process,
        int responseId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(
                cancellationToken);
            if (line is null)
            {
                throw new IOException(
                    "Codex App Server closed before returning a response.");
            }

            using var document = JsonDocument.Parse(line);
            if (
                document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var value) &&
                value == responseId)
            {
                return line;
            }
        }
    }

    private static bool HasStringValue(
        JsonElement element,
        string propertyName,
        string expected)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            string.Equals(
                property.GetString(),
                expected,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadUnixSeconds(
        JsonElement element,
        string propertyName,
        out DateTimeOffset value)
    {
        value = default;
        if (
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var unixSeconds))
        {
            return false;
        }

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
