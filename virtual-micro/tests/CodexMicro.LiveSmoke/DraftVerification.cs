using CodexMicro.Codex;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Input;
using Path = System.IO.Path;
using AgentController.MicroSurface.Wpf.SoftwareControl;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Services;

internal static class DraftVerification
{
    internal static async Task<int> RunAsync(MicroSurfaceWindow window, MicroProfileSettings profile,
        string restoreThreadId, string reportPath, string? restoreModel = null, string? restoreEffort = null)
    {
        var cases = new List<object>();
        var errors = new List<string>();
        await using var reader = new KeypadController();
        var observer = new CodexDraftComposerModelSelector();
        (CodexQuickModel Model, string? Effort)? original = null;
        CodexModelToggleService.ForegroundDraftPresentationContext context = default;
        JsonNode? existing = null;
        var draftRestored = false;
        var acknowledgements = 0;
        var ledChanges = DependencyPropertyDescriptor.FromProperty(Shape.FillProperty, typeof(Ellipse));
        EventHandler acknowledged = (_, _) => { if ((window.ActivityLed.Fill as SolidColorBrush)?.Color.ToString() == "#FF74D9A0") acknowledgements++; };
        ledChanges.AddValueChanged(window.ActivityLed, acknowledged);
        try
        {
            Console.WriteLine("Draft: snapshot existing chat");
            existing = await reader.ExecuteAsync("get_keypad_state", new() { ["thread_id"] = restoreThreadId });
            // Honor the user's existing setting; the test never enables automatic approval on disk.
            profile.SetAutoConfirmUltraFullAccess(new MicroProfileSettings().Current.AutoConfirmUltraFullAccess);
            if (restoreModel is not null && profile.Current.AutoConfirmUltraFullAccess)
            {
                var recoveryWindow = CodexWindowActivator.CaptureForegroundWindow();
                if (recoveryWindow != IntPtr.Zero)
                    await observer.TryConfirmNativeUltraAsync(recoveryWindow,
                        () => CodexWindowActivator.IsForegroundWindow(recoveryWindow), () => { }, CancellationToken.None);
            }
            Console.WriteLine("Draft: open blank composer");
            await reader.ExecuteAsync("new_keypad_thread", new());
            if (!CodexWindowActivator.TryActivate(null)) throw new Exception("App could not be activated");
            await Task.Delay(1500);
            context = new(CodexWindowActivator.CaptureForegroundWindow(), "live-draft-readback", 0, null);
            if (context.Window == IntPtr.Zero) throw new Exception("App is not foreground");
            Console.WriteLine("Draft: read composer selection");
            original = await Read();
            if (restoreModel is not null) original = (CodexModelToggleService.ParseModelId(restoreModel), restoreEffort);
            var start = original ?? throw new Exception("Draft model/effort could not be read");
            if (start.Effort is null) throw new Exception("Draft effort is unavailable");
            if (start.Effort == "ultra" && !profile.Current.AutoConfirmUltraFullAccess)
                throw new Exception("Ultra round trips require the user's existing auto-confirm preference; no setting was changed");
            Console.WriteLine($"Draft: baseline {start.Model.Id}/{start.Effort}");
            await profile.RefreshModelsAsync(CancellationToken.None);
            var alternate = CodexModelToggleService.ParseModelId(start.Model.Id == "gpt-6.1-sol" ? "gpt-6-astra" : "gpt-6.1-sol");
            profile.SetQuickModelA(start.Model);
            profile.SetQuickModelB(alternate);
            profile.SetQuickModelAEffort(start.Effort);
            profile.SetQuickModelBEffort("high");

            // Observe the actual composer independently of Micro's action result and presentation.
            foreach (var expected in new[] { (alternate, "high"), (start.Model, start.Effort), (alternate, "high"), (start.Model, start.Effort) })
            {
                var previousAcknowledgements = acknowledgements;
                var watch = Stopwatch.StartNew();
                window.SettingsKey.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                while (await Read() != expected || window.SettingsKey.IsUpdating ||
                    window.SettingsKey.ModelId != expected.Item1.Id || window.SettingsKey.ReasoningEffort != expected.Item2 ||
                    acknowledgements != previousAcknowledgements + 1 ||
                    (window.RuntimeLed.Fill as SolidColorBrush)?.Color.ToString() != "#FF9EBDFF")
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(25))
                        throw new Exception($"Draft did not reach {expected.Item1.Id}/{expected.Item2}; {AutomationProperties.GetHelpText(window)}");
                    await Task.Delay(150);
                }
                cases.Add(new { model = expected.Item1.Id, effort = expected.Item2, elapsedMs = watch.ElapsedMilliseconds });
                Console.WriteLine($"Draft: confirmed {expected.Item1.Id}/{expected.Item2} ({watch.ElapsedMilliseconds} ms)");
                await Task.Delay(750);
            }
            var efforts = profile.GetSupportedReasoningEfforts(start.Model).ToList();
            var effortIndex = efforts.IndexOf(start.Effort);
            if (effortIndex < 1) throw new Exception("A draft above the minimum effort is required for the wheel round trip");
            foreach (var (delta, expectedEffort) in new[] { (120, efforts[effortIndex - 1]), (-120, start.Effort) })
            {
                var watch = Stopwatch.StartNew();
                window.SettingsKey.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.MouseWheelEvent });
                while (await Read() != (start.Model, expectedEffort) || window.SettingsKey.IsUpdating || window.SettingsKey.ReasoningEffort != expectedEffort)
                {
                    if (watch.Elapsed > TimeSpan.FromSeconds(25)) throw new Exception("Draft wheel/readout did not reach " + expectedEffort);
                    await Task.Delay(150);
                }
                cases.Add(new { name = "draft reasoning wheel", model = start.Model.Id, effort = expectedEffort, elapsedMs = watch.ElapsedMilliseconds });
                Console.WriteLine($"Draft: wheel confirmed {expectedEffort}");
            }
            draftRestored = await Read() == start;
            var captured = await observer.CaptureDraftContextAsync(null, CancellationToken.None)
                ?? throw new Exception("Could not capture the tested draft before navigation");
            await reader.ExecuteAsync("open_keypad_thread", new() { ["thread_id"] = restoreThreadId });
            var navigation = Stopwatch.StartNew();
            while (await Task.Run(captured.IsCurrent))
            {
                if (navigation.Elapsed > TimeSpan.FromSeconds(8)) throw new Exception("Draft identity survived navigation to an existing chat");
                await Task.Delay(100);
            }
            var rejected = await observer.ToggleAsync(context.Window, start.Model, start.Effort, alternate, "high",
                profile.Current.AutoConfirmUltraFullAccess, "foreground-new-task:" + Guid.NewGuid().ToString("N"),
                captured.IsCurrent, CancellationToken.None);
            if (rejected.Succeeded) throw new Exception("A stale draft action was accepted");
            cases.Add(new { name = "navigation invalidates draft; stale switch rejected" });
            var after = await reader.ExecuteAsync("get_keypad_state", new() { ["thread_id"] = restoreThreadId });
            if (after["model"]?.ToJsonString() != existing["model"]?.ToJsonString() || after["effort"]?.ToJsonString() != existing["effort"]?.ToJsonString())
                throw new Exception("Existing chat model/effort changed while switching draft");
        }
        catch (Exception error) { errors.Add(error.ToString()); Console.WriteLine(error.Message); }
        finally
        {
            ledChanges.RemoveValueChanged(window.ActivityLed, acknowledged);
            await window.CloseForApplicationExitAsync();
            // A failed switch must not leave a modified draft as the next task's defaults.
            try
            {
                if (!draftRestored && original is { } start && await Read() is { } current && current != start)
                {
                    Console.WriteLine($"Draft: restore {current.Model.Id}/{current.Effort} to {start.Model.Id}/{start.Effort}");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    if (current.Model == start.Model)
                        await observer.SetReasoningAsync(context.Window, new("draft-restoration", start.Model.Id, start.Effort!),
                            profile.Current.AutoConfirmUltraFullAccess, () => CodexWindowActivator.IsForegroundWindow(context.Window), timeout.Token);
                    else
                        await observer.ToggleAsync(context.Window, start.Model, start.Effort,
                            current.Model, current.Effort, profile.Current.AutoConfirmUltraFullAccess, "foreground-new-task:" + Guid.NewGuid().ToString("N"),
                            () => CodexWindowActivator.IsForegroundWindow(context.Window), timeout.Token).WaitAsync(timeout.Token);
                    if (await Read() != start) throw new Exception("Draft restoration was not confirmed");
                }
            }
            catch (Exception error) { errors.Add("Cleanup: " + error.Message); }
            await reader.ExecuteAsync("open_keypad_thread", new() { ["thread_id"] = restoreThreadId });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow, cases, errors,
            scope = "WPF click/wheel events, real native composer readback and stale-target guard; no OS pointer hit testing",
            restored = draftRestored }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{(errors.Count == 0 ? "PASS" : "FAIL")}: {cases.Count} draft checks; {reportPath}");
        return errors.Count == 0 ? 0 : 1;

        async Task<(CodexQuickModel Model, string? Effort)?> Read() => await observer.ObserveSelectionAsync(context, false,
            () => CodexWindowActivator.IsForegroundWindow(context.Window), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(8));
    }
}
