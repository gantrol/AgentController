using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class WindowDesignTests
{
    [Fact]
    public void VoiceRecordingVisualAnimatesWithoutChangingTheInputSurface()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };

                var window = new MicroSurfaceWindow();
                window.SetVoiceRecordingVisual(recording: true);

                Assert.True(window.VoiceFlowGlow.HasAnimatedProperties);
                Assert.True(window.VoiceWaveLayer.HasAnimatedProperties);
                Assert.True(window.VoiceWaveParticles.HasAnimatedProperties);
                Assert.False(window.VoiceReadyFlash.HasAnimatedProperties);
                Assert.False(window.VoiceWaveLayer.IsHitTestVisible);
                Assert.Equal("ACT10_ACT11", window.ActionKey10.Tag);
                Assert.Equal(
                    Color.FromRgb(0x20, 0x8F, 0x80),
                    Assert.IsType<SolidColorBrush>(window.ActionIcon10.IconBrush).Color);

                window.SetVoiceRecordingVisual(recording: false);

                Assert.False(window.VoiceFlowGlow.HasAnimatedProperties);
                Assert.False(window.VoiceWaveLayer.HasAnimatedProperties);
                Assert.False(window.VoiceWaveParticles.HasAnimatedProperties);
                Assert.True(window.VoiceReadyFlash.HasAnimatedProperties);
                Assert.Equal("ACT10_ACT11", window.ActionKey10.Tag);
                Assert.Equal(
                    Color.FromRgb(0x17, 0x17, 0x17),
                    Assert.IsType<SolidColorBrush>(window.ActionIcon10.IconBrush).Color);
                window.CloseForApplicationExit();
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

    [Fact]
    public void KeyboardLayoutRendersOffscreenWithSquareKeycaps()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = Application.Current ?? new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };

                var window = new MicroSurfaceWindow();
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
                Assert.True(window.Topmost);
                Assert.Equal(442.5, window.Width, 3);
                Assert.Equal(457.5, window.Height, 3);
                Assert.False(window.ShowInTaskbar);
                Assert.Contains("Micro Surface", window.Title);
                Assert.True(window.TopmostMenuItem.IsCheckable);
                Assert.NotNull(window.DeviceFrame.ContextMenu);
                Assert.Equal(Visibility.Collapsed, window.DialSelectionHud.Visibility);
                Assert.Equal(250, window.DialSelectionHud.Width, 3);

                Assert.IsType<LinearGradientBrush>(
                    window.DeviceFrame.Background);
                Assert.IsType<LinearGradientBrush>(
                    window.PearlLightGuide.Background);
                Assert.IsType<LinearGradientBrush>(
                    window.CrystalPrismRim.BorderBrush);
                Assert.NotNull(window.CrystalDepthPlate.Background);
                Assert.True(window.CrystalLightClip.ClipToBounds);
                Assert.False(window.CrystalLowerRefraction.IsHitTestVisible);
                Assert.Equal(0.15, window.CrystalLowerRefraction.Opacity, 3);
                Assert.Null(window.FindName("CrystalTopRefraction"));
                Assert.Null(window.FindName("AuroraFilm"));
                Assert.Null(window.FindName("InnerCrystalEtch"));
                Assert.Null(window.FindName("CrystalFlowBand"));
                Assert.Null(window.FindName("CrystalEdgeLightSource"));
                Assert.Null(window.FindName("CrystalEdgeLightTransform"));
                Assert.Null(window.FindName("CrystalKeyLightLayer"));
                Assert.Null(window.FindName("CrystalKeyLightSource"));
                Assert.Null(window.FindName("CrystalKeyLightTransform"));
                Assert.Null(window.FindName("AmbientMintGlow"));
                Assert.Null(window.FindName("AmbientVioletGlow"));
                Assert.Null(window.FindName("InnerFlowRotateTransform"));
                Assert.Null(window.FindName("CrystalFastenerTopLeft"));
                Assert.Null(window.FindName("CrystalFastenerTopRight"));
                Assert.Null(window.FindName("CrystalFastenerBottomLeft"));
                Assert.Null(window.FindName("CrystalFastenerBottomRight"));
                Assert.Contains("CRYSTAL HID", window.LeftSilkScreen.Text);
                Assert.Contains("OPTICAL INPUT", window.RightSilkScreen.Text);

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
                var statusLightField = Assert.IsType<Ellipse>(
                    window.AgentKey0.Template.FindName(
                        "StatusLightField",
                        window.AgentKey0));
                var agentWell = Assert.IsType<Ellipse>(
                    window.AgentKey0.Template.FindName(
                        "AgentWell",
                        window.AgentKey0));
                var statusLightRing = Assert.IsType<Ellipse>(
                    window.AgentKey0.Template.FindName(
                        "StatusLightRing",
                        window.AgentKey0));
                var agentWellHighlight = Assert.IsType<Ellipse>(
                    window.AgentKey0.Template.FindName(
                        "AgentWellHighlight",
                        window.AgentKey0));
                var agentGlyph = Assert.IsType<Grid>(
                    window.AgentKey0.Template.FindName(
                        "AgentGlyph",
                        window.AgentKey0));
                var agentCap = Assert.IsType<Border>(
                    window.AgentKey0.Template.FindName(
                        "Cap",
                        window.AgentKey0));
                Assert.Equal(80, statusLightField.Width, 3);
                Assert.Equal(78, agentWell.Width, 3);
                Assert.Equal(76, agentWellHighlight.Width, 3);
                Assert.IsType<RadialGradientBrush>(agentWell.Fill);
                Assert.IsType<LinearGradientBrush>(agentWellHighlight.Stroke);
                Assert.Equal(2, agentGlyph.Children.Count);
                Assert.Equal(18, agentGlyph.Width, 3);
                var activeLight = new SolidColorBrush(
                    Color.FromRgb(0x30, 0x4F, 0xFE));
                window.AgentKey0.BorderBrush = activeLight;
                Assert.False(activeLight.HasAnimatedProperties);
                Assert.Same(activeLight, statusLightField.Fill);
                Assert.Same(activeLight, statusLightRing.Stroke);
                Assert.IsType<LinearGradientBrush>(agentCap.Background);
                Assert.Equal(0.12, statusLightField.Opacity, 3);
                Assert.IsType<BlurEffect>(statusLightField.Effect);
                Assert.Equal(72, statusLightRing.Width, 3);
                Assert.Equal(0.52, statusLightRing.Opacity, 3);
                Assert.Equal(1.8, statusLightRing.StrokeThickness, 3);
                var hoverTrigger = window.AgentKey0.Template.Triggers
                    .OfType<Trigger>()
                    .Single(trigger => trigger.Property.Name == "IsMouseOver");
                Assert.DoesNotContain(
                    hoverTrigger.Setters.OfType<Setter>(),
                    setter => setter.TargetName is
                        "StatusLightField" or "StatusLightRing" or "CrystalSide");
                var pressTrigger = window.AgentKey0.Template.Triggers
                    .OfType<Trigger>()
                    .Single(trigger => trigger.Property.Name == "IsPressed");
                Assert.NotEmpty(pressTrigger.EnterActions);
                Assert.NotEmpty(pressTrigger.ExitActions);

                window.ActionKey06.ApplyTemplate();
                var commandWell = Assert.IsType<Border>(
                    window.ActionKey06.Template.FindName(
                        "KeyWell",
                        window.ActionKey06));
                var commandCap = Assert.IsType<Border>(
                    window.ActionKey06.Template.FindName(
                        "Cap",
                        window.ActionKey06));
                Assert.Equal(76, commandWell.Width, 3);
                Assert.IsType<RadialGradientBrush>(commandWell.Background);
                Assert.Equal(new CornerRadius(14), commandCap.CornerRadius);
                Assert.Equal(28, window.ActionIcon06.Width, 3);

                window.ActionKey10.ApplyTemplate();
                var voiceWell = Assert.IsType<Border>(
                    window.ActionKey10.Template.FindName(
                        "KeyWell",
                        window.ActionKey10));
                Assert.Equal(160, voiceWell.Width, 3);
                Assert.Equal(28, window.ActionIcon10.Width, 3);
                Assert.False(window.VoiceWaveLayer.IsHitTestVisible);
                Assert.Equal(0.22, window.VoiceWaveLayer.Opacity, 3);
                Assert.Equal(0.12, window.VoiceFlowGlow.Opacity, 3);
                Assert.Equal(0, window.VoiceReadyFlash.Opacity, 3);
                Assert.Equal(
                    DoubleCollection.Parse("0.1 4"),
                    window.VoiceWaveParticles.StrokeDashArray);
                Assert.Equal(
                    Color.FromRgb(0x17, 0x17, 0x17),
                    Assert.IsType<SolidColorBrush>(window.ActionIcon10.IconBrush).Color);

                Assert.InRange(window.JoystickCap.ActualWidth, 66.5, 67.5);
                Assert.InRange(window.JoystickCap.ActualHeight, 66.5, 67.5);
                Assert.Equal(3, window.JoystickCap.Children.Count);
                Assert.IsType<RadialGradientBrush>(
                    Assert.IsType<Ellipse>(window.JoystickCap.Children[0]).Fill);
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
                Assert.Null(settingsKnob.Stroke);

                var runtimeLedTop = window.RuntimeLed
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .Y;
                var driverLedTop = window.DriverLed
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .Y;
                var activityLedTop = window.ActivityLed
                    .TranslatePoint(new Point(), window.DesignSurface)
                    .Y;
                Assert.True(runtimeLedTop < driverLedTop);
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
                // Async observers may refresh the slot while this off-screen
                // test is running. Restore the representative active color
                // immediately before visual QA rendering.
                window.AgentKey0.BorderBrush = activeLight;
                statusLightField.Fill = activeLight;
                statusLightRing.Stroke = activeLight;
                statusLightField.InvalidateVisual();
                statusLightRing.InvalidateVisual();
                window.DesignSurface.UpdateLayout();
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

                var activeKeyStage = new Grid
                {
                    Width = 96,
                    Height = 96,
                    Background = Brushes.White,
                };
                var isolatedActiveKey = new Button
                {
                    Width = 96,
                    Height = 96,
                    Style = window.AgentKey0.Style,
                    BorderBrush = activeLight,
                };
                activeKeyStage.Children.Add(isolatedActiveKey);
                activeKeyStage.Measure(new Size(96, 96));
                activeKeyStage.Arrange(new Rect(0, 0, 96, 96));
                activeKeyStage.UpdateLayout();
                var activeKeyBitmap = new RenderTargetBitmap(
                    96,
                    96,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                activeKeyBitmap.Render(activeKeyStage);
                var activeKeyPixels = new byte[96 * 96 * 4];
                activeKeyBitmap.CopyPixels(activeKeyPixels, 96 * 4, 0);
                var ringBlue = BlueEmphasisAt(
                    activeKeyPixels,
                    width: 96,
                    x: 12,
                    y: 48);
                var quietCenterBlue = BlueEmphasisAt(
                    activeKeyPixels,
                    width: 96,
                    x: 48,
                    y: 24);
                Assert.InRange(quietCenterBlue, 0, 35);
                Assert.True(ringBlue > quietCenterBlue + 30);
                var activeKeyPreviewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_ACTIVE_KEY_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(activeKeyPreviewPath))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(activeKeyBitmap));
                    using var stream = new FileStream(
                        activeKeyPreviewPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
                    encoder.Save(stream);
                }

                window.CloseForApplicationExit();
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

    private static double BlueEmphasisAt(
        byte[] pixels,
        int width,
        int x,
        int y)
    {
        var offset = ((y * width) + x) * 4;
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        return blue - ((red + green) / 2.0);
    }

    private static void AssertSquare(FrameworkElement element) =>
        Assert.Equal(element.ActualWidth, element.ActualHeight, 3);
}
