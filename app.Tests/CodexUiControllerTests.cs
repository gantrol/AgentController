using AgentController.Adapters.Codex.Windows;
using System.Windows;
using Xunit;

namespace CodexController.Tests;

public sealed class CodexUiControllerTests
{
    [Theory]
    [InlineData(CodexUiOperation.Submit)]
    [InlineData(CodexUiOperation.ToggleSidebar)]
    [InlineData(CodexUiOperation.Back)]
    [InlineData(CodexUiOperation.Forward)]
    [InlineData(CodexUiOperation.ScrollUp)]
    [InlineData(CodexUiOperation.ScrollDown)]
    [InlineData(CodexUiOperation.ScrollTop)]
    [InlineData(CodexUiOperation.ScrollBottom)]
    [InlineData(CodexUiOperation.ComposerNext)]
    [InlineData(CodexUiOperation.ComposerPrevious)]
    [InlineData(CodexUiOperation.ComposerActivate)]
    public async Task MutationWithoutReadbackIsNeverConfirmedOrRetried(CodexUiOperation operation)
    {
        var desktop = new CodexUiDesktopFixture { IgnoreMutations = true };
        using var controller = new CodexUiController(desktop);
        var result = await controller.ExecuteAsync(new(operation));
        Assert.Equal(CodexUiDisposition.OutcomeUnknown, result.Disposition);
        Assert.Single(desktop.Commands);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("foreground")]
    [InlineData("route")]
    [InlineData("text")]
    [InlineData("guard")]
    public async Task ChangedSubmitTargetDoesNotDispatch(string change)
    {
        var desktop = new CodexUiDesktopFixture { Available = change != "unavailable", IsCurrent = change != "foreground" };
        desktop.BeforeRead = () =>
        {
            if (desktop.Reads != 2) return;
            if (change == "route") desktop.Set("route-a", node => node with { Name = "Another chat" });
            if (change == "text") desktop.Set("editor", node => node with { Text = "User edited this" });
        };
        using var controller = new CodexUiController(desktop);
        var result = await controller.ExecuteAsync(new(CodexUiOperation.Submit), _ => Task.FromResult(change != "guard"));
        Assert.Equal(CodexUiDisposition.NotSent, result.Disposition);
        Assert.Empty(desktop.Commands);
    }

    [Fact]
    public async Task ForegroundLossAfterDispatchIsUnknownAndNeverRetried()
    {
        var desktop = new CodexUiDesktopFixture();
        desktop.AfterMutation = () => desktop.IsCurrent = false;
        using var controller = new CodexUiController(desktop);
        var result = await controller.ExecuteAsync(new(CodexUiOperation.Submit));
        Assert.Equal(CodexUiDisposition.OutcomeUnknown, result.Disposition);
        Assert.Single(desktop.Commands);
    }

    [Fact]
    public async Task CanceledProviderRetainsLeaseUntilNativeCallReturns()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var desktop = new CodexUiDesktopFixture();
        desktop.BeforeRead = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); };
        using var controller = new CodexUiController(desktop);
        using var cancellation = new CancellationTokenSource();
        try
        {
            var pending = controller.ExecuteAsync(new(CodexUiOperation.Submit), cancellationToken: cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.Equal(CodexUiDisposition.NotSent, (await pending.WaitAsync(TimeSpan.FromSeconds(2))).Disposition);
            Assert.Equal("ui.busy", (await controller.ExecuteAsync(new(CodexUiOperation.Submit))).Code);
            Assert.Empty(desktop.Commands);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(0, CodexUiOperation.ScrollUp)]
    [InlineData(100, CodexUiOperation.ScrollDown)]
    public async Task ScrollBoundaryDoesNotInjectAnotherInput(double percent, CodexUiOperation operation)
    {
        var desktop = new CodexUiDesktopFixture();
        desktop.Set("conversation", node => node with { ScrollPercent = percent });
        using var controller = new CodexUiController(desktop);
        Assert.Equal("ui.scroll.boundary", (await controller.ExecuteAsync(new(operation))).Code);
        Assert.Empty(desktop.Commands);
    }

    [Fact]
    public async Task UnrelatedMenuPreventsSubmit()
    {
        var desktop = new CodexUiDesktopFixture();
        desktop.Nodes.Add(new("other-menu", "root", UiRole.Menu, "", "", new Rect(0, 100, 100, 100)));
        desktop.Nodes.Add(new("other-action", "other-menu", UiRole.MenuItem, "Delete", "", new Rect(0, 100, 100, 30)));
        using var controller = new CodexUiController(desktop);
        Assert.Equal("ui.menu.unrelated", (await controller.ExecuteAsync(new(CodexUiOperation.Submit))).Code);
        Assert.Empty(desktop.Commands);
    }

    [Fact]
    public void AmbiguousEditorsAndSendButtonsAreNotSelected()
    {
        var desktop = new CodexUiDesktopFixture();
        desktop.Nodes.Add(desktop.Nodes.Single(node => node.Id == "send") with { Id = "other-send" });
        Assert.Null(desktop.Read().Send);
        desktop.Nodes.Add(desktop.Nodes.Single(node => node.Id == "editor") with { Id = "other-editor" });
        Assert.Null(desktop.Read().Composer);
    }
}
