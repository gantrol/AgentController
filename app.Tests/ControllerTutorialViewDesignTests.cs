using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexController.Controllers;
using CodexController.Core.Bridge;
using CodexController.Localization;
using CodexController.Models;
using CodexController.Presentation.Feedback;
using CodexController.ViewModels;
using CodexController.Views;

namespace CodexController.Tests;

public sealed class ControllerTutorialViewDesignTests
{
    [Fact]
    public void RendersInteractiveGuideAtDashboardAndMinimumSizes()
    {
        WpfTestHost.Run(RenderAndAssert);
    }

    private static void RenderAndAssert()
    {
        var english = CreateViewModel(
            AppLanguage.EnUs,
            BuiltInControllerProfiles.Xbox);
        var view = new ControllerTutorialView
        {
            DataContext = english,
        };

        Arrange(view, width: 760, height: 425);
        AssertTabs(view);
        Assert.Equal("Basics", view.OverviewTutorialButton.Content);
        Assert.Contains(
            "common actions",
            Assert.IsType<string>(view.OverviewTutorialButton.ToolTip),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, english.Items.Count);
        Assert.Contains(english.Items, item => item.Glyph == "LS");
        Assert.Contains(english.Items, item => item.Glyph == "RS");
        Assert.Contains(
            FindVisualChildren<ControllerGlyphView>(view),
            glyph => glyph.Glyph == "⧉");
        Assert.Contains(
            FindVisualChildren<ControllerGlyphView>(view),
            glyph => glyph.Glyph == "☰");
        AssertHotspots(view, english, ControllerTutorialMode.Overview);
        AssertLinkedHighlight(view, new(ControllerTutorialMode.Overview, LogicalInput.LeftStick, 1), 2);
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_OVERVIEW_PREVIEW_PATH");

        english.SelectActionCommand.Execute(null);
        view.UpdateLayout();
        Assert.True(view.ActionTutorialButton.IsChecked);
        AssertHotspots(view, english, ControllerTutorialMode.Action);
        AssertLinkedHighlight(view, new(ControllerTutorialMode.Action, LogicalInput.DPadUp), 2);
        AssertLinkedHighlight(view, new(ControllerTutorialMode.Action, LogicalInput.FaceSouth), 2);
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_TUTORIAL_PREVIEW_PATH");

        english.SelectStickPressCommand.Execute(null);
        view.UpdateLayout();
        Assert.True(view.StickPressTutorialButton.IsChecked);
        AssertHotspots(view, english, ControllerTutorialMode.StickPress);
        AssertLinkedHighlight(view, new(ControllerTutorialMode.StickPress, LogicalInput.LeftStickPress), 2);
        AssertLinkedHighlight(view, new(ControllerTutorialMode.StickPress, LogicalInput.RightStickPress), 2);
        Assert.Equal(2, english.Items.Count);
        Assert.Equal("LS / L3", english.Items[0].Glyph);
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_STICK_PRESS_PREVIEW_PATH");

        var chinese = CreateViewModel(
            AppLanguage.ZhCn,
            BuiltInControllerProfiles.Ultimate2);
        view.DataContext = chinese;
        chinese.SelectAgentCommand.Execute(null);
        Arrange(view, width: 603, height: 320);
        AssertTabs(view);
        Assert.True(view.AgentTutorialButton.IsChecked);
        AssertHotspots(view, chinese, ControllerTutorialMode.Agent);
        AssertLinkedHighlight(view, new(ControllerTutorialMode.Agent, LogicalInput.LeftShoulder), 1);
        AssertLinkedHighlight(view, new(ControllerTutorialMode.Agent, LogicalInput.View), 2);
        Assert.Equal(6, chinese.Items.Count);
        Assert.True(view.ActualWidth >= 602.5);
        Assert.True(view.ActualHeight >= 319.5);
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_TUTORIAL_MIN_PREVIEW_PATH");

        RenderDashboardPreview();
    }

    private static void AssertHotspots(
        ControllerTutorialView view,
        ControllerTutorialViewModel model,
        ControllerTutorialMode mode)
    {
        AssertTabs(view);
        Assert.Equal(mode, view.ControllerArtwork.TutorialMode);
        var hotspots = view.Hotspots.Children.Cast<ControllerInputButton>().ToArray();
        var buttons = new[]
        {
            LogicalInput.LeftTrigger, LogicalInput.RightTrigger,
            LogicalInput.LeftShoulder, LogicalInput.RightShoulder,
            LogicalInput.FaceNorth, LogicalInput.FaceEast,
            LogicalInput.FaceSouth, LogicalInput.FaceWest,
            LogicalInput.View, LogicalInput.Menu,
            LogicalInput.DPadUp, LogicalInput.DPadRight,
            LogicalInput.DPadDown, LogicalInput.DPadLeft,
            LogicalInput.LeftStickPress, LogicalInput.RightStickPress,
        };
        Assert.Equal(buttons.Length + 8, hotspots.Length);
        foreach (var input in buttons)
        {
            var hotspot = Assert.Single(hotspots, button => button.Input == input);
            Assert.Equal(0, hotspot.Direction);
        }
        foreach (var stick in new[] { LogicalInput.LeftStick, LogicalInput.RightStick })
        {
            Assert.Equal(new[] { 1, 2, 3, 4 }, hotspots
                .Where(button => button.Input == stick)
                .Select(button => button.Direction).Order());
        }
        Assert.All(hotspots, button =>
        {
            Assert.Equal(mode, button.Mode);
            Assert.True(button.Focusable);
            Assert.True(button.IsHitTestVisible);
            Assert.True(button.Width > 0 && button.Height > 0);
            Assert.InRange(Canvas.GetLeft(button), 0, view.Hotspots.Width - button.Width);
            Assert.InRange(Canvas.GetTop(button), 0, view.Hotspots.Height - button.Height);
            var name = AutomationProperties.GetName(button);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.StartsWith(model.InputName(button.Input), name);
            Assert.Equal(name, button.ToolTip);
        });
    }

    private static void AssertLinkedHighlight(
        ControllerTutorialView view, TutorialInput input, int expectedHighlights)
    {
        var buttons = ControllerTutorialView.InputButtons(view)
            .Where(button => button.Visibility == Visibility.Visible)
            .ToArray();
        var hotspot = Assert.Single(view.Hotspots.Children.Cast<ControllerInputButton>(),
            button => button.Input == input.Input && button.Direction == input.Direction);
        view.Highlight(input);
        Assert.True(hotspot.IsLinkedHighlighted);
        var highlighted = buttons.Where(button => button.IsLinkedHighlighted).ToArray();
        Assert.Equal(expectedHighlights, highlighted.Length);
        if (expectedHighlights == 2)
        {
            Assert.Contains(highlighted, button =>
                button.DataContext is ControllerTutorialItem item && item.Input == input.Input);
        }
        Assert.All(buttons, button => Assert.False(button.IsInputPressed));
        view.Highlight(null);
        Assert.All(buttons, button => Assert.False(button.IsLinkedHighlighted));
    }

    private static void RenderDashboardPreview()
    {
        var timestamp = new DateTimeOffset(
            2026,
            7,
            19,
            8,
            45,
            26,
            TimeSpan.FromHours(-7));
        var rows = new ObservableCollection<BridgeFeedbackLogRow>
        {
            new(
                new BridgeEvent(
                    BridgeEventKeys.LegacyMessage,
                    timestamp,
                    BridgeEventSeverity.Warning),
                "本地光标已移动 · 提取微信加仓动图"),
            new(
                new BridgeEvent(
                    BridgeEventKeys.LegacyMessage,
                    timestamp.AddSeconds(-1),
                    BridgeEventSeverity.Info),
                "范围已切换 · 游离任务"),
        };
        var recentEvents = new ReadOnlyObservableCollection<
            BridgeFeedbackLogRow>(
            rows);
        var viewModel = new DevicePageViewModel(
            new ObservableCollection<SidebarEntry>(),
            recentEvents,
            refresh: () => { },
            selectRootScope: _ => { });
        var strings = new LocalizationService(AppLanguage.ZhCn).Strings;
        viewModel.UpdateContext(
            strings,
            "Codex",
            BuiltInControllerProfiles.Xbox);
        viewModel.UpdateControllerState(new ControllerState(
            IsConnected: true,
            UserIndex: 0,
            PacketNumber: 1,
            Backend: "Windows.Gaming.Input",
            Buttons: ControllerButtons.None,
            LeftX: 0,
            LeftY: 0,
            RightX: 0,
            RightY: 0,
            LeftTrigger: 0,
            RightTrigger: 0));
        viewModel.Tutorial.SelectActionCommand.Execute(null);
        var view = new DevicePageView
        {
            DataContext = viewModel,
        };

        Arrange(view, width: 1184, height: 660);

        Assert.True(view.ControllerTutorial.ActualWidth > 700);
        Assert.True(view.ControllerTutorial.ActualHeight > 390);
        Assert.Equal(
            Visibility.Visible,
            view.ControllerTutorial.Visibility);
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_DASHBOARD_TUTORIAL_PREVIEW_PATH");

        Arrange(view, width: 1024, height: 550);

        Assert.True(view.ControllerTutorial.ActualWidth > 560);
        Assert.True(view.ControllerTutorial.ActualHeight > 280);
        Assert.True(
            view.ControllerTutorial.StickPressTutorialButton.ActualWidth >
            80);
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_DASHBOARD_TUTORIAL_MIN_PREVIEW_PATH");
    }

    private static void AssertTabs(ControllerTutorialView view)
    {
        var tabs = new[]
        {
            view.OverviewTutorialButton,
            view.ActionTutorialButton,
            view.AgentTutorialButton,
            view.TurnTutorialButton,
            view.CommandTutorialButton,
            view.StickPressTutorialButton,
        };
        Assert.Equal(6, tabs.Length);
        Assert.All(tabs, tab =>
        {
            tab.ApplyTemplate();
            Assert.True(
                tab.ActualWidth > 70,
                $"Tutorial tab width was {tab.ActualWidth}.");
            Assert.True(
                tab.ActualHeight >= 28,
                $"Tutorial tab height was {tab.ActualHeight}.");
            Assert.True(
                tab.Focusable,
                $"Tutorial tab '{tab.Content}' must accept keyboard focus.");
            Assert.Equal(180, ToolTipService.GetInitialShowDelay(tab));
            Assert.Equal(30000, ToolTipService.GetShowDuration(tab));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(tab)));
            var outline = Assert.IsType<ConnectedTabOutline>(
                tab.Template.FindName("SelectedOutline", tab));
            Assert.Equal(
                tab.IsChecked == true
                    ? Visibility.Visible
                    : Visibility.Collapsed,
                outline.Visibility);
        });
    }

    private static ControllerTutorialViewModel CreateViewModel(
        AppLanguage language,
        ControllerProfile profile)
    {
        var strings = new LocalizationService(language).Strings;
        var viewModel = new ControllerTutorialViewModel();
        viewModel.UpdateContext(
            strings,
            profile,
            strings.ControlLeftStickHint("LS", "A"),
            strings.ControlRightStickHint("RS", "B", "A"));
        return viewModel;
    }

    private static void Arrange(
        FrameworkElement view,
        double width,
        double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
    }

    private static void WritePreviewFromEnvironment(
        FrameworkElement view,
        string variableName)
    {
        var path = Environment.GetEnvironmentVariable(variableName);
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
}
