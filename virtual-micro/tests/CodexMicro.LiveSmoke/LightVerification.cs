using CodexMicro.Codex;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AgentController.MicroSurface.Wpf.SoftwareControl;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Services;

internal static class LightVerification
{
    internal static async Task<int> RunAsync(MicroSurfaceWindow window, string threadId, string restoreId, string reportPath)
    {
        var cases = new List<object>();
        var errors = new List<string>();
        var restored = false;
        var touched = false;
        await using var controller = new KeypadController();
        var unread = new CodexUnreadStateReader();
        try
        {
            var state = await controller.ExecuteAsync("get_keypad_state", new() { ["thread_id"] = threadId });
            if (threadId == restoreId || state["activeTurnId"] is not null ||
                state["approvals"]?.AsArray().Count > 0 || await IsUnread())
                throw new Exception("Use a different idle, already-read chat without approvals");
            var title = state["title"]!.GetValue<string>();
            Click(window.MonitorPageButton);
            Button? Key() => window.MonitorGrid.Children.OfType<Button>()
                .SingleOrDefault(key => AutomationProperties.GetName(key) == title);
            await Until(() => Task.FromResult(Key() is { IsEnabled: true }));
            await Task.Delay(600);
            for (var round = 1; round <= 3; round++)
            {
                await controller.ExecuteAsync("open_keypad_thread", new() { ["thread_id"] = restoreId });
                await Until(() => Task.FromResult(Key() is { IsEnabled: true } key &&
                    Color(key.BorderBrush) != "#FF00FF4C"));
                await Task.Delay(700);
                var key = Key()!;
                var start = Stopwatch.GetTimestamp();
                touched = true;
                key.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                    { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent });
                if (key.Opacity != .65) throw new Exception("Missing immediate unread-pending feedback");
                await Until(() => Task.FromResult(Color(Key()!.BorderBrush) == "#FF00FF4C"));
                var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (!await IsUnread()) throw new Exception("Green appeared without native unread confirmation");
                var stable = Stopwatch.GetTimestamp();
                while (Stopwatch.GetElapsedTime(stable) < TimeSpan.FromSeconds(3))
                {
                    if (Color(Key()!.BorderBrush) != "#FF00FF4C")
                        throw new Exception("Confirmed green flickered during background refresh");
                    await Task.Delay(40);
                }
                Click(Key()!);
                await Until(async () => !await IsUnread() && Color(Key()!.BorderBrush) != "#FF00FF4C");
                cases.Add(new { name = $"Unread/read round trip {round}; green remains stable", passed = true, elapsedMs });
            }
            Click(window.ActionKey12);
            await Until(() => Task.FromResult(Color(window.ActivityLed.Fill) == "#FF74D9A0"));
            await Until(() => Task.FromResult(new[] { window.RuntimeLed, window.DriverLed, window.ActivityLed }
                .All(led => Color(led.Fill) == "#FF9EBDFF")));
            cases.Add(new { name = "Successful action returns to three blue lights", passed = true });
        }
        catch (Exception error) { errors.Add(error.ToString()); }
        finally
        {
            try
            {
                if (touched && await IsUnread())
                {
                    await controller.ExecuteAsync("open_keypad_thread", new() { ["thread_id"] = threadId });
                    await Until(async () => !await IsUnread());
                }
                await controller.ExecuteAsync("open_keypad_thread", new() { ["thread_id"] = restoreId });
                restored = !touched || !await IsUnread();
            }
            catch (Exception error) { errors.Add("Cleanup: " + error.Message); }
            await window.CloseForApplicationExitAsync();
        }
        var report = JsonSerializer.Serialize(new { cases, errors, restored,
            scope = "Real WPF right-click/click events, native Codex unread readback; no OS pointer hit testing" },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, report);
        Console.WriteLine(report);
        return errors.Count == 0 && restored ? 0 : 1;

        async Task<bool> IsUnread() => (await unread.ReadAsync() ?? throw new Exception("Unread state unavailable"))
            .ThreadIds.Contains(threadId);
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static string? Color(Brush brush) => (brush as SolidColorBrush)?.Color.ToString();
    private static async Task Until(Func<Task<bool>> predicate)
    {
        var start = Stopwatch.GetTimestamp();
        do { if (await predicate()) return; await Task.Delay(60); }
        while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(15));
        throw new TimeoutException("Expected light/native state was not observed within 15 seconds");
    }
}
