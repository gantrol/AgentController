using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexController.Localization;
using CodexController.ViewModels;
using CodexController.Views;

namespace CodexController.Tests;

public sealed class SettingsPageViewDesignTests
{
    [Fact]
    public void ShowsVisibleRestoreDefaultsActionWithoutSaveButton()
    {
        WpfTestHost.Run(RenderAndAssert);
    }

    private static void RenderAndAssert()
    {
        var localization =
            new LocalizationService(AppLanguage.ZhCn);
        var viewModel = new SettingsPageViewModel(
            () => { },
            () => { },
            () => { },
            _ => { });
        viewModel.UpdateContext(
            localization.Strings,
            "Codex",
            "Menu",
            "Controller Studio");

        var view = new SettingsPageView
        {
            DataContext = viewModel,
            Strings = localization.Strings,
            Localization = localization,
        };
        view.Measure(new Size(900, 680));
        view.Arrange(new Rect(0, 0, 900, 680));
        view.UpdateLayout();

        var icon = Assert.IsType<ContentControl>(
            view.RestoreDefaultsButton.Content);
        Assert.Same(view.FindResource("Icon.Reset"), icon.Content);
        Assert.False(Assert.IsAssignableFrom<Geometry>(icon.Content).Bounds.IsEmpty);
        Assert.Equal(
            "恢复默认值",
            AutomationProperties.GetName(view.RestoreDefaultsButton));
        Assert.Same(viewModel.ResetCommand, view.RestoreDefaultsButton.Command);
        Assert.True(view.RestoreDefaultsButton.Focusable);
        Assert.Equal(
            "恢复默认值",
            view.RestoreDefaultsButton.ToolTip);
        Assert.Equal(
            Visibility.Visible,
            view.RestoreDefaultsButton.Visibility);
        Assert.Same(view.FindResource("Button.Icon"), view.RestoreDefaultsButton.Style);
        var iconButtonSize = Assert.IsType<double>(view.FindResource("Control.H.LG"));
        Assert.Equal(iconButtonSize, view.RestoreDefaultsButton.ActualWidth);
        Assert.Equal(iconButtonSize, view.RestoreDefaultsButton.ActualHeight);
        Assert.True(
            view.RestoreDefaultsButton
                .TransformToAncestor(view)
                .Transform(new Point()).Y < 80);

        WritePreviewFromEnvironment(view);
    }

    private static void WritePreviewFromEnvironment(
        FrameworkElement view)
    {
        var path = Environment.GetEnvironmentVariable(
            "AGENT_CONTROLLER_SETTINGS_PREVIEW_PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bitmap = new RenderTargetBitmap(
            (int)view.ActualWidth,
            (int)view.ActualHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
