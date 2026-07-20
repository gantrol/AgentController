using System.Windows;
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

        Assert.Equal("↺", view.RestoreDefaultsButton.Content);
        Assert.Equal(
            "恢复默认值",
            view.RestoreDefaultsButton.ToolTip);
        Assert.Equal(
            Visibility.Visible,
            view.RestoreDefaultsButton.Visibility);
        Assert.True(view.RestoreDefaultsButton.ActualWidth >= 38);
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
