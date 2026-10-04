using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexMicro.Desktop.Services;
using Xunit;

namespace CodexMicro.Desktop.Tests;

[Collection(WpfUiCollection.Name)]
[Trait("Category", "BehaviorAcceptance")]
public sealed class PanelAcceptanceTests
{
    [Fact]
    public Task BothPageSelectorsRemainReachable() => OnUiThread(async () =>
    {
        await using var rig = new BehaviorAcceptanceRig("reasoning", null);
        var window = new MicroSurfaceWindow(new(MicroLanguage.ZhCn),
            profileSettings: MicroProfileSettings.CreateTransient(), transport: rig.Transport);
        try
        {
            Assert.True(Attached(window.ControlPageButton, window), "Control page selector is detached");
            Assert.True(Attached(window.MonitorPageButton, window), "Monitor page selector is detached");
            Assert.Equal(Visibility.Visible, window.ControlPageButton.Visibility);
            Assert.Equal(Visibility.Visible, window.MonitorPageButton.Visibility);
            Assert.True(window.ControlPageButton.IsEnabled && window.MonitorPageButton.IsEnabled);
        }
        finally { await window.CloseForApplicationExitAsync(); }
    });

    [Fact]
    public Task MonitorKeepsFourteenTaskKeysAndSharedControls() => OnUiThread(async () =>
    {
        await using var rig = new BehaviorAcceptanceRig("reasoning", null);
        var window = new MicroSurfaceWindow(new(MicroLanguage.ZhCn),
            profileSettings: MicroProfileSettings.CreateTransient(), transport: rig.Transport);
        try
        {
            Assert.True(Attached(window.MonitorGrid, window), "Monitor panel is detached");
            Assert.Equal(14, window.MonitorGrid.Children.OfType<Button>().Count());
            Assert.True(Attached(window.ModelKnob, window));
            Assert.True(Attached(window.ActionKey12, window));
            Assert.False(Attached(window.ModelKnob, window.ControlGrid), "Knob would disappear with the control panel");
            Assert.False(Attached(window.ActionKey12, window.ControlGrid), "Submit would disappear with the control panel");
        }
        finally { await window.CloseForApplicationExitAsync(); }
    });

    private static bool Attached(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? node = element; node is not null; node = LogicalTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

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
