using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexController.Agents.DeepSeek;

public enum DeepSeekSessionStatus
{
    Idle,
    Running,
    Completed,
    WaitingForInput,
    Error,
}

public sealed record DeepSeekHarnessSession(
    string Id,
    string DisplayTitle,
    DeepSeekSessionStatus Status,
    long UpdatedAt);

public sealed record DeepSeekHarnessState(
    IReadOnlyList<DeepSeekHarnessSession> Sessions,
    string? CurrentSessionId,
    IReadOnlySet<string> Actions,
    int NavigationDepth,
    string? CurrentModel,
    DateTimeOffset ReadAt);

public sealed record DeepSeekHarnessResponse(
    bool Success,
    string Message,
    string? Status = null,
    int? WindowProcessId = null,
    DeepSeekHarnessState? State = null,
    string? ErrorCode = null)
{
    public bool WasDispatched =>
        Success && Status is "completed" or "foreground";
}

/// <summary>
/// Bounded loopback client for the DeepSeek Harness control bridge. The
/// gamepad source is distinct from Codex Micro; a 400 response is retried once
/// with the legacy source so already-installed Harness builds keep working.
/// </summary>
public sealed class DeepSeekHarnessClient
{
    public const string DefaultEndpoint =
        "http://127.0.0.1:3080/__agentcontroller/micro/request";
    private const int MaximumResponseBytes = 1024 * 1024;
    private static readonly TimeSpan RequestTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly HttpClient SharedClient = new(
        new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromMilliseconds(700),
        })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private readonly Uri _endpoint;
    private readonly HttpClient _httpClient;

    public DeepSeekHarnessClient(
        string endpoint = DefaultEndpoint,
        HttpClient? httpClient = null)
        : this(ParseEndpoint(endpoint), httpClient)
    {
    }

    public DeepSeekHarnessClient(
        Uri endpoint,
        HttpClient? httpClient = null)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _httpClient = httpClient ?? SharedClient;
    }

    public Uri Endpoint => _endpoint;

    public Task<DeepSeekHarnessResponse> ActivateAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync("activate", cancellationToken: cancellationToken);

    public Task<DeepSeekHarnessResponse> ReadStateAsync(
        CancellationToken cancellationToken = default) =>
        SendAsync("state/read", cancellationToken: cancellationToken);

    public Task<DeepSeekHarnessResponse> ActivateSessionAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return SendAsync(
            "session/activate",
            new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId.Trim(),
            },
            cancellationToken);
    }

    public Task<DeepSeekHarnessResponse> ExecuteActionAsync(
        string actionId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        var values = new Dictionary<string, object?>
        {
            ["actionId"] = actionId.Trim(),
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            values["sessionId"] = sessionId.Trim();
        }

        return SendAsync("action/execute", values, cancellationToken);
    }

    private async Task<DeepSeekHarnessResponse> SendAsync(
        string action,
        IReadOnlyDictionary<string, object?>? values = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var modern = await ExchangeAsync(
                    action,
                    values,
                    source: "agent-controller",
                    cancellationToken)
                .ConfigureAwait(false);
            var exchange = modern.StatusCode == HttpStatusCode.BadRequest
                ? await ExchangeAsync(
                        action,
                        values,
                        source: "codex-micro",
                        cancellationToken)
                    .ConfigureAwait(false)
                : modern;
            if (!exchange.IsSuccessStatusCode)
            {
                return new(
                    false,
                    $"DeepSeek Harness returned HTTP {(int)exchange.StatusCode}.",
                    ErrorCode: "deepseek.harness.http-error");
            }

            return ParseResponse(exchange.Body);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return new(
                false,
                "DeepSeek Harness did not respond before the local timeout.",
                ErrorCode: "deepseek.harness.timeout");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                IOException or
                JsonException or
                InvalidDataException)
        {
            return new(
                false,
                $"DeepSeek Harness is unavailable ({exception.GetType().Name}).",
                ErrorCode: "deepseek.harness.offline");
        }
    }

    private async Task<HttpExchange> ExchangeAsync(
        string action,
        IReadOnlyDictionary<string, object?>? values,
        string source,
        CancellationToken cancellationToken)
    {
        var requestValues = new Dictionary<string, object?>(
            StringComparer.Ordinal)
        {
            ["version"] = 1,
            ["source"] = source,
            ["action"] = action,
        };
        if (values is not null)
        {
            foreach (var (key, value) in values)
            {
                requestValues[key] = value;
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestValues),
                Encoding.UTF8,
                "application/json"),
        };
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token)
            .ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "DeepSeek Harness returned an oversized response.");
        }

        var bytes = await response.Content
            .ReadAsByteArrayAsync(timeout.Token)
            .ConfigureAwait(false);
        if (bytes.Length > MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "DeepSeek Harness returned an oversized response.");
        }

        return new(
            response.StatusCode,
            response.IsSuccessStatusCode,
            Encoding.UTF8.GetString(bytes));
    }

    private static DeepSeekHarnessResponse ParseResponse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("success", out var successElement) ||
            successElement.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidDataException(
                "DeepSeek Harness returned a malformed response.");
        }

        var success = successElement.GetBoolean();
        var message = ReadString(root, "message") ??
            (success
                ? "DeepSeek Harness accepted the request."
                : "DeepSeek Harness rejected the request.");
        var state = root.TryGetProperty("state", out var stateElement)
            ? ParseState(stateElement)
            : null;
        return new(
            success,
            message,
            ReadString(root, "status"),
            root.TryGetProperty("windowProcessId", out var processElement) &&
            processElement.TryGetInt32(out var processId)
                ? processId
                : null,
            state,
            success ? null : "deepseek.harness.rejected");
    }

    private static DeepSeekHarnessState? ParseState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var sessions = new List<DeepSeekHarnessSession>();
        if (state.TryGetProperty("sessions", out var sessionValues) &&
            sessionValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in sessionValues.EnumerateArray())
            {
                var id = ReadString(value, "id");
                if (id is null)
                {
                    continue;
                }

                var running = value.TryGetProperty(
                        "running",
                        out var runningValue) &&
                    runningValue.ValueKind == JsonValueKind.True;
                sessions.Add(new(
                    id,
                    ReadString(value, "displayTitle") ?? id,
                    ParseStatus(ReadString(value, "status"), running),
                    value.TryGetProperty("updatedAt", out var updated) &&
                    updated.TryGetInt64(out var timestamp)
                        ? timestamp
                        : 0));
            }
        }

        var actions = new HashSet<string>(StringComparer.Ordinal);
        if (state.TryGetProperty("capabilities", out var capabilities) &&
            capabilities.ValueKind == JsonValueKind.Object &&
            capabilities.TryGetProperty("actions", out var actionValues) &&
            actionValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var actionValue in actionValues.EnumerateArray())
            {
                if (actionValue.ValueKind == JsonValueKind.String &&
                    actionValue.GetString() is { Length: > 0 } actionId)
                {
                    actions.Add(actionId);
                }
            }
        }

        string? currentModel = null;
        if (state.TryGetProperty("components", out var components) &&
            components.ValueKind == JsonValueKind.Object)
        {
            currentModel = ReadString(components, "currentModel");
        }

        return new(
            sessions
                .OrderByDescending(item => item.UpdatedAt)
                .Take(6)
                .ToArray(),
            ReadString(state, "currentSessionId"),
            actions,
            state.TryGetProperty("navigationDepth", out var depth) &&
            depth.TryGetInt32(out var navigationDepth)
                ? Math.Max(0, navigationDepth)
                : 0,
            currentModel,
            DateTimeOffset.Now);
    }

    private static DeepSeekSessionStatus ParseStatus(
        string? value,
        bool runningFallback) => value switch
    {
        "running" => DeepSeekSessionStatus.Running,
        "completed" => DeepSeekSessionStatus.Completed,
        "waiting" => DeepSeekSessionStatus.WaitingForInput,
        "error" => DeepSeekSessionStatus.Error,
        _ => runningFallback
            ? DeepSeekSessionStatus.Running
            : DeepSeekSessionStatus.Idle,
    };

    private static string? ReadString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!.Trim()
            : null;

    private static Uri ParseEndpoint(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint))
        {
            throw new ArgumentException(
                "DeepSeek Harness endpoint must be an absolute URI.",
                nameof(value));
        }

        return endpoint;
    }

    private static Uri ValidateEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (endpoint.Scheme != Uri.UriSchemeHttp ||
            !endpoint.IsLoopback ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.Equals(
                endpoint.AbsolutePath,
                "/__agentcontroller/micro/request",
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "DeepSeek Harness endpoint must be the loopback HTTP control path.",
                nameof(endpoint));
        }

        return endpoint;
    }

    private sealed record HttpExchange(
        HttpStatusCode StatusCode,
        bool IsSuccessStatusCode,
        string Body);
}
