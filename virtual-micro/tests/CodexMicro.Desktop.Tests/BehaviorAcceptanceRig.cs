using CodexMicro.Codex;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentController.MicroBroker;
using AgentController.Adapters.Codex.Windows;
using AgentController.MicroSurface.Wpf.SoftwareControl;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop.Tests;

// Replaces external processes only. No product policy or private field is mocked.
internal sealed class BehaviorAcceptanceRig : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly RecordingDriver _driver = new();
    private readonly MicroBrokerHost? _host;
    private readonly NamedPipeServerStream? _desktop;
    private readonly Task _server;
    private readonly ConcurrentQueue<JsonObject> _desktopRequests = new();
    private readonly ConcurrentQueue<string> _openedUris = new();
    internal BehaviorUiDesktop Ui { get; } = new();
    private const string ThreadId = "01000000-0000-0000-0000-000000000001";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "micro-acceptance-" + Guid.NewGuid().ToString("N"));
    internal IMicroTransport Transport { get; }
    internal static bool Legacy => Environment.GetEnvironmentVariable("MICRO_ACCEPTANCE_TRANSPORT") switch
    {
        "legacy" => true,
        null or "software" => false,
        var value => throw new InvalidOperationException($"Unknown acceptance transport: {value}"),
    };

    internal BehaviorAcceptanceRig(string mode, string? binding)
    {
        Directory.CreateDirectory(_directory);
        var pipe = "micro-acceptance-" + Guid.NewGuid().ToString("N");
        if (Legacy)
        {
            _host = new(_driver, pipe, Path.Combine(_directory, "lease"), TimeSpan.FromMinutes(1));
            _server = _host.RunAsync(_lifetime.Token);
            Transport = new VirtualMicroBroker(new MicroBrokerClient("acceptance", null, pipe, launchEnabled: false));
        }
        else
        {
            _desktop = new(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _server = ServeDesktopAsync();
            Transport = new SoftwareMicroTransport(new KeypadController(new CodexPeerClient(pipe),
                (method, _, _) => method == "collaborationMode/list"
                    ? Task.FromResult<JsonNode>(JsonSerializer.SerializeToNode(new { data = new[] { new { mode = "plan" }, new { mode = "default" } } })!)
                    : throw new InvalidOperationException("This dispatch lane does not provide a model catalog"),
                uri => _openedUris.Enqueue(uri)), new CodexUiController(Ui));
        }
        var slots = CodexMicroLayoutObserver.DefaultSlots.ToDictionary();
        if (binding == "skill") File.WriteAllText(Path.Combine(_directory, "SKILL.md"), "synthetic fixture");
        if (binding is not null)
            slots["ACT06"] = binding == "skill"
                ? new("APPS", null, new("skill", "fixture-skill", Path.Combine(_directory, "SKILL.md")))
                : new("DIFF", binding);
        Transport.CaptureContext = () => new(ThreadId,
            new Dictionary<string, string>(), new(slots, mode, new Dictionary<string, string>(), "synthetic"),
            MicroProfileSettings.CreateTransient().Current);
    }

    internal async Task ConnectAsync()
    {
        if (Legacy)
        {
            if (!Transport.TryConnect(out _, out var error)) throw new IOException(error);
        }
        else await Transport.RecoverCodexLinkAsync();
    }

    internal bool SawControlInput(string gesture, string? binding)
    {
        if (!Legacy && binding == "toggleReviewTab")
            return _openedUris.Count(uri => uri == $"codex://threads/{ThreadId}?view=review") == 1;
        if (!Legacy && gesture == "up")
            return _desktopRequests.Any(request => request["method"]?.GetValue<string>() == "thread-follower-update-thread-settings" &&
                request["params"]?["conversationId"]?.GetValue<string>() == ThreadId &&
                request["params"]?["threadSettings"]?["collaborationMode"]?["mode"]?.GetValue<string>() == "plan");
        if (!Legacy && binding != "turn.cancel") return (gesture, binding) switch
        {
            ("ACT12", null) => Ui.Commands.SequenceEqual(new[] { ("invoke", "send") }) && Ui.Read().Composer?.Text == "",
            ("ENC", null) => Ui.Commands.SequenceEqual(new[] { ("invoke", "model") }) && Ui.Read().MenuItems.Count == 2,
            ("wheel+", null) => Ui.Commands.SequenceEqual(new[] { ("ScrollUp", "conversation") }) && Ui.Read().Scroller?.ScrollPercent == 40,
            ("wheel-", null) => Ui.Commands.SequenceEqual(new[] { ("ScrollDown", "conversation") }) && Ui.Read().Scroller?.ScrollPercent == 60,
            ("down", null) => Ui.Commands.SequenceEqual(new[] { ("invoke", "sidebar") }) && Ui.Read().Sidebar?.Name == "Show sidebar",
            ("left", null) => Ui.Commands.SequenceEqual(new[] { ("history", "back") }) && Ui.Read().Route!.EndsWith("Chat Previous", StringComparison.Ordinal),
            ("right", null) => Ui.Commands.SequenceEqual(new[] { ("history", "forward") }) && Ui.Read().Route!.EndsWith("Chat B", StringComparison.Ordinal),
            ("ACT06", "skill") => Ui.Commands.SequenceEqual(new[] { ("paste-mention", "editor") }) && Ui.PastedName == "fixture-skill" && Ui.PastedPath == Path.Combine(_directory, "SKILL.md"),
            _ => throw new InvalidOperationException("FIXTURE GAP: model the new desktop command before accepting its dispatch"),
        };
        if (!Legacy) return _desktopRequests.Any(request =>
            request["method"]?.GetValue<string>() == "thread-follower-interrupt-turn" &&
            request["params"]?["conversationId"]?.GetValue<string>() == ThreadId &&
            request["params"]?["expectedTurnId"]?.GetValue<string>() == "fixture-turn" &&
            request["params"]?["mode"]?.GetValue<string>() == "user-stop");
        return _driver.Messages.Any(message =>
        {
            var parameters = message["p"];
            if (gesture is "up" or "right" or "down" or "left")
                return message["m"]?.GetValue<string>() == "v.oai.rad" && parameters?["d"]?.GetValue<double>() == 1 &&
                    parameters?["a"]?.GetValue<double>() == (gesture switch { "up" => 0, "right" => .25, "down" => .5, _ => .75 });
            var key = gesture switch { "wheel+" => "ENC_CW", "wheel-" => "ENC_CC", _ => gesture };
            return message["m"]?.GetValue<string>() == "v.oai.hid" && parameters?["k"]?.GetValue<string>() == key &&
                parameters?["act"]?.GetValue<int>() == (gesture.StartsWith("wheel", StringComparison.Ordinal) ? 2 : 1);
        });
    }

    private async Task ServeDesktopAsync()
    {
        try
        {
            var token = _lifetime.Token;
            await _desktop!.WaitForConnectionAsync(token);
            while (!token.IsCancellationRequested)
            {
                var header = new byte[4];
                await _desktop.ReadExactlyAsync(header, token);
                var bytes = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
                await _desktop.ReadExactlyAsync(bytes, token);
                var request = JsonNode.Parse(bytes)!.AsObject();
                var method = request["method"]!.GetValue<string>();
                if (request["type"]?.GetValue<string>() == "broadcast")
                {
                    if (method == "thread-stream-following-changed" && request["params"]?["following"]?.GetValue<bool>() == true)
                        await SendAsync(new
                        {
                            type = "broadcast", method = "thread-stream-state-changed", version = 11, sourceClientId = "owner",
                            @params = new
                            {
                                hostId = "local", conversationId = ThreadId,
                                change = new { type = "snapshot", revision = 1, conversationState = new
                                {
                                    latestModel = "fixture-model", latestReasoningEffort = "medium",
                                    latestCollaborationMode = new { mode = "default", settings = new { model = "fixture-model", reasoning_effort = "medium", developer_instructions = (string?)null } },
                                    turns = new[] { new { turnId = "fixture-turn", status = "inProgress", items = Array.Empty<object>() } },
                                } },
                            },
                        });
                    continue;
                }
                if (request["type"]?.GetValue<string>() != "request") continue;
                var supported = method is "initialize" or "thread-owner-discovery" or "thread-follower-interrupt-turn" ||
                    method == "thread-follower-update-thread-settings" &&
                    request["params"]?["threadSettings"]?["collaborationMode"]?["mode"]?.GetValue<string>() == "plan";
                if (method is not ("initialize" or "thread-owner-discovery")) _desktopRequests.Enqueue(request);
                // Only modeled operations receive an acknowledgement; unknown methods identify a fixture gap.
                await SendAsync(new
                {
                    type = "response", requestId = request["requestId"]!.GetValue<string>(), method,
                    resultType = supported ? "success" : "error", handledByClientId = "owner",
                    result = new { clientId = "isolated-desktop-client", ok = true, applied = supported }, error = "FIXTURE GAP: " + method,
                });
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task SendAsync(object response)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await _desktop!.WriteAsync(header, _lifetime.Token);
        await _desktop.WriteAsync(bytes, _lifetime.Token);
        await _desktop.FlushAsync(_lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await Transport.DisposeAsync();
        _lifetime.Cancel();
        _desktop?.Dispose();
        await _server;
        _host?.Dispose();
        _lifetime.Dispose();
        // Only the explicitly created synthetic directory can be removed.
        var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(_directory).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unexpected fixture directory");
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class RecordingDriver : IMicroDriverEndpoint
    {
        internal ConcurrentQueue<JsonObject> Messages { get; } = new();
        public bool IsConnected { get; private set; }
        public BrokerDriverInfo Connect()
        {
            IsConnected = true;
            return new(1, 0, 0, 0, MicroBrokerProtocol.DriverInfoFlagReady, "isolated-receiver", true);
        }
        public MicroSendResult Submit(IReadOnlyList<byte[]> reports)
        {
            var text = Encoding.UTF8.GetString(MicroRpcCodec.DecodePayload(reports.Select(report => (ReadOnlyMemory<byte>)report)));
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Messages.Enqueue(JsonNode.Parse(line)!.AsObject());
            return new(MicroSendDisposition.Accepted, reports.Count, reports.Count, 0, "recorded at external boundary");
        }
        public MicroSendResult TapKeyboard(BrokerKeyboardKey key, bool shift) => throw new NotSupportedException();
        public DriverOutputReport? TryReadOutput() => null;
        public BrokerDriverInfo ResetTransport() => throw new NotSupportedException();
        public void Dispose() => IsConnected = false;
    }
}
