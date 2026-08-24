using System.Net;
using System.Text;
using System.Text.Json;
using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Agents.DeepSeek;
using CodexController.Controllers;
using CodexController.Models;
using CodexController.Services;

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

    [Theory]
    [InlineData("thread.create", "session/new", false)]
    [InlineData("thread.fork", "session/fork", false)]
    [InlineData("composer.submit", "composer/submit", false)]
    [InlineData("turn.stop", "turn/cancel", true)]
    [InlineData("approval.accept", "interaction/approve", true)]
    [InlineData("approval.decline", "interaction/reject", false)]
    [InlineData("sidebar.toggle", "layout/toggle-sidebar", false)]
    [InlineData("navigation.back", "layout/close-details", false)]
    [InlineData("navigation.forward", "layout/open-details", false)]
    public async Task DirectExecutorMapsControllerActionToHarnessAction(
        string controllerActionId,
        string expectedHarnessActionId,
        bool highRisk)
    {
        string? actionRequestJson = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            var json = await request.Content!.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            if (document.RootElement
                    .GetProperty("action")
                    .GetString() == "action/execute")
            {
                actionRequestJson = json;
            }

            return Json(
                HttpStatusCode.OK,
                "{\"success\":true,\"message\":\"sent\",\"status\":\"completed\"}");
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var executor = new DeepSeekHarnessActionExecutor(target);

        var result = await executor.ExecuteAsync(CreateRequest(
            ActionId.Parse(controllerActionId),
            highRisk
                ? ActionSafetyLevel.HighRisk
                : ActionSafetyLevel.Routine));

        Assert.Equal(ActionOutcome.Succeeded, result.Outcome);
        using var request = JsonDocument.Parse(actionRequestJson!);
        Assert.Equal(
            "action/execute",
            request.RootElement.GetProperty("action").GetString());
        Assert.Equal(
            expectedHarnessActionId,
            request.RootElement.GetProperty("actionId").GetString());
        Assert.Equal(
            "agent-controller",
            request.RootElement.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData(false, 1, "composer/select-previous")]
    [InlineData(true, -1, "composer/select-previous")]
    [InlineData(false, -1, "composer/select-next")]
    [InlineData(true, 1, "composer/select-next")]
    public void PhysicalRightStickDirectionsPreservePreviousNextContract(
        bool horizontal,
        int physicalDirection,
        string expectedHarnessActionId)
    {
        var actions = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync());
            var action = document.RootElement
                .GetProperty("action")
                .GetString();
            if (action == "action/execute")
            {
                actions.Add(document.RootElement
                    .GetProperty("actionId")
                    .GetString()!);
            }

            return action == "state/read"
                ? Json(HttpStatusCode.OK, """
                    {
                      "success": true,
                      "message": "ready",
                      "status": "completed",
                      "state": {
                        "navigationDepth": 1,
                        "components": {
                          "currentModel": "DeepSeek-V4-Flash"
                        }
                      }
                    }
                    """)
                : Json(
                    HttpStatusCode.OK,
                    "{\"success\":true,\"message\":\"moved\",\"status\":\"completed\"}");
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var encoderSteps = horizontal
            ? VirtualDialInputPolicy.ResolveHorizontalEncoderSteps(
                physicalDirection)
            : VirtualDialInputPolicy.ResolveVerticalEncoderSteps(
                physicalDirection);

        var result = target.Composer.DialStep(encoderSteps, new());
        var navigation = (horizontal, physicalDirection) switch
        {
            (true, < 0) => ComposerDialNavigation.Left,
            (true, _) => ComposerDialNavigation.Right,
            (false, > 0) => ComposerDialNavigation.Up,
            _ => ComposerDialNavigation.Down,
        };
        var navigationResult = target.Composer.DialNavigate(
            navigation,
            new());

        Assert.True(result.Succeeded);
        Assert.True(navigationResult.Succeeded);
        Assert.Equal(
            [expectedHarnessActionId, expectedHarnessActionId],
            actions);
    }

    [Theory]
    [InlineData("press", "composer/activate-selection")]
    [InlineData("cancel", "composer/back")]
    [InlineData("open-picker", "composer/activate-selection")]
    [InlineData("reasoning-up", "reasoning/increase")]
    [InlineData("reasoning-down", "reasoning/decrease")]
    [InlineData("fast", "model/toggle-quick")]
    public async Task ComposerAdapterMapsControllerOperationsToHarnessActions(
        string operation,
        string expectedHarnessActionId)
    {
        var actions = new List<string>();
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            using var document = JsonDocument.Parse(
                await request.Content!.ReadAsStringAsync());
            var action = document.RootElement
                .GetProperty("action")
                .GetString();
            if (action == "action/execute")
            {
                actions.Add(document.RootElement
                    .GetProperty("actionId")
                    .GetString()!);
            }

            return action == "state/read"
                ? Json(HttpStatusCode.OK, """
                    {
                      "success": true,
                      "message": "ready",
                      "status": "completed",
                      "state": {
                        "navigationDepth": 1
                      }
                    }
                    """)
                : Json(
                    HttpStatusCode.OK,
                    "{\"success\":true,\"message\":\"done\",\"status\":\"completed\"}");
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var settings = new AppSettings();

        var succeeded = operation switch
        {
            "press" => target.Composer.DialPress(settings).Succeeded,
            "cancel" => target.Composer.Cancel(settings).Succeeded,
            "open-picker" => (await target.Composer.OpenPickerAsync(
                ComposerPickerView.Simple,
                settings,
                CancellationToken.None)).Succeeded,
            "reasoning-up" =>
                (await target.Composer.StepSimplePowerAsync(
                    1,
                    allowShortcutFastPath: false,
                    settings,
                    CancellationToken.None)).Succeeded,
            "reasoning-down" =>
                (await target.Composer.StepSimplePowerAsync(
                    -1,
                    allowShortcutFastPath: false,
                    settings,
                    CancellationToken.None)).Succeeded,
            "fast" => (await target.Composer.SetSimpleSpeedAsync(
                true,
                allowShortcutFastPath: false,
                settings,
                CancellationToken.None)).Succeeded,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        Assert.True(succeeded);
        Assert.Equal([expectedHarnessActionId], actions);
    }

    [Fact]
    public async Task DirectExecutorActivatesSelectedHarnessSession()
    {
        string? requestJson = null;
        using var httpClient = new HttpClient(new StubHandler(async request =>
        {
            requestJson = await request.Content!.ReadAsStringAsync();
            return Json(
                HttpStatusCode.OK,
                "{\"success\":true,\"message\":\"opened\",\"status\":\"completed\"}");
        }));
        var target = new DeepSeekAgentTarget(new DeepSeekHarnessClient(
            DeepSeekHarnessClient.DefaultEndpoint,
            httpClient));
        var executor = new DeepSeekHarnessActionExecutor(target);

        var result = await executor.ExecuteAsync(CreateRequest(
            OpenThreadActionContract.Id,
            parameters: new Dictionary<string, string>
            {
                [OpenThreadActionContract.ThreadIdParameter] = "session-7",
            }));

        Assert.Equal(ActionOutcome.Succeeded, result.Outcome);
        using var request = JsonDocument.Parse(requestJson!);
        Assert.Equal(
            "session/activate",
            request.RootElement.GetProperty("action").GetString());
        Assert.Equal(
            "session-7",
            request.RootElement.GetProperty("sessionId").GetString());
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
        ActionSafetyLevel safetyLevel = ActionSafetyLevel.Routine,
        IReadOnlyDictionary<string, string>? parameters = null)
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
            DateTimeOffset.UtcNow,
            parameters);
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
