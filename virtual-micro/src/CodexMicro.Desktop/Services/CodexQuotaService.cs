using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexQuotaWindow(
    double UsedPercent,
    int WindowDurationMinutes,
    DateTimeOffset ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100 - UsedPercent, 0, 100);
}

internal sealed record CodexQuotaResetCredit(
    string Title,
    DateTimeOffset ExpiresAt);

internal sealed record CodexQuotaSnapshot(
    CodexQuotaWindow Primary,
    CodexQuotaWindow? Secondary,
    string? PlanType,
    DateTimeOffset ReadAt)
{
    public IReadOnlyList<CodexQuotaResetCredit>? AvailableResets { get; init; }

    public IReadOnlyList<CodexQuotaWindow> Windows => Secondary is null
        ? [Primary]
        : [Primary, Secondary];

    public CodexQuotaWindow DisplayWindow => Windows
        .OrderBy(window => window.RemainingPercent)
        .ThenBy(window => window.WindowDurationMinutes)
        .First();
}

/// <summary>
/// Reads the current ChatGPT/Codex rate-limit windows from the local Codex App
/// Server. The service never writes account data and never treats a failed
/// read as an exhausted quota.
/// </summary>
internal sealed class CodexQuotaService
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(8);

    public async Task<CodexQuotaSnapshot?> ReadAsync(
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
        var started = false;

        try
        {
            started = process.Start();
            if (!started)
            {
                return null;
            }

            // Drain stderr so a verbose App Server cannot block while this
            // small read waits for its JSONL response on stdout.
            process.BeginErrorReadLine();
            await WriteRequestAsync(
                process,
                """
                {"method":"initialize","id":1,"params":{"clientInfo":{"name":"agent-controller-micro","title":"Agent Controller Micro","version":"1.2.0"}}}
                """,
                timeout.Token);
            _ = await ReadResponseAsync(process, responseId: 1, timeout.Token);

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

            return Parse(response);
        }
        catch (Exception exception)
            when (exception is IOException or
                InvalidOperationException or
                Win32Exception or
                JsonException or
                OperationCanceledException)
        {
            return null;
        }
        finally
        {
            await CodexAppServerProcess.StopAsync(process, started);
        }
    }

    internal static CodexQuotaSnapshot? Parse(
        string response,
        DateTimeOffset? readAt = null)
    {
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !TryReadRateLimitsWithPrimary(
                result,
                out var rateLimits,
                out var primary))
        {
            return null;
        }

        CodexQuotaWindow? secondary = null;
        if (rateLimits.TryGetProperty("secondary", out var secondaryElement) &&
            secondaryElement.ValueKind == JsonValueKind.Object &&
            TryReadWindow(secondaryElement, out var parsedSecondary))
        {
            secondary = parsedSecondary;
        }

        string? planType = null;
        if (rateLimits.TryGetProperty("planType", out var planElement) &&
            planElement.ValueKind == JsonValueKind.String)
        {
            planType = planElement.GetString();
        }

        var snapshotReadAt = readAt ?? DateTimeOffset.Now;
        return new CodexQuotaSnapshot(
            primary,
            secondary,
            planType,
            snapshotReadAt)
        {
            AvailableResets = ReadAvailableResets(result, snapshotReadAt),
        };
    }

    private static IReadOnlyList<CodexQuotaResetCredit>? ReadAvailableResets(
        JsonElement result,
        DateTimeOffset readAt)
    {
        if (!result.TryGetProperty("rateLimitResetCredits", out var resets) ||
            resets.ValueKind != JsonValueKind.Object ||
            !resets.TryGetProperty("credits", out var credits) ||
            credits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var available = new List<CodexQuotaResetCredit>();
        foreach (var credit in credits.EnumerateArray())
        {
            if (credit.ValueKind != JsonValueKind.Object ||
                !credit.TryGetProperty("status", out var status) ||
                status.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (!string.Equals(
                    status.GetString(),
                    "available",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!credit.TryGetProperty("title", out var title) ||
                title.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(title.GetString()) ||
                !credit.TryGetProperty("expiresAt", out var expiresAt) ||
                expiresAt.ValueKind != JsonValueKind.Number ||
                !expiresAt.TryGetInt64(out var expiresAtSeconds))
            {
                return null;
            }

            DateTimeOffset expiration;
            try
            {
                expiration = DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }

            if (expiration > readAt)
            {
                available.Add(new CodexQuotaResetCredit(title.GetString()!, expiration));
            }
        }

        return available.OrderBy(credit => credit.ExpiresAt).ToArray();
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

    private static bool TryReadRateLimitsWithPrimary(
        JsonElement result,
        out JsonElement rateLimits,
        out CodexQuotaWindow primary)
    {
        if (result.TryGetProperty("rateLimits", out rateLimits) &&
            rateLimits.ValueKind == JsonValueKind.Object &&
            rateLimits.TryGetProperty("primary", out var primaryElement) &&
            TryReadWindow(primaryElement, out primary))
        {
            return true;
        }

        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimit) &&
            byLimit.ValueKind == JsonValueKind.Object &&
            byLimit.TryGetProperty("codex", out rateLimits) &&
            rateLimits.ValueKind == JsonValueKind.Object &&
            rateLimits.TryGetProperty("primary", out primaryElement) &&
            TryReadWindow(primaryElement, out primary))
        {
            return true;
        }

        rateLimits = default;
        primary = default!;
        return false;
    }

    private static bool TryReadWindow(
        JsonElement element,
        out CodexQuotaWindow window)
    {
        window = default!;
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("usedPercent", out var usedElement) ||
            usedElement.ValueKind != JsonValueKind.Number ||
            !usedElement.TryGetDouble(out var usedPercent) ||
            !double.IsFinite(usedPercent) ||
            !element.TryGetProperty(
                "windowDurationMins",
                out var durationElement) ||
            durationElement.ValueKind != JsonValueKind.Number ||
            !durationElement.TryGetInt32(out var durationMinutes) ||
            durationMinutes <= 0 ||
            !element.TryGetProperty("resetsAt", out var resetElement) ||
            resetElement.ValueKind != JsonValueKind.Number ||
            !resetElement.TryGetInt64(out var resetSeconds))
        {
            return false;
        }

        try
        {
            window = new CodexQuotaWindow(
                Math.Clamp(usedPercent, 0, 100),
                durationMinutes,
                DateTimeOffset.FromUnixTimeSeconds(resetSeconds));
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
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
            if (document.RootElement.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number &&
                id.TryGetInt32(out var value) &&
                value == responseId)
            {
                return line;
            }
        }
    }
}
