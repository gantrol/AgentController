using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexController.Localization;
using CodexController.ViewModels;
using CodexController.Views;

namespace CodexController.Tests;

public sealed class ConfigPageViewDesignTests
{
    [Fact]
    public void ShowsVisibleRestoreDefaultsActionWithoutSaveButton()
    {
        WpfTestHost.Run(RenderAndAssert);
    }

    private static void RenderAndAssert()
    {
        var strings =
            new LocalizationService(AppLanguage.ZhCn).Strings;
        var viewModel = new ConfigPageViewModel(
            () => { },
            () => { });
        viewModel.UpdateContext(
            strings,
            "Codex",
            "L3",
            "Y",
            "R3");

        var view = new ConfigPageView
        {
            DataContext = viewModel,
            Strings = strings,
        };
        view.Measure(new Size(1060, 720));
        view.Arrange(new Rect(0, 0, 1060, 720));
        view.UpdateLayout();

        Assert.Equal(
            "恢复默认值",
            view.RestoreDefaultsButton.Content);
        Assert.Equal(
            Visibility.Visible,
            view.RestoreDefaultsButton.Visibility);
        Assert.True(view.RestoreDefaultsButton.ActualWidth >= 72);
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
            "AGENT_CONTROLLER_CONFIG_PREVIEW_PATH");
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
