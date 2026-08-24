using System.Net;
using System.Text;
using System.Text.Json;
using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents.DeepSeek;

namespace CodexController.Tests;

public sealed class DeepSeekSessionSyncTests
{
    [Fact]
    public async Task ClientKeepsTheCurrentOldBlankFirstAndInsideSixSessions()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Task.FromResult(Json(
                """
                {
                  "success": true,
                  "message": "ready",
                  "status": "completed",
                  "state": {
                    "currentSessionId": "current-blank",
                    "sessions": [
                      {"id":"recent-0","displayTitle":"Recent 0","status":"idle","running":false,"updatedAt":100},
                      {"id":"recent-1","displayTitle":"Recent 1","status":"idle","running":false,"updatedAt":99},
                      {"id":"recent-2","displayTitle":"Recent 2","status":"idle","running":false,"updatedAt":98},
                      {"id":"recent-3","displayTitle":"Recent 3","status":"idle","running":false,"updatedAt":97},
                      {"id":"recent-4","displayTitle":"Recent 4","status":"idle","running":false,"updatedAt":96},
                      {"id":"recent-5","displayTitle":"Recent 5","status":"idle","running":false,"updatedAt":95},
                      {"id":"recent-6","displayTitle":"Recent 6","status":"idle","running":false,"updatedAt":94},
                      {"id":"current-blank","displayTitle":"New Session","status":"idle","running":false,"updatedAt":1}
                    ]
                  }
                }
                """))));
        var client = new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient);

        var response = await client.ReadStateAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.State);
        Assert.Equal("current-blank", response.State.CurrentSessionId);
        Assert.Equal(6, response.State.Sessions.Count);
        Assert.Equal(
            [
                "current-blank",
                "recent-0",
                "recent-1",
                "recent-2",
                "recent-3",
                "recent-4",
            ],
            response.State.Sessions.Select(session => session.Id));
    }

    [Theory]
    [InlineData("thread.create", "session/new")]
    [InlineData("thread.fork", "session/fork")]
    public async Task TopologyActionsRefreshTheSelectedSessionCache(
        string controllerActionId,
        string harnessActionId)
    {
        var actions = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync());
            var action = document.RootElement
                .GetProperty("action")
                .GetString()!;
            actions.Add(action);
            if (action == "action/execute")
            {
                Assert.Equal(
                    harnessActionId,
                    document.RootElement
                        .GetProperty("actionId")
                        .GetString());
                return Json(
                    """
                    {"success":true,"message":"done","status":"completed"}
                    """);
            }

            Assert.Equal("state/read", action);
            return Json(
                """
                {
                  "success": true,
                  "message": "ready",
                  "status": "completed",
                  "state": {
                    "currentSessionId": "new-current",
                    "sessions": [{
                      "id": "new-current",
                      "displayTitle": "New Session",
                      "status": "idle",
                      "running": false,
                      "updatedAt": 1
                    }]
                  }
                }
                """);
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var executor = new DeepSeekHarnessActionExecutor(target);

        var result = await executor.ExecuteAsync(CreateRequest(
            ActionId.Parse(controllerActionId)));

        Assert.Equal(ActionOutcome.Succeeded, result.Outcome);
        Assert.Equal(["action/execute", "state/read"], actions);
        Assert.Equal(
            "New Session",
            target.Sidebar.TryGetCurrentThreadTitle());
    }

    [Fact]
    public async Task FailedPostCreateRefreshDoesNotReuseThePreviousSessionId()
    {
        var requestBodies = new List<JsonElement>();
        var readCount = 0;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync());
            requestBodies.Add(document.RootElement.Clone());
            var action = document.RootElement
                .GetProperty("action")
                .GetString();
            if (action == "state/read")
            {
                readCount++;
                return readCount == 1
                    ? Json(
                        """
                        {
                          "success": true,
                          "message": "ready",
                          "status": "completed",
                          "state": {
                            "currentSessionId": "old-session",
                            "sessions": [{
                              "id": "old-session",
                              "displayTitle": "Old session",
                              "status": "idle",
                              "running": false,
                              "updatedAt": 1
                            }]
                          }
                        }
                        """)
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent("unavailable"),
                    };
            }

            return Json(
                """
                {"success":true,"message":"done","status":"completed"}
                """);
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var executor = new DeepSeekHarnessActionExecutor(target);
        _ = target.Workspace.LoadSnapshot();

        var created = await executor.ExecuteAsync(CreateRequest(
            CreateThreadActionContract.Id));
        var submitted = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.SubmitId));

        Assert.Equal(ActionOutcome.Succeeded, created.Outcome);
        Assert.Equal(ActionOutcome.Succeeded, submitted.Outcome);
        var submittedBody = requestBodies.Last(element =>
            element.GetProperty("action").GetString() == "action/execute" &&
            element.GetProperty("actionId").GetString() == "composer/submit");
        Assert.False(submittedBody.TryGetProperty("sessionId", out _));
    }

    [Fact]
    public async Task OlderStateReadCannotOverwriteANewerCompletedRead()
    {
        var first = PendingResponse();
        var second = PendingResponse();
        var readCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Interlocked.Increment(ref readCount) == 1
                ? first.Task
                : second.Task));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));

        var olderRead = target.RefreshStateAsync();
        var newerRead = target.RefreshStateAsync();
        second.SetResult(StateResponse("new-session"));
        _ = await newerRead;
        first.SetResult(StateResponse("old-session"));
        var superseded = await olderRead;

        Assert.Equal("new-session", target.CurrentSessionId);
        Assert.Equal("new-session", superseded.State?.CurrentSessionId);
    }

    [Fact]
    public async Task NewerFailedReadDoesNotSupersedeAnEarlierSuccessfulRead()
    {
        var earlierSuccess = PendingResponse();
        var newerFailure = PendingResponse();
        var readCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Interlocked.Increment(ref readCount) == 1
                ? earlierSuccess.Task
                : newerFailure.Task));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));

        var postNavigationRead = target.RefreshStateAsync();
        var periodicRead = target.RefreshStateAsync();
        newerFailure.SetResult(new HttpResponseMessage(
            HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("unavailable"),
        });
        var failed = await periodicRead;
        earlierSuccess.SetResult(StateResponse("new-session"));
        var succeeded = await postNavigationRead;

        Assert.False(failed.Success);
        Assert.Null(failed.State);
        Assert.True(succeeded.Success);
        Assert.Equal("new-session", succeeded.State?.CurrentSessionId);
        Assert.Equal("new-session", target.CurrentSessionId);
    }

    [Fact]
    public async Task InvalidationBlocksAnEarlierStateReadFromRestoringCurrent()
    {
        var delayed = PendingResponse();
        var readCount = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
            Interlocked.Increment(ref readCount) == 1
                ? Task.FromResult(StateResponse("seed-session"))
                : delayed.Task));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        _ = await target.RefreshStateAsync();

        var staleRead = target.RefreshStateAsync();
        target.InvalidateCurrentSessionCache();
        delayed.SetResult(StateResponse("stale-session"));
        var superseded = await staleRead;

        Assert.Null(target.CurrentSessionId);
        Assert.Null(superseded.State?.CurrentSessionId);
    }

    [Fact]
    public async Task ActivationBlocksAnEarlierStateReadFromRestoringOldCurrent()
    {
        var delayed = PendingResponse();
        var readCount = 0;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync());
            var action = document.RootElement.GetProperty("action").GetString();
            if (action == "session/activate")
            {
                return Json(
                    """
                    {"success":true,"message":"opened","status":"completed"}
                    """);
            }

            Assert.Equal("state/read", action);
            return Interlocked.Increment(ref readCount) == 1
                ? StateResponse("old-session")
                : await delayed.Task;
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        _ = await target.RefreshStateAsync();

        var staleRead = target.RefreshStateAsync();
        var activated = await target.ActivateSessionAsync("new-session");
        delayed.SetResult(StateResponse("old-session"));
        var superseded = await staleRead;

        Assert.True(activated.Success);
        Assert.Equal("new-session", target.CurrentSessionId);
        Assert.Equal("new-session", superseded.State?.CurrentSessionId);
    }

    private static ActionRequest CreateRequest(ActionId actionId)
    {
        var requestId = Guid.NewGuid();
        return new(
            requestId,
            actionId,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.radial.command")),
            InputContext.Parse("radial.command"),
            $"test-{requestId:N}",
            ActionSafetyLevel.Routine,
            DateTimeOffset.UtcNow);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json"),
        };

    private static TaskCompletionSource<HttpResponseMessage> PendingResponse() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static HttpResponseMessage StateResponse(string currentSessionId) =>
        Json(JsonSerializer.Serialize(new
        {
            success = true,
            message = "ready",
            status = "completed",
            state = new
            {
                currentSessionId,
                sessions = new[]
                {
                    new
                    {
                        id = currentSessionId,
                        displayTitle = currentSessionId,
                        status = "idle",
                        running = false,
                        updatedAt = 1,
                    },
                },
            },
        }));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>>
            _handler;

        internal StubHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => _handler(request);
    }
}
