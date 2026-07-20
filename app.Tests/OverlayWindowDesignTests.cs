using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexController.Tests;

public sealed class OverlayWindowDesignTests
{
    [Fact]
    public void ToastUsesAUniformCrystalPlateInsteadOfAKeycapProfile()
    {
        WpfTestHost.Run(RenderAndAssert);
    }

    private static void RenderAndAssert()
    {
        var window = new OverlayWindow();
        try
        {
            window.OverlayTitle.Text = "Codex";
            window.OverlayValue.Text =
                "Codex is now in the foreground";

            var plate = window.RootBorder;
            plate.Measure(new Size(440, 96));
            plate.Arrange(new Rect(0, 0, 440, 96));
            plate.UpdateLayout();

            Assert.Equal(new Thickness(4), plate.BorderThickness);
            Assert.Equal(new CornerRadius(20), plate.CornerRadius);
            Assert.True(plate.MinHeight >= 72);
            Assert.NotNull(plate.Background);
            Assert.NotNull(plate.Effect);

            WritePreviewFromEnvironment(plate);
        }
        finally
        {
            window.Close();
        }
    }

    private static void WritePreviewFromEnvironment(
        FrameworkElement element)
    {
        var path = Environment.GetEnvironmentVariable(
            "AGENT_CONTROLLER_TOAST_PREVIEW_PATH");
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
            (int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
