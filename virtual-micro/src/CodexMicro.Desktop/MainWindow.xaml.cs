using System.ComponentModel;
using System.Diagnostics;
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
using System.Windows.Shapes;
using AgentController.MicroBroker;
using CodexMicro.Desktop.Controls;
using CodexMicro.Desktop.Services;
using CodexMicro.Protocol;

namespace CodexMicro.Desktop;

public partial class MicroSurfaceWindow : Window
{
    private static readonly TimeSpan EncoderStepInterval =
        TimeSpan.FromMilliseconds(24);
    private static readonly TimeSpan EncoderIntentMaximumAge =
        TimeSpan.FromMilliseconds(180);

    private readonly record struct JoystickReport(
        double Angle,
        double Distance,
        string Label);

    private readonly VirtualMicroBroker _broker = new();
    private readonly CodexMicroLayoutObserver _layoutObserver = new();
    private readonly CodexAgentRosterObserver _agentRosterObserver = new();
    private readonly CodexMenuSelectionObserver _menuSelectionObserver = new();
    private readonly DialGestureTracker _dialGesture = new();
    private readonly EncoderStepAccumulator _encoderSteps = new(3);
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
    private Border[] _agentWideGlows = [];
    private Border[] _agentNearGlows = [];
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
    private SlotLightingSnapshot? _latestSlotLighting;
    private CodexAgentRosterSnapshot? _latestAgentRoster;
    private int? _currentAgentSlotId;
    private int _dialSelectionFeedbackVersion;
    private int _dialSelectionHudVersion;
    private bool _dialSelectionFeedbackRunning;
    private bool _encoderStepPumpRunning;
    private bool _dialSurfaceMayBeMounting;
    private long _dialSurfaceNotBeforeTimestamp;
    private CodexMenuSelection? _cachedDialSelection;
    private bool _windowClosed;
    private bool _allowApplicationClose;
    private Point _joystickDragOrigin;
    private string? _joystickActiveDirection;
    private double _dialVisualAngle = 42;
    private string _transportName = "虚拟 HID";
    private string _status = "正在检查 Codex 与虚拟 HID。";

    public MicroSurfaceWindow()
    {
        InitializeComponent();
        _agentKeys =
        [
            AgentKey0,
            AgentKey1,
            AgentKey2,
            AgentKey3,
            AgentKey4,
            AgentKey5,
        ];
        _agentWideGlows =
        [
            AgentGlowWide0,
            AgentGlowWide1,
            AgentGlowWide2,
            AgentGlowWide3,
            AgentGlowWide4,
            AgentGlowWide5,
        ];
        _agentNearGlows =
        [
            AgentGlowNear0,
            AgentGlowNear1,
            AgentGlowNear2,
            AgentGlowNear3,
            AgentGlowNear4,
            AgentGlowNear5,
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
        _agentRosterObserver.RosterChanged += AgentRosterObserver_RosterChanged;
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
        SetHelp(RuntimeLed, "Codex 运行时握手", "正在等待 Codex 运行时能力信号。");
        SetHelp(DriverLed, "虚拟 HID", "正在等待驱动连接。");
        SetHelp(ActivityLed, "最近事件", "尚未发送事件。");
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
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
                "全局旋钮捕获未启动；仍可按住左键上下或左右拖动选择。\n短按：打开或确认。");
        }

        _layoutObserver.Start();
        _agentRosterObserver.Start();
        _latestAgentRoster = _agentRosterObserver.Current;
        ResolveCurrentAgentSlot();
        RefreshAgentSlotPresentation();
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
        _encoderSteps.Clear();
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
        _joystickReportQueue.Clear();
        _layoutObserver.LayoutChanged -= LayoutObserver_LayoutChanged;
        _layoutObserver.Dispose();
        _agentRosterObserver.RosterChanged -= AgentRosterObserver_RosterChanged;
        _agentRosterObserver.Dispose();
        _broker.Dispose();
    }

    private async Task ConnectAsync()
    {
        if (_connecting)
        {
            return;
        }

        _connecting = true;
        SetLed(RuntimeLed, "#B8B98B", "正在探测 Codex 运行时能力");
        SetLed(DriverLed, "#B8B98B", "正在查找 Codex Micro HID");
        SetLed(ActivityLed, "#B8B98B", "等待");
        SetStatus("正在连接共享 Micro Broker 与 Codex Micro HID。\n右键左下旋钮可重新连接。");
        try
        {
            await Task.Yield();
            ApplyPackageAssets(packageRoot: null);
            try
            {
                var info = _broker.Connect();
                _transportName = info.TransportName;
                SetLed(
                    DriverLed,
                    "#9EBDFF",
                    $"{info.TransportName} 已连接 · epoch {info.ConnectionEpoch:X16}");
                if (_broker.CodexLinkObserved)
                {
                    ApplyRuntimeReadyState();
                }
                else
                {
                    ApplyTransportReadyState();
                }
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
            var isAgentKey = TryParseAgentSlot(key, out var selectedSlot);
            try
            {
                if (key == "AG00")
                {
                    CodexRequestCardCancellationResult cancellation;
                    try
                    {
                        cancellation = await Task.Run(
                            () => CodexRequestCardCancellation
                                .TryCancelForegroundRequestCard());
                    }
                    catch
                    {
                        cancellation = CodexRequestCardCancellationResult.Failed;
                    }

                    switch (cancellation)
                    {
                        case CodexRequestCardCancellationResult.Cancelled:
                            SetLed(ActivityLed, "#9EBDFF", "Plan 提问已取消");
                            SetStatus("已向当前 Plan 提问卡片发送一次 Escape；未追加 AG00。");
                            return;
                        case CodexRequestCardCancellationResult.Blocked:
                            SetLed(ActivityLed, "#FF7994", "Plan 提问卡片无法安全确认");
                            SetStatus("检测到疑似或不唯一的 Plan 提问卡片；本次操作已消费，未发送 AG00。");
                            return;
                        case CodexRequestCardCancellationResult.Failed:
                            SetLed(ActivityLed, "#FF7994", "Plan 取消未发送");
                            SetStatus("Plan 提问卡片取消失败；本次操作已消费，未发送 AG00。");
                            return;
                    }
                }

                var result = await RunActionAsync(
                    () => _broker.TapKeyAsync(key),
                    key);
                if (
                    result is { WasPossiblySent: true } &&
                    isAgentKey)
                {
                    _currentAgentSlotId = selectedSlot;
                    RefreshAgentSlotPresentation();
                }

            }
            finally
            {
                // AG00's Plan-card compatibility branch can consume the input
                // before HID delivery. Foreground activation is an independent
                // part of every Agent click, including a repeated click on the
                // already-selected slot, so it must survive every early return.
                if (ShouldActivateCodexForKey(key))
                {
                    await ActivateCodexAsync(
                        initialDelayMilliseconds: isAgentKey ? 0 : 90);
                }
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
        SetVoiceRecordingVisual(recording: true);
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
        SetVoiceRecordingVisual(recording: true);
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
        SetVoiceRecordingVisual(recording: false);
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

    private static bool TryParseAgentSlot(string key, out int slotId)
    {
        slotId = -1;
        return
            key.Length == 4 &&
            key.StartsWith("AG", StringComparison.Ordinal) &&
            int.TryParse(
                key.AsSpan(2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out slotId) &&
            slotId is >= 0 and < 6;
    }

    internal static bool ShouldActivateCodexForKey(string key) =>
        key == "ACT12" || TryParseAgentSlot(key, out _);

    internal void SetVoiceRecordingVisual(bool recording)
    {
        ActionIcon10.IconBrush = new SolidColorBrush(
            recording
                ? Color.FromRgb(0x0C, 0x8E, 0x7E)
                : Color.FromRgb(0x17, 0x17, 0x17));
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
        Point localPoint;
        try
        {
            localPoint = DialButton.PointFromScreen(input.ScreenPoint);
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
                    _dialGesture.Begin(localPoint.X, localPoint.Y);
                }

                break;
            case RoutedDialPointerAction.Moved:
                if (!_dialGesture.IsPointerDown)
                {
                    return;
                }

                var update = _dialGesture.Move(localPoint.X, localPoint.Y);
                if (update.Steps != 0)
                {
                    EnqueueEncoderSteps(update.Steps, "旋钮拖动");
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

    private void QueueDialWheelDelta(int delta)
    {
        if (!_broker.IsReady)
        {
            _ = RunDialInputSafelyAsync(
                EnsureReadyFeedbackAsync,
                "旋钮滚轮");
            return;
        }

        var steps = _dialGesture.AddWheelDelta(delta);
        if (steps != 0)
        {
            EnqueueEncoderSteps(steps, "旋钮滚轮");
        }
    }

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

        var pointer = e.GetPosition(DialButton);
        _dialGesture.Begin(pointer.X, pointer.Y);
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
        var pointer = e.GetPosition(DialButton);
        var update = _dialGesture.Move(pointer.X, pointer.Y);
        if (update.Steps != 0)
        {
            EnqueueEncoderSteps(update.Steps, "旋钮拖动");
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

    private void EnqueueEncoderSteps(int steps, string operation)
    {
        _encoderSteps.Add(steps, Stopwatch.GetTimestamp());
        if (_encoderStepPumpRunning || _encoderSteps.Pending == 0)
        {
            return;
        }

        StartEncoderStepPump(operation);
    }

    private void StartEncoderStepPump(string operation)
    {
        _encoderStepPumpRunning = true;
        _ = RunDialInputSafelyAsync(
            PumpEncoderStepsAsync,
            operation);
    }

    private async Task PumpEncoderStepsAsync()
    {
        try
        {
            while (!_windowClosed)
            {
                var intent = _encoderSteps.TakeNext(
                    Stopwatch.GetTimestamp(),
                    ToStopwatchTicks(EncoderIntentMaximumAge));
                if (intent is null)
                {
                    return;
                }

                var sendStarted = Stopwatch.GetTimestamp();
                await SendEncoderStepAsync(intent.Value);
                if (Stopwatch.GetTimestamp() - sendStarted >
                    ToStopwatchTicks(EncoderIntentMaximumAge))
                {
                    // Do not replay pointer input accumulated while a driver
                    // call or another encoder action was stalled.
                    _encoderSteps.Clear();
                }

                if (_encoderSteps.Pending != 0)
                {
                    await Task.Delay(EncoderStepInterval);
                }
            }
        }
        finally
        {
            _encoderStepPumpRunning = false;
            if (!_windowClosed && _encoderSteps.Pending != 0)
            {
                StartEncoderStepPump("旋钮合并输入");
            }
        }
    }

    private async Task SendEncoderStepAsync(EncoderStepIntent intent)
    {
        await _encoderInputGate.WaitAsync();
        try
        {
            if (_dialSurfaceMayBeMounting)
            {
                _dialSurfaceMayBeMounting = false;
                var remainingTicks =
                    _dialSurfaceNotBeforeTimestamp - Stopwatch.GetTimestamp();
                if (remainingTicks > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(
                        (double)remainingTicks / Stopwatch.Frequency));
                }
            }

            if (Stopwatch.GetTimestamp() - intent.InputTimestamp >
                ToStopwatchTicks(EncoderIntentMaximumAge))
            {
                return;
            }

            var step = intent.Direction;
            var clockwise = step > 0;
            var routesDialog = _cachedDialSelection is
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

            Func<Task<MicroSendResult>> sendStep = routesDialog
                ? () => _broker.TapDialogKeyAsync(
                    BrokerKeyboardKey.Tab,
                    shift: clockwise)
                : () => _broker.StepEncoderAsync(clockwise);
            var stepLabel = routesDialog
                ? clockwise
                    ? "确认框向上选择 · VHF Shift+Tab"
                    : "确认框向下选择 · VHF Tab"
                : clockwise
                    ? "向上选择 · ENC_CW"
                    : "向下选择 · ENC_CC";
            var result = await RunActionAsync(sendStep, stepLabel);
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
            else
            {
                _encoderSteps.Clear();
            }

            if (animationError is not null)
            {
                SetLed(ActivityLed, "#FFD66E", "旋钮动画已跳过");
                SetStatus(
                    $"VHF 事件已继续交付；旋钮动画已跳过：{animationError.Message}");
            }
        }
        finally
        {
            _encoderInputGate.Release();
        }
    }

    private async Task TapEncoderAsync()
    {
        _encoderSteps.Clear();
        await _encoderInputGate.WaitAsync();
        try
        {
            var routesDialog = _cachedDialSelection is
            {
                Surface: CodexSelectionSurface.Dialog,
            };
            var result = await RunActionAsync(
                routesDialog
                    ? () => _broker.TapDialogKeyAsync(BrokerKeyboardKey.Enter)
                    : () => _broker.TapKeyAsync("ENC"),
                routesDialog
                    ? "确认当前对话框选项 · VHF Enter"
                    : "打开或确认当前选项 · ENC");
            if (result is not null && result.Value.Disposition is
                MicroSendDisposition.Accepted or
                MicroSendDisposition.OutcomeUnknown)
            {
                _cachedDialSelection = null;
                if (!routesDialog)
                {
                    MarkDialSurfaceMayBeMounting();
                }

                QueueDialSelectionFeedback();
            }
        }
        finally
        {
            _encoderInputGate.Release();
        }
    }

    private void MarkDialSurfaceMayBeMounting()
    {
        _dialSurfaceMayBeMounting = true;
        _dialSurfaceNotBeforeTimestamp = Stopwatch.GetTimestamp() +
            checked((long)(Stopwatch.Frequency * 0.08));
    }

    private static long ToStopwatchTicks(TimeSpan duration) =>
        checked((long)(duration.TotalSeconds * Stopwatch.Frequency));

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
                    packageRoot: null);
                if (_windowClosed)
                {
                    return;
                }

                _cachedDialSelection = selection;
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
                    packageRoot: null))
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
        if (_windowSource is not null)
        {
            NonActivatingWindow.ApplyNoActivateStyle(_windowSource.Handle);
            _windowSource.AddHook(WindowMessageHook);
        }
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

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Hide();

    private void SetTopmostState(bool value)
    {
        Topmost = value;
        TopmostMenuItem.IsChecked = value;
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
            if (state == "ready")
            {
                ApplyRuntimeReadyState();
            }
            else if (state == "transport-ready")
            {
                ApplyTransportReadyState();
            }
            else if (state.StartsWith("faulted:", StringComparison.Ordinal))
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

    private void ApplyRuntimeReadyState()
    {
        SetLed(RuntimeLed, "#9EBDFF", "Codex 运行时握手已确认 · 无版本白名单");
        SetLed(DriverLed, "#9EBDFF", $"{_transportName} 已连接");
        SetLed(ActivityLed, "#9EBDFF", "HID / RPC 已就绪");
        SetStatus(
            $"{_transportName} 与 Codex 运行时握手已完成。\n" +
            "点击黑色设置旋钮打开设置；Codex 键会将 Codex 切到前台。");
    }

    private void ApplyTransportReadyState()
    {
        SetLed(
            RuntimeLed,
            "#FFD66E",
            "驱动已就绪，但尚未确认 Codex 已连接；Fast、语音和旋钮可能不会生效");
        SetLed(DriverLed, "#9EBDFF", $"{_transportName} 已连接");
        SetLed(ActivityLed, "#FFD66E", "等待 Codex 识别 Micro HID");
        SetStatus(
            $"{_transportName} 已连接，输入只走 Micro HID，不会降级到 UIA。\n" +
            "黄灯表示 Codex 尚未回传连接信号；此时驱动接受按键，不代表 Codex 已处理。");
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
            _latestSlotLighting = snapshot;
            ResolveCurrentAgentSlot();
            RefreshAgentSlotPresentation();

            var lightingBySlot = snapshot.Slots
                .Where(slot => slot.SlotId is >= 0 and < 6)
                .ToDictionary(slot => slot.SlotId);
            var litSlots = Enumerable.Range(0, _agentKeys.Length).Count(slotId =>
            {
                lightingBySlot.TryGetValue(slotId, out var slot);
                return AgentLightingAppearance.From(
                    slot,
                    slotId == _currentAgentSlotId).IsActive;
            });

            SetHelp(
                DriverLed,
                "虚拟 HID",
                $"{_transportName} · Agent 状态已同步 · {litSlots} 个亮灯槽位");
        });
    }

    private void AgentRosterObserver_RosterChanged(
        object? sender,
        CodexAgentRosterSnapshot snapshot)
    {
        _ = Dispatcher.InvokeAsync(() =>
        {
            _latestAgentRoster = snapshot;
            ResolveCurrentAgentSlot();
            RefreshAgentSlotPresentation();
        });
    }

    private void ResolveCurrentAgentSlot()
    {
        _currentAgentSlotId = AgentLightingAppearance.ResolveCurrentSessionSlot(
            _latestSlotLighting?.Slots ?? [],
            _currentAgentSlotId,
            _latestAgentRoster?.Entries.Select(entry => entry.SlotId));
    }

    private void RefreshAgentSlotPresentation()
    {
        var lightingBySlot = _latestSlotLighting?.Slots
            .Where(slot => slot.SlotId is >= 0 and < 6)
            .ToDictionary(slot => slot.SlotId) ?? [];

        for (var slotId = 0; slotId < _agentKeys.Length; slotId++)
        {
            lightingBySlot.TryGetValue(slotId, out var lighting);
            var appearance = AgentLightingAppearance.From(
                lighting,
                slotId == _currentAgentSlotId);
            ApplyAgentLightingAppearance(slotId, appearance);

            var rosterEntry = _latestAgentRoster?.GetSlot(slotId);
            var title = rosterEntry?.DisplayTitle ?? $"Agent 槽位 {slotId + 1}";
            var state = appearance.UsesWhiteFallback
                ? $"{appearance.StatusName} · 白光选择提示"
                : appearance.IsActive
                ? $"{appearance.StatusName} · #{lighting!.Color:X6} · " +
                    $"{appearance.EffectName} · " +
                    $"显示亮度 {appearance.DisplayOpacity:P0}"
                : appearance.StatusName;
            if (appearance.IsCurrentSession && !appearance.UsesWhiteFallback)
            {
                state = $"当前会话 · {state}";
            }

            var localMatch = rosterEntry is null
                ? string.Empty
                : "\n项目与标题来自 Codex 本地最近任务索引。";
            SetHelp(
                _agentKeys[slotId],
                title,
                $"Agent 槽位 {slotId + 1} · AG{slotId:00} · {state} · 单击切换。" +
                localMatch);
        }
    }

    internal static void ApplyAgentLightingAppearance(
        Button key,
        AgentLightingAppearance appearance)
    {
        key.BorderBrush = new SolidColorBrush(appearance.Color)
        {
            Opacity = appearance.DisplayOpacity,
        };
        key.ApplyTemplate();
        SetTemplatePartOpacity(key, "GlowWide", appearance.WideGlowOpacity);
        SetTemplatePartOpacity(key, "Glow", appearance.OuterGlowOpacity);
        SetTemplatePartOpacity(key, "StatusCapWash", appearance.CapWashOpacity);
        SetTemplatePartOpacity(
            key,
            "StatusLightField",
            appearance.LightFieldOpacity);
        SetTemplatePartOpacity(key, "StatusWellWash", appearance.WellWashOpacity);
    }

    internal void ApplyAgentLightingAppearance(
        int slotId,
        AgentLightingAppearance appearance)
    {
        var key = _agentKeys[slotId];
        ApplyAgentLightingAppearance(key, appearance);

        // The window renders all outer light before any physical key. Disable
        // the self-contained template bloom so later siblings cannot paint a
        // colored outline over earlier keycaps.
        SetTemplatePartOpacity(key, "GlowWide", 0);
        SetTemplatePartOpacity(key, "Glow", 0);

        var wideGlow = _agentWideGlows[slotId];
        var nearGlow = _agentNearGlows[slotId];
        wideGlow.Background = key.BorderBrush;
        nearGlow.Background = key.BorderBrush;
        wideGlow.Opacity = appearance.WideGlowOpacity;
        nearGlow.Opacity = appearance.OuterGlowOpacity;
    }

    private static void SetTemplatePartOpacity(
        Button key,
        string partName,
        double opacity)
    {
        if (key.Template.FindName(partName, key) is UIElement part)
        {
            part.Opacity = opacity;
        }
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
                ? "滚轮/按住左键上下或左右拖动：调节推理强度或移动菜单选项。\n短按：打开或确认。"
                : "滚轮/按住左键上下或左右拖动：移动输入区控件或菜单选项。\n短按：打开或确认。");

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

    private void SetStatus(string value)
    {
        _status = value;
        AutomationProperties.SetHelpText(this, value);
        SetHelp(
            DeviceFrame,
            "Agent Controller · Micro Surface",
            $"{value}\n\n拖动机身移动 · 拖动边缘缩放 · 右击机身打开窗口菜单");
    }

    internal void ShowSurface()
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
    }

    internal void CloseForApplicationExit()
    {
        _allowApplicationClose = true;
        Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowApplicationClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void SetLed(
        Ellipse led,
        string color,
        string tooltip)
    {
        led.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
        var title = led == RuntimeLed
            ? "Codex 运行时握手"
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
        var content = new StackPanel
        {
            MaxWidth = 360,
        };
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
