using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CodexMicro.Desktop.Services;

internal sealed record CodexThreadReadContext(
    JsonElement Identity,
    string IdentityKey,
    string ExecutionHostKey);

internal sealed record CodexUnreadStateSnapshot(
    IReadOnlySet<string> ThreadIds,
    CodexThreadReadContext? Context);

internal sealed class CodexUnreadStateReader
{
    private const string UnreadStateKey = "unread-thread-ids-by-host-v1";
    private const string HostReadStateKey = "electron-thread-read-state-v1";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private readonly string _globalStatePath;

    public CodexUnreadStateReader(string? globalStatePath = null)
    {
        var codexRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");
        _globalStatePath = globalStatePath ??
            Path.Combine(codexRoot, ".codex-global-state.json");
    }

    internal static bool ContainsUnreadThread(
        string? globalStateJson,
        string threadId,
        string hostId = "local",
        CodexThreadReadContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(globalStateJson) ||
            string.IsNullOrWhiteSpace(threadId) ||
            string.IsNullOrWhiteSpace(hostId))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(globalStateJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var normalizedThreadId = threadId.Trim();
            var normalizedHostId = hostId.Trim();
            if (root.TryGetProperty(HostReadStateKey, out var hostReadState))
            {
                return normalizedHostId == "local" &&
                    ReadHostThreadIds(hostReadState, context).Contains(normalizedThreadId);
            }

            if (ContainsUnreadThread(
                    root,
                    normalizedThreadId,
                    normalizedHostId))
            {
                return true;
            }

            return root.TryGetProperty(
                    "electron-persisted-atom-state",
                    out var persistedAtoms) &&
                persistedAtoms.ValueKind == JsonValueKind.Object &&
                ContainsUnreadThread(
                    persistedAtoms,
                    normalizedThreadId,
                    normalizedHostId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal async Task<CodexUnreadStateSnapshot?> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await ReadSharedTextAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (root.TryGetProperty(HostReadStateKey, out var hostReadState))
            {
                var context = await ReadContextAsync(cancellationToken);
                return context is null
                    ? null
                    : new(ReadHostThreadIds(hostReadState, context), context);
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            AddLegacyThreadIds(root, ids);
            if (root.TryGetProperty("electron-persisted-atom-state", out var atoms))
            {
                AddLegacyThreadIds(atoms, ids);
            }

            return new(ids, null);
        }
        catch (Exception exception) when (exception is IOException or
            UnauthorizedAccessException or JsonException or Win32Exception or
            InvalidOperationException)
        {
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    internal async Task<bool> WaitUntilUnreadAsync(
        string threadId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        CodexThreadReadContext? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var normalizedThreadId = threadId.Trim();
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var json = await ReadSharedTextAsync(cancellationToken);
                if (ContainsUnreadThread(json, normalizedThreadId, context: context))
                {
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(
                remaining < PollInterval ? remaining : PollInterval,
                cancellationToken);
        }
    }

    private static HashSet<string> ReadHostThreadIds(
        JsonElement state,
        CodexThreadReadContext? context)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (context is not null && state.ValueKind == JsonValueKind.Object &&
            state.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var number) &&
            number == 1 &&
            state.TryGetProperty("unreadByIdentity", out var identities) &&
            identities.ValueKind == JsonValueKind.Object &&
            identities.TryGetProperty(context.IdentityKey, out var hosts) &&
            hosts.ValueKind == JsonValueKind.Object &&
            hosts.TryGetProperty(context.ExecutionHostKey, out var threads))
        {
            AddThreadIds(threads, ids);
        }

        return ids;
    }

    private static void AddLegacyThreadIds(JsonElement container, HashSet<string> ids)
    {
        if (container.ValueKind == JsonValueKind.Object &&
            container.TryGetProperty(UnreadStateKey, out var hosts) &&
            hosts.ValueKind == JsonValueKind.Object &&
            hosts.TryGetProperty("local", out var threads))
        {
            AddThreadIds(threads, ids);
        }
    }

    private static void AddThreadIds(JsonElement threads, HashSet<string> ids)
    {
        if (threads.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var thread in threads.EnumerateArray())
        {
            if (thread.ValueKind == JsonValueKind.String && thread.GetString() is { } id)
            {
                ids.Add(id);
            }
        }
    }

    private static async Task<CodexThreadReadContext?> ReadContextAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
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
            _ = await RequestAsync(process, 1, "initialize", new
            {
                clientInfo = new { name = "agent-controller-micro", version = "1.2.0" },
            }, timeout.Token);
            await process.StandardInput.WriteLineAsync(
                """{"method":"initialized","params":{}}""".AsMemory(), timeout.Token);
            await process.StandardInput.FlushAsync(timeout.Token);
            var auth = await RequestAsync(process, 2, "getAuthStatus", new
            {
                includeToken = true,
                refreshToken = false,
            }, timeout.Token);
            return ParseContext(auth);
        }
        finally
        {
            await CodexAppServerProcess.StopAsync(process, started);
        }
    }

    private static async Task<JsonElement> RequestAsync(
        Process process, int id, string method, object parameters,
        CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Serialize(new { id, method, @params = parameters });
        await process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
        while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var response = document.RootElement;
            if (!response.TryGetProperty("id", out var responseId) ||
                responseId.ValueKind != JsonValueKind.Number ||
                !responseId.TryGetInt32(out var number) || number != id)
            {
                continue;
            }

            if (response.TryGetProperty("error", out _) ||
                !response.TryGetProperty("result", out var result))
            {
                throw new InvalidDataException("Codex read-state identity is unavailable.");
            }

            return result.Clone();
        }

        throw new EndOfStreamException("Codex read-state identity is unavailable.");
    }

    private static CodexThreadReadContext? ParseContext(JsonElement auth)
    {
        var method = ReadString(auth, "authMethod");
        JsonElement identity;
        string[] identityParts;
        if (method is "chatgpt" or "chatgptAuthTokens")
        {
            var payload = ReadString(auth, "authToken")?.Split('.').ElementAtOrDefault(1);
            if (payload is null)
            {
                return null;
            }

            try
            {
                payload = payload.Replace('-', '+').Replace('_', '/');
                using var token = JsonDocument.Parse(Convert.FromBase64String(
                    payload.PadRight((payload.Length + 3) / 4 * 4, '=')));
                if (!token.RootElement.TryGetProperty("https://api.openai.com/auth", out var claims))
                {
                    return null;
                }

                var account = ReadString(claims, "chatgpt_account_id") ?? ReadString(claims, "account_id");
                var user = ReadString(claims, "user_id") ?? ReadString(claims, "chatgpt_user_id");
                if (account is null || user is null)
                {
                    return null;
                }

                identity = JsonSerializer.SerializeToElement(new
                {
                    kind = "chatgpt", accountId = account, userId = user,
                });
                identityParts = ["chatgpt", account, user];
            }
            catch (Exception exception) when (exception is JsonException or FormatException)
            {
                return null;
            }
        }
        else
        {
            if (method is null && (!auth.TryGetProperty("requiresOpenaiAuth", out var required) ||
                required.ValueKind != JsonValueKind.False))
            {
                return null;
            }

            identity = JsonSerializer.SerializeToElement(new
            {
                kind = "execution-storage", authMode = method ?? "none",
            });
            identityParts = ["execution-storage", method ?? "none"];
        }

        // Codex v3 hashes the identity tuple and the local stdio host tuple
        // independently; neither bucket is the legacy literal host id.
        return new(identity, HashParts(identityParts),
            "local:" + HashParts(["local", "local", null]));
    }

    private static string? ReadString(JsonElement value, string key) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property) &&
        property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString() : null;

    private static string HashParts(string?[] parts) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            parts, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));

    private static bool ContainsUnreadThread(
        JsonElement container,
        string threadId,
        string hostId)
    {
        if (!container.TryGetProperty(UnreadStateKey, out var byHost) ||
            byHost.ValueKind != JsonValueKind.Object ||
            !byHost.TryGetProperty(hostId, out var unreadThreads) ||
            unreadThreads.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var candidate in unreadThreads.EnumerateArray())
        {
            if (candidate.ValueKind == JsonValueKind.String &&
                string.Equals(
                    candidate.GetString(),
                    threadId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<string?> ReadSharedTextAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_globalStatePath))
        {
            return null;
        }

        await using var stream = new FileStream(
            _globalStatePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
