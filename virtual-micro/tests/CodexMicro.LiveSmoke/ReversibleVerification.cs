using CodexMicro.Codex;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.IO.Path;
using AgentController.MicroSurface.Wpf.SoftwareControl;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Controls;
using CodexMicro.Desktop.Services;

// Real WPF Click handlers -> real desktop IPC -> independent readback and rendered properties.
// No fake owner, state injection, messages, approvals, stop, fork, or configuration writes.
internal static class ReversibleVerification
{
    internal static async Task<int> RunAsync(MicroSurfaceWindow window, string threadId,
        string secondThreadId, string restoreThreadId, string reportPath)
    {
        var cases = new List<object>();
        var errors = new List<string>();
        var cleanup = new List<string>();
        await using var reader = new KeypadController();
        JsonNode? baseline = null;
        var fastTouched = false;
        var planTouched = false;
        try
        {
            baseline = await State(threadId);
            var second = await State(secondThreadId);
            RequireIdle(baseline);
            RequireIdle(second);
            var title = baseline["title"]?.GetValue<string>() ?? throw new Exception("Missing first title");
            var secondTitle = second["title"]?.GetValue<string>() ?? throw new Exception("Missing second title");
            if (threadId == secondThreadId || title == secondTitle) throw new Exception("Two distinct, uniquely titled idle chats are required");
            await Until(() => Task.FromResult(FindAgent(title) is not null && FindAgent(secondTitle) is not null));
            await Case("agent A -> B -> A", async () =>
            {
                foreach (var target in new[] { title, secondTitle, title })
                {
                    var button = FindAgent(target)!;
                    Click(button);
                    await Until(() => Task.FromResult(AutomationProperties.GetHelpText(button).Contains("当前会话", StringComparison.Ordinal)));
                }
            });

            await Case("Codex key foregrounds main app; success returns to three blue lights", async () =>
            {
                if (!AutomationProperties.GetHelpText(window.ActionKey12).Contains("打开或置前 Codex", StringComparison.Ordinal))
                    throw new Exception("Codex key is not using the activation binding");
                var acknowledgements = 0;
                var changes = DependencyPropertyDescriptor.FromProperty(Shape.FillProperty, typeof(Ellipse));
                EventHandler onChange = (_, _) => { if (ColorOf(window.ActivityLed.Fill) == "#FF74D9A0") acknowledgements++; };
                changes.AddValueChanged(window.ActivityLed, onChange);
                try
                {
                    Click(window.ActionKey12);
                    await Until(() => Task.FromResult(acknowledgements == 1 && CodexWindowActivator.IsForeground()));
                    await Until(() => Task.FromResult(new[] { window.RuntimeLed, window.DriverLed, window.ActivityLed }
                        .All(led => ColorOf(led.Fill) == "#FF9EBDFF")));
                }
                finally { changes.RemoveValueChanged(window.ActivityLed, onChange); }
            });

            var fastButton = Enumerable.Range(6, 4).Select(i => (Button)window.FindName($"ActionKey{i:00}"))
                .Single(button => AutomationProperties.GetHelpText(button).Contains("composer.toggleFastMode", StringComparison.Ordinal));
            var fastIcon = (KeycapIcon)window.FindName("ActionIcon" + fastButton.Name["ActionKey".Length..]);
            var originalFast = IsFast(baseline);
            await Case("Fast round trip: desktop tier, icon, press feedback", async () =>
            {
                foreach (var expected in new[] { !originalFast, originalFast })
                {
                    fastTouched = true;
                    Click(fastButton);
                    if (fastIcon.RenderTransform is not ScaleTransform { HasAnimatedProperties: true })
                        throw new Exception("No Fast press animation");
                    await Until(async () => IsFast(await State(threadId)) == expected &&
                        ColorOf(fastIcon.IconBrush) == (expected ? "#FF14876D" : "#FF171717"));
                    await UnchangedSettings();
                }
            });

            await Case("Six rapid Fast clicks: six acknowledgements, original tier restored", async () =>
            {
                var acknowledgements = 0;
                var changes = DependencyPropertyDescriptor.FromProperty(Shape.FillProperty, typeof(Ellipse));
                EventHandler onChange = (_, _) => { if (ColorOf(window.ActivityLed.Fill) == "#FF74D9A0") acknowledgements++; };
                changes.AddValueChanged(window.ActivityLed, onChange);
                try
                {
                    for (var i = 0; i < 6; i++) Click(fastButton);
                    await Until(() => Task.FromResult(acknowledgements >= 6));
                    if (acknowledgements != 6) throw new Exception($"Expected six acknowledgements, got {acknowledgements}");
                    if (IsFast(await State(threadId)) != originalFast) throw new Exception("Fast state was not restored");
                    await UnchangedSettings();
                }
                finally { changes.RemoveValueChanged(window.ActivityLed, onChange); }
            });

            await Case("Plan round trip preserves model, effort and Fast", async () =>
            {
                var originalMode = Mode(baseline);
                if (originalMode is not ("plan" or "default")) throw new Exception("Unknown collaboration mode");
                if (!AutomationProperties.GetHelpText(window.JoystickUp).Contains("composer.togglePlanMode", StringComparison.Ordinal))
                    throw new Exception("Joystick up is not bound to Plan");
                foreach (var expected in new[] { originalMode == "plan" ? "default" : "plan", originalMode })
                {
                    planTouched = true;
                    Click(window.JoystickUp);
                    await Until(async () => Mode(await State(threadId)) == expected);
                    await UnchangedSettings();
                    if (IsFast(await State(threadId)) != originalFast) throw new Exception("Plan changed Fast state");
                }
            });

            await Case("Control/monitor panels round trip twice", async () =>
            {
                for (var i = 0; i < 2; i++)
                {
                    Click(window.MonitorPageButton);
                    await Until(() => Task.FromResult(window.MonitorGrid.Visibility == Visibility.Visible && window.ControlGrid.Visibility == Visibility.Collapsed && window.MonitorPageButton.IsEnabled));
                    if (window.MonitorGrid.Children.OfType<Button>().Count() != 14) throw new Exception("Monitor lost task slots");
                    Click(window.ControlPageButton);
                    await Until(() => Task.FromResult(window.ControlGrid.Visibility == Visibility.Visible && window.MonitorGrid.Visibility == Visibility.Collapsed && window.ControlPageButton.IsEnabled));
                    if (!window.ModelKnob.IsVisible || !window.ActionKey12.IsVisible) throw new Exception("Shared controls disappeared");
                }
            });
        }
        catch (Exception error) { errors.Add(error.Message); }
        finally
        {
            // Stop the test window's pending dispatch before reading final state and restoring anything.
            await window.CloseForApplicationExitAsync();
            if (baseline is not null && (fastTouched || planTouched))
            {
                try
                {
                    var current = await State(threadId);
                    RequireIdle(current);
                    if (current["model"]?.ToJsonString() != baseline["model"]?.ToJsonString() ||
                        current["effort"]?.ToJsonString() != baseline["effort"]?.ToJsonString())
                        throw new Exception("Concurrent model/effort change; restoration was not applied");
                    if (fastTouched && IsFast(current) != IsFast(baseline))
                        await reader.ExecuteAsync("set_keypad_fast", new() { ["thread_id"] = threadId, ["enabled"] = IsFast(baseline) });
                    if (planTouched && Mode(current) != Mode(baseline))
                        await reader.ExecuteAsync("toggle_keypad_plan", new() { ["thread_id"] = threadId });
                    current = await State(threadId);
                    if (IsFast(current) != IsFast(baseline) || Mode(current) != Mode(baseline)) throw new Exception("Settings restoration was not confirmed");
                    cleanup.Add("Original Fast/Plan/model/effort confirmed");
                }
                catch (Exception error) { errors.Add("Cleanup: " + error.Message); }
            }
            try
            {
                await reader.ExecuteAsync("open_keypad_thread", new() { ["thread_id"] = restoreThreadId });
                cleanup.Add("Requested original chat navigation");
            }
            catch (Exception error) { errors.Add("Navigation cleanup: " + error.Message); }
        }
        var report = new { timeUtc = DateTimeOffset.UtcNow, cases, errors, cleanup,
            scope = "In-process WPF button events with real Codex IPC and readback; OS pointer hit testing is not covered" };
        var path = Path.GetFullPath(reportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")}: {cases.Count} completed cases; {path}");
        return errors.Count == 0 ? 0 : 1;

        Task<JsonNode> State(string id) => reader.ExecuteAsync("get_keypad_state", new() { ["thread_id"] = id });
        Button? FindAgent(string title)
        {
            var matches = Enumerable.Range(0, 6).Select(i => (Button)window.FindName("AgentKey" + i))
                .Where(button => AutomationProperties.GetName(button).EndsWith(" › " + title, StringComparison.Ordinal)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        async Task UnchangedSettings()
        {
            var current = await State(threadId);
            if (current["model"]?.ToJsonString() != baseline!["model"]?.ToJsonString() ||
                current["effort"]?.ToJsonString() != baseline["effort"]?.ToJsonString())
                throw new Exception("Unrelated model or effort changed");
        }
        async Task Case(string name, Func<Task> body)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                RequireIdle(await State(threadId));
                await body();
                cases.Add(new { name, passed = true, elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds });
            }
            catch (Exception error) { cases.Add(new { name, passed = false, error = error.Message }); throw; }
        }
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static string? ColorOf(Brush brush) => (brush as SolidColorBrush)?.Color.ToString();
    private static string? Mode(JsonNode state) => state["collaborationMode"]?["mode"]?.GetValue<string>();
    private static bool IsFast(JsonNode state) => state["serviceTier"]?.GetValue<string>() is "priority" or "fast";
    private static void RequireIdle(JsonNode state)
    {
        if (state["activeTurnId"] is not null || state["approvals"] is JsonArray { Count: > 0 })
            throw new Exception("Only idle chats without approvals can be used");
    }
    private static async Task Until(Func<Task<bool>> predicate)
    {
        var deadline = Stopwatch.GetTimestamp();
        do { if (await predicate()) return; await Task.Delay(60); }
        while (Stopwatch.GetElapsedTime(deadline) < TimeSpan.FromSeconds(15));
        throw new TimeoutException("Expected visible/native state was not observed within 15 seconds");
    }
}
