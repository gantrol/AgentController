using System.Windows;
using System.Windows.Controls;
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

    [Fact]
    public void LongTextTooltipsWrapWithoutClipping()
    {
        WpfTestHost.Run(AssertLongTooltipTemplate);
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

        Assert.Equal(
            viewModel.AgentShortcutsDescription,
            view.AgentShortcutsSection.ToolTip);

        WritePreviewFromEnvironment(view);
    }

    private static void AssertLongTooltipTemplate()
    {
        const string longDescription =
            "The app safely appends fallback bindings; " +
            "new bindings take effect after Codex restarts.";
        var tooltip = new ToolTip
        {
            Content = longDescription,
            Style = (Style)Application.Current.FindResource(
                typeof(ToolTip)),
        };

        tooltip.ApplyTemplate();
        tooltip.Measure(new Size(
            double.PositiveInfinity,
            double.PositiveInfinity));
        tooltip.Arrange(new Rect(
            0,
            0,
            tooltip.DesiredSize.Width,
            tooltip.DesiredSize.Height));
        tooltip.UpdateLayout();

        var wrappedText = FindVisualChildren<TextBlock>(tooltip)
            .Single(text => text.Text == longDescription);
        Assert.Equal(TextWrapping.Wrap, wrappedText.TextWrapping);
        Assert.InRange(wrappedText.MaxWidth, 240, 320);
        Assert.True(tooltip.DesiredSize.Width <= 340);
    }

    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
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
