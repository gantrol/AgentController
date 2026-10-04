using CodexMicro.Codex;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentController.MicroSurface.Wpf.SoftwareControl;
using CodexController.Services;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class SoftwareControlRegressionTests
{
    private const string A = "01000000-0000-0000-0000-000000000001";
    private const string B = "01000000-0000-0000-0000-000000000002";

    [Fact]
    public async Task PlanModeRoundTripPreservesOtherSettings()
    {
        await using var owner = new FakeOwner();
        await using var controller = new KeypadController(new(owner.Name), Catalog);
        foreach (var expected in new[] { "plan", "default" })
        {
            await controller.ExecuteAsync("toggle_keypad_plan", new() { ["thread_id"] = A });
            var readback = await controller.ExecuteAsync("get_keypad_state", new() { ["thread_id"] = A });
            Assert.Equal(expected, readback["collaborationMode"]!["mode"]!.GetValue<string>());
            Assert.Null(readback["collaborationMode"]!["settings"]!["developer_instructions"]);
            Assert.Equal("model-a", readback["collaborationMode"]!["settings"]!["model"]!.GetValue<string>());
            Assert.Equal("high", readback["collaborationMode"]!["settings"]!["reasoning_effort"]!.GetValue<string>());
            Assert.Equal("model-a", readback["model"]!.GetValue<string>());
            Assert.Equal("high", readback["effort"]!.GetValue<string>());
            Assert.Null(readback["serviceTier"]);
        }
    }

    [Fact]
    public async Task SixRapidFastClicksAlternateTheDesktopTierAndPreserveModelAndEffort()
    {
        await using var owner = new FakeOwner();
        await using var transport = new SoftwareMicroTransport(new KeypadController(new(owner.Name), Catalog));
        transport.CaptureContext = () => new(A, new Dictionary<string, string>(),
            new(CodexMicroLayoutObserver.DefaultSlots, "reasoning", new Dictionary<string, string>(), "fixture"),
            MicroProfileSettings.CreateTransient().Current);
        var acknowledged = new List<string?>();
        transport.ServiceTierApplied = (_, tier) => acknowledged.Add(tier);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => transport.TapKeyAsync("ACT06")));

        Assert.All(results, result => Assert.Equal(MicroSendDisposition.Accepted, result.Disposition));
        Assert.Equal(new string?[] { "priority", null, "priority", null, "priority", null }, owner.Tiers);
        Assert.Equal(owner.Tiers, acknowledged);
        Assert.Equal("model-a", owner.Model);
    }

    [Fact]
    public async Task RejectedFastChangeDoesNotPublishSuccess()
    {
        await using var owner = new FakeOwner { RejectSettings = true };
        await using var transport = new SoftwareMicroTransport(new KeypadController(new(owner.Name), Catalog));
        transport.CaptureContext = () => new(A, new Dictionary<string, string>(),
            new(CodexMicroLayoutObserver.DefaultSlots, "reasoning", new Dictionary<string, string>(), "fixture"),
            MicroProfileSettings.CreateTransient().Current);
        var published = false;
        transport.ServiceTierApplied = (_, _) => published = true;
        Assert.Equal(MicroSendDisposition.Rejected, (await transport.TapKeyAsync("ACT06")).Disposition);
        Assert.False(published);
        Assert.Empty(owner.Tiers);
    }

    [Fact]
    public void FastObservationFollowsSnapshotPatchesAndSettingsReplacement()
    {
        var accumulator = new CodexThreadModelStateAccumulator(A, "owner");
        var changes = new[]
        {
            """{"type":"snapshot","revision":1,"conversationState":{"latestModel":"model-a","latestReasoningEffort":"high","latestThreadSettings":{"serviceTier":"priority"}}}""",
            """{"type":"patches","baseRevision":1,"revision":2,"patches":[{"op":"replace","path":["latestThreadSettings","serviceTier"],"value":null}]}""",
            """{"type":"patches","baseRevision":2,"revision":3,"patches":[{"op":"add","path":["latestThreadSettings","serviceTier"],"value":"fast"}]}""",
            """{"type":"patches","baseRevision":3,"revision":4,"patches":[{"op":"replace","path":["latestThreadSettings"],"value":{"model":"model-a","effort":"high"}}]}""",
        };
        var expected = new[] { true, false, true, false };
        for (var i = 0; i < changes.Length; i++)
        {
            using var json = JsonDocument.Parse(changes[i]);
            var state = Assert.IsType<CodexThreadModelState>(accumulator.ApplyChange(json.RootElement).State);
            Assert.Equal(expected[i], CodexServiceTier.IsFast(state.ServiceTier));
            Assert.Equal("model-a", state.ModelId);
            Assert.Equal("high", state.Effort);
        }
    }

    [Fact]
    public async Task SelectionFollowsAThenBThenAThenDraftDespiteSixBackgroundSubscriptions()
    {
        await using var service = new CodexModelToggleService();
        var followed = (Dictionary<string, string>)typeof(CodexModelToggleService)
            .GetField("_visibleThreadByClient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(service)!;
        var titles = new Dictionary<string, string> { [A] = "Alpha", [B] = "Beta" };
        foreach (var selected in new[] { "Alpha", "Beta", "Alpha", "" })
        {
            service.ObserveSelectedThread(CodexSelectedThreadReader.Resolve([selected], titles));
            for (var index = 0; index < 6; index++) followed["same-window"] = $"background-{index}";
            var expected = selected == "Alpha" ? A : selected == "Beta" ? B : null;
            Assert.Equal(expected, service.CurrentVisibleThreadId);
            Assert.Equal(expected, service.CurrentForegroundVisibleThreadId(nint.Zero));
        }
    }

    [Fact]
    public void DuplicateTitlesAndMissingSelectionCannotTargetAConversation()
    {
        var titles = new Dictionary<string, string> { [A] = "Duplicate", [B] = "Duplicate" };
        Assert.Null(CodexSelectedThreadReader.Resolve(["Duplicate"], titles));
        Assert.Null(CodexSelectedThreadReader.Resolve([], titles));
        Assert.Null(CodexSelectedThreadReader.Resolve(["Home"], titles));
    }

    [Fact]
    public async Task ModelChangesCanRepeatUsingFreshSnapshotsAndRawConditions()
    {
        await using var owner = new FakeOwner();
        await using var controller = new KeypadController(new(owner.Name), Catalog);
        for (var index = 0; index < 6; index++)
        {
            var target = index % 2 == 0 ? "model-b" : "model-a";
            var result = await controller.ExecuteAsync("set_keypad_model", new()
            {
                ["thread_id"] = A, ["model"] = target,
            }, canApply: () => Task.FromResult(true));
            Assert.True(result["applied"]!.GetValue<bool>());
            var state = await controller.ExecuteAsync("get_keypad_state", new() { ["thread_id"] = A });
            Assert.Equal(target, state["model"]!.GetValue<string>());
        }
        Assert.Equal(6, owner.Updates);
    }

    [Fact]
    public async Task SelectionChangedDuringCatalogReadPreventsMutation()
    {
        await using var owner = new FakeOwner();
        var selected = A;
        await using var controller = new KeypadController(new(owner.Name), (method, args, token) =>
        {
            selected = B;
            return Catalog(method, args, token);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ExecuteAsync(
            "set_keypad_model", new() { ["thread_id"] = A, ["model"] = "model-b" },
            canApply: () => Task.FromResult(selected == A)));
        Assert.Equal(0, owner.Updates);
    }

    [Fact]
    public async Task StaleExpectedSettingsRejectWithoutMutation()
    {
        await using var owner = new FakeOwner();
        await using var controller = new KeypadController(new(owner.Name), Catalog);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ExecuteAsync(
            "set_keypad_model", new()
            {
                ["thread_id"] = A, ["model"] = "model-b", ["expected_model"] = "outdated",
            }));
        Assert.Equal(0, owner.Updates);
    }

    [Theory]
    [InlineData("steeringUserMessage", "pending", false)]
    [InlineData("steeringUserMessage", "rejected", false)]
    [InlineData("steeringUserMessage", "accepted", true)]
    [InlineData("userMessage", null, true)]
    public void OnlyAcceptedAnswersClearYellowQuestion(string type, string? status, bool clears)
    {
        var path = Path.GetTempFileName();
        try
        {
            SeedQuestion(path);
            var reader = new CodexRolloutStatusReader();
            Assert.True(reader.ReadSnapshot(path).HasPendingQuestion);
            var stream = new CodexQuestionAnswerStream();
            using var state = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                turns = new[] { new { items = new[] { Answer(type, status) } } },
            }));
            stream.ReadState(state.RootElement);
            Assert.Equal(!clears, reader.ReadSnapshot(path, stream.DrainAcceptedReplies()).HasPendingQuestion);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AcceptedAnswerPatchClearsQuestionButPendingPatchKeepsIt()
    {
        var path = Path.GetTempFileName();
        try
        {
            SeedQuestion(path);
            var reader = new CodexRolloutStatusReader();
            var accumulator = new CodexThreadModelStateAccumulator(A, "owner");
            using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                type = "snapshot", revision = 1, conversationState = new
                {
                    turns = new[] { new { items = new[] { Answer("steeringUserMessage", "pending") } } },
                },
            }));
            accumulator.ApplyChange(snapshot.RootElement);
            Assert.True(reader.ReadSnapshot(path, accumulator.QuestionAnswers.DrainAcceptedReplies()).HasPendingQuestion);
            using var patch = JsonDocument.Parse("""
                {"type":"patches","baseRevision":1,"revision":2,"patches":[
                  {"op":"replace","path":["turns",0,"items",0,"status"],"value":"accepted"}
                ]}
                """);
            Assert.True(accumulator.ApplyChange(patch.RootElement).Applied);
            Assert.False(reader.ReadSnapshot(path, accumulator.QuestionAnswers.DrainAcceptedReplies()).HasPendingQuestion);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SkippingQuestionClearsYellowWithoutEndingTheTurn()
    {
        var path = Path.GetTempFileName();
        try
        {
            SeedQuestion(path);
            var reader = new CodexRolloutStatusReader();
            Assert.True(reader.ReadSnapshot(path).HasPendingQuestion);
            reader.ObserveSkippedQuestion(path, Assert.Single(reader.GetPendingQuestions(path)));
            var state = reader.ReadSnapshot(path);
            Assert.False(state.HasPendingQuestion);
            Assert.Equal(CodexController.Models.ThreadStatus.Thinking, state.Status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task QuestionAnswersAreObservedForBothThreadsIndependentOfSelection()
    {
        await using var owner = new FakeOwner();
        owner.AnswerInSnapshots = true;
        using var observer = new SoftwareQuestionObserver(new(owner.Name));
        var received = new HashSet<string>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        observer.AnswersAccepted += (id, replies) =>
        {
            Assert.Single(replies);
            lock (received) { received.Add(id); if (received.Count == 2) completed.TrySetResult(); }
        };
        await observer.RefreshAsync([A, B]);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains(A, received);
        Assert.Contains(B, received);
    }

    private static void SeedQuestion(string path) => File.WriteAllText(path,
        """{"type":"event_msg","payload":{"type":"task_started","turn_id":"turn"}}""" + "\n" +
        """{"type":"event_msg","payload":{"type":"item_completed","turn_id":"turn","item":{"type":"AgentMessage","id":"question","delivery":"async","questions":[{"title":"Choose"}]}}}""" + "\n");

    private static object Answer(string type, string? status)
    {
        var reply = "<send_user_message_question_reply>" + JsonSerializer.Serialize(new[] { new
        {
            questionItemId = JsonSerializer.Serialize(new object[] { "request_user_input_async", "question", 0 }),
            question = "Choose", answer = "Yes",
        } }) + "</send_user_message_question_reply>";
        var content = new[] { new { type = "text", text = reply } };
        return new { type, id = "reply", status, input = content, content };
    }

    private static Task<JsonNode> Catalog(string method, object args, CancellationToken token) =>
        method == "collaborationMode/list"
        ? Task.FromResult<JsonNode>(JsonSerializer.SerializeToNode(new { data = new[] { new { mode = "plan" }, new { mode = "default" } } })!)
        : Task.FromResult<JsonNode>(JsonSerializer.SerializeToNode(new
        {
            data = new[] { "model-a", "model-b" }.Select(model => new
            {
                model, defaultReasoningEffort = "high",
                supportedReasoningEfforts = new[] { new { reasoningEffort = "high" } },
                serviceTiers = new[] { new { id = "priority", name = "Fast" } },
                additionalSpeedTiers = new[] { "fast" },
            }),
        })!);

    private sealed class FakeOwner : IAsyncDisposable
    {
        internal string Name { get; } = "micro-test-" + Guid.NewGuid().ToString("N");
        private readonly NamedPipeServerStream _pipe;
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(15));
        private readonly Task _loop;
        private string _model = "model-a";
        private string? _tier;
        private JsonObject _collaboration = new()
        {
            ["mode"] = "default",
            ["settings"] = new JsonObject { ["model"] = "previous-model", ["reasoning_effort"] = "low", ["developer_instructions"] = "Previous mode instruction fixture" },
        };
        internal string Model => _model;
        internal List<string?> Tiers { get; } = [];
        internal bool RejectSettings;
        internal int Updates;
        internal bool AnswerInSnapshots;

        internal FakeOwner()
        {
            _pipe = new(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            _loop = RunAsync();
        }

        private async Task RunAsync()
        {
            try
            {
                await _pipe.WaitForConnectionAsync(_lifetime.Token);
                while (!_lifetime.IsCancellationRequested)
                {
                    var header = new byte[4];
                    await _pipe.ReadExactlyAsync(header, _lifetime.Token);
                    var bytes = new byte[BinaryPrimitives.ReadInt32LittleEndian(header)];
                    await _pipe.ReadExactlyAsync(bytes, _lifetime.Token);
                    var message = JsonNode.Parse(bytes)!;
                    var method = message["method"]!.GetValue<string>();
                    if (message["type"]!.GetValue<string>() == "broadcast")
                    {
                        if (message["params"]?["following"]?.GetValue<bool>() == true)
                            await SendAsync(new
                            {
                                type = "broadcast", method = "thread-stream-state-changed", version = 11, sourceClientId = "owner",
                                @params = new
                                {
                                    hostId = "local", conversationId = message["params"]!["conversationId"]!.GetValue<string>(),
                                    change = new { type = "snapshot", revision = 1, conversationState = new
                                    {
                                        latestModel = "raw-model", latestReasoningEffort = "medium",
                                        latestThreadSettings = new { model = _model, effort = "high", serviceTier = _tier },
                                        latestCollaborationMode = _collaboration,
                                        turns = new[] { new { items = AnswerInSnapshots ? new[] { Answer("steeringUserMessage", "accepted") } : Array.Empty<object>() } },
                                    } },
                                },
                            });
                        continue;
                    }
                    object result = new { clientId = "test-client" };
                    if (method == "thread-follower-update-thread-settings")
                    {
                        Assert.Equal("raw-model", message["params"]!["condition"]!["ifModelEquals"]!.GetValue<string>());
                        Assert.Equal("medium", message["params"]!["condition"]!["ifEffortEquals"]!.GetValue<string>());
                        var settings = message["params"]!["threadSettings"]!.AsObject();
                        if (!RejectSettings)
                        {
                            if (settings.ContainsKey("model")) _model = settings["model"]!.GetValue<string>();
                            if (settings["collaborationMode"] is JsonObject collaboration) _collaboration = (JsonObject)collaboration.DeepClone();
                            if (settings.ContainsKey("serviceTier"))
                            {
                                var requested = settings["serviceTier"]?.GetValue<string>();
                                // Match the real desktop boundary, including its legacy alias normalization.
                                _tier = requested == "fast" ? "priority" : requested;
                                Tiers.Add(_tier);
                            }
                            Interlocked.Increment(ref Updates);
                        }
                        result = new { applied = !RejectSettings };
                    }
                    await SendAsync(new
                    {
                        type = "response", requestId = message["requestId"]!.GetValue<string>(), method,
                        resultType = "success", handledByClientId = "owner", result,
                    });
                }
            }
            catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException) { }
        }

        private async Task SendAsync(object value)
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(value);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
            await _pipe.WriteAsync(header, _lifetime.Token);
            await _pipe.WriteAsync(data, _lifetime.Token);
            await _pipe.FlushAsync(_lifetime.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            _pipe.Dispose();
            await _loop;
            _lifetime.Dispose();
        }
    }
}
