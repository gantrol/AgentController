using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Desktop.Tests;

[Collection(WpfUiCollection.Name)]
public sealed class AgentLightingVisualTests
{
    private const int IsolatedRenderSize = 166;
    private static readonly Color InactiveCarrierColor =
        Color.FromRgb(0x8D, 0xB5, 0xFF);

    private sealed record LightingCase(
        string State,
        string Variant,
        Color ExpectedColor,
        bool ExpectedActive,
        AgentLightingAppearance Appearance);

    private sealed record HarnessLedCase(
        string Name,
        bool Running,
        bool VoiceReady,
        string Browser,
        Color Runtime,
        Color Driver,
        Color Activity);

    private sealed record RenderedKey(
        SolidColorBrush Carrier,
        Color WellSample,
        RenderTargetBitmap Bitmap);

    [Fact]
    public void RealXamlAgentKeysMatchTheScriptedColorMatrix()
    {
        RunOnStaThread(() =>
        {
            var window = new MicroSurfaceWindow(
                new MicroLocalization(MicroLanguage.ZhCn));
            var cases = CreateLightingCases();

            Assert.Equal(17, cases.Count);
            foreach (var lightingCase in cases)
            {
                var rendered = RenderAgentKey(
                    window.AgentKey0.Style,
                    lightingCase.Appearance);

                Assert.Equal(
                    lightingCase.ExpectedActive,
                    lightingCase.Appearance.IsActive);
                Assert.Equal(lightingCase.ExpectedColor, rendered.Carrier.Color);
                Assert.Equal(
                    lightingCase.Appearance.DisplayOpacity,
                    rendered.Carrier.Opacity,
                    3);

                if (!lightingCase.ExpectedActive)
                {
                    Assert.Equal(0, rendered.Carrier.Opacity, 3);
                    continue;
                }

                Assert.True(
                    rendered.Carrier.Opacity > 0,
                    $"{lightingCase.State} must have a visible carrier.");
                AssertRenderedHue(lightingCase, rendered.WellSample);
            }

            var matrix = RenderLightingMatrix(window.AgentKey0.Style, cases);
            SavePreviewFromEnvironment(
                matrix,
                "CODEX_MICRO_SCRIPTED_LIGHTING_MATRIX_PREVIEW_PATH");
            window.CloseForApplicationExit();
        });
    }

    [Fact]
    public void HarnessStatusLedsKeepVoiceReadinessIndependentFromTaskState()
    {
        RunOnStaThread(() =>
        {
            var profile = MicroProfileSettings.CreateTransient();
            profile.SetActiveHarness("deepseek-harness");
            var window = new MicroSurfaceWindow(
                new MicroLocalization(MicroLanguage.ZhCn),
                profileSettings: profile);
            window.DesignSurface.Measure(new Size(590, 610));
            window.DesignSurface.Arrange(new Rect(0, 0, 590, 610));
            window.DesignSurface.UpdateLayout();

            // With no adapter snapshot or configured voice service, all three
            // component signals are neutral.
            AssertLedColors(
                window,
                ColorFromRgb(0xB8B98B),
                ColorFromRgb(0xB8B98B),
                ColorFromRgb(0xB8B98B));

            var cases = new HarnessLedCase[]
            {
                new(
                    "connected / idle",
                    Running: false,
                    VoiceReady: false,
                    Browser: "connected",
                    Runtime: ColorFromRgb(0x78A6FF),
                    Driver: ColorFromRgb(0x78A6FF),
                    Activity: ColorFromRgb(0xB8B98B)),
                new(
                    "browser waiting / voice ready",
                    Running: false,
                    VoiceReady: true,
                    Browser: "starting",
                    Runtime: ColorFromRgb(0x78A6FF),
                    Driver: ColorFromRgb(0xFFC85A),
                    Activity: ColorFromRgb(0x304FFE)),
                new(
                    "connected / running / voice unavailable",
                    Running: true,
                    VoiceReady: false,
                    Browser: "connected",
                    Runtime: ColorFromRgb(0x78A6FF),
                    Driver: ColorFromRgb(0x78A6FF),
                    Activity: ColorFromRgb(0xB8B98B)),
                new(
                    "browser waiting / running / voice ready",
                    Running: true,
                    VoiceReady: true,
                    Browser: "starting",
                    Runtime: ColorFromRgb(0x78A6FF),
                    Driver: ColorFromRgb(0xFFC85A),
                    Activity: ColorFromRgb(0x304FFE)),
            };

            foreach (var ledCase in cases)
            {
                window.ApplyVoiceServiceStateForVisualTest(
                    ledCase.VoiceReady);
                window.ApplyHarnessStateForVisualTest(CreateHarnessSnapshot(
                    ledCase.Running,
                    ledCase.Browser));
                window.DesignSurface.UpdateLayout();
                AssertLedColors(
                    window,
                    ledCase.Runtime,
                    ledCase.Driver,
                    ledCase.Activity);

                if (ledCase.Name == "browser waiting / voice ready")
                {
                    SavePreviewFromEnvironment(
                        RenderSurface(window),
                        "CODEX_MICRO_DEEPSEEK_IDLE_PREVIEW_PATH");
                }
            }

            window.CloseForApplicationExit();
        });
    }

    private static IReadOnlyList<LightingCase> CreateLightingCases() =>
    [
        ProtocolCase("THINK", "Codex · background", 0x304FFE),
        ProtocolCase(
            "THINK",
            "Codex · current",
            0x304FFE,
            isCurrentSession: true,
            effect: 4),
        HarnessCase("RUNNING", "Harness · background", isCurrent: false),
        HarnessCase("RUNNING", "Harness · current", isCurrent: true),
        ProtocolCase("COMPLETED", "Codex · background", 0x00FF4C),
        ProtocolCase(
            "COMPLETED",
            "Codex · current",
            0x00FF4C,
            isCurrentSession: true,
            effect: 4),
        ProtocolCase("IDLE", "Codex · protocol", 0xFFFFFF),
        new(
            "CURRENT",
            "Codex · no status",
            Colors.White,
            ExpectedActive: true,
            AgentLightingAppearance.From(
                lighting: null,
                isCurrentSession: true)),
        ProtocolCase("WAITING", "Codex · background", 0xFF6D00),
        ProtocolCase(
            "WAITING",
            "Codex · current",
            0xFF6D00,
            isCurrentSession: true,
            effect: 4),
        ProtocolCase("ERROR", "Codex · background", 0xFF0033),
        ProtocolCase(
            "ERROR",
            "Codex · current",
            0xFF0033,
            isCurrentSession: true,
            effect: 4),
        ProtocolCase("CUSTOM", "Protocol · purple", 0x7C3AED),
        ProtocolCase("CUSTOM", "Protocol · cyan", 0x00B8D4),
        new(
            "IDLE",
            "Harness · background",
            InactiveCarrierColor,
            ExpectedActive: false,
            AgentLightingAppearance.FromHarnessSession(
                isRunning: false,
                isCurrentSession: false)),
        new(
            "IDLE",
            "Harness · current",
            InactiveCarrierColor,
            ExpectedActive: false,
            AgentLightingAppearance.FromHarnessSession(
                isRunning: false,
                isCurrentSession: true)),
        new(
            "OFF",
            "Unassigned",
            InactiveCarrierColor,
            ExpectedActive: false,
            AgentLightingAppearance.From(null)),
    ];

    private static LightingCase ProtocolCase(
        string state,
        string variant,
        int rgb,
        bool isCurrentSession = false,
        int effect = 1) =>
        new(
            state,
            variant,
            ColorFromRgb(rgb),
            ExpectedActive: true,
            AgentLightingAppearance.From(
                new SlotLighting(
                    SlotId: 0,
                    Color: rgb,
                    Brightness: 1,
                    Effect: effect,
                    Speed: effect == 4 ? 0.4 : 0,
                    SyncKeysLighting: false,
                    SyncAmbientLighting: false,
                    LightingAmbiguous: state == "IDLE"),
                isCurrentSession));

    private static LightingCase HarnessCase(
        string state,
        string variant,
        bool isCurrent) =>
        new(
            state,
            variant,
            ColorFromRgb(0x304FFE),
            ExpectedActive: true,
            AgentLightingAppearance.FromHarnessSession(
                isRunning: true,
                isCurrentSession: isCurrent));

    private static MicroHarnessStateSnapshot CreateHarnessSnapshot(
        bool running,
        string browser) =>
        new(
            "deepseek-harness",
            [
                new(
                    "scripted-session",
                    running ? "Running task" : "Idle task",
                    running,
                    DateTimeOffset.Now.ToUnixTimeMilliseconds()),
            ],
            "scripted-session",
            new(
                SessionList: true,
                SessionActivation: true,
                KnobSettings: true,
                VoiceInput: true,
                Actions: new HashSet<string>()),
            NavigationDepth: 0,
            new(
                Adapter: "ready",
                Browser: browser),
            DateTimeOffset.Now);

    private static RenderedKey RenderAgentKey(
        Style style,
        AgentLightingAppearance appearance)
    {
        var stage = new Grid
        {
            Width = IsolatedRenderSize,
            Height = IsolatedRenderSize,
            Background = new SolidColorBrush(ColorFromRgb(0xEEF2F7)),
        };
        var key = CreateAgentKey(style, appearance, IsolatedRenderSize);
        stage.Children.Add(key);
        stage.Measure(new Size(IsolatedRenderSize, IsolatedRenderSize));
        stage.Arrange(new Rect(0, 0, IsolatedRenderSize, IsolatedRenderSize));
        stage.UpdateLayout();

        AssertTemplateCarrierOpacity(
            key,
            "GlowWide",
            appearance.WideGlowOpacity);
        AssertTemplateCarrierOpacity(
            key,
            "Glow",
            appearance.OuterGlowOpacity);
        AssertTemplateCarrierOpacity(
            key,
            "StatusCapWash",
            appearance.CapWashOpacity);
        AssertTemplateCarrierOpacity(
            key,
            "StatusLightField",
            appearance.LightFieldOpacity);
        AssertTemplateCarrierOpacity(
            key,
            "StatusWellWash",
            appearance.WellWashOpacity);

        var bitmap = RenderElement(stage, IsolatedRenderSize, IsolatedRenderSize);
        var pixels = CopyPixels(bitmap, IsolatedRenderSize, IsolatedRenderSize);
        return new RenderedKey(
            Assert.IsType<SolidColorBrush>(key.BorderBrush),
            AveragePatch(
                pixels,
                IsolatedRenderSize,
                x: 65,
                y: 83,
                radius: 4),
            bitmap);
    }

    private static Button CreateAgentKey(
        Style style,
        AgentLightingAppearance appearance,
        double size)
    {
        var key = new Button
        {
            Width = size,
            Height = size,
            Style = style,
            IsHitTestVisible = false,
        };
        MicroSurfaceWindow.ApplyAgentLightingAppearance(key, appearance);
        return key;
    }

    private static RenderTargetBitmap RenderLightingMatrix(
        Style style,
        IReadOnlyList<LightingCase> cases)
    {
        const int width = 1200;
        const int height = 780;
        var root = new Grid
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(ColorFromRgb(0xF3F6FA)),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(72) });
        root.RowDefinitions.Add(new RowDefinition());

        var header = new StackPanel
        {
            Margin = new Thickness(22, 12, 22, 4),
        };
        header.Children.Add(new TextBlock
        {
            Text = "Codex Micro · scripted real-XAML lighting matrix",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ColorFromRgb(0x182033)),
        });
        header.Children.Add(new TextBlock
        {
            Text = "Expected: THINK/RUN blue · COMPLETED green · IDLE white · WAIT amber · ERROR red · OFF transparent",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = new SolidColorBrush(ColorFromRgb(0x657087)),
        });
        root.Children.Add(header);

        var matrix = new UniformGrid
        {
            Columns = 6,
            Rows = 3,
            Margin = new Thickness(12, 0, 12, 12),
        };
        Grid.SetRow(matrix, 1);
        foreach (var lightingCase in cases)
        {
            var card = new Border
            {
                Margin = new Thickness(5),
                Padding = new Thickness(5, 1, 5, 8),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(ColorFromRgb(0xDCE3ED)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
            };
            var content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            content.Children.Add(CreateAgentKey(style, lightingCase.Appearance, 152));
            content.Children.Add(new TextBlock
            {
                Text = $"{lightingCase.State} · {lightingCase.Variant}",
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ColorFromRgb(0x20283A)),
            });
            content.Children.Add(new TextBlock
            {
                Text = lightingCase.ExpectedActive
                    ? ToHex(lightingCase.ExpectedColor)
                    : "OFF · opacity 0",
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                Foreground = LightingLabelBrush(lightingCase),
            });
            card.Child = content;
            matrix.Children.Add(card);
        }

        root.Children.Add(matrix);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        return RenderElement(root, width, height);
    }

    private static Brush LightingLabelBrush(LightingCase lightingCase)
    {
        if (!lightingCase.ExpectedActive || lightingCase.ExpectedColor == Colors.White)
        {
            return new SolidColorBrush(ColorFromRgb(0x6F788B));
        }

        return new SolidColorBrush(lightingCase.ExpectedColor);
    }

    private static void AssertRenderedHue(
        LightingCase lightingCase,
        Color sample)
    {
        if (lightingCase.ExpectedColor == Colors.White)
        {
            Assert.InRange(Saturation(sample), 0, 0.10);
            Assert.True(
                Luminance(sample) > 220,
                $"{lightingCase.State} white sample {ToHex(sample)} is too dark.");
            return;
        }

        var saturation = Saturation(sample);
        var expectedHue = Hue(lightingCase.ExpectedColor);
        var actualHue = Hue(sample);
        var distance = HueDistance(expectedHue, actualHue);
        Assert.True(
            saturation >= 0.08,
            $"{lightingCase.State} sample {ToHex(sample)} is not chromatic enough.");
        Assert.True(
            distance <= 35,
            $"{lightingCase.State} expected {ToHex(lightingCase.ExpectedColor)} " +
            $"(hue {expectedHue:F1}) but rendered {ToHex(sample)} " +
            $"(hue {actualHue:F1}, distance {distance:F1}).");
    }

    private static void AssertLedColors(
        MicroSurfaceWindow window,
        Color runtime,
        Color driver,
        Color activity)
    {
        Assert.Equal(runtime, FillColor(window.RuntimeLed));
        Assert.Equal(driver, FillColor(window.DriverLed));
        Assert.Equal(activity, FillColor(window.ActivityLed));
    }

    private static Color FillColor(Shape shape) =>
        Assert.IsType<SolidColorBrush>(shape.Fill).Color;

    private static void AssertTemplateCarrierOpacity(
        Button key,
        string partName,
        double expected)
    {
        var part = Assert.IsAssignableFrom<UIElement>(
            key.Template.FindName(partName, key));
        Assert.Equal(expected, part.Opacity, 3);
    }

    private static RenderTargetBitmap RenderSurface(MicroSurfaceWindow window)
    {
        window.DesignSurface.UpdateLayout();
        return RenderElement(window.DesignSurface, 590, 610);
    }

    private static RenderTargetBitmap RenderElement(
        Visual visual,
        int width,
        int height)
    {
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return bitmap;
    }

    private static byte[] CopyPixels(
        BitmapSource bitmap,
        int width,
        int height)
    {
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    private static Color AveragePatch(
        byte[] pixels,
        int width,
        int x,
        int y,
        int radius)
    {
        long red = 0;
        long green = 0;
        long blue = 0;
        var count = 0;
        for (var row = y - radius; row <= y + radius; row++)
        {
            for (var column = x - radius; column <= x + radius; column++)
            {
                var offset = ((row * width) + column) * 4;
                blue += pixels[offset];
                green += pixels[offset + 1];
                red += pixels[offset + 2];
                count++;
            }
        }

        return Color.FromRgb(
            (byte)(red / count),
            (byte)(green / count),
            (byte)(blue / count));
    }

    private static double Saturation(Color color)
    {
        var max = Math.Max(color.R, Math.Max(color.G, color.B));
        var min = Math.Min(color.R, Math.Min(color.G, color.B));
        return max == 0 ? 0 : (max - min) / (double)max;
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);

    private static double Hue(Color color)
    {
        var red = color.R / 255.0;
        var green = color.G / 255.0;
        var blue = color.B / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;
        if (delta == 0)
        {
            return 0;
        }

        var hue = max == red
            ? 60 * (((green - blue) / delta) % 6)
            : max == green
                ? 60 * (((blue - red) / delta) + 2)
                : 60 * (((red - green) / delta) + 4);
        return hue < 0 ? hue + 360 : hue;
    }

    private static double HueDistance(double first, double second)
    {
        var distance = Math.Abs(first - second);
        return Math.Min(distance, 360 - distance);
    }

    private static Color ColorFromRgb(int rgb) =>
        Color.FromRgb(
            (byte)(rgb >> 16),
            (byte)(rgb >> 8),
            (byte)rgb);

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void SavePreviewFromEnvironment(
        BitmapSource bitmap,
        string environmentVariable)
    {
        var path = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(stream);
    }

    private static void RunOnStaThread(Action action)
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
                action();
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
}
