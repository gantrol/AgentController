using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CodexMicro.Desktop.Controls;
using CodexMicro.Desktop.Driver;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop;

public partial class MainWindow : Window
{
    private readonly record struct JoystickReport(
        double Angle,
        double Distance,
        string Label);

    private readonly CodexCompatibilityProbe _compatibilityProbe = new();
    private readonly VirtualMicroBroker _broker = new();
    private readonly CodexMicroLayoutObserver _layoutObserver = new();
    private readonly CodexMenuSelectionObserver _menuSelectionObserver = new();
    private readonly DialGestureTracker _dialGesture = new();
    private readonly SemaphoreSlim _encoderInputGate = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer
        _dialSelectionHideTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(2400),
        };
    private readonly LinkedList<JoystickReport> _joystickReportQueue = new();
    private readonly IReadOnlyDictionary<string, (Button Button, KeycapIcon Icon)>
        _actionKeys;
    private readonly IReadOnlyDictionary<string, Button> _joystickButtons;
    private readonly KeycapIcon[] _brandAwareIcons;
    private Button[] _agentKeys = [];
    private CodexCompatibilityResult? _compatibility;
    private TrayIconController? _trayIcon;
    private InactiveDialInputRouter? _inactiveDialInputRouter;
    private HwndSource? _windowSource;
    private bool _connecting;
    private bool _joystickDragging;
    private bool _joystickHasReportedState;
    private bool _joystickReportPumpActive;
    private bool _voicePressed;
    private int _joystickFeedbackVersion;
    private long _dialInputSequence;
    private long _dialWheelRouteSequence;
    private long _lastSlotLightingSequence;
    private int _dialSelectionFeedbackVersion;
    private int _dialSelectionHudVersion;
    private bool _dialSelectionFeedbackRunning;
    private bool _windowClosed;
    private Point _joystickDragOrigin;
    private string? _joystickActiveDirection;
    private double _dialVisualAngle = 42;
    private string _transportName = "虚拟 HID";
    private string _status = "正在检查 Codex 与虚拟 HID。";

    public MainWindow()
    {
        InitializeComponent();
        ApplyApplicationIcon();
        _agentKeys =
        [
            AgentKey0,
            AgentKey1,
            AgentKey2,
            AgentKey3,
            AgentKey4,
            AgentKey5,
        ];
        _actionKeys = new Dictionary<string, (Button, KeycapIcon)>(StringComparer.Ordinal)
        {
            ["ACT06"] = (ActionKey06, ActionIcon06),
            ["ACT07"] = (ActionKey07, ActionIcon07),
            ["ACT08"] = (ActionKey08, ActionIcon08),
            ["ACT09"] = (ActionKey09, ActionIcon09),
            ["ACT10_ACT11"] = (ActionKey10, ActionIcon10),
            ["ACT12"] = (ActionKey12, ActionIcon12),
        };
        _joystickButtons = new Dictionary<string, Button>(StringComparer.Ordinal)
        {
            ["up"] = JoystickUp,
            ["right"] = JoystickRight,
            ["down"] = JoystickDown,
            ["left"] = JoystickLeft,
        };
        _brandAwareIcons =
        [
            BrandCodexIcon,
            ActionIcon06,
            ActionIcon07,
            ActionIcon08,
            ActionIcon09,
            ActionIcon10,
            ActionIcon12,
        ];
        _broker.Log += Broker_Log;
        _broker.StateChanged += Broker_StateChanged;
        _broker.SlotLightingObserved += Broker_SlotLightingObserved;
        _layoutObserver.LayoutChanged += LayoutObserver_LayoutChanged;
        _dialSelectionHideTimer.Tick += DialSelectionHideTimer_Tick;
        InitializeHoverHelp();
        ApplyLayout(_layoutObserver.Current);
        SetStatus(_status);
    }

    private void InitializeHoverHelp()
    {
        for (var slotId = 0; slotId < _agentKeys.Length; slotId++)
        {
            SetHelp(
                _agentKeys[slotId],
                $"Agent 槽位 {slotId + 1}",
                $"AG{slotId:00} · 单击切换到该槽位；颜色由 Codex 状态同步。");
        }

        SetHelp(
            JoystickSurface,
            "模拟摇杆",
            "拖动黑色圆帽进行连续输入，或单击四周阴刻方向键。");
        SetHelp(
            JoystickCap,
            "模拟摇杆",
            "按住并向任意方向拖动，松开后自动回中。");
        SetHelp(
            SettingsKey,
            "Codex Micro 设置",
            "左键：长按 ENC 并打开 Micro 设置。\n右键：重新连接虚拟 HID。");
        SetHelp(CompatibilityLed, "Codex 兼容性", "正在等待兼容性检查。");
        SetHelp(DriverLed, "虚拟 HID", "正在等待驱动连接。");
        SetHelp(ActivityLed, "最近事件", "尚未发送事件。");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _trayIcon ??= new TrayIconController(
            RequestShowFromTray,
            RequestReconnectFromTray,
            RequestTopmostFromTray,
            RequestExitFromTray,
            Topmost);
        _inactiveDialInputRouter ??= new InactiveDialInputRouter(
            RouteInactiveDialWheel,
            RouteInactiveDialPointer);
        if (_inactiveDialInputRouter.Start())
        {
            AutomationProperties.SetItemStatus(
                DialButton,
                "未激活窗口滚轮捕获已就绪");
        }
        else
        {
            AutomationProperties.SetItemStatus(
                DialButton,
                $"旋钮输入捕获失败 · Win32 {_inactiveDialInputRouter.LastError}");
            SetHelp(
                DialButton,
                "选择旋钮",
                "全局旋钮捕获未启动；仍可上下拖动选择。\n左键：打开或确认。");
        }

        _layoutObserver.Start();
        await ConnectAsync();
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_joystickDragging)
        {
            EndJoystickDrag();
        }

        if (_dialGesture.IsPointerDown)
        {
            CancelDialGesture();
        }

        if (_voicePressed)
        {
            await ReleaseVoiceAsync();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _windowClosed = true;
        _dialSelectionFeedbackVersion++;
        _dialSelectionHideTimer.Stop();
        _dialSelectionHideTimer.Tick -= DialSelectionHideTimer_Tick;
        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowMessageHook);
            _windowSource = null;
        }

        _inactiveDialInputRouter?.Dispose();
        _inactiveDialInputRouter = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _joystickReportQueue.Clear();
        _layoutObserver.LayoutChanged -= LayoutObserver_LayoutChanged;
        _layoutObserver.Dispose();
        _broker.Dispose();
    }

    private async Task ConnectAsync()
    {
        if (_connecting)
        {
            return;
        }

        _connecting = true;
        SetLed(CompatibilityLed, "#B8B98B", "正在核对 Codex 指纹");
        SetLed(DriverLed, "#B8B98B", "正在查找 Codex Micro HID");
        SetLed(ActivityLed, "#B8B98B", "等待");
        SetStatus("正在核对当前 Codex Micro 协议并连接独立虚拟 HID。\n右键左下旋钮可重新连接。");
        try
        {
            _compatibility = await _compatibilityProbe.ProbeAsync();
            ApplyPackageAssets(_compatibility.PackageRoot);
            if (!_compatibility.IsCompatible)
            {
                SetLed(CompatibilityLed, "#FF7994", $"Codex {_compatibility.Build} · 指纹不兼容");
                SetLed(DriverLed, "#B8B98B", "兼容性门未通过，未连接驱动");
                SetStatus(_compatibility.Detail);
                return;
            }

            SetLed(
                CompatibilityLed,
                "#9EBDFF",
                $"Codex {_compatibility.Build} · 已验证\n{_compatibility.Fingerprint}");
            try
            {
                var info = _broker.Connect(_compatibility);
                _transportName = info.TransportName;
                SetLed(DriverLed, "#B8B98B", $"{info.TransportName} 已连接 · epoch {info.ConnectionEpoch:X16}");
                SetLed(ActivityLed, "#B8B98B", "HID / RPC 已就绪");
                SetStatus(
                    $"Codex {_compatibility.Build} 已验证。\n" +
                    $"{info.TransportName} 已连接 · epoch {info.ConnectionEpoch:X16} · drops {info.DroppedOutputReports}。\n" +
                    "点击黑色设置旋钮打开设置；Codex 键会将 Codex 切到前台。");
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or
                    Win32Exception or
                    InvalidDataException or
                    IOException)
            {
                SetLed(DriverLed, "#FFD66E", "虚拟 HID 未连接");
                SetLed(ActivityLed, "#B8B98B", "无事件链路");
                SetStatus(LocalizeDriverError(exception));
            }
        }
        finally
        {
            _connecting = false;
        }
    }

    private async void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string key })
        {
            await RunActionAsync(() => _broker.TapKeyAsync(key), key);
            if (key == "ACT12")
            {
                await ActivateCodexAsync();
            }
        }
    }

    private async void Voice_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.LeftButton != MouseButtonState.Pressed || _voicePressed)
        {
            return;
        }

        if (!_broker.IsReady)
        {
            await EnsureReadyFeedbackAsync();
            return;
        }

        _voicePressed = true;
        _ = ActionKey10.CaptureMouse();
        await RunActionAsync(
            () => _broker.SetKeyAsync("ACT10", true),
            "voice down");
    }

    private async void Voice_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ReleaseVoiceAsync();
    }

    private async void Voice_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (_voicePressed)
        {
            await ReleaseVoiceAsync(releaseCapture: false);
        }
    }

    private async void Voice_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter) || e.IsRepeat)
        {
            return;
        }

        e.Handled = true;
        if (_voicePressed)
        {
            return;
        }

        if (!_broker.IsReady)
        {
            await EnsureReadyFeedbackAsync();
            return;
        }

        _voicePressed = true;
        await RunActionAsync(
            () => _broker.SetKeyAsync("ACT10", true),
            "voice down");
    }

    private async void Voice_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter))
        {
            return;
        }

        e.Handled = true;
        await ReleaseVoiceAsync();
    }

    private async Task ReleaseVoiceAsync(bool releaseCapture = true)
    {
        if (!_voicePressed)
        {
            return;
        }

        _voicePressed = false;
        if (releaseCapture && Mouse.Captured == ActionKey10)
        {
            ActionKey10.ReleaseMouseCapture();
        }

        if (_broker.IsReady)
        {
            await RunActionAsync(
                () => _broker.SetKeyAsync("ACT10", false),
                "voice up");
        }
    }

    private void Dial_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        QueueDialWheelDelta(e.Delta);
    }

    private bool RouteInactiveDialWheel(Point screenPoint, int delta)
    {
        if (!IsScreenPointOverDial(screenPoint))
        {
            return false;
        }

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() =>
            {
                var routeSequence = ++_dialWheelRouteSequence;
                AutomationProperties.SetItemStatus(
                    DialButton,
                    $"滚轮路由已接收 · #{routeSequence}");
                QueueDialWheelDelta(delta);
            }));
        return true;
    }

    private bool RouteInactiveDialPointer(RoutedDialPointerInput input)
    {
        if (input.Action == RoutedDialPointerAction.Pressed &&
            !IsScreenPointOverDial(input.ScreenPoint))
        {
            return false;
        }

        if (!Dispatcher.CheckAccess() ||
            !IsVisible ||
            WindowState == WindowState.Minimized)
        {
            return false;
        }

        _ = Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => ProcessInactiveDialPointer(input)));
        return true;
    }

    private bool IsScreenPointOverDial(Point screenPoint)
    {
        if (!Dispatcher.CheckAccess() ||
            !IsVisible ||
            WindowState == WindowState.Minimized ||
            !DialButton.IsVisible ||
            DialButton.ActualWidth <= 0 ||
            DialButton.ActualHeight <= 0)
        {
            return false;
        }

        Point localPoint;
        try
        {
            localPoint = DialButton.PointFromScreen(screenPoint);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return localPoint.X >= 0 &&
            localPoint.Y >= 0 &&
            localPoint.X < DialButton.ActualWidth &&
            localPoint.Y < DialButton.ActualHeight;
    }

    private void ProcessInactiveDialPointer(RoutedDialPointerInput input)
    {
        double localY;
        try
        {
            localY = DialButton.PointFromScreen(input.ScreenPoint).Y;
        }
        catch (InvalidOperationException)
        {
            CancelDialGesture();
            return;
        }

        switch (input.Action)
        {
            case RoutedDialPointerAction.Pressed:
                if (!_broker.IsReady)
                {
                    _ = RunDialInputSafelyAsync(
                        EnsureReadyFeedbackAsync,
                        "旋钮按压");
                    return;
                }

                if (!_dialGesture.IsPointerDown)
                {
                    _dialGesture.Begin(localY);
                }

                break;
            case RoutedDialPointerAction.Moved:
                if (!_dialGesture.IsPointerDown)
                {
                    return;
                }

                var update = _dialGesture.Move(localY);
                if (update.Steps != 0)
                {
                    _ = RunDialInputSafelyAsync(
                        () => SendEncoderStepsAsync(update.Steps),
                        "旋钮拖动");
                }

                break;
            case RoutedDialPointerAction.Released:
                if (!_dialGesture.IsPointerDown)
                {
                    return;
                }

                if (_dialGesture.End())
                {
                    _ = RunDialInputSafelyAsync(TapEncoderAsync, "旋钮确认");
                }

                break;
        }
    }

    private void QueueDialWheelDelta(int delta) =>
        _ = RunDialInputSafelyAsync(
            async () =>
            {
                if (!_broker.IsReady)
                {
                    await EnsureReadyFeedbackAsync();
                    return;
                }

                var steps = _dialGesture.AddWheelDelta(delta);
                if (steps != 0)
                {
                    await SendEncoderStepsAsync(steps);
                }
            },
            "旋钮滚轮");

    private void Dial_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.LeftButton != MouseButtonState.Pressed || _dialGesture.IsPointerDown)
        {
            return;
        }

        if (!_broker.IsReady)
        {
            _ = RunDialInputSafelyAsync(
                EnsureReadyFeedbackAsync,
                "旋钮按压");
            return;
        }

        _dialGesture.Begin(e.GetPosition(DialButton).Y);
        _ = DialButton.CaptureMouse();
    }

    private void Dial_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dialGesture.IsPointerDown)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelDialGesture();
            return;
        }

        e.Handled = true;
        var update = _dialGesture.Move(e.GetPosition(DialButton).Y);
        if (update.Steps != 0)
        {
            _ = RunDialInputSafelyAsync(
                () => SendEncoderStepsAsync(update.Steps),
                "旋钮拖动");
        }
    }

    private void Dial_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!_dialGesture.IsPointerDown)
        {
            return;
        }

        var shouldTap = _dialGesture.End();
        if (Mouse.Captured == DialButton)
        {
            DialButton.ReleaseMouseCapture();
        }

        if (shouldTap)
        {
            _ = RunDialInputSafelyAsync(TapEncoderAsync, "旋钮确认");
        }
    }

    private void Dial_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dialGesture.IsPointerDown)
        {
            _dialGesture.Cancel();
        }
    }

    private void CancelDialGesture()
    {
        _dialGesture.Cancel();
        if (Mouse.Captured == DialButton)
        {
            DialButton.ReleaseMouseCapture();
        }
    }

    private async Task SendEncoderStepsAsync(int steps)
    {
        await _encoderInputGate.WaitAsync();
        try
        {
            var clockwise = steps > 0;
            for (var index = 0; index < Math.Abs(steps); index++)
            {
                var surface = await ObserveCurrentDialSurfaceAsync();
                var routesDialog = surface is
                {
                    Surface: CodexSelectionSurface.Dialog,
                };
                Exception? animationError = null;
                try
                {
                    AnimateDialStep(clockwise);
                }
                catch (Exception exception)
                {
                    animationError = exception;
                }

                var result = await RunActionAsync(
                    routesDialog
                        ? () => _broker.TapDialogKeyAsync(
                            VhfKeyboardKey.Tab,
                            shift: clockwise)
                        : () => _broker.StepEncoderAsync(clockwise),
                    routesDialog
                        ? clockwise
                            ? "确认框向上选择 · VHF Shift+Tab"
                            : "确认框向下选择 · VHF Tab"
                        : clockwise
                            ? "向上选择 · ENC_CW"
                            : "向下选择 · ENC_CC");
                if (result is not null && result.Value.Disposition is
                    MicroSendDisposition.Accepted or
                    MicroSendDisposition.OutcomeUnknown)
                {
                    var sequence = ++_dialInputSequence;
                    AutomationProperties.SetItemStatus(
                        DialButton,
                        $"{(routesDialog
                            ? clockwise ? "VHF Shift+Tab" : "VHF Tab"
                            : clockwise ? "ENC_CW" : "ENC_CC")} 已交付 · #{sequence}");
                    QueueDialSelectionFeedback();
                }

                if (animationError is not null)
                {
                    SetLed(ActivityLed, "#FFD66E", "旋钮动画已跳过");
                    SetStatus(
                        $"VHF 事件已继续交付；旋钮动画已跳过：{animationError.Message}");
                }

                if (result is null || result.Value.Disposition is
                    MicroSendDisposition.NotSent or
                    MicroSendDisposition.Rejected)
                {
                    break;
                }
            }
        }
        finally
        {
            _encoderInputGate.Release();
        }
    }

    private async Task TapEncoderAsync()
    {
        await _encoderInputGate.WaitAsync();
        try
        {
            var surface = await ObserveCurrentDialSurfaceAsync();
            var routesDialog = surface is
            {
                Surface: CodexSelectionSurface.Dialog,
            };
            var result = await RunActionAsync(
                routesDialog
                    ? () => _broker.TapDialogKeyAsync(VhfKeyboardKey.Enter)
                    : () => _broker.TapKeyAsync("ENC"),
                routesDialog
                    ? "确认当前对话框选项 · VHF Enter"
                    : "打开或确认当前选项 · ENC");
            if (result is not null && result.Value.Disposition is
                MicroSendDisposition.Accepted or
                MicroSendDisposition.OutcomeUnknown)
            {
                QueueDialSelectionFeedback();
            }
        }
        finally
        {
            _encoderInputGate.Release();
        }
    }

    private async Task<CodexMenuSelection?> ObserveCurrentDialSurfaceAsync()
    {
        try
        {
            return await _menuSelectionObserver.ObserveCurrentAsync(
                _compatibility?.PackageRoot);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.Runtime.InteropServices.COMException or
                ElementNotAvailableException)
        {
            AutomationProperties.SetItemStatus(
                DialButton,
                $"当前选择面读取已跳过 · {exception.Message}");
            return null;
        }
    }

    internal void AnimateDialStep(bool clockwise)
    {
        DialButton.ApplyTemplate();
        if (DialButton.Template.FindName("DialIndicator", DialButton) is not
            Border { RenderTransform: RotateTransform rotation } indicator)
        {
            return;
        }

        rotation = rotation.CloneCurrentValue();
        indicator.RenderTransform = rotation;

        _dialVisualAngle += clockwise ? 18 : -18;
        rotation.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation
            {
                To = _dialVisualAngle,
                Duration = TimeSpan.FromMilliseconds(105),
                EasingFunction = new CubicEase
                {
                    EasingMode = EasingMode.EaseOut,
                },
                FillBehavior = FillBehavior.HoldEnd,
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void QueueDialSelectionFeedback()
    {
        _dialSelectionFeedbackVersion++;
        if (_dialSelectionFeedbackRunning || _windowClosed)
        {
            return;
        }

        _dialSelectionFeedbackRunning = true;
        _ = ObserveDialSelectionFeedbackAsync();
    }

    private async Task ObserveDialSelectionFeedbackAsync()
    {
        try
        {
            int observedVersion;
            do
            {
                observedVersion = _dialSelectionFeedbackVersion;
                var selection = await _menuSelectionObserver.ObserveAsync(
                    _compatibility?.PackageRoot);
                if (_windowClosed)
                {
                    return;
                }

                if (selection is { } current)
                {
                    ShowDialSelectionFeedback(current.DisplayText);
                }
            }
            while (observedVersion != _dialSelectionFeedbackVersion);
        }
        catch (OperationCanceledException)
        {
            // Closing the window is allowed to abandon secondary feedback.
        }
        catch (Exception exception)
        {
            AutomationProperties.SetItemStatus(
                DialButton,
                $"菜单位置读取已跳过 · {exception.Message}");
        }
        finally
        {
            _dialSelectionFeedbackRunning = false;
            if (!_windowClosed &&
                DialSelectionHud.Visibility != Visibility.Visible &&
                _dialSelectionFeedbackVersion > 0)
            {
                // A final report can arrive while the observer is unwinding.
                // Starting once more is bounded by the unchanged version.
                var version = _dialSelectionFeedbackVersion;
                _ = Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        if (!_windowClosed &&
                            version != _dialSelectionFeedbackVersion)
                        {
                            QueueDialSelectionFeedback();
                        }
                    }));
            }
        }
    }

    private void ShowDialSelectionFeedback(string text)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                new Action(() => ShowDialSelectionFeedback(text)));
            return;
        }

        _dialSelectionHudVersion++;
        DialSelectionText.Text = text;
        DialSelectionHud.Visibility = Visibility.Visible;
        DialSelectionHud.BeginAnimation(OpacityProperty, null);
        DialSelectionHud.Opacity = 1;
        AutomationProperties.SetItemStatus(DialButton, text);
        _dialSelectionHideTimer.Stop();
        _dialSelectionHideTimer.Start();
    }

    private void DialSelectionHideTimer_Tick(object? sender, EventArgs e)
    {
        _dialSelectionHideTimer.Stop();
        var version = _dialSelectionHudVersion;
        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut,
            },
            FillBehavior = FillBehavior.HoldEnd,
        };
        fade.Completed += (_, _) =>
        {
            if (version == _dialSelectionHudVersion)
            {
                DialSelectionHud.Visibility = Visibility.Collapsed;
            }
        };
        DialSelectionHud.BeginAnimation(
            OpacityProperty,
            fade,
            HandoffBehavior.SnapshotAndReplace);
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        await OpenCodexMicroSettingsAsync("打开 Codex Micro 设置");
    }

    private async Task OpenCodexMicroSettingsAsync(string label)
    {
        // Codex's own Micro bridge owns this route: an ENC press held for
        // 500 ms navigates directly to /settings/codex-micro. Do not follow
        // it with a generic settings deep link, which would overwrite the
        // correct in-app destination with the settings landing page.
        MicroSendResult? result;
        await _encoderInputGate.WaitAsync();
        try
        {
            result = await RunActionAsync(
                () => _broker.OpenCodexMicroSettingsAsync(),
                label);
        }
        finally
        {
            _encoderInputGate.Release();
        }

        if (result is null || result.Value.Disposition is not (
            MicroSendDisposition.Accepted or
            MicroSendDisposition.OutcomeUnknown))
        {
            return;
        }

        if (await ActivateCodexAsync(140))
        {
            SetLed(ActivityLed, "#9EBDFF", "Codex Micro 设置已打开");
            SetStatus("Codex Micro 设置已打开，并已将 Codex 主窗口切到前台。");
        }
    }

    private async Task RunDialInputSafelyAsync(
        Func<Task> action,
        string operation)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            CancelDialGesture();
            SetLed(ActivityLed, "#FF7994", $"{operation}失败");
            SetStatus($"{operation}失败，但模拟器仍在运行：{exception.Message}");
        }
    }

    private async Task<bool> ActivateCodexAsync(
        int initialDelayMilliseconds = 90)
    {
        await Task.Delay(initialDelayMilliseconds);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (CodexWindowActivator.TryActivate(
                    _compatibility?.PackageRoot))
                {
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception or
                    EntryPointNotFoundException or
                    DllNotFoundException)
            {
                SetLed(ActivityLed, "#FF7994", "Codex 主窗口激活失败");
                SetStatus($"Codex 主窗口激活失败。\n{exception.Message}");
                return false;
            }

            if (attempt < 4)
            {
                await Task.Delay(140);
            }
        }

        SetLed(ActivityLed, "#FFD66E", "未找到 Codex 主窗口");
        SetStatus("事件已交付，但当前没有可激活的 Codex 主窗口。");
        return false;
    }

    private async void Settings_PreviewMouseRightButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        await ConnectAsync();
    }

    private async void Joystick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag })
        {
            return;
        }

        var parts = tag.Split('|');
        if (
            parts.Length != 2 ||
            !double.TryParse(
                parts[0],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var angle))
        {
            return;
        }

        var radians = angle * Math.Tau;
        var feedbackVersion = ++_joystickFeedbackVersion;
        StopJoystickAnimations();
        JoystickTranslate.X = Math.Cos(radians) * 11;
        JoystickTranslate.Y = Math.Sin(radians) * 11;
        JoystickScale.ScaleX = 0.965;
        JoystickScale.ScaleY = 0.965;
        try
        {
            await RunActionAsync(
                () => _broker.MoveJoystickAsync(angle, 1, parts[1]),
                $"analog {parts[1]}");
        }
        finally
        {
            if (feedbackVersion == _joystickFeedbackVersion)
            {
                AnimateJoystickReturn();
            }
        }
    }

    private async void JoystickCap_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ++_joystickFeedbackVersion;
        StopJoystickAnimations();
        JoystickScale.ScaleX = 0.965;
        JoystickScale.ScaleY = 0.965;
        _joystickDragging = true;
        _joystickHasReportedState = false;
        _joystickActiveDirection = null;
        _joystickDragOrigin = e.GetPosition(JoystickSurface);
        _ = JoystickCap.CaptureMouse();
        if (!_broker.IsReady)
        {
            await EnsureReadyFeedbackAsync();
        }
    }

    private void JoystickCap_MouseMove(
        object sender,
        MouseEventArgs e)
    {
        if (!_joystickDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        e.Handled = true;
        UpdateJoystickDrag(e.GetPosition(JoystickSurface));
    }

    private void JoystickCap_MouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_joystickDragging)
        {
            return;
        }

        e.Handled = true;
        UpdateJoystickDrag(e.GetPosition(JoystickSurface));
        EndJoystickDrag();
    }

    private void JoystickCap_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (_joystickDragging)
        {
            EndJoystickDrag(releaseCapture: false);
        }
    }

    private void UpdateJoystickDrag(Point position)
    {
        var vector = JoystickGeometry.ResolveDelta(
            position.X - _joystickDragOrigin.X,
            position.Y - _joystickDragOrigin.Y);
        JoystickTranslate.X = vector.VisualX;
        JoystickTranslate.Y = vector.VisualY;
        QueueJoystickReport(
            vector.Angle,
            vector.Distance,
            vector.Direction ?? "center");
        _joystickHasReportedState = true;

        if (vector.Distance < JoystickGeometry.ActivationDistance)
        {
            _joystickActiveDirection = null;
            return;
        }

        if (vector.Direction == _joystickActiveDirection)
        {
            return;
        }

        _joystickActiveDirection = vector.Direction;
        SetLed(
            ActivityLed,
            "#9EBDFF",
            $"摇杆 {vector.Direction} · {vector.Distance:P0}");
    }

    private void EndJoystickDrag(bool releaseCapture = true)
    {
        if (!_joystickDragging)
        {
            return;
        }

        _joystickDragging = false;
        _joystickActiveDirection = null;
        AnimateJoystickReturn();
        if (releaseCapture && Mouse.Captured == JoystickCap)
        {
            JoystickCap.ReleaseMouseCapture();
        }

        if (_joystickHasReportedState)
        {
            QueueJoystickReport(0, 0, "center");
            _joystickHasReportedState = false;
        }
    }

    private void StopJoystickAnimations()
    {
        JoystickTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            null);
        JoystickTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            null);
        JoystickScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        JoystickScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
    }

    private void AnimateJoystickReturn()
    {
        var fromX = JoystickTranslate.X;
        var fromY = JoystickTranslate.Y;
        var fromScaleX = JoystickScale.ScaleX;
        var fromScaleY = JoystickScale.ScaleY;
        StopJoystickAnimations();

        JoystickTranslate.X = 0;
        JoystickTranslate.Y = 0;
        JoystickScale.ScaleX = 1;
        JoystickScale.ScaleY = 1;

        var easing = new BackEase
        {
            Amplitude = 0.28,
            EasingMode = EasingMode.EaseOut,
        };
        var duration = new Duration(TimeSpan.FromMilliseconds(180));
        JoystickTranslate.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(fromX, 0, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        JoystickTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(fromY, 0, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        JoystickScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(fromScaleX, 1, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
        JoystickScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(fromScaleY, 1, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop,
            });
    }

    private void QueueJoystickReport(
        double angle,
        double distance,
        string label)
    {
        if (!_broker.IsReady)
        {
            return;
        }

        var report = new JoystickReport(angle, distance, label);
        var last = _joystickReportQueue.Last;
        if (last is not null && last.Value.Distance > 0 && distance > 0)
        {
            // Coalesce intermediate drag samples while preserving every
            // neutral report between separate gestures.
            last.Value = report;
        }
        else if (last is null || last.Value.Distance > 0 || distance > 0)
        {
            _joystickReportQueue.AddLast(report);
        }

        if (!_joystickReportPumpActive)
        {
            _ = DrainJoystickReportsAsync();
        }
    }

    private async Task DrainJoystickReportsAsync()
    {
        _joystickReportPumpActive = true;
        try
        {
            while (_joystickReportQueue.First is { } node)
            {
                var report = node.Value;
                _joystickReportQueue.RemoveFirst();
                var result = await _broker.SetJoystickStateAsync(
                    report.Angle,
                    report.Distance,
                    report.Label);
                if (result.Disposition is
                    MicroSendDisposition.NotSent or
                    MicroSendDisposition.Rejected)
                {
                    _joystickReportQueue.Clear();
                    SetLed(ActivityLed, "#FF7994", "摇杆事件未发送");
                    SetStatus(result.Detail);
                    break;
                }

                if (result.Disposition == MicroSendDisposition.OutcomeUnknown)
                {
                    SetLed(ActivityLed, "#FFD66E", "摇杆事件结果未知");
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                IOException or
                Win32Exception or
                InvalidDataException)
        {
            _joystickReportQueue.Clear();
            SetLed(ActivityLed, "#FF7994", "摇杆事件发送失败");
            SetStatus(exception.Message);
        }
        finally
        {
            _joystickReportPumpActive = false;
        }
    }

    private async Task<MicroSendResult?> RunActionAsync(
        Func<Task<MicroSendResult>> action,
        string label)
    {
        if (!_broker.IsReady)
        {
            await EnsureReadyFeedbackAsync();
            return null;
        }

        SetLed(ActivityLed, "#9EBDFF", $"正在发送 {label}");
        try
        {
            var result = await action();
            switch (result.Disposition)
            {
                case MicroSendDisposition.Accepted:
                    SetLed(ActivityLed, "#9EBDFF", $"{label} 已交付");
                    SetStatus($"{label} 已通过 {_transportName} 交付。\n{result.Detail}");
                    break;
                case MicroSendDisposition.OutcomeUnknown:
                    SetLed(ActivityLed, "#FFD66E", $"{label} 结果未知");
                    SetStatus($"{label} 效果未知；为避免双执行不会自动重试。\n{result.Detail}");
                    break;
                default:
                    SetLed(ActivityLed, "#FF7994", $"{label} 未发送");
                    SetStatus($"{label} 未发送。\n{result.Detail}");
                    break;
            }

            return result;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                IOException or
                Win32Exception or
                InvalidDataException)
        {
            SetLed(ActivityLed, "#FF7994", "事件发送失败");
            SetStatus(exception.Message);
            return null;
        }
    }

    private async Task EnsureReadyFeedbackAsync()
    {
        SetLed(ActivityLed, "#FF7994", "虚拟 HID 尚未连接");
        SetStatus("虚拟 HID 链路尚未就绪。\n右键左下黑色旋钮重新连接。");
        await Task.Delay(120);
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = PresentationSource.FromVisual(this) as HwndSource;
        _windowSource?.AddHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(
        IntPtr windowHandle,
        int message,
        IntPtr wordParameter,
        IntPtr longParameter,
        ref bool handled)
    {
        if (NonActivatingWindow.TryHandleMessage(
            message,
            ref handled,
            out var nonActivatingResult))
        {
            return nonActivatingResult;
        }

        if (
            message != BorderlessResize.WmNcHitTest ||
            ResizeMode == ResizeMode.NoResize ||
            WindowState != WindowState.Normal)
        {
            return IntPtr.Zero;
        }

        var packed = longParameter.ToInt64();
        var screenPoint = new Point(
            unchecked((short)(packed & 0xFFFF)),
            unchecked((short)((packed >> 16) & 0xFFFF)));
        var clientPoint = PointFromScreen(screenPoint);
        var hit = BorderlessResize.HitTest(
            new Size(ActualWidth, ActualHeight),
            clientPoint,
            10);
        if (hit == 0)
        {
            return IntPtr.Zero;
        }

        handled = true;
        return new IntPtr(hit);
    }

    private void DeviceFrame_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void DeviceFrame_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject) is not null)
        {
            e.Handled = true;
        }
    }

    private void DeviceContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        TopmostMenuItem.IsChecked = Topmost;
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetTopmostState(TopmostMenuItem.IsChecked);

    private async void ReconnectMenuItem_Click(object sender, RoutedEventArgs e) =>
        await ConnectAsync();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Close();

    private void SetTopmostState(bool value)
    {
        Topmost = value;
        TopmostMenuItem.IsChecked = value;
        _trayIcon?.SetTopmost(value);
        SetStatus(value
            ? "窗口已置顶。右击机身空白处可取消置顶。"
            : "窗口已取消置顶。右击机身空白处可再次置顶。");
    }

    private void Broker_Log(object? sender, string message)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            SetHelp(ActivityLed, "最近事件", message);
        });
    }

    private void Broker_StateChanged(object? sender, string state)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (state.StartsWith("faulted:", StringComparison.Ordinal))
            {
                var detail = state["faulted:".Length..];
                SetLed(DriverLed, "#FF7994", "虚拟 HID 链路故障");
                SetLed(ActivityLed, "#FF7994", "Broker 已停止");
                SetStatus(
                    "虚拟 HID 链路已停止；右键左下黑色旋钮重新连接。\n" +
                    detail);
            }
        });
    }

    private void Broker_SlotLightingObserved(
        object? sender,
        SlotLightingSnapshot snapshot)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (snapshot.Sequence <= _lastSlotLightingSequence)
            {
                return;
            }

            _lastSlotLightingSequence = snapshot.Sequence;
            for (var slotId = 0; slotId < _agentKeys.Length; slotId++)
            {
                _agentKeys[slotId].BorderBrush = new SolidColorBrush(
                    Color.FromArgb(0, 0x8D, 0xB5, 0xFF));
                SetHelp(
                    _agentKeys[slotId],
                    $"Agent 槽位 {slotId + 1}",
                    $"AG{slotId:00} · 空闲 · 单击切换到该槽位。");
            }

            var activeSlots = 0;
            foreach (var slot in snapshot.Slots)
            {
                if (slot.SlotId is < 0 or >= 6)
                {
                    continue;
                }

                var active = slot.Color != 0 && slot.Brightness > 0;
                if (active)
                {
                    activeSlots++;
                }

                var color = !active
                    ? Color.FromArgb(0, 0x8D, 0xB5, 0xFF)
                    : Color.FromRgb(
                        (byte)(slot.Color >> 16),
                        (byte)(slot.Color >> 8),
                        (byte)slot.Color);
                _agentKeys[slot.SlotId].BorderBrush = new SolidColorBrush(color);
                SetHelp(
                    _agentKeys[slot.SlotId],
                    $"Agent 槽位 {slot.SlotId + 1}",
                    $"AG{slot.SlotId:00} · " +
                    (active
                        ? $"活动 · #{slot.Color:X6} · effect {slot.Effect}"
                        : "空闲") +
                    " · 单击切换到该槽位。");
            }

            SetHelp(
                DriverLed,
                "虚拟 HID",
                $"{_transportName} · Agent 状态已同步 · {activeSlots} 个活动槽位");
        });
    }

    private void LayoutObserver_LayoutChanged(
        object? sender,
        CodexMicroLayoutSnapshot snapshot)
    {
        _ = Dispatcher.InvokeAsync(() => ApplyLayout(snapshot));
    }

    private void ApplyLayout(CodexMicroLayoutSnapshot snapshot)
    {
        foreach (var (slotId, presentation) in _actionKeys)
        {
            var binding = snapshot.GetSlot(slotId);
            var definition = CodexKeycapCatalog.Get(binding.KeycapId);
            presentation.Icon.KeycapId = binding.KeycapId;
            var action = binding.CommandId ?? definition.DefaultAction;
            var physicalKeys = slotId == "ACT10_ACT11"
                ? "ACT10 / ACT11"
                : slotId;
            var gesture = slotId == "ACT10_ACT11"
                ? "按住说话，松开结束。"
                : "单击执行。";
            SetHelp(
                presentation.Button,
                definition.Label,
                $"{physicalKeys} · {action}\n{gesture}键帽图标随 Codex Micro 设置同步。");
        }

        SetHelp(
            DialButton,
            snapshot.EncoderMode == "reasoning"
                ? "推理强度旋钮"
                : "选择旋钮",
            snapshot.EncoderMode == "reasoning"
                ? "滚轮/上下拖动：调节推理强度或移动菜单选项。\n左键：打开或确认。"
                : "滚轮/上下拖动：移动输入区控件或菜单选项。\n左键：打开或确认。");

        var defaultAnalog = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["up"] = "composer.togglePlanMode",
            ["right"] = "navigateForward",
            ["down"] = "toggleSidebar",
            ["left"] = "navigateBack",
        };
        foreach (var (direction, button) in _joystickButtons)
        {
            var action = snapshot.AnalogActions.TryGetValue(direction, out var configured)
                ? configured
                : defaultAnalog[direction];
            SetHelp(
                button,
                $"摇杆方向 · {direction}",
                $"{action} · 单击触发并自动回中。");
        }
    }

    private void ApplyPackageAssets(string? packageRoot)
    {
        foreach (var icon in _brandAwareIcons)
        {
            icon.PackageRoot = packageRoot;
        }
    }

    private void ApplyApplicationIcon()
    {
        using var icon = TrayIconController.CreateApplicationIcon();
        var image = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        image.Freeze();
        Icon = image;
    }

    private void SetStatus(string value)
    {
        _status = value;
        AutomationProperties.SetHelpText(this, value);
        SetHelp(
            DeviceFrame,
            "Codex Micro Simulator",
            $"{value}\n\n拖动机身移动 · 拖动边缘缩放 · 右击机身打开窗口菜单");
    }

    private void RequestShowFromTray() => DispatchToWindow(() =>
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        NonActivatingWindow.ShowWithoutActivation(
            _windowSource?.Handle ?? IntPtr.Zero,
            Topmost);
    });

    private void RequestReconnectFromTray() =>
        DispatchToWindow(() => _ = ConnectAsync());

    private void RequestTopmostFromTray(bool value) =>
        DispatchToWindow(() => SetTopmostState(value));

    private void RequestExitFromTray() => DispatchToWindow(Close);

    private void DispatchToWindow(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = Dispatcher.InvokeAsync(action);
        }
    }

    private void SetLed(
        Ellipse led,
        string color,
        string tooltip)
    {
        led.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
        var title = led == CompatibilityLed
            ? "Codex 兼容性"
            : led == DriverLed
                ? "虚拟 HID"
                : "最近事件";
        SetHelp(led, title, tooltip);
    }

    private static void SetHelp(
        FrameworkElement element,
        string title,
        string detail)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x34, 0x38)),
        });
        content.Children.Add(new TextBlock
        {
            Text = detail,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 11.5,
            LineHeight = 17,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x70, 0x76)),
        });

        element.ToolTip = new ToolTip
        {
            Content = content,
            IsHitTestVisible = false,
            Placement = PlacementMode.MousePoint,
        };
        ToolTipService.SetInitialShowDelay(element, 320);
        ToolTipService.SetBetweenShowDelay(element, 100);
        ToolTipService.SetShowDuration(element, 16000);
        AutomationProperties.SetName(element, title);
        AutomationProperties.SetHelpText(element, detail);
    }

    private static string LocalizeDriverError(Exception exception) =>
        exception.Message.Contains("device interface is not present", StringComparison.OrdinalIgnoreCase)
            ? "Codex Micro 虚拟 HID 尚未出现。"
            : $"虚拟 HID 连接失败：{exception.Message}";

    private static T? FindAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
