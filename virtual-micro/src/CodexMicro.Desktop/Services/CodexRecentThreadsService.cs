using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexRecentThread(
    string ThreadId,
    string Title,
    string? WorkspacePath,
    DateTimeOffset RecencyAt);

/// <summary>
/// Reads the same recency ordering used by Codex's local thread list. The
/// legacy session_index.jsonl timestamp is a metadata-update timestamp and can
/// lag behind the renderer's recency order, so it cannot safely assign titles
/// to physical Agent slots on its own.
/// </summary>
internal sealed class CodexRecentThreadsService
{
    internal const string SourceName = "Codex App Server recent roster";

    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(8);

    public async Task<IReadOnlyList<CodexRecentThread>?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = CodexQuotaService.ResolveCodexExecutable(),
                Arguments = "app-server --stdio",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false),
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
                """{"method":"thread/list","id":2,"params":{"limit":18,"sortKey":"recency_at","sortDirection":"desc","useStateDbOnly":true}}""",
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

    internal static IReadOnlyList<CodexRecentThread>? Parse(string response)
    {
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var threads = new List<CodexRecentThread>(6);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !TryReadString(item, "id", out var threadId) ||
                string.IsNullOrWhiteSpace(threadId))
            {
                continue;
            }

            _ = TryReadString(item, "name", out var name);
            _ = TryReadString(item, "preview", out var preview);
            var title = FirstContentLine(name) ??
                FirstContentLine(preview) ??
                threadId.Trim();

            _ = TryReadString(item, "cwd", out var workspacePath);
            var recencyAt = TryReadUnixTime(item, "recencyAt", out var recency)
                ? recency
                : TryReadUnixTime(item, "updatedAt", out var updated)
                    ? updated
                    : DateTimeOffset.UnixEpoch;

            threads.Add(new CodexRecentThread(
                threadId.Trim(),
                title,
                string.IsNullOrWhiteSpace(workspacePath)
                    ? null
                    : workspacePath.Trim(),
                recencyAt));
            if (threads.Count == 6)
            {
                break;
            }
        }

        return threads;
    }

    private static string? FirstContentLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    private static bool TryReadString(
        JsonElement root,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            property.GetString() is not { } text)
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryReadUnixTime(
        JsonElement root,
        string name,
        out DateTimeOffset value)
    {
        value = default;
        if (!root.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var seconds))
        {
            return false;
        }

        try
        {
            value = DateTimeOffset.FromUnixTimeSeconds(seconds);
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
