using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using CodexMicro.Desktop;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;

namespace CodexMicro.AssetExporter;

internal static class Program
{
    private const int SurfaceWidth = 590;
    private const int SurfaceHeight = 610;

    private static readonly Palette[] Palettes =
    [
        new(
            "pearl",
            "原白",
            Color.FromRgb(0x30, 0x4F, 0xFE),
            Color.FromRgb(0x00, 0xFF, 0x4C),
            Color.FromRgb(0x98, 0xE8, 0xD5),
            0),
        new(
            "mint",
            "水绿色",
            Color.FromRgb(0x28, 0xB9, 0x86),
            Color.FromRgb(0x75, 0xE5, 0xBA),
            Color.FromRgb(0x72, 0xD8, 0xB0),
            0.22),
        new(
            "blue",
            "蓝色",
            Color.FromRgb(0x4D, 0x6F, 0xE8),
            Color.FromRgb(0x87, 0xA4, 0xFF),
            Color.FromRgb(0x78, 0xA6, 0xFF),
            0.20),
        new(
            "violet",
            "紫色",
            Color.FromRgb(0x7B, 0x5D, 0xCC),
            Color.FromRgb(0xAF, 0x91, 0xF0),
            Color.FromRgb(0xA3, 0x86, 0xE8),
            0.20),
        new(
            "amber",
            "琥珀色",
            Color.FromRgb(0xD0, 0x82, 0x2E),
            Color.FromRgb(0xF2, 0xBC, 0x68),
            Color.FromRgb(0xE7, 0xB0, 0x63),
            0.17),
        new(
            "rose",
            "玫红色",
            Color.FromRgb(0xC8, 0x58, 0x78),
            Color.FromRgb(0xEC, 0x93, 0xAC),
            Color.FromRgb(0xE1, 0x86, 0xA0),
            0.17),
    ];

    private static readonly AgentState[] SixColorAgents =
    [
        new("thinking", "运行中", Color.FromRgb(0x30, 0x4F, 0xFE), false, 1),
        new("completed", "已完成", Color.FromRgb(0x00, 0xFF, 0x4C), false, 1),
        new("idle", "空闲", Colors.White, false, 1),
        new("waiting", "等待输入", Color.FromRgb(0xFF, 0x6D, 0x00), false, 1),
        new("error", "错误", Color.FromRgb(0xFF, 0x00, 0x33), true, 4),
        new("off", "未分配", null, false, 0),
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        var outputDirectory = ResolveOutputDirectory(args);
        Directory.CreateDirectory(outputDirectory);

        _ = Application.Current ?? new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };

        var profile = MicroProfileSettings.CreateTransient();
        profile.SetActiveHarness("codex");
        var window = new MicroSurfaceWindow(
            new MicroLocalization(MicroLanguage.ZhCn),
            profileSettings: profile);

        PrepareSurface(window);
        foreach (var palette in Palettes)
        {
            ApplyPalette(window, palette);
            window.DesignSurface.UpdateLayout();
            var fileName = $"codex-micro-keypad-{palette.Id}.png";
            var filePath = Path.Combine(outputDirectory, fileName);
            SavePng(window.DesignSurface, filePath);
            Console.WriteLine(filePath);
        }

        ApplySixColorComposition(window);
        window.DesignSurface.UpdateLayout();
        var sixColorFilePath = Path.Combine(
            outputDirectory,
            "codex-micro-keypad-six-color.png");
        SavePng(window.DesignSurface, sixColorFilePath);
        Console.WriteLine(sixColorFilePath);

        SaveManifest(outputDirectory);
        window.CloseForApplicationExit();
        return 0;
    }

    private static string ResolveOutputDirectory(string[] args)
    {
        if (args.Length > 1)
        {
            throw new ArgumentException(
                "Usage: CodexMicro.AssetExporter [output-directory]");
        }

        return Path.GetFullPath(
            args.Length == 1
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory, "exports"));
    }

    private static void PrepareSurface(MicroSurfaceWindow window)
    {
        window.DesignSurface.Measure(new Size(SurfaceWidth, SurfaceHeight));
        window.DesignSurface.Arrange(
            new Rect(0, 0, SurfaceWidth, SurfaceHeight));
        window.DesignSurface.UpdateLayout();
    }

    private static void ApplyPalette(
        MicroSurfaceWindow window,
        Palette palette)
    {
        window.HarnessThemeWash.Background = CreateThemeWash(palette.Wash);
        window.HarnessThemeWash.Opacity = palette.WashOpacity;
        window.CrystalLowerRefraction.Background = CreateEdgeBrush(
            palette.Wash);

        var mutedAccent = Color.FromArgb(
            0x80,
            palette.Accent.R,
            palette.Accent.G,
            palette.Accent.B);
        var accentBrush = new SolidColorBrush(mutedAccent);
        window.LeftSilkScreen.Foreground = accentBrush;
        window.RightSilkScreen.Foreground = accentBrush;
        window.BrandWordmarkText.Foreground = accentBrush;
        window.BrandCodexIcon.IconBrush = accentBrush;

        ApplyAgentLight(
            window,
            slotId: 0,
            palette.Accent,
            isCurrentSession: true,
            effect: 4);
        ApplyAgentLight(
            window,
            slotId: 1,
            palette.Secondary,
            isCurrentSession: false,
            effect: 1);
        for (var slotId = 2; slotId < 6; slotId++)
        {
            window.ApplyAgentLightingAppearance(
                slotId,
                AgentLightingAppearance.From(null));
        }
    }

    private static void ApplyAgentLight(
        MicroSurfaceWindow window,
        int slotId,
        Color color,
        bool isCurrentSession,
        int effect)
    {
        var rgb = (color.R << 16) | (color.G << 8) | color.B;
        var lighting = new SlotLighting(
            slotId,
            rgb,
            Brightness: 1,
            Effect: effect,
            Speed: effect == 4 ? 0.4 : 0,
            SyncKeysLighting: false,
            SyncAmbientLighting: false,
            LightingAmbiguous: false);
        window.ApplyAgentLightingAppearance(
            slotId,
            AgentLightingAppearance.From(lighting, isCurrentSession));
    }

    private static void ApplySixColorComposition(MicroSurfaceWindow window)
    {
        var neutralAccent = Color.FromRgb(0x98, 0xE8, 0xD5);
        window.HarnessThemeWash.Opacity = 0;
        window.CrystalLowerRefraction.Background = CreateEdgeBrush(
            neutralAccent);

        var neutralInk = new SolidColorBrush(
            Color.FromArgb(0x80, 0x60, 0x6A, 0x70));
        window.LeftSilkScreen.Foreground = neutralInk;
        window.RightSilkScreen.Foreground = neutralInk;
        window.BrandWordmarkText.Foreground = neutralInk;
        window.BrandCodexIcon.IconBrush = neutralInk;

        for (var slotId = 0; slotId < SixColorAgents.Length; slotId++)
        {
            var state = SixColorAgents[slotId];
            if (state.Color is { } color)
            {
                ApplyAgentLight(
                    window,
                    slotId,
                    color,
                    state.IsCurrentSession,
                    state.Effect);
            }
            else
            {
                window.ApplyAgentLightingAppearance(
                    slotId,
                    AgentLightingAppearance.From(null));
            }
        }

        ApplyReadyQuotaPresentation(window);
    }

    private static void ApplyReadyQuotaPresentation(MicroSurfaceWindow window)
    {
        var quotaAccent = Color.FromRgb(0xA8, 0xC7, 0xFF);
        window.QuotaCaptionText.Visibility = Visibility.Visible;
        window.QuotaCaptionText.Text = "SOL";
        window.QuotaValueText.Text = "100%";
        window.QuotaValueText.FontSize = 13.5;
        window.QuotaGauge.Opacity = 1;
        window.QuotaProgressRing.Data =
            MicroSurfaceWindow.CreateQuotaArcGeometry(100);
        window.QuotaProgressRing.Stroke = new SolidColorBrush(quotaAccent);

        var readyColor = Color.FromRgb(0x9E, 0xBD, 0xFF);
        foreach (var led in new[]
                 {
                     window.RuntimeLed,
                     window.DriverLed,
                     window.ActivityLed,
                 })
        {
            led.Fill = new SolidColorBrush(readyColor);
            led.Effect = new DropShadowEffect
            {
                Color = readyColor,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.78,
            };
        }
    }

    private static RadialGradientBrush CreateThemeWash(Color color)
    {
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.52, 0.44),
            GradientOrigin = new Point(0.52, 0.44),
            RadiusX = 0.78,
            RadiusY = 0.72,
        };
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0xA0, color.R, color.G, color.B),
            0));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x48, color.R, color.G, color.B),
            0.62));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x00, color.R, color.G, color.B),
            1));
        return brush;
    }

    private static LinearGradientBrush CreateEdgeBrush(Color color)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
        };
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x00, color.R, color.G, color.B),
            0));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0xA5, color.R, color.G, color.B),
            0.48));
        brush.GradientStops.Add(new GradientStop(
            Color.FromArgb(0x00, color.R, color.G, color.B),
            1));
        return brush;
    }

    private static void SavePng(Visual visual, string filePath)
    {
        var bitmap = new RenderTargetBitmap(
            SurfaceWidth,
            SurfaceHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        encoder.Save(stream);
    }

    private static void SaveManifest(string outputDirectory)
    {
        var manifest = new
        {
            generatedFrom = "virtual-micro/src/CodexMicro.Desktop/MainWindow.xaml",
            background = "transparent",
            width = SurfaceWidth,
            height = SurfaceHeight,
            format = "png",
            composition = new
            {
                id = "six-color",
                displayName = "六色同屏",
                file = "codex-micro-keypad-six-color.png",
                agentKeys = SixColorAgents.Select((agent, slotId) => new
                {
                    slotId,
                    agent.Id,
                    agent.DisplayName,
                    color = agent.Color is { } color ? ToHex(color) : "off",
                }),
                quota = "100%",
                model = "SOL",
            },
            palettes = Palettes.Select(palette => new
            {
                id = palette.Id,
                displayName = palette.DisplayName,
                accent = ToHex(palette.Accent),
                secondary = ToHex(palette.Secondary),
                file = $"codex-micro-keypad-{palette.Id}.png",
            }),
        };
        var json = JsonSerializer.Serialize(
            manifest,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(
            Path.Combine(outputDirectory, "manifest.json"),
            json + Environment.NewLine);
    }

    private static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private sealed record Palette(
        string Id,
        string DisplayName,
        Color Accent,
        Color Secondary,
        Color Wash,
        double WashOpacity);

    private sealed record AgentState(
        string Id,
        string DisplayName,
        Color? Color,
        bool IsCurrentSession,
        int Effect);
}
