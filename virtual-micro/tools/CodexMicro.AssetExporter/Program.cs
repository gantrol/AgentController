using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
        var lightingPagesOnly = args.Length > 0 && args[0] == "--lighting-pages";
        var outputDirectory = ResolveOutputDirectory(
            lightingPagesOnly ? args[1..] : args);
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
        if (lightingPagesOnly)
        {
            ExportLightingPages(window, outputDirectory);
            window.CloseForApplicationExit();
            return 0;
        }

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
                "Usage: CodexMicro.AssetExporter [--lighting-pages] [output-directory]");
        }

        return Path.GetFullPath(
            args.Length == 1
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory, "exports"));
    }

    private static void PrepareSurface(MicroSurfaceWindow window)
    {
        // The live window uses a 29%-opacity exterior shadow so it separates
        // from desktop content. Exported assets need a strictly transparent
        // exterior, so keep the transparent stage and omit that window-only
        // shadow before rendering.
        window.DesignSurface.Background = Brushes.Transparent;
        window.DeviceFrame.Effect = null;
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
        ApplyNeutralPalette(window);

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

    private static void ApplyNeutralPalette(MicroSurfaceWindow window)
    {
        window.HarnessThemeWash.Opacity = 0;
        window.CrystalLowerRefraction.Background = CreateEdgeBrush(
            Color.FromRgb(0x98, 0xE8, 0xD5));

        var neutralInk = new SolidColorBrush(
            Color.FromArgb(0x80, 0x60, 0x6A, 0x70));
        window.LeftSilkScreen.Foreground = neutralInk;
        window.RightSilkScreen.Foreground = neutralInk;
        window.BrandWordmarkText.Foreground = neutralInk;
        window.BrandCodexIcon.IconBrush = neutralInk;
    }

    private static void ApplyReadyQuotaPresentation(
        MicroSurfaceWindow window,
        int remainingPercent = 100)
    {
        var quotaAccent = Color.FromRgb(0xA8, 0xC7, 0xFF);
        window.QuotaCaptionText.Visibility = Visibility.Visible;
        window.QuotaCaptionText.Text = "SOL";
        window.QuotaValueText.Text = $"{remainingPercent}%";
        window.QuotaValueText.FontSize = remainingPercent == 100 ? 13.5 : 15;
        window.QuotaGauge.Opacity = 1;
        window.QuotaProgressRing.Data =
            MicroSurfaceWindow.CreateQuotaArcGeometry(remainingPercent);
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

    private static void ExportLightingPages(
        MicroSurfaceWindow window,
        string outputDirectory)
    {
        ApplyNeutralPalette(window);
        ApplyReadyQuotaPresentation(window, 77);

        var firstPage = new (MicroHarnessSessionStatus Status, bool Selected)[]
        {
            (MicroHarnessSessionStatus.Idle, true),
            (MicroHarnessSessionStatus.Running, false),
            (MicroHarnessSessionStatus.Completed, false),
            (MicroHarnessSessionStatus.WaitingForInput, false),
            (MicroHarnessSessionStatus.Error, false),
            (MicroHarnessSessionStatus.Idle, false),
        };
        for (var slotId = 0; slotId < firstPage.Length; slotId++)
        {
            var state = firstPage[slotId];
            window.ApplyAgentLightingAppearance(slotId,
                AgentLightingAppearance.FromCodexSession(
                    state.Status, state.Selected));
        }

        window.ControlPageButton.IsChecked = true;
        window.MonitorPageButton.IsChecked = false;
        window.ControlGrid.Visibility = Visibility.Visible;
        window.MonitorGrid.Visibility = Visibility.Collapsed;
        window.DesignSurface.UpdateLayout();
        SavePng(window.DesignSurface, Path.Combine(
            outputDirectory, "codex-micro-first-screen-xaml.png"));

        var secondPage = new (MicroHarnessSessionStatus? Status, bool Selected)[]
        {
            (MicroHarnessSessionStatus.Idle, true),
            (MicroHarnessSessionStatus.Running, false),
            (MicroHarnessSessionStatus.Completed, false),
            (MicroHarnessSessionStatus.WaitingForInput, false),
            (MicroHarnessSessionStatus.Error, false),
            (MicroHarnessSessionStatus.Idle, false),
            (MicroHarnessSessionStatus.Idle, false),
            (MicroHarnessSessionStatus.WaitingForInput, false),
            (MicroHarnessSessionStatus.Completed, false),
            (MicroHarnessSessionStatus.Running, false),
            (MicroHarnessSessionStatus.Completed, false),
            (MicroHarnessSessionStatus.Idle, false),
            (MicroHarnessSessionStatus.Running, false),
            (MicroHarnessSessionStatus.Idle, false),
        };
        var monitorKeys = window.MonitorGrid.Children.OfType<Button>().ToArray();
        var wideGlows = window.MonitorGrid.Children.OfType<Border>()
            .Where(border => Panel.GetZIndex(border) == -10).ToArray();
        var nearGlows = window.MonitorGrid.Children.OfType<Border>()
            .Where(border => Panel.GetZIndex(border) == -9).ToArray();
        if (monitorKeys.Length != secondPage.Length ||
            wideGlows.Length != secondPage.Length ||
            nearGlows.Length != secondPage.Length)
        {
            throw new InvalidOperationException("Monitor XAML layout has changed.");
        }

        for (var index = 0; index < secondPage.Length; index++)
        {
            var state = secondPage[index];
            var key = monitorKeys[index];
            key.IsEnabled = state.Status is not null;
            key.Opacity = state.Status is null ? 0.42 : 1;
            var appearance = state.Status is { } status
                ? AgentLightingAppearance.FromCodexSession(status, state.Selected)
                : AgentLightingAppearance.From(null);
            appearance = MicroSurfaceWindow.ApplyAgentLightingAppearance(key, appearance);
            if (key.Template.FindName("GlowWide", key) is FrameworkElement wide)
            {
                wide.Opacity = 0;
            }
            if (key.Template.FindName("Glow", key) is FrameworkElement near)
            {
                near.Opacity = 0;
            }
            MicroSurfaceWindow.ApplyAgentGlowAppearance(
                wideGlows[index], nearGlows[index], key.BorderBrush, appearance);
        }

        window.ControlPageButton.IsChecked = false;
        window.MonitorPageButton.IsChecked = true;
        window.ControlGrid.Visibility = Visibility.Collapsed;
        window.MonitorGrid.Visibility = Visibility.Visible;
        window.DesignSurface.UpdateLayout();
        SavePng(window.DesignSurface, Path.Combine(
            outputDirectory, "codex-micro-second-screen-xaml.png"));
    }

    private static void ExportSelectedLightingComparison(
        MicroSurfaceWindow window,
        string outputDirectory)
    {
        var statuses = new[]
        {
            MicroHarnessSessionStatus.Idle,
            MicroHarnessSessionStatus.Running,
            MicroHarnessSessionStatus.Completed,
            MicroHarnessSessionStatus.WaitingForInput,
            MicroHarnessSessionStatus.Error,
        };
        var surface = new Grid
        {
            Width = 700,
            Height = 160,
            Background = window.PearlLightGuide.Background,
        };
        for (var index = 0; index < statuses.Length; index++)
        {
            surface.ColumnDefinitions.Add(new ColumnDefinition());
            var wide = new Border { Style = (Style)window.FindResource("AgentWideHalo") };
            var near = new Border { Style = (Style)window.FindResource("AgentNearHalo") };
            var key = new Button
            {
                Width = 96,
                Height = 96,
                Style = (Style)window.FindResource("AgentKey"),
                Focusable = false,
            };
            foreach (var element in new FrameworkElement[] { wide, near, key })
            {
                Grid.SetColumn(element, index);
                surface.Children.Add(element);
            }
            Panel.SetZIndex(wide, -10);
            Panel.SetZIndex(near, -9);
            var appearance = MicroSurfaceWindow.ApplyAgentLightingAppearance(
                key, AgentLightingAppearance.FromCodexSession(statuses[index], true));
            foreach (var name in new[] { "GlowWide", "Glow" })
            {
                if (key.Template.FindName(name, key) is FrameworkElement halo)
                {
                    halo.Opacity = 0;
                }
            }
            MicroSurfaceWindow.ApplyAgentGlowAppearance(
                wide, near, key.BorderBrush, appearance);
        }
        surface.Measure(new Size(700, 160));
        surface.Arrange(new Rect(0, 0, 700, 160));
        surface.UpdateLayout();
        SavePng(surface, Path.Combine(outputDirectory, "selected-lighting-comparison.png"), 700, 160);
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

    private static void SavePng(
        Visual visual,
        string filePath,
        int width = SurfaceWidth,
        int height = SurfaceHeight)
    {
        var bitmap = new RenderTargetBitmap(
            width,
            height,
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
