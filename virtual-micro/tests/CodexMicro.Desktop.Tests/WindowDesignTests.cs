using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;
using Xunit;

namespace CodexMicro.Desktop.Tests;

[Collection(WpfUiCollection.Name)]
public sealed class WindowDesignTests
{
    private const int IsolatedAgentRenderSize = 166;

    [Fact]
    public void QuickModelSnapshotResetsBeforeBindingTheNextTask()
    {
        var firstTask = MicroSurfaceWindow.ReduceQuickModelSnapshot(
            new CodexThreadModelState(
                "thread-a",
                "gpt-5.6-luna",
                "max"));

        Assert.Equal("thread-a", firstTask.ThreadId);
        Assert.Equal(CodexQuickModel.Luna, firstTask.Model);

        var changingTask = MicroSurfaceWindow.ReduceQuickModelSnapshot(null);

        Assert.Null(changingTask.ThreadId);
        Assert.Equal(CodexQuickModel.Unknown, changingTask.Model);

        var secondTask = MicroSurfaceWindow.ReduceQuickModelSnapshot(
            new CodexThreadModelState(
                "thread-b",
                "gpt-5.6-sol",
                "ultra"));

        Assert.Equal("thread-b", secondTask.ThreadId);
        Assert.Equal(CodexQuickModel.Sol, secondTask.Model);
    }

    [Fact]
    public void DelayedOrFailedToggleCannotOverwriteAnotherTaskSnapshot()
    {
        var secondTask = MicroSurfaceWindow.ReduceQuickModelSnapshot(
            new CodexThreadModelState(
                "thread-b",
                "gpt-5.6-sol",
                "ultra"));
        var delayedSuccessForFirstTask = new CodexModelToggleResult(
            Succeeded: true,
            Previous: CodexQuickModel.Sol,
            Current: CodexQuickModel.Luna,
            ThreadId: "thread-a");
        var failedResultForFirstTask = new CodexModelToggleResult(
            Succeeded: false,
            Previous: CodexQuickModel.Luna,
            Current: CodexQuickModel.Luna,
            ThreadId: "thread-a",
            Error: "thread-settings-rejected");
        var resultForCurrentTask = new CodexModelToggleResult(
            Succeeded: true,
            Previous: CodexQuickModel.Luna,
            Current: CodexQuickModel.Sol,
            ThreadId: "thread-b");
        var unscopedFailureAfterTaskChange = new CodexModelToggleResult(
            Succeeded: false,
            Previous: CodexQuickModel.Unknown,
            Current: CodexQuickModel.Unknown,
            Error: "ipc-timeout");

        Assert.False(MicroSurfaceWindow.QuickModelResultTargetsCurrentThread(
            secondTask,
            delayedSuccessForFirstTask));
        Assert.False(MicroSurfaceWindow.QuickModelResultTargetsCurrentThread(
            secondTask,
            failedResultForFirstTask));
        Assert.True(MicroSurfaceWindow.QuickModelResultTargetsCurrentThread(
            secondTask,
            resultForCurrentTask));
        Assert.False(MicroSurfaceWindow.QuickModelResultCanDescribeCurrentThread(
            secondTask,
            "thread-a",
            unscopedFailureAfterTaskChange));
    }

    [Theory]
    [InlineData("no-visible-thread", true)]
    [InlineData("multiple-visible-threads", true)]
    [InlineData("thread-owner-unavailable", true)]
    [InlineData("thread-state-unavailable", true)]
    [InlineData("ipc-timeout", true)]
    [InlineData("ipc-disconnected", true)]
    [InlineData("visible-thread-changed", true)]
    [InlineData("cancelled", true)]
    [InlineData("thread-settings-rejected", false)]
    [InlineData("ipc-unavailable", false)]
    [InlineData(null, false)]
    public void QuickModelErrorsUseTransientSeverityOnlyWhenRetryIsSafe(
        string? error,
        bool expected) =>
        Assert.Equal(
            expected,
            MicroSurfaceWindow.IsTransientQuickModelError(error));

    [Theory]
    [InlineData(true, false, "reasoning", false)]
    [InlineData(true, false, "command", true)]
    [InlineData(true, true, "command", false)]
    [InlineData(false, false, "command", false)]
    public void ReasoningPressDoesNotDependOnTheHidBroker(
        bool isCodexHarness,
        bool brokerReady,
        string encoderMode,
        bool expected) =>
        Assert.Equal(
            expected,
            MicroSurfaceWindow.RequiresBrokerForDialPress(
                isCodexHarness,
                brokerReady,
                encoderMode));

    [Fact]
    public void ApplicationCloseTaskWaitsForWindowCleanup()
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
                var window = new MicroSurfaceWindow(
                    new MicroLocalization(MicroLanguage.ZhCn));

                var close = window.CloseForApplicationExitAsync();
                if (!close.IsCompleted)
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    var frame = new DispatcherFrame();
                    _ = close.ContinueWith(
                        _ => dispatcher.BeginInvoke(
                            new Action(() => frame.Continue = false)),
                        TaskScheduler.Default);
                    Dispatcher.PushFrame(frame);
                }

                close.GetAwaiter().GetResult();
                Assert.True(close.IsCompletedSuccessfully);
                Assert.Same(
                    close,
                    window.CloseForApplicationExitAsync());
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
    public void PlusActionAddsAKeypadWithoutChangingTheCurrentKeypad()
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

                var profile = MicroProfileSettings.CreateTransient();
                profile.SetActiveHarness("codex");
                string? addedHarnessId = null;
                var window = new MicroSurfaceWindow(
                    new MicroLocalization(MicroLanguage.ZhCn),
                    profileSettings: profile,
                    openHarnessInNewKeypad: harnessId =>
                        addedHarnessId = harnessId);

                window.PopulateHarnessContextMenu();
                var deepSeekItem = window.HarnessContextMenu.Items
                    .OfType<MenuItem>()
                    .Single(item => Equals(item.Tag, "deepseek-harness"));

                window.AddHarnessInNewKeypad(
                    "deepseek-harness",
                    "DeepSeek");
                Assert.Equal("deepseek-harness", addedHarnessId);
                Assert.Equal("codex", profile.Current.ActiveHarnessId);

                // A MenuItem can still raise Click for the pointer gesture
                // that began on its embedded "+" badge. That routed parent
                // click must be consumed rather than switching this keypad.
                deepSeekItem.RaiseEvent(new RoutedEventArgs(
                    MenuItem.ClickEvent,
                    deepSeekItem));
                Assert.Equal("codex", profile.Current.ActiveHarnessId);

                // Reopening the menu starts a new gesture, so an ordinary row
                // click continues to switch the current keypad as before.
                window.PopulateHarnessContextMenu();
                deepSeekItem = window.HarnessContextMenu.Items
                    .OfType<MenuItem>()
                    .Single(item => Equals(item.Tag, "deepseek-harness"));
                deepSeekItem.RaiseEvent(new RoutedEventArgs(
                    MenuItem.ClickEvent,
                    deepSeekItem));
                Assert.Equal("deepseek-harness", profile.Current.ActiveHarnessId);
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

                var window = new MicroSurfaceWindow(
                    new MicroLocalization(MicroLanguage.ZhCn));
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
    public void StructuredProgressTargetsTheDeepSeekAndVoiceKeys()
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

                var profile = MicroProfileSettings.CreateTransient();
                profile.SetActiveHarness("deepseek-harness");
                var window = new MicroSurfaceWindow(
                    new MicroLocalization(MicroLanguage.ZhCn),
                    profileSettings: profile);
                window.ApplyHarnessProgressForVisualTest(
                    step: 1,
                    totalSteps: 7);

                Assert.Equal(3, Grid.GetColumn(window.HarnessActionProgressRing));
                Assert.Equal(
                    Visibility.Collapsed,
                    window.HarnessActionProgressRing.Visibility);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.HarnessActionStatusBadge.Visibility);
                Assert.Equal("1/7", window.QuotaValueText.Text);
                Assert.Equal(16, window.QuotaValueText.FontSize);
                Assert.Equal(Visibility.Collapsed, window.QuotaCaptionText.Visibility);
                Assert.NotEqual(Geometry.Empty, window.QuotaProgressRing.Data);
                Assert.Equal(
                    Visibility.Visible,
                    window.HarnessProgressStatusText.Visibility);
                Assert.StartsWith(
                    "检查适配器",
                    window.HarnessProgressStatusText.Text);
                Assert.Equal(16.5, window.HarnessProgressStatusText.FontSize);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.BrandWordmarkPanel.Visibility);
                Assert.Equal(
                    Color.FromRgb(0xFA, 0xFA, 0xF8),
                    Assert.IsType<SolidColorBrush>(window.SettingsKey.Background).Color);
                Assert.Equal(
                    Color.FromRgb(0x1D, 0x1D, 0x1B),
                    Assert.IsType<SolidColorBrush>(window.QuotaValueText.Foreground).Color);
                Assert.Contains(
                    "1/7 检查适配器",
                    AutomationProperties.GetItemStatus(window.SettingsKey));
                Assert.Contains(
                    "1/7 检查适配器",
                    AutomationProperties.GetItemStatus(window.ActionKey12));
                var progressToolTip = Assert.IsType<ToolTip>(
                    window.SettingsKey.ToolTip);
                var progressToolTipContent = Assert.IsType<StackPanel>(
                    progressToolTip.Content);
                var progressHelp = string.Join(
                    '\n',
                    progressToolTipContent.Children
                        .OfType<TextBlock>()
                        .Select(item => item.Text));
                Assert.Contains("1/7 检查适配器", progressHelp);
                Assert.Contains("Visual progress test.", progressHelp);
                var progressPreviewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_DEEPSEEK_PROGRESS_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(progressPreviewPath))
                {
                    window.DesignSurface.Measure(new Size(590, 610));
                    window.DesignSurface.Arrange(new Rect(0, 0, 590, 610));
                    window.DesignSurface.UpdateLayout();
                    var progressBitmap = new RenderTargetBitmap(
                        590,
                        610,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    progressBitmap.Render(window.DesignSurface);
                    var progressEncoder = new PngBitmapEncoder();
                    progressEncoder.Frames.Add(BitmapFrame.Create(progressBitmap));
                    using var progressStream = new FileStream(
                        progressPreviewPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
                    progressEncoder.Save(progressStream);
                }

                window.ApplyHarnessProgressForVisualTest(
                    step: 6,
                    totalSteps: 8,
                    onVoiceKey: true);

                Assert.Equal(1, Grid.GetColumn(window.HarnessActionProgressRing));
                Assert.Equal(2, Grid.GetColumnSpan(window.HarnessActionProgressRing));
                Assert.Equal(
                    Visibility.Collapsed,
                    window.HarnessActionStatusBadge.Visibility);
                Assert.Equal(
                    Visibility.Visible,
                    window.HarnessProgressStatusText.Visibility);
                Assert.StartsWith(
                    "连接语音通道",
                    window.HarnessProgressStatusText.Text);
                Assert.Equal(16.5, window.HarnessProgressStatusText.FontSize);
                Assert.Equal("6/8", window.QuotaValueText.Text);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.BrandWordmarkPanel.Visibility);
                Assert.Contains(
                    "6/8 连接语音通道",
                    AutomationProperties.GetItemStatus(window.ActionKey10));
                Assert.Contains(
                    "6/8 连接语音通道",
                    AutomationProperties.GetItemStatus(window.SettingsKey));

                window.ApplyVoiceStatusForVisualTest(
                    "连接 / 启用麦克风后重试",
                    failed: true);

                Assert.Equal(
                    Visibility.Collapsed,
                    window.HarnessActionStatusBadge.Visibility);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.HarnessActionProgressRing.Visibility);
                Assert.Equal("!", window.QuotaValueText.Text);
                Assert.Equal(
                    "连接 / 启用麦克风后重试",
                    window.HarnessProgressStatusText.Text);
                Assert.Equal(16.5, window.HarnessProgressStatusText.FontSize);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.BrandWordmarkPanel.Visibility);
                Assert.Contains(
                    "连接 / 启用麦克风后重试",
                    AutomationProperties.GetItemStatus(window.ActionKey10));
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

                var profile = MicroProfileSettings.CreateTransient();
                var window = new MicroSurfaceWindow(
                    new MicroLocalization(MicroLanguage.ZhCn),
                    profileSettings: profile,
                    openHarnessInNewKeypad: _ => { });
                window.DesignSurface.Measure(new Size(590, 610));
                window.DesignSurface.Arrange(new Rect(0, 0, 590, 610));
                window.DesignSurface.UpdateLayout();

                AssertSquare(window.AgentKey0);
                Assert.Equal(96, window.AgentKey0.ActualWidth, 3);
                Assert.Equal(Visibility.Collapsed, window.AgentBackGlyph.Visibility);
                Assert.False(window.AgentBackGlyph.IsHitTestVisible);
                Assert.Equal(0, Grid.GetRow(window.AgentBackGlyph));
                Assert.Equal(1, Grid.GetColumn(window.AgentBackGlyph));
                AssertSquare(window.ActionKey06);
                AssertSquare(window.SettingsKey);
                AssertSquare(window.ActionKey12);
                Assert.Same(window.QuotaGauge, window.SettingsKey.Content);
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
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                Assert.True(window.AllowsTransparency);
                Assert.Equal(Brushes.Transparent, window.Background);
                Assert.Contains("Codex Micro", window.Title);
                Assert.True(window.TopmostMenuItem.IsCheckable);
                Assert.NotNull(window.DeviceFrame.ContextMenu);
                Assert.Equal(2, window.SettingsMenuItem.Items.Count);
                Assert.NotNull(window.SettingsKey.ContextMenu);
                Assert.Equal(3, window.KnobContextMenu.Items.Count);
                Assert.NotNull(window.ActionKey12.ContextMenu);
                Assert.Equal(
                    Visibility.Collapsed,
                    window.CloseKeypadMenuItem.Visibility);
                Assert.Equal(Visibility.Collapsed, window.HarnessActionProgressRing.Visibility);
                Assert.False(window.HarnessActionProgressRing.IsHitTestVisible);
                Assert.Equal(76, window.HarnessActionProgressRing.Width, 3);
                Assert.Equal(76, window.HarnessActionProgressRing.Height, 3);
                Assert.Equal(Visibility.Collapsed, window.ActionSendBadge.Visibility);
                Assert.False(window.ActionSendBadge.IsHitTestVisible);
                Assert.Equal(20, window.ActionSendBadge.Width, 3);
                Assert.Equal(20, window.ActionSendBadge.Height, 3);
                Assert.Equal(HorizontalAlignment.Right, window.ActionSendBadge.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Bottom, window.ActionSendBadge.VerticalAlignment);
                Assert.NotEqual(Geometry.Empty, window.ActionSendPlane.Data);
                Assert.Null(window.FindName("HarnessConnectionStatusDot"));
                Assert.Equal(3, Grid.GetRow(window.HarnessActionProgressRing));
                Assert.Equal(3, Grid.GetColumn(window.HarnessActionProgressRing));
                Assert.Equal(Visibility.Collapsed, window.HarnessActionStatusBadge.Visibility);
                Assert.False(window.HarnessActionStatusBadge.IsHitTestVisible);
                Assert.Equal(84, window.HarnessActionStatusBadge.Width, 3);
                Assert.Equal(20, window.HarnessActionStatusBadge.Height, 3);
                Assert.Equal(3, Grid.GetRow(window.HarnessActionStatusBadge));
                Assert.Equal(3, Grid.GetColumn(window.HarnessActionStatusBadge));
                window.PopulateHarnessContextMenu();
                var harnessItems = window.HarnessContextMenu.Items
                    .OfType<MenuItem>()
                    .ToArray();
                Assert.Contains(harnessItems, item =>
                    Equals(item.Tag, "codex") && item.IsChecked);
                Assert.Contains(harnessItems, item =>
                    Equals(item.Tag, "deepseek-harness"));
                Assert.All(harnessItems, item =>
                    Assert.IsAssignableFrom<FrameworkElement>(item.Header));
                foreach (var harnessItem in harnessItems)
                {
                    var header = Assert.IsType<Grid>(harnessItem.Header);
                    Assert.Equal(34, header.Width, 3);
                    Assert.IsType<CodexMicro.Desktop.Controls.KeycapIcon>(
                        header.Children[0]);
                    if (harnessItem.Tag is string)
                    {
                        Assert.Equal(2, header.Children.Count);
                        var badge = Assert.IsType<Border>(header.Children[1]);
                        Assert.Equal(16, badge.Width, 3);
                        Assert.Equal(
                            "+",
                            Assert.IsType<TextBlock>(badge.Child).Text);
                    }
                    else
                    {
                        Assert.Single(header.Children);
                    }
                    var tooltip = Assert.IsType<ToolTip>(harnessItem.ToolTip);
                    Assert.Equal(
                        2,
                        Assert.IsType<StackPanel>(tooltip.Content).Children.Count);
                }
                profile.SetActiveHarness("deepseek-harness");
                Assert.Equal("DEEPSEEK", window.ActionIcon12.KeycapId);
                Assert.Equal("DEEPSEEK", window.BrandCodexIcon.KeycapId);
                Assert.Equal(
                    "DEEPSEEK  /  MICRO  /  DIRECT BRIDGE",
                    window.LeftSilkScreen.Text);
                Assert.Equal(
                    "DEEPSEEK  HARNESS",
                    window.BrandWordmarkText.Text);
                Assert.InRange(window.HarnessThemeWash.Opacity, 0.27, 0.29);
                Assert.IsType<RadialGradientBrush>(
                    window.HarnessThemeWash.Background);
                profile.SetActiveHarness("codex");
                Assert.Equal("CODEX", window.ActionIcon12.KeycapId);
                Assert.Equal("CODEX", window.BrandCodexIcon.KeycapId);
                Assert.Equal(
                    "CODEX  /  MICRO  /  CRYSTAL HID",
                    window.LeftSilkScreen.Text);
                Assert.Equal("OPENAI  CODEX", window.BrandWordmarkText.Text);
                Assert.Equal(0, window.HarnessThemeWash.Opacity, 3);
                Assert.Equal(Visibility.Visible, window.ActionKey10.Visibility);
                Assert.Equal(Visibility.Collapsed, window.ActionKey10Split.Visibility);
                Assert.Equal(Visibility.Collapsed, window.ActionKey11Split.Visibility);
                Assert.Equal(Visibility.Collapsed, window.DialSelectionHud.Visibility);
                Assert.Equal(250, window.DialSelectionHud.Width, 3);

                window.ApplyActionTargetForegroundForVisualTest(isForeground: true);
                Assert.Equal(Visibility.Visible, window.ActionSendBadge.Visibility);
                profile.SetActiveHarness("deepseek-harness");
                Assert.Equal(Visibility.Collapsed, window.ActionSendBadge.Visibility);
                profile.SetActiveHarness("codex");
                window.ApplyActionTargetForegroundForVisualTest(isForeground: true);
                Assert.Equal(Visibility.Visible, window.ActionSendBadge.Visibility);
                window.ApplyActionTargetForegroundForVisualTest(isForeground: false);
                Assert.Equal(Visibility.Collapsed, window.ActionSendBadge.Visibility);

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
                Assert.Single(agentGlyph.Children);
                Assert.IsType<Ellipse>(agentGlyph.Children[0]);
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
                Assert.Equal(2, window.JoystickCap.Children.Count);
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
                var knobContent = Assert.IsType<Grid>(
                    window.SettingsKey.Template.FindName(
                        "KnobContent",
                        window.SettingsKey));
                Assert.InRange(settingsKnob.ActualWidth, 57.5, 58.5);
                Assert.Equal(
                    Color.FromRgb(0x2D, 0x29, 0x25),
                    Assert.IsType<SolidColorBrush>(settingsKnob.Fill).Color);
                Assert.Null(settingsKnob.Stroke);
                Assert.Equal(HorizontalAlignment.Right, knobContent.HorizontalAlignment);
                Assert.Equal(58, knobContent.Width, 3);
                Assert.Equal(5, knobContent.Margin.Right, 3);
                Assert.Same(window.QuotaGauge, window.SettingsKey.Content);
                Assert.Equal("—", window.QuotaValueText.Text);
                Assert.Equal("S↔L", window.QuotaCaptionText.Text);
                Assert.Contains(
                    "短按",
                    AutomationProperties.GetHelpText(window.SettingsKey));
                Assert.Contains(
                    "长按",
                    AutomationProperties.GetHelpText(window.SettingsKey));

                window.ApplyQuotaSnapshot(new CodexQuotaSnapshot(
                    new CodexQuotaWindow(
                        UsedPercent: 63,
                        WindowDurationMinutes: 300,
                        ResetsAt: DateTimeOffset.Now.AddHours(2)),
                    Secondary: null,
                    PlanType: "pro",
                    ReadAt: DateTimeOffset.Now));
                window.DesignSurface.UpdateLayout();

                Assert.Equal("37%", window.QuotaValueText.Text);
                Assert.IsType<StreamGeometry>(window.QuotaProgressRing.Data);
                Assert.Equal(
                    Color.FromRgb(0xA8, 0xC7, 0xFF),
                    Assert.IsType<SolidColorBrush>(
                        window.QuotaProgressRing.Stroke).Color);
                Assert.Contains(
                    "37%",
                    AutomationProperties.GetItemStatus(window.SettingsKey));

                window.ApplyQuickModel("visual-thread", CodexQuickModel.Luna);
                Assert.Equal("LUNA", window.QuotaCaptionText.Text);
                Assert.Contains(
                    "Luna",
                    AutomationProperties.GetItemStatus(window.SettingsKey));

                var knobCenter = settingsKnob.TranslatePoint(
                    new Point(
                        settingsKnob.ActualWidth / 2,
                        settingsKnob.ActualHeight / 2),
                    window.DesignSurface);
                var quotaCenter = window.QuotaGauge.TranslatePoint(
                    new Point(
                        window.QuotaGauge.ActualWidth / 2,
                        window.QuotaGauge.ActualHeight / 2),
                    window.DesignSurface);
                Assert.InRange(
                    Math.Abs(knobCenter.X - quotaCenter.X),
                    0,
                    0.5);
                Assert.InRange(
                    Math.Abs(knobCenter.Y - quotaCenter.Y),
                    0,
                    0.5);

                window.ApplyQuotaSnapshot(new CodexQuotaSnapshot(
                    new CodexQuotaWindow(
                        UsedPercent: 92,
                        WindowDurationMinutes: 10080,
                        ResetsAt: DateTimeOffset.Now.AddDays(2)),
                    Secondary: null,
                    PlanType: "pro",
                    ReadAt: DateTimeOffset.Now));
                Assert.Equal(
                    Color.FromRgb(0xFF, 0x9E, 0x8B),
                    Assert.IsType<SolidColorBrush>(
                        window.QuotaProgressRing.Stroke).Color);

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

                var deepSeekPreviewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_DEEPSEEK_PREVIEW_PATH");
                profile.SetActiveHarness("deepseek-harness");
                window.ApplyVoiceServiceStateForVisualTest(ready: true);
                window.ApplyHarnessStateForVisualTest(new(
                    "deepseek-harness",
                    [
                        new(
                            "visual-running-session",
                            "Running DeepSeek task",
                            Status: MicroHarnessSessionStatus.Running,
                            UpdatedAt: DateTimeOffset.Now.ToUnixTimeMilliseconds()),
                        new(
                            "visual-completed-session",
                            "Completed DeepSeek task",
                            Status: MicroHarnessSessionStatus.Completed,
                            UpdatedAt: DateTimeOffset.Now.AddMinutes(-1)
                                .ToUnixTimeMilliseconds()),
                        new(
                            "visual-waiting-session",
                            "Waiting DeepSeek task",
                            Status: MicroHarnessSessionStatus.WaitingForInput,
                            UpdatedAt: DateTimeOffset.Now.AddMinutes(-2)
                                .ToUnixTimeMilliseconds()),
                        new(
                            "visual-error-session",
                            "Failed DeepSeek task",
                            Status: MicroHarnessSessionStatus.Error,
                            UpdatedAt: DateTimeOffset.Now.AddMinutes(-3)
                                .ToUnixTimeMilliseconds()),
                        new(
                            "visual-idle-session",
                            "Idle DeepSeek task",
                            Status: MicroHarnessSessionStatus.Idle,
                            UpdatedAt: DateTimeOffset.Now.AddMinutes(-4)
                                .ToUnixTimeMilliseconds()),
                    ],
                    "visual-running-session",
                    new(
                        SessionList: true,
                        SessionActivation: true,
                        KnobSettings: true,
                        VoiceInput: true,
                        Actions: new HashSet<string>
                        {
                            MicroHarnessActionIds.ComposerSubmit,
                        }),
                    NavigationDepth: 0,
                     new(
                         Adapter: "ready",
                         Browser: "connected",
                         CurrentModel: "DeepSeek-V4-Pro"),
                     DateTimeOffset.Now));
                window.ApplyActionTargetForegroundForVisualTest(
                    isForeground: true);
                window.DesignSurface.UpdateLayout();
                Assert.Equal(Visibility.Visible, window.ActionSendBadge.Visibility);
                Assert.Equal("PRO", window.QuotaValueText.Text);
                Assert.Equal(Visibility.Collapsed, window.QuotaCaptionText.Visibility);
                Assert.Equal(Geometry.Empty, window.QuotaProgressRing.Data);
                Assert.Equal(
                    Color.FromRgb(0xFA, 0xFA, 0xF8),
                    Assert.IsType<SolidColorBrush>(window.SettingsKey.Background).Color);
                Assert.Equal(
                    Color.FromRgb(0x1D, 0x1D, 0x1B),
                    Assert.IsType<SolidColorBrush>(window.QuotaValueText.Foreground).Color);
                Assert.Equal(
                    Color.FromRgb(0x30, 0x4F, 0xFE),
                    Assert.IsType<SolidColorBrush>(
                        window.AgentKey0.BorderBrush).Color);
                Assert.Equal(
                    Color.FromRgb(0x78, 0xA6, 0xFF),
                    Assert.IsType<SolidColorBrush>(
                        window.ActivityLed.Fill).Color);
                if (!string.IsNullOrWhiteSpace(deepSeekPreviewPath))
                {
                    var deepSeekBitmap = new RenderTargetBitmap(
                        590,
                        610,
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    deepSeekBitmap.Render(window.DesignSurface);
                    var deepSeekEncoder = new PngBitmapEncoder();
                    deepSeekEncoder.Frames.Add(BitmapFrame.Create(deepSeekBitmap));
                    using var deepSeekStream = new FileStream(
                        deepSeekPreviewPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
                    deepSeekEncoder.Save(deepSeekStream);
                }
                profile.SetActiveHarness("codex");
                window.ApplyActionTargetForegroundForVisualTest(isForeground: true);

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

                const int wellSampleX = 65; // left of the centered dot glyph
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

                // External Harnesses project the same status vocabulary as
                // Codex. Running and completed must render blue and green
                // through the real XAML key template.
                var harnessRunningPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    AgentLightingAppearance.FromHarnessSession(
                        MicroHarnessSessionStatus.Running,
                        isCurrentSession: true),
                    out var harnessRunningBitmap);
                var harnessRunningPreviewPath = Environment.GetEnvironmentVariable(
                    "CODEX_MICRO_HARNESS_RUNNING_KEY_PREVIEW_PATH");
                if (!string.IsNullOrWhiteSpace(harnessRunningPreviewPath))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(harnessRunningBitmap));
                    using var stream = new FileStream(
                        harnessRunningPreviewPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.Read);
                    encoder.Save(stream);
                }
                var harnessRunningBlue = BlueEmphasisAt(
                    harnessRunningPixels,
                    width: IsolatedAgentRenderSize,
                    x: wellSampleX,
                    y: wellSampleY);
                Assert.True(
                    harnessRunningBlue > inactiveWellBlue + 35,
                    $"Harness running well {harnessRunningBlue} is not the blue " +
                    $"thinking state above inactive {inactiveWellBlue}.");

                var harnessCompletedPixels = RenderIsolatedAgentKey(
                    window.AgentKey0.Style,
                    AgentLightingAppearance.FromHarnessSession(
                        MicroHarnessSessionStatus.Completed,
                        isCurrentSession: false),
                    out _);
                var completedGreen = GreenEmphasisAt(
                    harnessCompletedPixels,
                    width: IsolatedAgentRenderSize,
                    x: wellSampleX,
                    y: wellSampleY);
                Assert.True(
                    completedGreen > inactiveWellBlue + 35,
                    $"Harness completed well {completedGreen} is not the green " +
                    $"completion state above inactive {inactiveWellBlue}.");

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

    private static double GreenEmphasisAt(
        byte[] pixels,
        int width,
        int x,
        int y)
    {
        var offset = ((y * width) + x) * 4;
        var blue = pixels[offset];
        var green = pixels[offset + 1];
        var red = pixels[offset + 2];
        return green - ((red + blue) / 2.0);
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
