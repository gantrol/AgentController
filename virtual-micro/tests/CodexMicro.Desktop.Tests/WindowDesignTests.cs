using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Desktop.Tests;

public sealed class WindowDesignTests
{
    private const int IsolatedAgentRenderSize = 166;

    [Fact]
    public void VoiceRecordingVisualUsesTheStandardCommandKeySurface()
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
                window.ActionKey10.ApplyTemplate();
                Assert.Same(window.ActionIcon10, window.ActionKey10.Content);
                Assert.Null(window.FindName("VoiceFlowGlow"));
                Assert.Null(window.FindName("VoiceWaveLayer"));
                Assert.Null(window.FindName("VoiceReadyFlash"));

                window.SetVoiceRecordingVisual(recording: true);

                Assert.Equal("ACT10_ACT11", window.ActionKey10.Tag);
                Assert.Equal(
                    Color.FromRgb(0x0C, 0x8E, 0x7E),
                    Assert.IsType<SolidColorBrush>(window.ActionIcon10.IconBrush).Color);

                window.SetVoiceRecordingVisual(recording: false);

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
                Assert.Equal(96, window.AgentKey0.ActualWidth, 3);
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
                var statusCapWash = Assert.IsType<Border>(
                    window.AgentKey0.Template.FindName(
                        "StatusCapWash",
                        window.AgentKey0));
                var agentWell = Assert.IsType<Ellipse>(
                    window.AgentKey0.Template.FindName(
                        "AgentWell",
                        window.AgentKey0));
                var statusWellWash = Assert.IsType<Ellipse>(
                    window.AgentKey0.Template.FindName(
                        "StatusWellWash",
                        window.AgentKey0));
                var agentWideGlow = Assert.IsType<Border>(
                    window.AgentKey0.Template.FindName(
                        "GlowWide",
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
                Assert.Equal(96, agentCap.Width, 3);
                Assert.Equal(96, agentCap.Height, 3);

                // Paper agent keys use separate light carriers for the outer
                // bloom, full cap, circular field, and flat well wash. There is
                // no extra stroked status ring — it must stay removed.
                Assert.Null(window.AgentKey0.Template.FindName(
                    "StatusLightRing",
                    window.AgentKey0));

                // The light field is slightly larger than the neutral well so the
                // state color reads as a soft halo instead of a flat fill.
                Assert.Equal(82, statusLightField.Width, 3);
                Assert.Equal(76, agentWell.Width, 3);
                Assert.Equal(76, agentWellHighlight.Width, 3);
                Assert.True(statusLightField.Width > agentWell.Width);
                Assert.Equal(2, agentGlyph.Children.Count);
                Assert.Equal(18, agentGlyph.Width, 3);

                var backgroundAppearance = CreateLightingAppearance(
                    slotId: 0,
                    color: 0x304FFE);
                MicroSurfaceWindow.ApplyAgentLightingAppearance(
                    window.AgentKey0,
                    backgroundAppearance);
                var activeLight = Assert.IsType<SolidColorBrush>(
                    window.AgentKey0.BorderBrush);
                Assert.False(activeLight.HasAnimatedProperties);
                Assert.Same(activeLight, statusLightField.Fill);
                Assert.Same(activeLight, agentWideGlow.Background);
                Assert.Same(activeLight, agentGlow.Background);
                Assert.Same(activeLight, statusCapWash.Background);
                Assert.Same(activeLight, statusWellWash.Fill);

                // The center uses the exact same flat brush as the surrounding
                // keycap. A single solid ring is the only recessed cue.
                var agentCapFill = Assert.IsType<SolidColorBrush>(agentCap.Background);
                Assert.Equal(
                    Color.FromRgb(0xF7, 0xF8, 0xF6),
                    agentCapFill.Color);
                Assert.Same(agentCap.Background, agentWell.Fill);
                AssertRecessedRingBrush(agentWellHighlight.Stroke);
                Assert.Equal(1.6, agentWellHighlight.StrokeThickness, 3);
                Assert.Equal(0.30, agentWideGlow.Opacity, 3);
                Assert.Equal(0.14, agentGlow.Opacity, 3);
                Assert.Equal(0.07, statusCapWash.Opacity, 3);
                Assert.Equal(0.34, statusLightField.Opacity, 3);
                Assert.Equal(0.42, statusWellWash.Opacity, 3);
                Assert.Equal(
                    28,
                    Assert.IsType<BlurEffect>(agentWideGlow.Effect).Radius,
                    3);
                Assert.Equal(
                    16,
                    Assert.IsType<BlurEffect>(agentGlow.Effect).Radius,
                    3);
                Assert.Equal(
                    6.5,
                    Assert.IsType<BlurEffect>(statusCapWash.Effect).Radius,
                    3);

                // In the real surface, outer light belongs to a shared layer
                // below all keycaps. Template bloom is retained only so the
                // reusable key style still renders correctly in isolation.
                window.ApplyAgentLightingAppearance(0, backgroundAppearance);
                Assert.Equal(0, agentWideGlow.Opacity, 3);
                Assert.Equal(0, agentGlow.Opacity, 3);
                Assert.Same(window.AgentKey0.BorderBrush, window.AgentGlowWide0.Background);
                Assert.Same(window.AgentKey0.BorderBrush, window.AgentGlowNear0.Background);
                Assert.Equal(0.30, window.AgentGlowWide0.Opacity, 3);
                Assert.Equal(0.14, window.AgentGlowNear0.Opacity, 3);
                Assert.Equal(
                    28,
                    Assert.IsType<BlurEffect>(window.AgentGlowWide0.Effect).Radius,
                    3);
                Assert.Equal(
                    16,
                    Assert.IsType<BlurEffect>(window.AgentGlowNear0.Effect).Radius,
                    3);
                Assert.Equal(
                    5.5,
                    Assert.IsType<BlurEffect>(statusLightField.Effect).Radius,
                    3);
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
                Assert.Same(commandCap.Background, commandWell.Background);
                Assert.Equal(
                    Color.FromRgb(0xF7, 0xF8, 0xF6),
                    Assert.IsType<SolidColorBrush>(commandWell.Background).Color);
                AssertRecessedRingBrush(commandWell.BorderBrush);
                Assert.Equal(1.6, commandWell.BorderThickness.Left, 3);
                Assert.Null(window.ActionKey06.Template.FindName(
                    "KeyWellDark",
                    window.ActionKey06));
                Assert.Null(window.ActionKey06.Template.FindName(
                    "KeyWellHighlight",
                    window.ActionKey06));
                Assert.Equal(new CornerRadius(14), commandCap.CornerRadius);
                Assert.Equal(28, window.ActionIcon06.Width, 3);

                window.ActionKey10.ApplyTemplate();
                var voiceWell = Assert.IsType<Border>(
                    window.ActionKey10.Template.FindName(
                        "KeyWell",
                        window.ActionKey10));
                Assert.Equal(160, voiceWell.Width, 3);
                Assert.Equal(28, window.ActionIcon10.Width, 3);
                Assert.Same(window.ActionIcon10, window.ActionKey10.Content);
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
                window.ApplyAgentLightingAppearance(
                    0,
                    CreateLightingAppearance(0, 0x304FFE));
                window.ApplyAgentLightingAppearance(
                    1,
                    CreateLightingAppearance(1, 0x00FF4C));
                window.ApplyAgentLightingAppearance(
                    2,
                    CreateLightingAppearance(2, 0xFFFFFF));
                window.ApplyAgentLightingAppearance(
                    3,
                    CreateLightingAppearance(3, 0xFF6D00));
                window.ApplyAgentLightingAppearance(
                    4,
                    CreateLightingAppearance(
                        4,
                        0xFF0033,
                        isCurrentSession: true,
                        effect: 4));
                window.ApplyAgentLightingAppearance(
                    5,
                    AgentLightingAppearance.From(null));
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
                    backgroundAppearance,
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
                // A background session concentrates its light in the flat
                // circular well while keeping a quieter wash on the cap.
                var inactiveKeyPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    AgentLightingAppearance.From(null),
                    out _);

                const int wellSampleX = 65; // left of the centered plus glyph
                const int wellSampleY = 83;
                var activeWellBlue = BlueEmphasisAt(
                    activeKeyPixels,
                    width: IsolatedAgentRenderSize,
                    x: wellSampleX,
                    y: wellSampleY);
                var inactiveWellBlue = BlueEmphasisAt(
                    inactiveKeyPixels,
                    width: IsolatedAgentRenderSize,
                    x: wellSampleX,
                    y: wellSampleY);
                Assert.InRange(inactiveWellBlue, -10, 10);
                Assert.True(
                    activeWellBlue > inactiveWellBlue + 25,
                    $"Background-session well {activeWellBlue} not clearly " +
                    $"bluer than inactive {inactiveWellBlue}.");

                const int glowSampleX = 24;
                const int glowSampleY = 83;
                var activeGlowBlue = BlueEmphasisAt(
                    activeKeyPixels,
                    width: IsolatedAgentRenderSize,
                    x: glowSampleX,
                    y: glowSampleY);
                var inactiveGlowBlue = BlueEmphasisAt(
                    inactiveKeyPixels,
                    width: IsolatedAgentRenderSize,
                    x: glowSampleX,
                    y: glowSampleY);
                Assert.True(
                    activeGlowBlue > inactiveGlowBlue + 12,
                    $"Active perimeter glow {activeGlowBlue} not clearly bluer " +
                    $"than inactive {inactiveGlowBlue}.");

                var currentKeyPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    CreateLightingAppearance(
                        0,
                        0xFF0033,
                        isCurrentSession: true,
                        effect: 4),
                    out var currentKeyBitmap);
                var currentKeyPreviewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_CURRENT_KEY_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(currentKeyPreviewPath))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(currentKeyBitmap));
                    using var stream = new FileStream(
                        currentKeyPreviewPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
                    encoder.Save(stream);
                }

                const int capSampleX = 50;
                const int capSampleY = 50;
                var currentCapRed = RedEmphasisAt(
                    currentKeyPixels,
                    width: IsolatedAgentRenderSize,
                    x: capSampleX,
                    y: capSampleY);
                var inactiveCapRed = RedEmphasisAt(
                    inactiveKeyPixels,
                    width: IsolatedAgentRenderSize,
                    x: capSampleX,
                    y: capSampleY);
                Assert.True(
                    currentCapRed > inactiveCapRed + 20,
                    $"Current-session cap {currentCapRed} not clearly redder " +
                    $"than inactive {inactiveCapRed}.");

                var panelBrush = new SolidColorBrush(
                    Color.FromRgb(0xD7, 0xDC, 0xDA));
                var fallbackPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    AgentLightingAppearance.From(
                        lighting: null,
                        isCurrentSession: true),
                    out var fallbackBitmap,
                    panelBrush);
                var inactivePanelPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    AgentLightingAppearance.From(null),
                    out _,
                    panelBrush);
                var fallbackPreviewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_FALLBACK_KEY_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(fallbackPreviewPath))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(fallbackBitmap));
                    using var stream = new FileStream(
                        fallbackPreviewPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
                    encoder.Save(stream);
                }

                var fallbackGlowLuminance = LuminanceAt(
                    fallbackPixels,
                    width: IsolatedAgentRenderSize,
                    x: glowSampleX,
                    y: glowSampleY);
                var inactivePanelLuminance = LuminanceAt(
                    inactivePanelPixels,
                    width: IsolatedAgentRenderSize,
                    x: glowSampleX,
                    y: glowSampleY);
                Assert.True(
                    fallbackGlowLuminance > inactivePanelLuminance + 3,
                    $"White fallback glow {fallbackGlowLuminance} not visibly " +
                    $"brighter than inactive {inactivePanelLuminance}.");

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

    private static double RedEmphasisAt(
        byte[] pixels,
        int width,
        int x,
        int y)
    {
        var offset = ((y * width) + x) * 4;
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        return red - ((green + blue) / 2.0);
    }

    private static double LuminanceAt(
        byte[] pixels,
        int width,
        int x,
        int y)
    {
        var offset = ((y * width) + x) * 4;
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static void AssertRecessedRingBrush(Brush brush)
    {
        var ring = Assert.IsType<LinearGradientBrush>(brush);
        Assert.Equal(new Point(0, 0), ring.StartPoint);
        Assert.Equal(new Point(1, 1), ring.EndPoint);
        Assert.Collection(
            ring.GradientStops.OrderBy(stop => stop.Offset),
            stop =>
            {
                Assert.Equal(0, stop.Offset, 3);
                Assert.Equal(Color.FromArgb(0x28, 0x74, 0x7B, 0x77), stop.Color);
            },
            stop =>
            {
                Assert.Equal(0.42, stop.Offset, 3);
                Assert.Equal(Color.FromArgb(0x14, 0x74, 0x7B, 0x77), stop.Color);
            },
            stop =>
            {
                Assert.Equal(0.58, stop.Offset, 3);
                Assert.Equal(Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF), stop.Color);
            },
            stop =>
            {
                Assert.Equal(1, stop.Offset, 3);
                Assert.Equal(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF), stop.Color);
            });
    }

    private static byte[] RenderIsolatedAgentKey(
        Style style,
        AgentLightingAppearance appearance,
        out RenderTargetBitmap bitmap,
        Brush? stageBackground = null)
    {
        var stage = new Grid
        {
            Width = IsolatedAgentRenderSize,
            Height = IsolatedAgentRenderSize,
            Background = stageBackground ?? Brushes.White,
        };
        var key = new Button
        {
            Width = IsolatedAgentRenderSize,
            Height = IsolatedAgentRenderSize,
            Style = style,
        };
        stage.Children.Add(key);
        stage.Measure(new Size(IsolatedAgentRenderSize, IsolatedAgentRenderSize));
        stage.Arrange(new Rect(
            0,
            0,
            IsolatedAgentRenderSize,
            IsolatedAgentRenderSize));
        MicroSurfaceWindow.ApplyAgentLightingAppearance(key, appearance);
        stage.UpdateLayout();
        bitmap = new RenderTargetBitmap(
            IsolatedAgentRenderSize,
            IsolatedAgentRenderSize,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(stage);
        var pixels = new byte[
            IsolatedAgentRenderSize * IsolatedAgentRenderSize * 4];
        bitmap.CopyPixels(pixels, IsolatedAgentRenderSize * 4, 0);
        return pixels;
    }

    private static AgentLightingAppearance CreateLightingAppearance(
        int slotId,
        int color,
        bool isCurrentSession = false,
        int effect = 1) =>
        AgentLightingAppearance.From(
            new SlotLighting(
                slotId,
                color,
                1,
                effect,
                effect == 4 ? 0.4 : 0,
                false,
                false,
                false),
            isCurrentSession);

    private static void AssertSquare(FrameworkElement element) =>
        Assert.Equal(element.ActualWidth, element.ActualHeight, 3);
}
