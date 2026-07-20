using System.Collections.ObjectModel;
using System.Windows;
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
        Assert.Equal(7, english.Items.Count);
        Assert.Contains(
            FindVisualChildren<ControllerGlyphView>(view),
            glyph => glyph.Glyph == "⧉");
        Assert.Contains(
            FindVisualChildren<ControllerGlyphView>(view),
            glyph => glyph.Glyph == "☰");
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_OVERVIEW_PREVIEW_PATH");

        english.SelectActionCommand.Execute(null);
        view.UpdateLayout();
        Assert.True(view.TutorialActionButtonHalo.Tag is true);
        Assert.True(view.TutorialDPadHalo.Tag is true);
        Assert.True(view.TutorialFaceClusterHalo.Tag is true);
        Assert.True(view.TutorialActionButtonHalo.HasAnimatedProperties);
        Assert.Equal(208d, Canvas.GetLeft(view.TutorialDPadHalo));
        Assert.Equal(176d, Canvas.GetTop(view.TutorialDPadHalo));
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_TUTORIAL_PREVIEW_PATH");

        english.SelectStickPressCommand.Execute(null);
        view.UpdateLayout();
        Assert.True(view.TutorialLeftStickPressHalo.Tag is true);
        Assert.True(view.TutorialRightStickPressHalo.Tag is true);
        Assert.True(
            view.TutorialLeftPressArrow.RenderTransform
                .HasAnimatedProperties);
        Assert.True(
            view.TutorialRightPressArrow.RenderTransform
                .HasAnimatedProperties);
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
        Assert.True(view.TutorialLeftShoulderHalo.Tag is true);
        Assert.Equal(6, chinese.Items.Count);
        Assert.True(view.ActualWidth >= 602.5);
        Assert.True(view.ActualHeight >= 319.5);
        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_TUTORIAL_MIN_PREVIEW_PATH");

        RenderDashboardPreview();
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
                "侧边栏焦点未同步 · 目标 Agent 未在前台"),
            new(
                new BridgeEvent(
                    BridgeEventKeys.LegacyMessage,
                    timestamp.AddSeconds(-1),
                    BridgeEventSeverity.Info),
                "侧边栏焦点已同步 · Codex"),
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
            Assert.True(
                tab.ActualWidth > 70,
                $"Tutorial tab width was {tab.ActualWidth}.");
            Assert.True(
                tab.ActualHeight >= 28,
                $"Tutorial tab height was {tab.ActualHeight}.");
            Assert.True(
                tab.Focusable,
                $"Tutorial tab '{tab.Content}' must accept keyboard focus.");
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
