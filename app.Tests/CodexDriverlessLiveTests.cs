using AgentController.Adapters.Codex.Software;
using AgentController.Adapters.Codex.Windows;
using AgentController.Application.Actions;
using AgentController.Domain.Actions;
using AgentController.Domain.Inputs;
using CodexController.Native;
using CodexMicro.Codex;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexController.Services;

namespace CodexController.Tests;

[CollectionDefinition("Codex live", DisableParallelization = true)]
public sealed class CodexLiveCollection;

public sealed class CodexLiveFactAttribute : FactAttribute
{
    public CodexLiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AGENTCONTROLLER_LIVE_THREAD") is null)
            Skip = "Run scripts/test-micro-live.ps1 -Driverless with two idle chats and a restore chat.";
    }
}

[Collection("Codex live")]
[Trait("Category", "CodexLive")]
public sealed class CodexDriverlessLiveTests
{
    [CodexLiveFact]
    public async Task AgentControllerExecutorsControlRealCodexUsingPinnedComponent()
    {
        var threadId = Thread("THREAD");
        var secondId = Thread("SECOND_THREAD");
        var restoreId = Thread("RESTORE_THREAD");
        Assert.NotEqual(threadId, secondId);
        var reportPath = Environment.GetEnvironmentVariable("AGENTCONTROLLER_LIVE_REPORT")
            ?? Path.Combine(AppContext.BaseDirectory, "driverless.json");
        var cases = new List<object>();
        var errors = new List<string>();
        var cleanup = new List<string>();
        var observations = new List<object>();
        using var diagnostics = new StringWriter();
        using var trace = new TextWriterTraceListener(diagnostics);
        Trace.Listeners.Add(trace);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await using var reader = new KeypadController();
        await using var client = new CodexSoftwareClient();
        using var ui = new CodexUiController();
        var navigation = new CodexThreadNavigationExecutor(client);
        var executor = new CodexUiActionExecutor(ui);
        string? originalSidebar = null;
        JsonNode? baseline = null;
        JsonNode? secondBaseline = null;
        try
        {
            await Case("native sidebar sections and project traversal", () =>
            {
                var workspace = new CodexDataService();
                var snapshot = workspace.LoadSnapshot();
                var roots = workspace.BuildUnifiedEntries(snapshot);
                var tasks = SidebarTaskOrder.Flatten(roots, path =>
                    workspace.BuildEntries(snapshot, CodexController.Models.SidebarScope.ProjectTasks, path));
                Assert.NotEmpty(roots);
                Assert.Equal(tasks.Count, tasks.Select(entry => entry.ThreadId).Distinct().Count());
                foreach (var section in snapshot.SidebarLayout?.Sections ?? [])
                {
                    var actual = roots.Where(entry => entry.SectionId == section.Id).Select(entry => entry.Id).ToArray();
                    var expected = section.ItemIds.Where(id => roots.Any(entry => entry.Id == id)).ToArray();
                    Assert.Equal(expected, actual);
                }
                observations.Add(new { rootSections = roots.GroupBy(entry => entry.SectionKey)
                    .Select(group => new { id = group.Key, name = group.First().SectionName, count = group.Count() }),
                    taskCount = tasks.Count });
                return Task.CompletedTask;
            });
            baseline = await Load(threadId);
            secondBaseline = await Load(secondId);
            RequireIdle(baseline);
            RequireIdle(secondBaseline);
            var title = baseline["title"]!.GetValue<string>();
            var secondTitle = secondBaseline["title"]!.GetValue<string>();
            Assert.NotEqual(title, secondTitle);
            await Case("pinned component navigation A -> B -> A", async () =>
            {
                await Open(threadId, title);
                await Open(secondId, secondTitle);
                await Open(threadId, title);
            });
            var before = await Observe();
            originalSidebar = before.Sidebar?.Name;
            Assert.NotNull(originalSidebar);
            Assert.NotNull(before.Composer);

            await Case("sidebar round trip through AgentController executor", async () =>
            {
                await Execute(SidebarActionContract.ToggleId);
                Assert.NotEqual(originalSidebar, (await Observe()).Sidebar?.Name);
                await Execute(SidebarActionContract.ToggleId);
                Assert.Equal(originalSidebar, (await Observe()).Sidebar?.Name);
            });
            await Case("history back and forward with native selected-chat readback", async () =>
            {
                await Open(secondId, secondTitle);
                await Execute(NavigationActionContract.BackId);
                await Until(async () => await SelectedTitle() == title);
                await Execute(NavigationActionContract.ForwardId);
                await Until(async () => await SelectedTitle() == secondTitle);
                await Open(threadId, title);
            });
            await Case("stale target guard prevents a real sidebar mutation", async () =>
            {
                var result = await ui.ExecuteAsync(new(CodexUiOperation.ToggleSidebar), _ => Task.FromResult(false), lifetime.Token);
                Assert.Equal(CodexUiDisposition.NotSent, result.Disposition);
                Assert.Equal(originalSidebar, (await Observe()).Sidebar?.Name);
            });
        }
        catch (Exception error) { errors.Add(error.ToString()); }
        finally
        {
            try
            {
                if (originalSidebar is not null)
                {
                    Assert.True(CodexWindowActivator.TryActivate());
                    if ((await Observe()).Sidebar?.Name != originalSidebar)
                        await Execute(SidebarActionContract.ToggleId);
                    Assert.Equal(originalSidebar, (await Observe()).Sidebar?.Name);
                    cleanup.Add("Sidebar restored");
                }
                if (baseline is not null) await Unchanged(threadId, baseline);
                if (secondBaseline is not null) await Unchanged(secondId, secondBaseline);
                cleanup.Add("Both chat settings unchanged");
            }
            catch (Exception error) { errors.Add("Cleanup: " + error); }
            try
            {
                var restore = await State(restoreId);
                await Open(restoreId, restore["title"]!.GetValue<string>());
                cleanup.Add("Original chat observed");
            }
            catch (Exception error) { errors.Add("Navigation cleanup: " + error); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
            {
                timeUtc = DateTimeOffset.UtcNow, threadId, secondId,
                component = typeof(CodexSoftwareClient).Assembly.GetCustomAttributes(false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,
                cases, errors, cleanup, observations, diagnostics = diagnostics.ToString(),
                scope = "AgentController executors + pinned CodexMicro.Codex + real Codex IPC/UIA; no message submitted, physical controller not covered"
            }, new JsonSerializerOptions { WriteIndented = true }));
            Trace.Listeners.Remove(trace);
        }
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        Task<JsonNode> State(string id) => reader.ExecuteAsync("get_keypad_state", new() { ["thread_id"] = id }, lifetime.Token);
        async Task<JsonNode> Load(string id)
        {
            var result = await navigation.ExecuteAsync(Request(OpenThreadActionContract.Id,
                new Dictionary<string, string> { [OpenThreadActionContract.ThreadIdParameter] = id }), lifetime.Token);
            Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
            Assert.True(CodexWindowActivator.TryActivate());
            JsonNode? state = null;
            await Until(async () =>
            {
                try { state = await State(id); return true; }
                catch (IOException error) when (error.Message.Contains("no-client-found", StringComparison.Ordinal)) { return false; }
            });
            return state!;
        }
        async Task Open(string id, string title)
        {
            var result = await navigation.ExecuteAsync(Request(OpenThreadActionContract.Id,
                new Dictionary<string, string> { [OpenThreadActionContract.ThreadIdParameter] = id }), lifetime.Token);
            Assert.Equal(ActionOutcome.AcceptedUnverified, result.Outcome);
            Assert.True(CodexWindowActivator.TryActivate());
            await Until(async () => await SelectedTitle() == title && (await Observe()).Composer is not null);
            Assert.Equal(title, await Task.Run(new CodexSidebarService().TryGetCurrentThreadTitle));
        }
        async Task Execute(ActionId id)
        {
            var before = await Observe();
            var result = await executor.ExecuteAsync(Request(id), lifetime.Token);
            var after = await Observe();
            observations.Add(new { action = id.ToString(), outcome = result.Outcome.ToString(), result.ErrorCode,
                before = Snapshot(before), after = Snapshot(after) });
            Assert.True(result.Outcome == ActionOutcome.Succeeded, $"{id}: {result.Outcome}/{result.ErrorCode}");
            Assert.Contains(result.Evidence, evidence => evidence.Kind == ActionEvidenceKind.UiObservation);
        }
        async Task Unchanged(string id, JsonNode initial)
        {
            var current = await State(id);
            foreach (var field in new[] { "model", "effort", "serviceTier", "collaborationMode", "activeTurnId" })
                Assert.True(JsonNode.DeepEquals(initial[field], current[field]), field + " changed");
        }
        async Task Case(string name, Func<Task> body)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                if (baseline is not null) RequireIdle(await State(threadId));
                await body();
                cases.Add(new { name, passed = true, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
            }
            catch (Exception error) { cases.Add(new { name, passed = false, error = error.Message }); throw; }
        }
    }

    private static object Snapshot(UiState state) => new { state.Route, scroller = state.Scroller is null ? null :
        new { state.Scroller.Id, state.Scroller.ClassName, state.Scroller.ScrollPercent },
        markers = state.ScrollMarkers?.Select(node => new { node.Id, node.Bounds.Top, node.Bounds.Bottom }) };

    private static string Thread(string suffix) => Guid.Parse(Environment.GetEnvironmentVariable("AGENTCONTROLLER_LIVE_" + suffix)
        ?? throw new InvalidOperationException("Missing live test thread: " + suffix)).ToString();
    private static Task<string?> SelectedTitle() => Task.Run(() => WindowsSelection.ReadSelectedTitle(NativeUi.GetForegroundWindow()));
    private static async Task<UiState> Observe() => await Task.Run(() =>
    {
        using var session = new WindowsCodexUiDesktop().Capture() ?? throw new InvalidOperationException("Codex is not foreground");
        return session.Read();
    }).WaitAsync(TimeSpan.FromSeconds(15));
    private static void RequireIdle(JsonNode state) => Assert.True(state["activeTurnId"] is null && state["approvals"] is not JsonArray { Count: > 0 },
        "Only idle chats without approvals can be used: " + state["title"]);
    private static ActionRequest Request(ActionId id, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var requestId = Guid.NewGuid();
        return new(requestId, id, new("live-e2e", ControlId.Parse("controller.radial.command")),
            InputContext.Parse("radial.command"), requestId.ToString("N"), ActionSafetyLevel.Routine, DateTimeOffset.UtcNow, parameters);
    }
    private static async Task Until(Func<Task<bool>> predicate)
    {
        var started = Stopwatch.GetTimestamp();
        do { if (await predicate()) return; await Task.Delay(100); }
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(20));
        throw new TimeoutException("Native Codex readback was not observed within 20 seconds");
    }
}
