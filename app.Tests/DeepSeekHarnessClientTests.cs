using System.Net;
using System.Text;
using System.Text.Json;
using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents.DeepSeek;

namespace CodexController.Tests;

public sealed class DeepSeekHarnessClientTests
{
    [Theory]
    [InlineData("https://127.0.0.1:3080/__agentcontroller/micro/request")]
    [InlineData("http://example.com/__agentcontroller/micro/request")]
    [InlineData("http://127.0.0.1:3080/not-the-control-path")]
    public void EndpointMustBeExactLoopbackHttp(string endpoint)
    {
        Assert.Throws<ArgumentException>(
            () => new DeepSeekHarnessClient(endpoint));
    }

    [Fact]
    public async Task StateReadUsesGamepadSourceAndParsesHarnessState()
    {
        string? requestJson = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """
                {
                  "success": true,
                  "message": "ready",
                  "status": "completed",
                  "state": {
                    "currentSessionId": "waiting",
                    "navigationDepth": 2,
                    "capabilities": {
                      "actions": ["composer/submit", "turn/cancel"]
                    },
                    "components": {
                      "currentModel": "DeepSeek-V4-Pro"
                    },
                    "sessions": [
                      {
                        "id": "running",
                        "displayTitle": "Running task",
                        "status": "running",
                        "running": true,
                        "updatedAt": 10
                      },
                      {
                        "id": "waiting",
                        "displayTitle": "Waiting task",
                        "status": "waiting",
                        "running": false,
                        "updatedAt": 20
                      }
                    ]
                  }
                }
                """);
        }));
        var client = new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient);

        var response = await client.ReadStateAsync();

        Assert.True(response.Success);
        Assert.NotNull(response.State);
        Assert.Equal("waiting", response.State.CurrentSessionId);
        Assert.Equal(2, response.State.NavigationDepth);
        Assert.Equal("DeepSeek-V4-Pro", response.State.CurrentModel);
        Assert.Contains("composer/submit", response.State.Actions);
        Assert.Equal(
            DeepSeekSessionStatus.WaitingForInput,
            response.State.Sessions[0].Status);
        using var request = JsonDocument.Parse(requestJson!);
        Assert.Equal(
            "agent-controller",
            request.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            "state/read",
            request.RootElement.GetProperty("action").GetString());
    }

    [Fact]
    public async Task LegacySourceIsRetriedOnlyAfterExplicitBadRequest()
    {
        var sources = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync());
            sources.Add(document.RootElement
                .GetProperty("source")
                .GetString()!);
            return sources.Count == 1
                ? Json(HttpStatusCode.BadRequest, "invalid request")
                : Json(
                    HttpStatusCode.OK,
                    "{\"success\":true,\"message\":\"accepted\",\"status\":\"completed\"}");
        }));
        var client = new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient);

        var response = await client.ExecuteActionAsync(
            "composer/submit",
            "session-1");

        Assert.True(response.Success);
        Assert.Equal(["agent-controller", "codex-micro"], sources);
    }

    [Fact]
    public async Task DirectExecutorMapsSubmitToHarnessAction()
    {
        string? requestJson = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return Json(
                HttpStatusCode.OK,
                "{\"success\":true,\"message\":\"sent\",\"status\":\"completed\"}");
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var executor = new DeepSeekHarnessActionExecutor(target);

        var result = await executor.ExecuteAsync(CreateRequest(
            ComposerActionContract.SubmitId));

        Assert.Equal(ActionOutcome.Succeeded, result.Outcome);
        using var request = JsonDocument.Parse(requestJson!);
        Assert.Equal(
            "action/execute",
            request.RootElement.GetProperty("action").GetString());
        Assert.Equal(
            "composer/submit",
            request.RootElement.GetProperty("actionId").GetString());
        Assert.Equal(
            "agent-controller",
            request.RootElement.GetProperty("source").GetString());
    }

    [Fact]
    public async Task DirectExecutorRequiresHighRiskApprovalBeforeNetwork()
    {
        var requests = 0;
        using var httpClient = new HttpClient(new StubHandler(_ =>
        {
            requests++;
            return Task.FromResult(Json(
                HttpStatusCode.OK,
                "{\"success\":true,\"message\":\"approved\",\"status\":\"completed\"}"));
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var executor = new DeepSeekHarnessActionExecutor(target);

        var result = await executor.ExecuteAsync(CreateRequest(
            ApprovalActionContract.AcceptId));

        Assert.Equal(ActionOutcome.Blocked, result.Outcome);
        Assert.Equal(
            "action.high-risk-confirmation-required",
            result.ErrorCode);
        Assert.Equal(0, requests);
    }

    private static ActionRequest CreateRequest(
        ActionId actionId,
        ActionSafetyLevel safetyLevel = ActionSafetyLevel.Routine)
    {
        var requestId = Guid.NewGuid();
        return new ActionRequest(
            requestId,
            actionId,
            new ActionSource(
                "test.controller",
                ControlId.Parse("controller.radial.command")),
            InputContext.Parse("radial.command"),
            $"test-{requestId:N}",
            safetyLevel,
            DateTimeOffset.UtcNow);
    }

    private static HttpResponseMessage Json(
        HttpStatusCode status,
        string body) =>
        new(status)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json"),
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            Task<HttpResponseMessage>> _handler;

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
