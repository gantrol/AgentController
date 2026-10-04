using CodexMicro.Codex;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentController.MicroSurface.Wpf.SoftwareControl;

namespace CodexMicro.Plugin;

internal sealed class McpServer(KeypadController controller)
{
    private static JsonObject StringField(string description) => new() { ["type"] = "string", ["minLength"] = 1, ["description"] = description };
    private static readonly JsonObject ThreadField = StringField("Exact local Codex thread ID.");
    private static JsonObject Tool(string name, string description, bool readOnly, JsonObject properties, params string[] required) => new()
    {
        ["name"] = name, ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object", ["properties"] = properties,
            ["required"] = JsonSupport.Node(required), ["additionalProperties"] = false
        },
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = readOnly, ["destructiveHint"] = name is "stop_keypad_turn" or "reply_keypad_approval",
            ["idempotentHint"] = readOnly || name is "show_keypad" or "open_keypad_thread" or "set_keypad_model" or "set_keypad_reasoning" or "set_keypad_fast",
            ["openWorldHint"] = name is "send_keypad_message" or "reply_keypad_approval"
        }
    };

    internal static readonly JsonObject[] Tools =
    [
        Tool("get_keypad_capabilities", "Read implemented controls and unavailable original Micro features. This is not full device feature parity.", true, new()),
        Tool("show_keypad", "Show the original Codex Micro WPF keypad using software controls. Read get_keypad_capabilities for limitations. Optionally select an exact chat. Does not send input.", false,
            new() { ["thread_id"] = ThreadField.DeepClone() }),
        Tool("list_keypad_threads", "List recent local Codex chats and their exact IDs.", true, new()),
        Tool("get_keypad_models", "Read the live Codex model catalog and supported reasoning efforts.", true, new()),
        Tool("get_keypad_state", "Read an open chat's current model, active turn ID, and pending command/file approval requests from its desktop owner.", true,
            new() { ["thread_id"] = ThreadField.DeepClone() }, "thread_id"),
        Tool("open_keypad_thread", "Navigate Codex to the requested existing chat using its native deep link.", false,
            new() { ["thread_id"] = ThreadField.DeepClone() }, "thread_id"),
        Tool("new_keypad_thread", "Open a new Codex draft. Only when the user requests a new chat. Does not submit a message.", false, new()),
        Tool("fork_keypad_thread", "Fork the exact requested local chat and open the fork. Only when requested by the user. Do not retry after an unknown outcome.", false,
            new() { ["thread_id"] = ThreadField.DeepClone() }, "thread_id"),
        Tool("set_keypad_model", "Set the model for the selected chat's next turn, preserving permissions. Read get_keypad_models first.", false,
            new() { ["thread_id"] = ThreadField.DeepClone(), ["model"] = StringField("Catalog model ID."), ["effort"] = StringField("Supported effort; defaults to the model default.") }, "thread_id", "model"),
        Tool("set_keypad_reasoning", "Set a supported reasoning effort for the selected chat's next turn.", false,
            new() { ["thread_id"] = ThreadField.DeepClone(), ["effort"] = StringField("Supported reasoning effort.") }, "thread_id", "effort"),
        Tool("set_keypad_fast", "Enable or disable Fast service for the selected chat's next turn. May affect usage/cost; only when requested.", false,
            new() { ["thread_id"] = ThreadField.DeepClone(), ["enabled"] = new JsonObject { ["type"] = "boolean" } }, "thread_id", "enabled"),
        Tool("send_keypad_message", "Send the user's requested text to an idle selected chat through its existing desktop owner. Do not retry after a timeout.", false,
            new() { ["thread_id"] = ThreadField.DeepClone(), ["text"] = StringField("Exact text requested by the user.") }, "thread_id", "text"),
        Tool("stop_keypad_turn", "Stop only the exact active turn the user requested. Read its ID with get_keypad_state first.", false,
            new() { ["thread_id"] = ThreadField.DeepClone(), ["turn_id"] = StringField("Exact active turn ID from get_keypad_state.") }, "thread_id", "turn_id"),
        Tool("reply_keypad_approval", "Reply to one current command/file approval with the user's explicit decision. Read the request details with get_keypad_state first. No blanket or future approvals.", false,
            new() { ["thread_id"] = ThreadField.DeepClone(), ["request_id"] = StringField("Exact pending approval ID."),
                ["decision"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("accept", "decline") } }, "thread_id", "request_id", "decision")
    ];

    internal async Task RunAsync()
    {
        using var input = new StreamReader(Console.OpenStandardInput());
        await using var output = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        while (await input.ReadLineAsync() is { } line)
        {
            JsonNode? id = null;
            object response;
            try
            {
                if (line.Length > 1024 * 1024) throw new JsonException("Request too large");
                var message = JsonNode.Parse(line) as JsonObject ?? throw new JsonException("Expected object");
                if (!message.ContainsKey("id")) continue;
                id = message["id"]?.DeepClone();
                var method = message.Required("method");
                object result;
                switch (method)
                {
                    case "initialize":
                        result = new
                        {
                            protocolVersion = message["params"]?.Text("protocolVersion") is "2024-11-05" or "2025-03-26" or "2025-06-18" or "2025-11-25" ? message["params"]!.Text("protocolVersion") : "2025-11-25",
                            capabilities = new { tools = new { } },
                            serverInfo = new { name = "codex-micro-keypad", version = "0.3.2" },
                            instructions = "Resolve the exact target chat before controlling it. Read state before stopping or approving a specific request. Never retry a mutation with an unknown outcome."
                        };
                        break;
                    case "ping": result = new { }; break;
                    case "tools/list": result = new { tools = Tools }; break;
                    case "tools/call": result = await CallAsync(message["params"] as JsonObject ?? throw new ArgumentException("Missing tool parameters")); break;
                    default:
                        response = new { jsonrpc = "2.0", id, error = new { code = -32601, message = "Method not found" } };
                        await output.WriteLineAsync(JsonSerializer.Serialize(response, JsonSupport.Options));
                        continue;
                }
                response = new { jsonrpc = "2.0", id, result };
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
            {
                response = new { jsonrpc = "2.0", id, error = new { code = error is JsonException ? -32700 : -32602, message = error.Message } };
            }
            await output.WriteLineAsync(JsonSerializer.Serialize(response, JsonSupport.Options));
        }
    }

    private async Task<object> CallAsync(JsonObject parameters)
    {
        var name = parameters.Required("name");
        var tool = Tools.FirstOrDefault(item => item.Text("name") == name) ?? throw new ArgumentException("Unknown tool");
        var arguments = parameters["arguments"] as JsonObject ?? new JsonObject();
        var schema = tool["inputSchema"]!;
        foreach (var required in schema["required"]!.AsArray())
            if (!arguments.ContainsKey(required!.GetValue<string>())) throw new ArgumentException("Missing " + required);
        foreach (var (key, value) in arguments)
        {
            var field = schema["properties"]?[key] ?? throw new ArgumentException("Unknown argument: " + key);
            if (value is not JsonValue scalar || (field.Text("type") == "string" ? !scalar.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) : !scalar.TryGetValue<bool>(out _)))
                throw new ArgumentException("Invalid argument: " + key);
        }
        try
        {
            var result = name switch
            {
                "get_keypad_capabilities" => KeypadCapabilities.Read(),
                "show_keypad" => await KeypadWindowHost.ShowAsync(arguments.Text("thread_id"), CancellationToken.None),
                _ => await controller.ExecuteAsync(name, arguments)
            };
            return new { content = new[] { new { type = "text", text = result.ToJsonString() } }, structuredContent = result, isError = false };
        }
        catch (Exception error)
        {
            var detail = error is TimeoutException or OperationCanceledException ? "Codex timed out. A sent mutation may have an unknown outcome; do not retry it automatically." : error.Message;
            return new { content = new[] { new { type = "text", text = detail } }, isError = true };
        }
    }
}
