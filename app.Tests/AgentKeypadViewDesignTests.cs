using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexController.Controllers;
using CodexController.Models;
using CodexController.ViewModels;
using CodexController.Views;

namespace CodexController.Tests;

public sealed class AgentKeypadViewDesignTests
{
    [Fact]
    public void RendersMicroStyleOverlayFamily()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RenderAndAssertLayout();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static void RenderAndAssertLayout()
    {
        var application = new App
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
        };
        application.InitializeComponent();

        try
        {
            RenderAndAssertAgentLayout();
            RenderAndAssertActionLayouts();
            RenderAndAssertSidebarLayout();
        }
        finally
        {
            application.Shutdown();
        }
    }

    private static void RenderAndAssertAgentLayout()
    {
        var viewModel = new RadialMenuViewModel();
        viewModel.Update(CreatePreviewState());
        var view = new AgentKeypadView
        {
            DataContext = viewModel,
        };

        view.Measure(new Size(820, 360));
        view.Arrange(new Rect(0, 0, 820, 360));
        view.UpdateLayout();

        Assert.Equal(820, view.ActualWidth);
        Assert.Equal(360, view.ActualHeight);

        var controls = new[]
        {
            view.AgentUpKey,
            view.AgentRightKey,
            view.AgentDownKey,
            view.AgentLeftKey,
            view.AgentViewKey,
            view.AgentMenuKey,
        };
        Assert.Equal(6, controls.Length);

        foreach (var control in controls)
        {
            control.ApplyTemplate();
            var keycap = GetTemplatePart<Border>(control, "Keycap");
            var statusLed = GetTemplatePart<System.Windows.Shapes.Ellipse>(
                control,
                "StatusLed");
            var title = GetTemplatePart<TextBlock>(control, "SlotTitle");

            Assert.InRange(keycap.ActualWidth, 57.5, 58.5);
            Assert.InRange(keycap.ActualHeight, 57.5, 58.5);
            Assert.InRange(statusLed.ActualWidth, 15.5, 16.5);
            Assert.InRange(statusLed.ActualHeight, 15.5, 16.5);

            var keyPosition = keycap.TranslatePoint(new Point(), view);
            var titlePosition = title.TranslatePoint(new Point(), view);
            Assert.True(
                titlePosition.X > keyPosition.X + keycap.ActualWidth,
                "Each Agent title must appear beside its status key.");
        }

        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_LB_PREVIEW_PATH");
    }

    private static void RenderAndAssertActionLayouts()
    {
        var commandViewModel = new RadialMenuViewModel();
        commandViewModel.Update(CreateCommandPreviewState());
        var commandView = new ActionMenuView
        {
            DataContext = commandViewModel,
        };
        var commandSize = MeasureAndArrange(commandView);

        var keycaps = FindVisualChildren<Border>(commandView)
            .Where(element => element.Name == "Keycap")
            .ToArray();
        var titles = FindVisualChildren<TextBlock>(commandView)
            .Where(element => element.Name == "SlotTitle")
            .ToArray();
        var positionGlyphs = FindVisualChildren<TextBlock>(commandView)
            .Where(element =>
                element.Name == "PositionGlyph" &&
                element.Visibility == Visibility.Visible)
            .Select(element => element.Text)
            .ToArray();

        Assert.Equal(6, keycaps.Length);
        Assert.Equal(6, titles.Length);
        Assert.Equal(["↑", "→", "↓", "←"], positionGlyphs);
        Assert.Equal(Visibility.Visible, commandView.LearningPositionGuide.Visibility);
        foreach (var (keycap, title) in keycaps.Zip(titles))
        {
            Assert.InRange(keycap.ActualWidth, 57.5, 58.5);
            Assert.InRange(keycap.ActualHeight, 57.5, 58.5);
            var keyPosition = keycap.TranslatePoint(new Point(), commandView);
            var titlePosition = title.TranslatePoint(new Point(), commandView);
            Assert.True(
                titlePosition.X > keyPosition.X + keycap.ActualWidth,
                "Each action title must appear beside its physical key.");
        }

        var turnViewModel = new RadialMenuViewModel();
        turnViewModel.Update(CreateTurnPreviewState());
        var turnView = new ActionMenuView
        {
            DataContext = turnViewModel,
        };
        var turnSize = MeasureAndArrange(turnView);
        var turnKeycaps = FindVisualChildren<Border>(turnView)
            .Count(element => element.Name == "Keycap");

        Assert.Equal(4, turnKeycaps);
        Assert.True(
            turnSize.Height < commandSize.Height,
            "A four-action panel should collapse its unused third row.");

        WritePreviewFromEnvironment(
            turnView,
            "AGENT_CONTROLLER_TURN_PREVIEW_PATH");

        WritePreviewFromEnvironment(
            commandView,
            "AGENT_CONTROLLER_ACTION_PREVIEW_PATH");
    }

    private static Size MeasureAndArrange(FrameworkElement view)
    {
        view.Measure(new Size(820, double.PositiveInfinity));
        var height = Math.Ceiling(view.DesiredSize.Height);
        view.Arrange(new Rect(0, 0, 820, height));
        view.UpdateLayout();
        return new Size(view.ActualWidth, view.ActualHeight);
    }

    private static void RenderAndAssertSidebarLayout()
    {
        var viewModel = new SidebarNavigationMenuViewModel();
        viewModel.Update(new SidebarNavigationMenuState(
            new SidebarNavigationMenuItem(
                "分析大模型产业链趋势",
                SidebarScope.PinnedTasks,
                "置顶任务",
                CrossesSectionBoundary: true),
            new SidebarNavigationMenuItem(
                "规划项目未来架构",
                SidebarScope.Projects,
                "项目",
                CrossesSectionBoundary: false),
            new SidebarNavigationMenuItem(
                "更新 README 文档",
                SidebarScope.ProjectlessTasks,
                "未归项目任务",
                CrossesSectionBoundary: true),
            Position: 5,
            Count: 12)
        {
            Title = "侧边栏",
            NavigateGlyph = "LS",
            NavigateHint = "移动",
            CycleScopeGlyph = "L3",
            CycleScopeHint = "区域",
            OpenGlyph = "A",
            OpenHint = "打开",
        });
        var view = new SidebarNavigationMenuView
        {
            DataContext = viewModel,
        };

        view.Measure(new Size(560, 314));
        view.Arrange(new Rect(0, 0, 560, 314));
        view.UpdateLayout();

        Assert.Equal(560, view.ActualWidth);
        Assert.Equal(314, view.ActualHeight);
        Assert.InRange(view.PreviousRow.ActualHeight, 51.5, 52.5);
        Assert.InRange(view.CurrentRow.ActualHeight, 63.5, 64.5);
        Assert.InRange(view.NextRow.ActualHeight, 51.5, 52.5);
        Assert.Equal("LS", ChildText(view.NavigationGlyph));
        Assert.Equal("L3", ChildText(view.ScopeGlyph));
        Assert.Equal("A", ChildText(view.OpenGlyph));

        WritePreviewFromEnvironment(
            view,
            "AGENT_CONTROLLER_SIDEBAR_PREVIEW_PATH");
    }

    private static string ChildText(DependencyObject parent)
    {
        return FindVisualChildren<TextBlock>(parent)
            .Select(textBlock => textBlock.Text)
            .Single();
    }

    private static RadialMenuState CreatePreviewState()
    {
        return new RadialMenuState(
            RadialMenuLayerKind.Agent,
            "Agent tasks",
            "LB",
            [
                Item(
                    "agent-1",
                    RadialMenuSlotPosition.Top,
                    "Up",
                    "分析大模型产业链趋势",
                    ThreadStatus.Thinking),
                Item(
                    "agent-2",
                    RadialMenuSlotPosition.CenterRight,
                    "Right",
                    "审计并编译 grok-build",
                    ThreadStatus.RequiresInput,
                    isHighlighted: true),
                Item(
                    "agent-3",
                    RadialMenuSlotPosition.Bottom,
                    "Down",
                    "梳理测评受众与利益相关方",
                    ThreadStatus.CompleteUnread),
                Item(
                    "agent-4",
                    RadialMenuSlotPosition.CenterLeft,
                    "Left",
                    "调整云车游戏 play 路由",
                    ThreadStatus.Idle),
                Item(
                    "agent-5",
                    RadialMenuSlotPosition.Left,
                    "View",
                    "规划项目未来架构",
                    ThreadStatus.Error),
                Item(
                    "agent-6",
                    RadialMenuSlotPosition.Right,
                    "Menu",
                    "更新 README 文档",
                    ThreadStatus.Unassigned),
            ],
            RadialMenuDisplayMode.Always);
    }

    private static RadialMenuState CreateCommandPreviewState()
    {
        return new RadialMenuState(
            RadialMenuLayerKind.Command,
            "Codex commands",
            "RB",
            [
                ActionItem(
                    "fast",
                    RadialMenuSlotPosition.Top,
                    LogicalInput.FaceNorth,
                    "Y",
                    "Fast mode"),
                ActionItem(
                    "decline",
                    RadialMenuSlotPosition.Right,
                    LogicalInput.FaceEast,
                    "B",
                    "Decline changes"),
                ActionItem(
                    "approve",
                    RadialMenuSlotPosition.Bottom,
                    LogicalInput.FaceSouth,
                    "A",
                    "Approve changes",
                    "Press A again to confirm",
                    isHighlighted: true),
                ActionItem(
                    "fork",
                    RadialMenuSlotPosition.Left,
                    LogicalInput.FaceWest,
                    "X",
                    "Fork task"),
                ActionItem(
                    "dictation",
                    RadialMenuSlotPosition.CenterLeft,
                    LogicalInput.View,
                    "View",
                    "Dictation",
                    "Hold to talk"),
                ActionItem(
                    "send",
                    RadialMenuSlotPosition.CenterRight,
                    LogicalInput.Menu,
                    "Menu",
                    "Send"),
            ],
            RadialMenuDisplayMode.Learning,
            isLearningCueReady: true,
            subtitle: "L3 cancel");
    }

    private static RadialMenuState CreateTurnPreviewState()
    {
        return new RadialMenuState(
            RadialMenuLayerKind.Turn,
            "Active turn",
            "RT",
            [
                ActionItem(
                    "queue",
                    RadialMenuSlotPosition.Top,
                    LogicalInput.FaceNorth,
                    "Y",
                    "Queue next turn"),
                ActionItem(
                    "stop",
                    RadialMenuSlotPosition.Right,
                    LogicalInput.FaceEast,
                    "B",
                    "Stop",
                    "Hold for 3 seconds"),
                ActionItem(
                    "fork",
                    RadialMenuSlotPosition.Bottom,
                    LogicalInput.FaceSouth,
                    "A",
                    "Fork task"),
                ActionItem(
                    "steer",
                    RadialMenuSlotPosition.Left,
                    LogicalInput.FaceWest,
                    "X",
                    "Steer current turn"),
            ],
            RadialMenuDisplayMode.Learning,
            isLearningCueReady: true,
            subtitle: "Release RT to close");
    }

    private static RadialMenuItemState ActionItem(
        string id,
        RadialMenuSlotPosition position,
        LogicalInput logicalInput,
        string glyph,
        string title,
        string? subtitle = null,
        bool isHighlighted = false)
    {
        return new RadialMenuItemState(
            id,
            position,
            glyph,
            title,
            subtitle,
            isHighlighted: isHighlighted,
            logicalInput: logicalInput);
    }

    private static RadialMenuItemState Item(
        string id,
        RadialMenuSlotPosition position,
        string glyph,
        string title,
        ThreadStatus status,
        bool isHighlighted = false)
    {
        return new RadialMenuItemState(
            id,
            position,
            glyph,
            title,
            isHighlighted: isHighlighted,
            status: status);
    }

    private static T GetTemplatePart<T>(
        Control control,
        string name)
        where T : DependencyObject
    {
        var part = control.Template.FindName(name, control);
        return Assert.IsType<T>(part);
    }

    private static void WritePreview(
        FrameworkElement view,
        string path)
    {
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

    private static void WritePreviewFromEnvironment(
        FrameworkElement view,
        string variableName)
    {
        var previewPath = Environment.GetEnvironmentVariable(variableName);
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            WritePreview(view, previewPath);
        }
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
