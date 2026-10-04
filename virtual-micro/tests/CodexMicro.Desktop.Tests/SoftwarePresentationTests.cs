using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

[Collection(WpfUiCollection.Name)]
public sealed class SoftwarePresentationTests
{
    [Fact]
    public Task SubmitDoesNotReportSuccessFromApplicationActivation() => OnUiThread(async () =>
    {
        await using var rig = new BehaviorAcceptanceRig("reasoning", null);
        var calls = 0;
        var window = new MicroSurfaceWindow(new(MicroLanguage.ZhCn),
            profileSettings: MicroProfileSettings.CreateTransient(), transport: rig.Transport,
            activateSoftwareApplication: () => { calls++; return Task.FromResult(true); },
            readSoftwareSelection: _ => Task.FromResult<string?>(null));
        var terminal = new TaskCompletionSource<Color>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fill = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            System.Windows.Shapes.Shape.FillProperty, window.ActivityLed.GetType());
        void Changed(object? sender, EventArgs args)
        {
            var color = ((SolidColorBrush)window.ActivityLed.Fill).Color;
            if (color == Color.FromRgb(0xff, 0x79, 0x94) || color == Color.FromRgb(0x74, 0xd9, 0xa0) || color == Color.FromRgb(0xff, 0xd6, 0x6e))
                terminal.TrySetResult(color);
        }
        try
        {
            await rig.ConnectAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(Color.FromRgb(0x9e, 0xbd, 0xff), ((SolidColorBrush)window.ActivityLed.Fill).Color);
            fill.AddValueChanged(window.ActivityLed, Changed);
            window.ActionKey12.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, calls);
            // No selected chat or verified draft exists; activation cannot stand in for submission.
            Assert.Equal(Color.FromRgb(0xff, 0x79, 0x94), await terminal.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Empty(rig.Ui.Commands);
        }
        finally { fill.RemoveValueChanged(window.ActivityLed, Changed); await window.CloseForApplicationExitAsync(); }
    });

    [Fact]
    public Task SlowSkillCatalogDoesNotBlockOpeningOrReplaceTheUsersChoice() => OnUiThread(async () =>
    {
        var loaded = new TaskCompletionSource<IReadOnlyList<CodexSkillDefinition>>();
        var reads = 0;
        using var layout = new CodexMicroLayoutObserver();
        var editor = new KeycapEditorWindow("ACT09", new("FAST", null, new("skill", "saved-skill", "fixture/SKILL.md")),
            new(MicroLanguage.ZhCn), new CodexMicroConfigWriter(layout.ConfigPath), layout,
            _ => { reads++; return loaded.Task; });
        try
        {
            Assert.Equal(0, reads);
            Assert.Contains("saved-skill", editor.ActionCombo.SelectedItem.ToString());
            typeof(KeycapEditorWindow).GetMethod("LoadSkills", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(editor, [editor, EventArgs.Empty]);
            Assert.Equal(1, reads);
            Assert.False(loaded.Task.IsCompleted);
            var chosen = editor.ActionCombo.Items.Cast<object>().First(item => item.ToString()!.Contains("New task"));
            editor.ActionCombo.SelectedItem = chosen;
            loaded.SetResult([new("later-skill", "fixture/later/SKILL.md")]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(chosen.ToString(), editor.ActionCombo.SelectedItem.ToString());
            Assert.Contains(editor.ActionCombo.Items.Cast<object>(), item => item.ToString()!.Contains("later-skill"));
        }
        finally { editor.Close(); }
    });

    private static Task OnUiThread(Func<Task> run)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await run(); completion.TrySetResult(); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
