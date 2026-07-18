using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexController.Models;
using CodexController.ViewModels;
using CodexController.Views;

namespace CodexController.Tests;

public sealed class AgentKeypadViewDesignTests
{
    [Fact]
    public void RendersSixMicroStyleKeysWithTitlesBesideThem()
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

            var previewPath = Environment.GetEnvironmentVariable(
                "AGENT_CONTROLLER_LB_PREVIEW_PATH");
            if (!string.IsNullOrWhiteSpace(previewPath))
            {
                WritePreview(view, previewPath);
            }
        }
        finally
        {
            application.Shutdown();
        }
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
}
