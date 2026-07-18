using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class WindowDesignTests
{
    [Fact]
    public void KeyboardLayoutRendersOffscreenWithSquareKeycaps()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                app.InitializeComponent();

                var window = new MainWindow();
                window.DesignSurface.Measure(new Size(590, 610));
                window.DesignSurface.Arrange(new Rect(0, 0, 590, 610));
                window.DesignSurface.UpdateLayout();

                AssertSquare(window.AgentKey0);
                AssertSquare(window.ActionKey06);
                AssertSquare(window.SettingsKey);
                AssertSquare(window.ActionKey12);
                Assert.Null(window.SettingsKey.Content);
                Assert.NotSame(window.ActionKey12.Template, window.SettingsKey.Template);
                Assert.Equal(96, window.ActionKey06.ActualWidth, 3);
                Assert.Equal(96, window.ActionKey06.ActualHeight, 3);
                Assert.InRange(window.ActionKey10.ActualWidth, 201, 203);
                Assert.Equal(96, window.ActionKey10.ActualHeight, 3);
                Assert.False(window.ShowActivated);
                Assert.Equal(442.5, window.Width, 3);
                Assert.Equal(457.5, window.Height, 3);
                Assert.NotNull(window.Icon);
                Assert.True(window.TopmostMenuItem.IsCheckable);
                Assert.NotNull(window.DeviceFrame.ContextMenu);
                Assert.Equal(Visibility.Collapsed, window.DialSelectionHud.Visibility);
                Assert.Equal(250, window.DialSelectionHud.Width, 3);

                var dialHelp = Assert.IsType<ToolTip>(window.DialButton.ToolTip);
                var dialHelpContent = Assert.IsType<StackPanel>(dialHelp.Content);
                Assert.Equal(2, dialHelpContent.Children.Count);
                Assert.False(string.IsNullOrWhiteSpace(
                    AutomationProperties.GetName(window.DialButton)));
                var dialHelpText = AutomationProperties.GetHelpText(
                    window.DialButton);
                Assert.Contains("左键：打开或确认", dialHelpText);
                Assert.DoesNotContain("右键", dialHelpText);
                Assert.DoesNotContain("Micro 设置", dialHelpText);

                window.DialButton.ApplyTemplate();
                var dialIndicator = Assert.IsType<Border>(
                    window.DialButton.Template.FindName(
                        "DialIndicator",
                        window.DialButton));
                Assert.IsType<RotateTransform>(dialIndicator.RenderTransform);
                for (var step = 0; step < 32; step++)
                {
                    window.AnimateDialStep(clockwise: step % 3 != 0);
                }

                Assert.False(Assert.IsType<RotateTransform>(
                    dialIndicator.RenderTransform).IsFrozen);

                window.AgentKey0.ApplyTemplate();
                var statusDot = Assert.IsType<Ellipse>(
                    window.AgentKey0.Template.FindName(
                        "StatusDot",
                        window.AgentKey0));
                Assert.Equal(20, statusDot.ActualWidth, 3);
                Assert.Equal(
                    Color.FromArgb(0xD9, 0x68, 0x5F, 0xAE),
                    Assert.IsType<SolidColorBrush>(statusDot.Fill).Color);

                Assert.InRange(window.JoystickCap.ActualWidth, 53.5, 54.5);
                Assert.InRange(window.JoystickCap.ActualHeight, 53.5, 54.5);
                Assert.InRange(window.JoystickSeat.ActualWidth, 87, 89);

                window.JoystickUp.ApplyTemplate();
                var directionGlyph = Assert.IsType<Grid>(
                    window.JoystickUp.Template.FindName(
                        "DirectionGlyph",
                        window.JoystickUp));
                Assert.Equal(2, directionGlyph.Children.Count);
                var engravedHighlight = Assert.IsType<System.Windows.Shapes.Path>(
                    directionGlyph.Children[0]);
                var engravedEdge = Assert.IsType<System.Windows.Shapes.Path>(
                    directionGlyph.Children[1]);
                Assert.Equal(
                    Color.FromArgb(0xA6, 0xFF, 0xFF, 0xFF),
                    Assert.IsType<SolidColorBrush>(engravedHighlight.Stroke).Color);
                Assert.Equal(
                    Color.FromArgb(0xA6, 0x55, 0x4E, 0x47),
                    Assert.IsType<SolidColorBrush>(engravedEdge.Stroke).Color);
                Assert.Null(window.JoystickUp.Content);
                Assert.Same(window.JoystickUp.Template, window.JoystickLeft.Template);
                Assert.Equal(24, window.JoystickUp.ActualWidth, 3);
                Assert.Equal(24, window.JoystickUp.ActualHeight, 3);

                window.SettingsKey.ApplyTemplate();
                var settingsKnob = Assert.IsType<Ellipse>(
                    window.SettingsKey.Template.FindName(
                        "KnobFace",
                        window.SettingsKey));
                Assert.InRange(settingsKnob.ActualWidth, 57.5, 58.5);
                Assert.Equal(
                    Color.FromRgb(0x2D, 0x29, 0x25),
                    Assert.IsType<SolidColorBrush>(settingsKnob.Fill).Color);

                var compatibilityLedTop = window.CompatibilityLed
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .Y;
                var driverLedTop = window.DriverLed
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .Y;
                var activityLedTop = window.ActivityLed
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .Y;
                Assert.True(compatibilityLedTop < driverLedTop);
                Assert.True(driverLedTop < activityLedTop);

                var leftSilkRight = window.LeftSilkScreen
                    .TranslatePoint(
                        new Point(window.LeftSilkScreen.ActualWidth, 0),
                        window.DesignSurface)
                    .X;
                var controlLeft = window.ControlGrid
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .X;
                var rightSilkLeft = window.RightSilkScreen
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .X;
                var controlRight = controlLeft + window.ControlGrid.ActualWidth;
                Assert.True(leftSilkRight < controlLeft);
                Assert.True(rightSilkLeft > controlRight);

                var bitmap = new RenderTargetBitmap(
                    590,
                    610,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                bitmap.Render(window.DesignSurface);
                var previewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(previewPath))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new FileStream(
                        previewPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
                    encoder.Save(stream);
                }

                window.Close();
                app.Shutdown();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    private static void AssertSquare(FrameworkElement element) =>
        Assert.Equal(element.ActualWidth, element.ActualHeight, 3);
}
