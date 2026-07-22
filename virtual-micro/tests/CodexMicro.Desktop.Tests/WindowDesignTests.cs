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
                    Color.FromRgb(0x0C, 0x8E, 0x7E),
                    Assert.IsType<SolidColorBrush>(window.ActionIcon10.IconBrush).Color);

                window.SetVoiceRecordingVisual(recording: false);

                Assert.False(window.VoiceFlowGlow.HasAnimatedProperties);
                Assert.False(window.VoiceWaveLayer.HasAnimatedProperties);
                Assert.False(window.VoiceWaveParticles.HasAnimatedProperties);
                Assert.True(window.VoiceReadyFlash.HasAnimatedProperties);
                Assert.Equal("ACT10_ACT11", window.ActionKey10.Tag);
                Assert.Equal(
                    Color.FromRgb(0x17, 0xA8, 0x8F),
                    Assert.IsType<SolidColorBrush>(window.ActionIcon10.IconBrush).Color);
                Assert.Equal(0.34, window.VoiceFlowGlow.Opacity, 3);
                Assert.Equal(0.72, window.VoiceWaveLayer.Opacity, 3);
                Assert.IsType<RadialGradientBrush>(window.VoiceFlowGlow.Background);
                Assert.IsType<DropShadowEffect>(window.VoiceFlowGlow.Effect);
                Assert.IsType<LinearGradientBrush>(window.VoiceGlassSurface.Background);
                Assert.IsType<BlurEffect>(window.VoiceWaveAura.Effect);
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
                Assert.Contains("按住左键上下或左右拖动", dialHelpText);
                Assert.Contains("短按：打开或确认", dialHelpText);
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
                var agentGlow = Assert.IsType<Border>(
                    window.AgentKey0.Template.FindName(
                        "Glow",
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

                // Paper agent keys carry state color only in the outer glow and
                // the inner circular light field (export lines 32-107). There is
                // no extra stroked status ring — it must stay removed.
                Assert.Null(window.AgentKey0.Template.FindName(
                    "StatusLightRing",
                    window.AgentKey0));

                // The light field is slightly larger than the neutral well so the
                // state color reads as a soft halo instead of a flat fill.
                Assert.Equal(82, statusLightField.Width, 3);
                Assert.Equal(76, agentWell.Width, 3);
                Assert.Equal(74, agentWellHighlight.Width, 3);
                Assert.True(statusLightField.Width > agentWell.Width);
                Assert.IsType<RadialGradientBrush>(agentWell.Fill);
                Assert.IsType<LinearGradientBrush>(agentWellHighlight.Stroke);
                Assert.Equal(2, agentGlyph.Children.Count);
                Assert.Equal(18, agentGlyph.Width, 3);

                var activeLight = new SolidColorBrush(
                    Color.FromRgb(0x30, 0x4F, 0xFE));
                window.AgentKey0.BorderBrush = activeLight;
                Assert.False(activeLight.HasAnimatedProperties);
                Assert.Same(activeLight, statusLightField.Fill);
                Assert.Same(activeLight, agentGlow.Background);

                // Keep the translucent Paper cap but lift the neutral tone so
                // inactive slots stay glassy instead of reading as grey blocks.
                var agentCapFill = Assert.IsType<SolidColorBrush>(agentCap.Background);
                Assert.Equal(
                    Color.FromArgb(0xA3, 0xE8, 0xEE, 0xEB),
                    agentCapFill.Color);
                Assert.Equal(0.32, statusLightField.Opacity, 3);
                Assert.IsType<BlurEffect>(statusLightField.Effect);
                var hoverTrigger = window.AgentKey0.Template.Triggers
                    .OfType<Trigger>()
                    .Single(trigger => trigger.Property.Name == "IsMouseOver");
                Assert.DoesNotContain(
                    hoverTrigger.Setters.OfType<Setter>(),
                    setter => setter.TargetName is
                        "StatusLightField");
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
                Assert.Equal(40, window.ActionIcon10.Width, 3);
                Assert.False(window.VoiceWaveLayer.IsHitTestVisible);
                Assert.Equal(0.72, window.VoiceWaveLayer.Opacity, 3);
                Assert.Equal(0.34, window.VoiceFlowGlow.Opacity, 3);
                Assert.Equal(0, window.VoiceReadyFlash.Opacity, 3);
                Assert.Equal(
                    DoubleCollection.Parse("0.1 2.5"),
                    window.VoiceWaveParticles.StrokeDashArray);
                Assert.Equal(
                    Color.FromRgb(0x17, 0xA8, 0x8F),
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
                Assert.Equal(14, directionGlyph.Width, 3);
                Assert.Equal(10, directionGlyph.Height, 3);
                var engravedHighlight = Assert.IsType<System.Windows.Shapes.Path>(
                    directionGlyph.Children[0]);
                var engravedEdge = Assert.IsType<System.Windows.Shapes.Path>(
                    directionGlyph.Children[1]);
                Assert.Equal(
                    Color.FromArgb(0xD8, 0xFF, 0xFF, 0xFF),
                    Assert.IsType<SolidColorBrush>(engravedHighlight.Stroke).Color);
                Assert.Equal(
                    Color.FromArgb(0xB3, 0x47, 0x40, 0x3B),
                    Assert.IsType<SolidColorBrush>(engravedEdge.Stroke).Color);
                Assert.Null(window.JoystickUp.Content);
                Assert.Same(window.JoystickUp.Template, window.JoystickLeft.Template);
                Assert.Equal(24, window.JoystickUp.ActualWidth, 3);
                Assert.Equal(24, window.JoystickUp.ActualHeight, 3);
                Assert.Equal(-6, window.JoystickUp.Margin.Top, 3);
                Assert.Equal(-6, window.JoystickLeft.Margin.Left, 3);
                Assert.Equal(-6, window.JoystickRight.Margin.Right, 3);
                Assert.Equal(-6, window.JoystickDown.Margin.Bottom, 3);

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
                // Async observers may refresh slots while this off-screen test
                // runs. Paint the six agent keys with Paper's showcase states
                // (blue, green, white, amber, red, unassigned) right before
                // visual QA rendering so the snapshot mirrors the Paper export
                // row order (AgentKey0..5 == Paper cols).
                window.AgentKey0.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(0x30, 0x4F, 0xFE));
                window.AgentKey1.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x4C));
                window.AgentKey2.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                window.AgentKey3.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(0xFF, 0x6D, 0x00));
                window.AgentKey4.BorderBrush =
                    new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0x33));
                window.AgentKey5.BorderBrush =
                    new SolidColorBrush(Color.FromArgb(0x00, 0x8D, 0xB5, 0xFF));
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

                // Isolated active (blue) key rendered on white so the outer
                // glow's spill is visible for QA. The state color is an even
                // frosted wash (Paper's light field + glow), not a hard ring.
                var activeKeyPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    activeLight,
                    out var activeKeyBitmap);
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

                // The same key with no assigned state is the neutral reference.
                // Paper's active color must clearly paint the keycap versus the
                // unlit crystal base.
                var inactiveKeyPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    new SolidColorBrush(Color.FromArgb(0x00, 0x8D, 0xB5, 0xFF)),
                    out _);

                const int wellSampleX = 30; // left of the centered plus glyph
                const int wellSampleY = 48;
                var activeWellBlue = BlueEmphasisAt(
                    activeKeyPixels, width: 96, x: wellSampleX, y: wellSampleY);
                var inactiveWellBlue = BlueEmphasisAt(
                    inactiveKeyPixels, width: 96, x: wellSampleX, y: wellSampleY);
                Assert.True(
                    activeWellBlue > 30,
                    $"Active agent key should read blue; was {activeWellBlue}.");
                Assert.InRange(inactiveWellBlue, -25, 25);
                Assert.True(
                    activeWellBlue > inactiveWellBlue + 25,
                    $"Active {activeWellBlue} not clearly bluer than " +
                    $"inactive {inactiveWellBlue}.");

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

    private static byte[] RenderIsolatedAgentKey(
        Style style,
        Brush borderBrush,
        out RenderTargetBitmap bitmap)
    {
        var stage = new Grid
        {
            Width = 96,
            Height = 96,
            Background = Brushes.White,
        };
        stage.Children.Add(new Button
        {
            Width = 96,
            Height = 96,
            Style = style,
            BorderBrush = borderBrush,
        });
        stage.Measure(new Size(96, 96));
        stage.Arrange(new Rect(0, 0, 96, 96));
        stage.UpdateLayout();
        bitmap = new RenderTargetBitmap(96, 96, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var pixels = new byte[96 * 96 * 4];
        bitmap.CopyPixels(pixels, 96 * 4, 0);
        return pixels;
    }

    private static void AssertSquare(FrameworkElement element) =>
        Assert.Equal(element.ActualWidth, element.ActualHeight, 3);
}
