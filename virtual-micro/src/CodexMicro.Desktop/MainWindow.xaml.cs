using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
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
    private static readonly TimeSpan QuotaRefreshInterval =
        TimeSpan.FromMinutes(2);
    private static readonly TimeSpan HarnessStateRefreshInterval =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ForegroundRefreshInterval =
        TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan SettingsLongPressThreshold =
        TimeSpan.FromMilliseconds(650);
    private static readonly TimeSpan AgentDoubleTapThreshold =
        TimeSpan.FromMilliseconds(520);
    private const double StatusLedGlowBlurRadius = 8;
    private const double StatusLedGlowOpacity = 0.78;

    private static readonly StatusLedAppearance NeutralStatusLed =
        new(Color.FromRgb(0xB8, 0xB9, 0x8B), Glow: false);
    private static readonly StatusLedAppearance HealthyStatusLed =
        new(Color.FromRgb(0x78, 0xA6, 0xFF), Glow: true);
    private static readonly StatusLedAppearance ActiveStatusLed =
        new(Color.FromRgb(0x30, 0x4F, 0xFE), Glow: true);
    private static readonly StatusLedAppearance WaitingStatusLed =
        new(Color.FromRgb(0xFF, 0xC8, 0x5A), Glow: true);
    private static readonly StatusLedAppearance ErrorStatusLed =
        new(Color.FromRgb(0xFF, 0x79, 0x94), Glow: true);

    private readonly record struct JoystickReport(
        double Angle,
        double Distance,
        string Label);

    private readonly record struct SettingsDisplayProgress(
        int Version,
        int Step,
        int TotalSteps,
        string Text,
        string Detail,
        MicroHarnessDispatchStage Stage);

    private readonly record struct HarnessVoiceResult(
        bool Success,
        bool Active,
        string Message);

    private readonly record struct StatusLedAppearance(
        Color Color,
        bool Glow);

    private enum KeypadVoiceServiceState
    {
        Unconfigured,
        Checking,
        Ready,
        Listening,
        Unavailable,
        Error,
    }

    private readonly VirtualMicroBroker _broker = new();
    private readonly MicroLocalization _localization;
    private readonly DialDirectionSettings _dialDirectionSettings;
    private readonly MicroProfileSettings _profileSettings;
    private readonly Action<string>? _openHarnessInNewKeypad;
    private readonly Action? _closeKeypad;
    private readonly string _keypadDisplayName;
    private readonly bool _canCloseKeypad;
    private readonly CodexMicroConfigWriter _configWriter;
    private readonly MicroHarnessRegistry _harnessRegistry = new();
    private readonly DeepSeekSetupCoordinator _deepSeekSetupCoordinator;
    private readonly MicroVoiceInputService _voiceInput;
    private readonly SemaphoreSlim _externalVoiceGate = new(1, 1);
    private readonly SemaphoreSlim _streamingDictationGate = new(1, 1);
    private readonly Dictionary<FrameworkElement, (string Title, string Detail)>
        _helpContent = [];
    private readonly CodexMicroLayoutObserver _layoutObserver = new();
    private readonly CodexAgentRosterObserver _agentRosterObserver = new();
    private readonly CodexMenuSelectionObserver _menuSelectionObserver = new();
    private readonly CodexQuotaService _quotaService = new();
    private readonly CodexModelToggleService _modelToggleService = new();
    private readonly DialGestureTracker _dialGesture = new();
    private readonly EncoderStepAccumulator _encoderSteps = new(3);
    private readonly SemaphoreSlim _encoderInputGate = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer
        _dialSelectionHideTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(2400),
        };
    private readonly System.Windows.Threading.DispatcherTimer
        _quotaRefreshTimer = new()
        {
            Interval = QuotaRefreshInterval,
        };
    private readonly System.Windows.Threading.DispatcherTimer
        _harnessStateRefreshTimer = new()
        {
            Interval = HarnessStateRefreshInterval,
        };
    private readonly System.Windows.Threading.DispatcherTimer
        _harnessActionElapsedTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
    private readonly System.Windows.Threading.DispatcherTimer
        _foregroundRefreshTimer = new()
        {
            Interval = ForegroundRefreshInterval,
        };
    private readonly object _harnessActivationSync = new();
    private readonly LinkedList<JoystickReport> _joystickReportQueue = new();
    private readonly IReadOnlyDictionary<string, (Button Button, KeycapIcon Icon)>
        _actionKeys;
    private readonly IReadOnlyDictionary<string, Button> _joystickButtons;
    private readonly KeycapIcon[] _brandAwareIcons;
    private readonly Brush _codexDeviceFrameBackground;
    private readonly Brush _codexPearlLightGuideBackground;
    private readonly Brush _codexCrystalDepthBackground;
    private readonly Brush _codexCrystalPrismBorder;
    private readonly Brush _codexLowerRefractionBackground;
    private Button[] _agentKeys = [];
    private Border[] _agentWideGlows = [];
    private Border[] _agentNearGlows = [];
    private InactiveDialInputRouter? _inactiveDialInputRouter;
    private MicroSettingsWindow? _settingsWindow;
    private HwndSource? _windowSource;
    private bool _connecting;
    private bool _joystickDragging;
    private bool _joystickHasReportedState;
    private bool _joystickReportPumpActive;
    private bool _voicePressed;
    private bool _externalVoiceStopping;
    private bool _voiceStartedByBridgeButton;
    private Button? _voicePressedButton;
    private string? _voicePhysicalKey;
    private string? _voiceHarnessId;
    private string? _voiceTargetSessionId;
    private string? _voiceDictationId;
    private string? _lastDeliveredVoicePartial;
    private string? _lastPublishedVoiceState;
    private CancellationTokenSource? _voiceBridgeCancellation;
    private CancellationTokenSource? _voiceWarmUpCancellation;
    private CancellationTokenSource? _voiceHealthCancellation;
    private Task? _voiceBridgeTask;
    private KeypadVoiceServiceState _voiceServiceState =
        KeypadVoiceServiceState.Unconfigured;
    private string _voiceServiceMessage =
        "请先在小键盘中完成语音配置。";
    private int _voiceDispatchStatusVersion;
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
    private string? _dialSelectionText;
    private CancellationTokenSource? _quotaRefreshCancellation;
    private CancellationTokenSource? _modelRefreshCancellation;
    private CancellationTokenSource? _modelActionCancellation;
    private CancellationTokenSource? _harnessStateCancellation;
    private CodexQuotaSnapshot? _quotaSnapshot;
    private MicroHarnessStateSnapshot? _harnessStateSnapshot;
    private string? _selectedHarnessSessionId;
    private string _activeHarnessContextId = "codex";
    private bool _quotaRefreshFailed;
    private CodexQuickModel _quickModel;
    private bool _quickModelSwitching;
    private bool _harnessModelSwitching;
    private long _settingsPointerDownTimestamp;
    private long _lastAgentTapTimestamp;
    private string? _lastAgentTapKey;
    private string? _pendingHarnessSetupId;
    private string? _suppressedHarnessSelectionId;
    private int _harnessActionStatusVersion;
    private Task? _harnessActivationTask;
    private DateTimeOffset _harnessActionStartedAt;
    private string _harnessActionBaseText = string.Empty;
    private SettingsDisplayProgress? _settingsDisplayProgress;
    private bool _actionTargetIsForeground;
    private IntPtr _lastForegroundWindow;
    private bool _windowClosed;
    private bool _allowApplicationClose;
    private bool _voiceCloseReleasePending;
    private bool _windowMoving;
    private Point _windowMoveStartScreen;
    private Point _windowMoveStartPosition;
    private DpiScale _windowMoveDpi;
    private Point _joystickDragOrigin;
    private string? _joystickActiveDirection;
    private double _dialVisualAngle = 42;
    private string _transportName = "虚拟 HID";
    private string _status = "正在检查 Codex 与虚拟 HID。";

    public MicroSurfaceWindow()
        : this(
            new MicroLocalization(),
            MicroProfileSettings.CreateTransient())
    {
    }

    internal MicroSurfaceWindow(
        MicroLocalization localization,
        MicroProfileSettings? profileSettings = null,
        Action<string>? openHarnessInNewKeypad = null,
        Action? closeKeypad = null,
        string? keypadDisplayName = null,
        bool canCloseKeypad = false)
    {
        _localization = localization ??
            throw new ArgumentNullException(nameof(localization));
        _profileSettings = profileSettings ??
            MicroProfileSettings.CreateTransient();
        _voiceInput = new MicroVoiceInputService(_profileSettings);
        _dialDirectionSettings = new DialDirectionSettings(
            _profileSettings.Current.InvertDialDirection);
        _openHarnessInNewKeypad = openHarnessInNewKeypad;
        _closeKeypad = closeKeypad;
        _keypadDisplayName = string.IsNullOrWhiteSpace(keypadDisplayName)
            ? (_localization.IsEnglish ? "Keypad 1" : "小键盘 1")
            : keypadDisplayName.Trim();
        _canCloseKeypad = canCloseKeypad;
        _configWriter = new CodexMicroConfigWriter(_layoutObserver.ConfigPath);
        _deepSeekSetupCoordinator = new DeepSeekSetupCoordinator(
            _harnessRegistry);
        InitializeComponent();
        _codexDeviceFrameBackground = DeviceFrame.Background;
        _codexPearlLightGuideBackground = PearlLightGuide.Background;
        _codexCrystalDepthBackground = CrystalDepthPlate.Background;
        _codexCrystalPrismBorder = CrystalPrismRim.BorderBrush;
        _codexLowerRefractionBackground = CrystalLowerRefraction.Background;
        Topmost = _profileSettings.Current.WindowTopmost;
        CloseKeypadMenuItem.Visibility = _canCloseKeypad
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_profileSettings.Current is
            {
                WindowLeft: { } savedLeft,
                WindowTop: { } savedTop,
            } &&
            double.IsFinite(savedLeft) &&
            double.IsFinite(savedTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = savedLeft;
            Top = savedTop;
        }
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
            ["ACT10"] = (ActionKey10Split, ActionIcon10Split),
            ["ACT11"] = (ActionKey11Split, ActionIcon11Split),
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
            ActionIcon10Split,
            ActionIcon11Split,
            ActionIcon12,
        ];
        _broker.Log += Broker_Log;
        _broker.StateChanged += Broker_StateChanged;
        _broker.SlotLightingObserved += Broker_SlotLightingObserved;
        _layoutObserver.LayoutChanged += LayoutObserver_LayoutChanged;
        _agentRosterObserver.RosterChanged += AgentRosterObserver_RosterChanged;
        _dialSelectionHideTimer.Tick += DialSelectionHideTimer_Tick;
        _quotaRefreshTimer.Tick += QuotaRefreshTimer_Tick;
        _harnessStateRefreshTimer.Tick += HarnessStateRefreshTimer_Tick;
        _harnessActionElapsedTimer.Tick += HarnessActionElapsedTimer_Tick;
        _foregroundRefreshTimer.Tick += ForegroundRefreshTimer_Tick;
        _localization.LanguageChanged += Localization_LanguageChanged;
        _profileSettings.Changed += ProfileSettings_Changed;
        _harnessRegistry.Changed += HarnessRegistry_Changed;
        _voiceInput.Changed += VoiceInput_Changed;
        RefreshLocalizedChrome();
        InitializeHoverHelp();
        UpdateQuotaPresentation();
        ApplyLayout(_layoutObserver.Current);
        ApplyHarnessContext();
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
            "快捷模型切换",
            "短按：切换设置中的两个快捷模型。\n长按：打开官方 Micro 设置。\n右键：直达右下角当前 Agent 的软件设置。");
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
                Localize("未激活窗口滚轮捕获已就绪"));
        }
        else
        {
            AutomationProperties.SetItemStatus(
                DialButton,
                Localize(
                    $"旋钮输入捕获失败 · Win32 {_inactiveDialInputRouter.LastError}"));
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
        ApplyHarnessContext();
        StartForegroundRefresh();
        PromptForHarnessSetupIfNeeded();
        RestartVoiceBridgeMonitor();
        RestartKeypadVoiceWarmUp();
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

        if (_voicePressed &&
            !_voiceStartedByBridgeButton &&
            VoiceGesturePolicy.StopOnDeactivation(
                _profileSettings.Current.TapToToggleVoice))
        {
            await ReleaseVoiceAsync();
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _windowClosed = true;
        _voiceBridgeCancellation?.Cancel();
        _voiceWarmUpCancellation?.Cancel();
        _voiceHealthCancellation?.Cancel();
        _settingsWindow?.Close();
        _settingsWindow = null;
        _encoderSteps.Clear();
        _dialSelectionFeedbackVersion++;
        _dialSelectionHideTimer.Stop();
        _dialSelectionHideTimer.Tick -= DialSelectionHideTimer_Tick;
        PauseQuotaRefresh();
        PauseHarnessStateRefresh();
        _modelActionCancellation?.Cancel();
        _quotaRefreshTimer.Tick -= QuotaRefreshTimer_Tick;
        _harnessStateRefreshTimer.Tick -= HarnessStateRefreshTimer_Tick;
        _harnessActionElapsedTimer.Stop();
        _harnessActionElapsedTimer.Tick -= HarnessActionElapsedTimer_Tick;
        PauseForegroundRefresh();
        _foregroundRefreshTimer.Tick -= ForegroundRefreshTimer_Tick;
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
        _localization.LanguageChanged -= Localization_LanguageChanged;
        _profileSettings.Changed -= ProfileSettings_Changed;
        _harnessRegistry.Changed -= HarnessRegistry_Changed;
        _voiceInput.Changed -= VoiceInput_Changed;
        _broker.Dispose();
        if (_voiceBridgeTask is not null)
        {
            try
            {
                await _voiceBridgeTask;
            }
            catch (OperationCanceledException)
            {
                // Closing owns cancellation of the DeepSeek button poll.
            }
        }
        await _voiceInput.DisposeAsync();
        _externalVoiceGate.Dispose();
    }

    private void Window_IsVisibleChanged(
        object sender,
        DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            StartQuotaRefresh();
            StartHarnessStateRefresh();
            StartForegroundRefresh();
        }
        else
        {
            PauseQuotaRefresh();
            PauseHarnessStateRefresh();
            PauseForegroundRefresh();
        }
    }

    private void StartForegroundRefresh()
    {
        if (_windowClosed || !IsVisible)
        {
            return;
        }

        RefreshActionTargetForegroundState();
        RefreshTopmostContinuity();
        _foregroundRefreshTimer.Start();
    }

    private void PauseForegroundRefresh()
    {
        _foregroundRefreshTimer.Stop();
        _lastForegroundWindow = IntPtr.Zero;
    }

    private void ForegroundRefreshTimer_Tick(
        object? sender,
        EventArgs e)
    {
        RefreshActionTargetForegroundState();
        RefreshTopmostContinuity();
    }

    private void RefreshActionTargetForegroundState()
    {
        var harness = ActiveHarness();
        ApplyActionTargetForegroundPresentation(
            IsHarnessForeground(harness));
    }

    private void RefreshTopmostContinuity()
    {
        var foregroundWindow =
            NonActivatingWindow.GetForegroundWindowHandle();
        if (foregroundWindow == _lastForegroundWindow)
        {
            return;
        }

        _lastForegroundWindow = foregroundWindow;
        _ = NonActivatingWindow.ReassertTopmostAfterForegroundChange(
            _windowSource?.Handle ?? IntPtr.Zero,
            foregroundWindow,
            Topmost && IsVisible);
    }

    private void ApplyActionTargetForegroundPresentation(bool isForeground)
    {
        if (_actionTargetIsForeground == isForeground)
        {
            return;
        }

        _actionTargetIsForeground = isForeground;
        RefreshActionKeyPresentation();
    }

    internal void ApplyActionTargetForegroundForVisualTest(bool isForeground) =>
        ApplyActionTargetForegroundPresentation(isForeground);

    private void StartQuotaRefresh()
    {
        if (_windowClosed ||
            !IsVisible ||
            !IsCodexHarnessActive())
        {
            return;
        }

        _quotaRefreshTimer.Start();
        _ = RefreshQuotaAsync();
        _ = RefreshQuickModelAsync();
    }

    private void PauseQuotaRefresh()
    {
        _quotaRefreshTimer.Stop();
        _quotaRefreshCancellation?.Cancel();
        _modelRefreshCancellation?.Cancel();
    }

    private void QuotaRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _ = RefreshQuotaAsync();
        _ = RefreshQuickModelAsync();
    }

    private void StartHarnessStateRefresh()
    {
        if (_windowClosed || IsCodexHarnessActive() || !IsVisible)
        {
            return;
        }

        _harnessStateRefreshTimer.Start();
        _ = RefreshHarnessStateAsync();
        _ = RefreshVoiceServiceHealthAsync();
    }

    private void PauseHarnessStateRefresh()
    {
        _harnessStateRefreshTimer.Stop();
        _harnessStateCancellation?.Cancel();
        _voiceHealthCancellation?.Cancel();
    }

    private void HarnessStateRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _ = RefreshHarnessStateAsync();
        _ = RefreshVoiceServiceHealthAsync();
    }

    private async Task RefreshHarnessStateAsync()
    {
        var harness = ActiveHarness();
        if (_windowClosed || harness.Id == "codex" || !IsVisible)
        {
            return;
        }

        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var previous = Interlocked.Exchange(
            ref _harnessStateCancellation,
            cancellation);
        previous?.Cancel();
        try
        {
            var snapshot = await _harnessRegistry.ReadStateAsync(
                harness.Id,
                cancellation.Token);
            if (_windowClosed || ActiveHarness().Id != harness.Id)
            {
                return;
            }

            _harnessStateSnapshot = snapshot;
            if (snapshot is not null)
            {
                var selected = snapshot.Sessions.FirstOrDefault(item =>
                    item.Id == (snapshot.CurrentSessionId ??
                        _selectedHarnessSessionId));
                selected ??= snapshot.Sessions.FirstOrDefault(item => item.Running);
                selected ??= snapshot.Sessions.FirstOrDefault();
                _selectedHarnessSessionId = selected?.Id;
                _currentAgentSlotId = selected is null
                    ? null
                    : Enumerable.Range(0, snapshot.Sessions.Count)
                        .First(index => snapshot.Sessions[index].Id == selected.Id);
            }
            else
            {
                _currentAgentSlotId = null;
            }

            RefreshAgentSlotPresentation();
            RefreshHarnessPresentation();
            UpdateQuotaPresentation();
        }
        catch (OperationCanceledException)
        {
            // A hidden window, harness switch, or newer refresh owns the state.
        }
        finally
        {
            if (ReferenceEquals(_harnessStateCancellation, cancellation))
            {
                _harnessStateCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task RefreshVoiceServiceHealthAsync()
    {
        if (_windowClosed || IsCodexHarnessActive() || !IsVisible)
        {
            return;
        }

        var settings = _profileSettings.Current.VoiceSettings;
        if (!settings.SetupCompleted)
        {
            SetVoiceServiceState(
                KeypadVoiceServiceState.Unconfigured,
                _localization.IsEnglish
                    ? "Configure voice in the keypad first."
                    : "请先在小键盘中完成语音配置。");
            return;
        }
        if (settings.Provider != MicroVoiceProviders.LocalQwen)
        {
            SetVoiceServiceState(
                KeypadVoiceServiceState.Ready,
                settings.Provider == MicroVoiceProviders.System
                    ? _localization.IsEnglish
                        ? "Windows speech recognition is configured."
                        : "Windows 系统语音识别已配置。"
                    : _localization.IsEnglish
                        ? "The configured remote voice service is available to the keypad."
                        : "远程语音服务已在小键盘中配置。");
            return;
        }
        if (_voiceWarmUpCancellation is not null ||
            _voiceInput.Current.Active)
        {
            RefreshVoiceServiceLed();
            return;
        }

        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var previous = Interlocked.Exchange(
            ref _voiceHealthCancellation,
            cancellation);
        previous?.Cancel();
        try
        {
            var ready = await _voiceInput.ProbeLocalReadyAsync(
                cancellation.Token);
            var current = _profileSettings.Current.VoiceSettings;
            if (_windowClosed ||
                IsCodexHarnessActive() ||
                current.Provider != MicroVoiceProviders.LocalQwen ||
                !current.SetupCompleted)
            {
                return;
            }

            SetVoiceServiceState(
                ready
                    ? KeypadVoiceServiceState.Ready
                    : KeypadVoiceServiceState.Unavailable,
                ready
                    ? _localization.IsEnglish
                        ? "The keypad-owned Qwen voice service is ready."
                        : "小键盘持有的 Qwen 语音服务已就绪。"
                    : _localization.IsEnglish
                        ? "The keypad-owned Qwen voice service is not running."
                        : "小键盘持有的 Qwen 语音服务尚未启动。");
        }
        catch (OperationCanceledException)
        {
            // A hidden window, profile change, or newer probe owns the state.
        }
        catch (Exception exception) when (!_windowClosed)
        {
            SetVoiceServiceState(
                KeypadVoiceServiceState.Error,
                exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_voiceHealthCancellation, cancellation))
            {
                _voiceHealthCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task RefreshQuotaAsync()
    {
        if (_windowClosed ||
            !IsVisible ||
            !IsCodexHarnessActive() ||
            _quotaRefreshCancellation is not null)
        {
            return;
        }

        var refresh = new CancellationTokenSource();
        _quotaRefreshCancellation = refresh;
        try
        {
            var snapshot = await _quotaService.ReadAsync(refresh.Token);
            if (refresh.IsCancellationRequested || _windowClosed)
            {
                return;
            }

            _quotaRefreshFailed = snapshot is null;
            if (snapshot is not null)
            {
                _quotaSnapshot = snapshot;
            }

            UpdateQuotaPresentation();
        }
        finally
        {
            if (ReferenceEquals(_quotaRefreshCancellation, refresh))
            {
                _quotaRefreshCancellation = null;
            }

            var retryAfterQuickReshow =
                refresh.IsCancellationRequested && IsVisible && !_windowClosed;
            refresh.Dispose();
            if (retryAfterQuickReshow)
            {
                _ = RefreshQuotaAsync();
            }
        }
    }

    private async Task RefreshQuickModelAsync()
    {
        if (
            _windowClosed ||
            !IsVisible ||
            !IsCodexHarnessActive() ||
            _quickModelSwitching ||
            _modelRefreshCancellation is not null)
        {
            return;
        }

        var refresh = new CancellationTokenSource();
        _modelRefreshCancellation = refresh;
        try
        {
            var model = await _modelToggleService.ReadCurrentAsync(
                refresh.Token);
            if (
                !refresh.IsCancellationRequested &&
                !_windowClosed &&
                !_quickModelSwitching &&
                model != CodexQuickModel.Unknown)
            {
                _quickModel = model;
                UpdateQuotaPresentation();
            }
        }
        catch (OperationCanceledException)
        {
            // Hiding or closing the panel cancels the read-only probe.
        }
        finally
        {
            if (ReferenceEquals(_modelRefreshCancellation, refresh))
            {
                _modelRefreshCancellation = null;
            }

            refresh.Dispose();
        }
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
            var harness = ActiveHarness();
            var externalHarness = harness.Id != "codex";
            var focusAgentAfterTap = isAgentKey && ShouldFocusAgentAfterTap(key);
            try
            {
                // Harness selection is a hard routing boundary. Once an
                // external target is active, no physical control may fall
                // through to the Codex HID transport.
                if (externalHarness)
                {
                    if (isAgentKey)
                    {
                        if (IsHarnessMenuNavigationActive(harness))
                        {
                            if (selectedSlot == 0)
                            {
                                await ExecuteHarnessActionAsync(
                                    harness,
                                    MicroHarnessActionIds.ComposerBack,
                                    HarnessActionLabel(MicroHarnessActionIds.ComposerBack),
                                    CurrentHarnessSessionId());
                            }
                            else
                            {
                                SetLed(ActivityLed, "#E84F67", "请先返回上一级");
                                SetStatus(
                                    $"{harness.DisplayName} 当前位于选择菜单中；" +
                                    "AG00 已临时变为返回键，其他 Agent 键已锁定。");
                            }

                            return;
                        }

                        SelectHarnessSessionSlot(harness, selectedSlot);
                        if (focusAgentAfterTap)
                        {
                            await ActivateHarnessSessionSlotAsync(
                                harness,
                                selectedSlot);
                        }
                        else
                        {
                            SetStatus(
                                $"已选择 {harness.DisplayName} 槽位 {selectedSlot + 1}；" +
                                "再次单击可打开。此行为跟随 Agent 键双击设置。");
                        }
                    }
                    else
                    {
                        await RouteHarnessKeyAsync(harness, key);
                    }

                    return;
                }

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

                if (key == "ACT12")
                {
                    ShowHarnessActionStatus(
                        _localization.IsEnglish ? "OPENING CODEX" : "正在打开 Codex",
                        MicroHarnessDispatchStage.Opening,
                        autoHide: false);
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
                if (!externalHarness &&
                    (key == "ACT12" || focusAgentAfterTap))
                {
                    var activated = await ActivateCodexAsync(
                        initialDelayMilliseconds: isAgentKey ? 0 : 90);
                    if (key == "ACT12")
                    {
                        ShowHarnessActionStatus(
                            activated
                                ? _localization.IsEnglish ? "IN FRONT" : "已置前"
                                : _localization.IsEnglish ? "NOT FOUND" : "未找到窗口",
                            activated
                                ? MicroHarnessDispatchStage.Foreground
                                : MicroHarnessDispatchStage.Failed,
                            autoHide: true);
                    }
                }
            }
        }
    }

    private async void Voice_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not Button button)
        {
            return;
        }

        var decision = VoiceGesturePolicy.Press(
            _profileSettings.Current.TapToToggleVoice,
            _voicePressed,
            _externalVoiceStopping);
        if (decision == VoicePressDecision.Ignore)
        {
            return;
        }
        if (decision == VoicePressDecision.Stop)
        {
            await ReleaseVoiceAsync();
            return;
        }

        if (!IsCodexHarnessActive())
        {
            await BeginHarnessVoiceOrMappedActionAsync(
                ActiveHarness(),
                button,
                captureMouse: true);
            return;
        }

        if (!_broker.IsReady)
        {
            await EnsureReadyFeedbackAsync();
            return;
        }

        _voicePressed = true;
        _voicePressedButton = button;
        var physicalKey = button.Tag as string == "ACT11"
            ? "ACT11"
            : "ACT10";
        _voicePhysicalKey = physicalKey;
        SetVoiceRecordingVisual(recording: true);
        _ = button.CaptureMouse();
        await RunActionAsync(
            () => _broker.SetKeyAsync(physicalKey, true),
            "voice down");
    }

    private async void Voice_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!VoiceGesturePolicy.StopOnRelease(
                _profileSettings.Current.TapToToggleVoice))
        {
            if (sender is Button button && Mouse.Captured == button)
            {
                button.ReleaseMouseCapture();
            }
            return;
        }
        await ReleaseVoiceAsync();
    }

    private async void Voice_LostMouseCapture(
        object sender,
        MouseEventArgs e)
    {
        if (_voicePressed &&
            ReferenceEquals(sender, _voicePressedButton) &&
            VoiceGesturePolicy.StopOnRelease(
                _profileSettings.Current.TapToToggleVoice))
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
        if (sender is not Button button)
        {
            return;
        }

        var decision = VoiceGesturePolicy.Press(
            _profileSettings.Current.TapToToggleVoice,
            _voicePressed,
            _externalVoiceStopping);
        if (decision == VoicePressDecision.Ignore)
        {
            return;
        }
        if (decision == VoicePressDecision.Stop)
        {
            await ReleaseVoiceAsync();
            return;
        }

        if (!IsCodexHarnessActive())
        {
            await BeginHarnessVoiceOrMappedActionAsync(
                ActiveHarness(),
                button,
                captureMouse: false);
            return;
        }

        if (!_broker.IsReady)
        {
            await EnsureReadyFeedbackAsync();
            return;
        }

        _voicePressed = true;
        _voicePressedButton = button;
        var physicalKey = button.Tag as string == "ACT11"
            ? "ACT11"
            : "ACT10";
        _voicePhysicalKey = physicalKey;
        SetVoiceRecordingVisual(recording: true);
        await RunActionAsync(
            () => _broker.SetKeyAsync(physicalKey, true),
            "voice down");
    }

    private async void Voice_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter))
        {
            return;
        }

        e.Handled = true;
        if (!VoiceGesturePolicy.StopOnRelease(
                _profileSettings.Current.TapToToggleVoice))
        {
            return;
        }
        await ReleaseVoiceAsync();
    }

    private async Task ReleaseVoiceAsync(bool releaseCapture = true)
    {
        if (_voiceHarnessId is not null || _voiceInput.Current.Active)
        {
            _ = await StopHarnessVoiceAsync(releaseCapture);
            return;
        }

        if (!_voicePressed)
        {
            return;
        }

        _voicePressed = false;
        _voiceDispatchStatusVersion++;
        SetVoiceRecordingVisual(recording: false);
        var pressedButton = _voicePressedButton;
        if (releaseCapture &&
            pressedButton is not null &&
            Mouse.Captured == pressedButton)
        {
            pressedButton.ReleaseMouseCapture();
        }

        var physicalKey = _voicePhysicalKey;
        _voicePressedButton = null;
        _voicePhysicalKey = null;
        if (_broker.IsReady && !string.IsNullOrWhiteSpace(physicalKey))
        {
            await RunActionAsync(
                () => _broker.SetKeyAsync(physicalKey, false),
            "voice up");
        }
    }

    private async Task<HarnessVoiceResult> StopHarnessVoiceAsync(
        bool releaseCapture)
    {
        _voicePressed = false;
        _externalVoiceStopping = true;
        _voiceDispatchStatusVersion++;
        SetVoiceRecordingVisual(recording: false);
        var pressedButton = _voicePressedButton;
        if (releaseCapture &&
            pressedButton is not null &&
            Mouse.Captured == pressedButton)
        {
            pressedButton.ReleaseMouseCapture();
        }

        await _externalVoiceGate.WaitAsync();
        var harnessId = _voiceHarnessId;
        var sessionId = _voiceTargetSessionId;
        try
        {
            if (harnessId is null || !_voiceInput.Current.Active)
            {
                return new(
                    true,
                    Active: false,
                    "The keypad voice session is already inactive.");
            }

            ShowHarnessActionStatus(
                _localization.IsEnglish ? "TRANSCRIBING" : "正在转写",
                MicroHarnessDispatchStage.Connecting,
                autoHide: false,
                onVoiceKey: true);
            var stopped = await _voiceInput.StopAsync();
            await WaitForStreamingDictationAsync();
            MicroHarnessDispatchResult? delivery = null;
            var finalText = !string.IsNullOrWhiteSpace(stopped.Text)
                ? stopped.Text
                : _lastDeliveredVoicePartial ?? string.Empty;
            if (stopped.Success && !string.IsNullOrWhiteSpace(finalText))
            {
                delivery = await _harnessRegistry.SendDictationAsync(
                    harnessId,
                    finalText,
                    stopped.Language,
                    stopped.AutoSubmit,
                    sessionId,
                    dictationId: _voiceDictationId,
                    dictationPhase: _voiceDictationId is null ? null : "final");
            }
            else if (_voiceDictationId is not null &&
                !string.IsNullOrWhiteSpace(_lastDeliveredVoicePartial))
            {
                delivery = await _harnessRegistry.SendDictationAsync(
                    harnessId,
                    string.Empty,
                    stopped.Language,
                    autoSubmit: false,
                    sessionId,
                    dictationId: _voiceDictationId,
                    dictationPhase: "cancel");
            }

            var success = stopped.Success &&
                (delivery is null || delivery.Success);
            var message = !stopped.Success
                ? stopped.Message
                : delivery?.Message ?? stopped.Message;
            await _harnessRegistry.PublishVoiceStatusAsync(
                harnessId,
                active: false,
                phase: success ? "idle" : "error",
                message,
                sessionId);
            SetLed(
                ActivityLed,
                success ? "#9EBDFF" : "#FFD66E",
                message);
            ShowHarnessActionStatus(
                success
                    ? _localization.IsEnglish ? "VOICE READY" : "语音已完成"
                    : _localization.IsEnglish ? "VOICE FAILED" : "语音失败",
                success
                    ? MicroHarnessDispatchStage.Completed
                    : MicroHarnessDispatchStage.Background,
                autoHide: true,
                onVoiceKey: true);
            SetStatus(message);
            return new(success, Active: false, message);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            SetLed(ActivityLed, "#FFD66E", exception.Message);
            SetStatus(exception.Message);
            return new(false, Active: false, exception.Message);
        }
        finally
        {
            if (string.Equals(
                _voiceHarnessId,
                harnessId,
                StringComparison.OrdinalIgnoreCase))
            {
                _voiceHarnessId = null;
                _voiceTargetSessionId = null;
                _voiceDictationId = null;
                _lastDeliveredVoicePartial = null;
                _voiceStartedByBridgeButton = false;
                _voicePressedButton = null;
                _voicePhysicalKey = null;
                _lastPublishedVoiceState = null;
            }
            _externalVoiceStopping = false;
            _externalVoiceGate.Release();
        }
    }

    private async Task BeginHarnessVoiceOrMappedActionAsync(
        MicroHarnessDefinition harness,
        Button button,
        bool captureMouse)
    {
        var controlId = (button.Tag as string) switch
        {
            "ACT10" => MicroHarnessControlIds.VoiceLeft,
            "ACT11" => MicroHarnessControlIds.VoiceRight,
            _ => MicroHarnessControlIds.VoiceWide,
        };
        var actionId = _harnessRegistry.ResolveKeyMap(harness.Id)
            .Resolve(controlId);
        if (actionId != MicroHarnessActionIds.VoiceDictation)
        {
            await ExecuteHarnessMappedControlAsync(harness, controlId);
            return;
        }

        var harnessSnapshot = _harnessStateSnapshot?.HarnessId == harness.Id
            ? _harnessStateSnapshot
            : null;
        ShowHarnessActionProgress(new(
            MicroHarnessDispatchStage.Connecting,
            "Starting keypad voice input.",
            Step: 1,
            TotalSteps: 3),
            onVoiceKey: true);

        var capabilities = harnessSnapshot?.Capabilities;
        if (capabilities is not null && !capabilities.VoiceInput)
        {
            ShowHarnessActionStatus(
                VoiceFailureLabel(step: 1, totalSteps: 3),
                MicroHarnessDispatchStage.Background,
                autoHide: true,
                onVoiceKey: true);
            ShowUnavailableHarnessAction(
                harness,
                HarnessActionLabel(actionId),
                "适配器没有声明语音输入能力");
            return;
        }

        _ = await StartHarnessVoiceAsync(
            harness,
            CurrentHarnessSessionId(),
            button,
            captureMouse,
            startedByBridgeButton: false);
    }

    private async Task<HarnessVoiceResult> StartHarnessVoiceAsync(
        MicroHarnessDefinition harness,
        string? sessionId,
        Button? button,
        bool captureMouse,
        bool startedByBridgeButton)
    {
        await _externalVoiceGate.WaitAsync();
        try
        {
            if (_voiceInput.Current.Active)
            {
                return new(
                    true,
                    Active: true,
                    "The keypad microphone is already listening.");
            }

            _voicePressed = true;
            _voicePressedButton = button;
            _voiceHarnessId = harness.Id;
            _voiceTargetSessionId = sessionId;
            _voiceDictationId = Guid.NewGuid().ToString("N");
            _lastDeliveredVoicePartial = null;
            _voicePhysicalKey = null;
            _voiceStartedByBridgeButton = startedByBridgeButton;
            _lastPublishedVoiceState = null;
            SetVoiceRecordingVisual(recording: true);
            if (captureMouse && button is not null)
            {
                _ = button.CaptureMouse();
            }

            var result = await _voiceInput.StartAsync(sessionId);
            SetLed(
                ActivityLed,
                result.Success ? "#79A5FF" : "#FFD66E",
                result.Message);
            if (result.Success)
            {
                ShowHarnessActionStatus(
                    _localization.IsEnglish ? "KEYPAD LISTENING" : "小键盘正在聆听",
                    MicroHarnessDispatchStage.Completed,
                    autoHide: false,
                    onVoiceKey: true);
                if (!startedByBridgeButton)
                {
                    await TryActivateHarnessWindowAsync(
                        harness,
                        TimeSpan.FromSeconds(1.6));
                }
                return new(true, Active: true, result.Message);
            }

            _voicePressed = false;
            SetVoiceRecordingVisual(recording: false);
            if (button is not null && Mouse.Captured == button)
            {
                button.ReleaseMouseCapture();
            }
            _voicePressedButton = null;
            _voiceHarnessId = null;
            _voiceTargetSessionId = null;
            _voiceDictationId = null;
            _lastDeliveredVoicePartial = null;
            _voiceStartedByBridgeButton = false;
            ShowHarnessActionStatus(
                VoiceFailureLabel(step: 1, totalSteps: 3),
                MicroHarnessDispatchStage.Background,
                autoHide: true,
                onVoiceKey: true);
            SetStatus(result.Message);
            if (result.SetupRequired)
            {
                OpenKeypadVoiceSettings();
            }
            return new(false, Active: false, result.Message);
        }
        finally
        {
            _externalVoiceGate.Release();
        }
    }

    private void OpenKeypadVoiceSettings()
    {
        ShowHarnessActionStatus(
            _localization.IsEnglish
                ? "KEYPAD VOICE SETTINGS"
                : "小键盘语音设置",
            MicroHarnessDispatchStage.Foreground,
            autoHide: true,
            onVoiceKey: true);
        ShowSoftwareSettings();
        _settingsWindow?.FocusHarnessVoiceSettings();
        SetStatus(_localization.IsEnglish
            ? "Voice capture, recognition, model endpoints, and credentials are configured on this keypad. DeepSeek receives text only."
            : "语音采集、识别、模型地址和密钥均在此小键盘配置；DeepSeek 只接收文字。");
    }

    private void RestartVoiceBridgeMonitor()
    {
        _voiceBridgeCancellation?.Cancel();
        _voiceBridgeCancellation = null;
        if (_windowClosed)
        {
            return;
        }

        var harness = ActiveHarness();
        if (harness.Id == "codex")
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _voiceBridgeCancellation = cancellation;
        _voiceBridgeTask = MonitorVoiceBridgeAsync(harness.Id, cancellation);
    }

    private async Task MonitorVoiceBridgeAsync(
        string harnessId,
        CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested && !_windowClosed)
            {
                var request = await _harnessRegistry.WaitForVoiceRequestAsync(
                    harnessId,
                    cancellation.Token);
                if (request is null)
                {
                    await Task.Delay(450, cancellation.Token);
                    continue;
                }

                HarnessVoiceResult result;
                try
                {
                    var pending = await Dispatcher.InvokeAsync(() =>
                        ToggleHarnessVoiceFromBridgeAsync(harnessId, request));
                    result = await pending;
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException)
                {
                    result = new(
                        false,
                        _voiceInput.Current.Active,
                        exception.Message);
                }

                try
                {
                    await _harnessRegistry.CompleteVoiceRequestAsync(
                        harnessId,
                        request.RequestId,
                        result.Success,
                        result.Active,
                        result.Message);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException)
                {
                    if (!_windowClosed)
                    {
                        await Dispatcher.InvokeAsync(() =>
                            SetStatus(exception.Message));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Harness switching and shutdown own long-poll cancellation.
        }
        finally
        {
            if (ReferenceEquals(_voiceBridgeCancellation, cancellation))
            {
                _voiceBridgeCancellation = null;
                _voiceBridgeTask = null;
            }
            cancellation.Dispose();
        }
    }

    private async Task<HarnessVoiceResult> ToggleHarnessVoiceFromBridgeAsync(
        string harnessId,
        MicroHarnessVoiceRequest request)
    {
        if (_windowClosed ||
            !string.Equals(
                ActiveHarness().Id,
                harnessId,
                StringComparison.OrdinalIgnoreCase))
        {
            return new(
                false,
                _voiceInput.Current.Active,
                "This keypad is no longer connected to that Harness.");
        }
        if (_externalVoiceStopping)
        {
            return new(
                false,
                _voiceInput.Current.Active,
                "The keypad is already finishing voice input.");
        }
        if (_voiceInput.Current.Active)
        {
            return await StopHarnessVoiceAsync(releaseCapture: true);
        }
        if (_voicePressed)
        {
            return new(
                false,
                Active: false,
                "Another microphone session is already active on this keypad.");
        }

        return await StartHarnessVoiceAsync(
            _harnessRegistry.Resolve(harnessId),
            request.SessionId,
            button: null,
            captureMouse: false,
            startedByBridgeButton: true);
    }

    private async void VoiceInput_Changed(
        object? sender,
        MicroVoiceInputSnapshot snapshot)
    {
        if (_windowClosed)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyVoiceInputServiceState(snapshot);
        }
        else
        {
            await Dispatcher.InvokeAsync(() =>
                ApplyVoiceInputServiceState(snapshot));
        }

        var harnessId = _voiceHarnessId;
        if (string.IsNullOrWhiteSpace(harnessId))
        {
            return;
        }

        var signature = string.Join(
            "\u001f",
            snapshot.Phase,
            snapshot.Active,
            snapshot.SessionId,
            snapshot.Message);
        var previous = Interlocked.Exchange(
            ref _lastPublishedVoiceState,
            signature);
        if (!string.Equals(previous, signature, StringComparison.Ordinal))
        {
            try
            {
                await _harnessRegistry.PublishVoiceStatusAsync(
                    harnessId,
                    snapshot.Active,
                    snapshot.Phase,
                    snapshot.Message,
                    snapshot.SessionId);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                if (!_windowClosed)
                {
                    await Dispatcher.InvokeAsync(() =>
                        SetStatus(exception.Message));
                }
            }
        }

        if (snapshot.Active &&
            snapshot.Phase == "listening" &&
            !string.IsNullOrWhiteSpace(snapshot.Partial))
        {
            try
            {
                await QueueStreamingDictationAsync(
                    harnessId,
                    snapshot.SessionId,
                    snapshot.Partial);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                if (!_windowClosed)
                {
                    await Dispatcher.InvokeAsync(() =>
                        SetStatus(exception.Message));
                }
            }
        }
    }

    private void ApplyVoiceInputServiceState(
        MicroVoiceInputSnapshot snapshot)
    {
        var settings = _profileSettings.Current.VoiceSettings;
        if (!settings.SetupCompleted)
        {
            SetVoiceServiceState(
                KeypadVoiceServiceState.Unconfigured,
                snapshot.Message);
            return;
        }

        switch (snapshot.Phase)
        {
            case "starting":
                SetVoiceServiceState(
                    KeypadVoiceServiceState.Checking,
                    snapshot.Message);
                break;
            case "listening":
            case "stopping":
                SetVoiceServiceState(
                    KeypadVoiceServiceState.Listening,
                    snapshot.Message);
                break;
            case "error":
                SetVoiceServiceState(
                    KeypadVoiceServiceState.Error,
                    snapshot.Message);
                break;
            case "idle" when
                settings.Provider != MicroVoiceProviders.LocalQwen ||
                _voiceServiceState is KeypadVoiceServiceState.Ready or
                    KeypadVoiceServiceState.Listening:
                SetVoiceServiceState(
                    KeypadVoiceServiceState.Ready,
                    snapshot.Message);
                break;
            case "idle":
                _ = RefreshVoiceServiceHealthAsync();
                break;
        }
    }

    private async Task QueueStreamingDictationAsync(
        string harnessId,
        string? sessionId,
        string partial)
    {
        await _streamingDictationGate.WaitAsync();

        try
        {
            var next = partial.Trim();
            var dictationId = _voiceDictationId;
            if (_windowClosed ||
                string.IsNullOrWhiteSpace(next) ||
                string.IsNullOrWhiteSpace(dictationId) ||
                !string.Equals(
                    _voiceHarnessId,
                    harnessId,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    next,
                    _lastDeliveredVoicePartial,
                    StringComparison.Ordinal))
            {
                return;
            }

            var result = await _harnessRegistry.SendDictationAsync(
                harnessId,
                next,
                _profileSettings.Current.VoiceSettings.Language,
                autoSubmit: false,
                sessionId,
                dictationId: dictationId,
                dictationPhase: "partial");
            if (result.Success)
            {
                _lastDeliveredVoicePartial = next;
            }
            else if (!_windowClosed)
            {
                await Dispatcher.InvokeAsync(() => SetStatus(result.Message));
            }
        }
        finally
        {
            _streamingDictationGate.Release();
        }
    }

    private async Task WaitForStreamingDictationAsync()
    {
        await _streamingDictationGate.WaitAsync();
        _streamingDictationGate.Release();
    }

    private async Task RouteHarnessKeyAsync(
        MicroHarnessDefinition harness,
        string key)
    {
        if (key == "ACT12")
        {
            var isForeground = IsHarnessForeground(harness);
            ApplyActionTargetForegroundPresentation(isForeground);
            if (isForeground && SupportsHarnessComposerSubmit(harness))
            {
                await ExecuteHarnessActionAsync(
                    harness,
                    MicroHarnessActionIds.ComposerSubmit,
                    HarnessActionLabel(MicroHarnessActionIds.ComposerSubmit),
                    CurrentHarnessSessionId());
                return;
            }

            await ActivateHarnessAsync(harness);
            return;
        }

        await ExecuteHarnessMappedControlAsync(harness, key);
    }

    private async Task ExecuteHarnessMappedControlAsync(
        MicroHarnessDefinition harness,
        string controlId)
    {
        var actionId = _harnessRegistry.ResolveKeyMap(harness.Id)
            .Resolve(controlId);
        var label = HarnessActionLabel(actionId);
        switch (actionId)
        {
            case MicroHarnessActionIds.None:
                ShowUnavailableHarnessAction(
                    harness,
                    controlId,
                    "此键在当前 Harness 的独立键位设置中未分配");
                return;
            case MicroHarnessActionIds.ActivateSurface:
                await ActivateHarnessAsync(harness);
                return;
            case MicroHarnessActionIds.PreviousSession:
            case MicroHarnessActionIds.NextSession:
                var delta = actionId == MicroHarnessActionIds.NextSession ? 1 : -1;
                if (!SelectAdjacentHarnessSession(delta) ||
                    _currentAgentSlotId is not int adjacentSlot)
                {
                    ShowUnavailableHarnessAction(
                        harness,
                        label,
                        "适配器当前没有可切换的会话");
                    return;
                }

                await ActivateHarnessSessionSlotAsync(harness, adjacentSlot);
                return;
            case MicroHarnessActionIds.OpenSelectedSession:
                var selectedSlot = CurrentHarnessSessionSlot();
                if (selectedSlot is null)
                {
                    ShowUnavailableHarnessAction(
                        harness,
                        label,
                        "当前没有已选择的 Harness 会话");
                    return;
                }

                await ActivateHarnessSessionSlotAsync(harness, selectedSlot.Value);
                return;
            case MicroHarnessActionIds.NewSession:
            case MicroHarnessActionIds.ForkSession:
            case MicroHarnessActionIds.ArchiveSession:
            case MicroHarnessActionIds.CancelTurn:
            case MicroHarnessActionIds.ToggleConversationView:
            case MicroHarnessActionIds.ApproveInteraction:
            case MicroHarnessActionIds.RejectInteraction:
            case MicroHarnessActionIds.LoadOlderHistory:
            case MicroHarnessActionIds.ToggleSidebar:
            case MicroHarnessActionIds.OpenDetails:
            case MicroHarnessActionIds.CloseDetails:
                await ExecuteHarnessActionAsync(
                    harness,
                    actionId,
                    label,
                    HarnessActionRequiresSession(actionId)
                            ? CurrentHarnessSessionId()
                            : null);
                return;
            case MicroHarnessActionIds.VoiceDictation:
                ShowUnavailableHarnessAction(
                    harness,
                    label,
                    "语音输入需要由麦克风键的按下与松开边沿触发");
                return;
            default:
                ShowUnavailableHarnessAction(
                    harness,
                    controlId,
                    $"未知 Harness 动作 {actionId}");
                return;
        }
    }

    private Task ActivateHarnessAsync(
        MicroHarnessDefinition harness,
        bool onSettingsKey = false)
    {
        lock (_harnessActivationSync)
        {
            if (_harnessActivationTask is { IsCompleted: false } active)
            {
                return active;
            }

            var task = ActivateHarnessCoreAsync(harness, onSettingsKey);
            _harnessActivationTask = task;
            _ = ClearHarnessActivationTaskAsync(task);
            return task;
        }
    }

    private async Task ClearHarnessActivationTaskAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The original caller observes the activation failure; this
            // companion task owns only single-flight cleanup.
        }
        finally
        {
            lock (_harnessActivationSync)
            {
                if (ReferenceEquals(_harnessActivationTask, task))
                {
                    _harnessActivationTask = null;
                }
            }
        }
    }

    private async Task ActivateHarnessCoreAsync(
        MicroHarnessDefinition harness,
        bool onSettingsKey)
    {
        if (IsDeepSeekHarness(harness) &&
            !_harnessRegistry.IsSetupCompleted(harness.Id) &&
            !await EnsureDeepSeekSetupAsync(harness, onSettingsKey))
        {
            return;
        }

        // Managed setup writes a new endpoint and launch command into the
        // registry. Resolve again rather than using the pre-wizard snapshot.
        harness = _harnessRegistry.Resolve(harness.Id);
        var activationStep = 0;
        ShowHarnessActionProgress(new(
            MicroHarnessDispatchStage.Connecting,
            "Preparing DeepSeek Harness activation.",
            Step: 0,
            TotalSteps: 7),
            onSettingsKey: onSettingsKey);
        SetLed(ActivityLed, "#9EBDFF", $"正在激活 {harness.DisplayName}");
        var progress = new CallbackProgress<MicroHarnessDispatchProgress>(value =>
        {
            activationStep = Math.Max(
                activationStep,
                value.Step ?? activationStep);
            ShowHarnessActionProgress(
                value,
                onSettingsKey: onSettingsKey);
        });
        var dispatch = await _harnessRegistry.ActivateUntilSurfaceReadyAsync(
            harness.Id,
            progress);
        var browserReady = dispatch.Success &&
            dispatch.Stage != MicroHarnessDispatchStage.Opening;
        var foreground = browserReady &&
            dispatch.Stage == MicroHarnessDispatchStage.Foreground;
        if (browserReady)
        {
            activationStep = 6;
            ShowHarnessActionProgress(new(
                MicroHarnessDispatchStage.Background,
                "Bringing the native window to the foreground.",
                Step: 6,
                TotalSteps: 7),
                onSettingsKey: onSettingsKey);
            if (!foreground)
            {
                foreground = await TryActivateHarnessWindowAsync(
                    harness,
                    TimeSpan.FromSeconds(1.6),
                    dispatch.WindowProcessId);
            }
        }
        else if (dispatch.Success)
        {
            // The native window may exist even when its browser half failed
            // to report. Surface it for recovery, but do not claim step 7.
            foreground = await TryActivateHarnessWindowAsync(
                harness,
                TimeSpan.FromSeconds(15),
                dispatch.WindowProcessId);
        }

        var completed = browserReady && foreground;
        var stoppedAt = dispatch.Success
            ? browserReady ? 6 : 5
            : dispatch.Step ?? activationStep;
        ShowHarnessActionStatus(
            completed
                ? ActivationProgressLabel(step: 7, includeFraction: true)
                : ProgressAttentionLabel(stoppedAt, totalSteps: 7),
            completed
                ? MicroHarnessDispatchStage.Foreground
                : MicroHarnessDispatchStage.Background,
            autoHide: true,
            onSettingsKey: onSettingsKey,
            detail: dispatch.Message);
        SetLed(
            ActivityLed,
            completed ? "#9EBDFF" : "#FFD66E",
            dispatch.Message);
        SetStatus(completed
            ? $"已通过直连插件协议打开 {harness.DisplayName}，并将专用网页窗口置前。\n{dispatch.Message}"
            : dispatch.Success
                ? $"{harness.DisplayName} 停在第 {stoppedAt}/7 步；请查看任务栏中的窗口。\n{dispatch.Message}"
                : $"{harness.DisplayName} 停在第 {stoppedAt}/7 步，需要处理。\n{dispatch.Message}");
    }

    private async Task<bool> EnsureDeepSeekSetupAsync(
        MicroHarnessDefinition harness,
        bool onSettingsKey)
    {
        ShowDeepSeekSetupProgress(new(
            1,
            DeepSeekSetupCoordinator.TotalSetupSteps,
            _localization.IsEnglish
                ? "Detect existing Harness"
                : "检测已有 Harness",
            _localization.IsEnglish
                ? "Checking the saved address and official default port 3080."
                : "正在检查已保存地址和官方默认端口 3080。"),
            onSettingsKey);

        DeepSeekEndpointProbe probe;
        try
        {
            probe = await _deepSeekSetupCoordinator.ProbeAsync();
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                IOException or
                InvalidOperationException)
        {
            ShowHarnessActionStatus(
                _localization.IsEnglish ? "SETUP NEEDS ATTENTION" : "配置需要处理",
                MicroHarnessDispatchStage.Background,
                autoHide: true,
                onSettingsKey: onSettingsKey);
            SetStatus($"{harness.DisplayName} 尚未配置：{exception.Message}");
            return false;
        }

        if (probe.BridgeReachable)
        {
            if (!string.Equals(
                    harness.ControlUri,
                    probe.ControlUri.AbsoluteUri,
                    StringComparison.OrdinalIgnoreCase))
            {
                _harnessRegistry.UpdateConnectionSettings(
                    harness.Id,
                    harness.Connection with
                    {
                        ControlUri = probe.ControlUri.AbsoluteUri,
                    });
            }
            _harnessRegistry.MarkSetupCompleted(harness.Id);
            return true;
        }

        var setupWindow = new DeepSeekSetupWindow(
            probe,
            (progress, cancellationToken) =>
                _deepSeekSetupCoordinator.ConfigureManagedAsync(
                    progress,
                    cancellationToken),
            value => ShowDeepSeekSetupProgress(value, onSettingsKey),
            _localization.IsEnglish)
        {
            Owner = this,
            Topmost = Topmost,
        };
        setupWindow.ShowDialog();

        switch (setupWindow.Choice)
        {
            case DeepSeekSetupWindowChoice.ManagedReady:
                ShowHarnessActionStatus(
                    _localization.IsEnglish ? "8/8 CONFIGURED" : "8/8 配置完成",
                    MicroHarnessDispatchStage.Completed,
                    autoHide: false,
                    onSettingsKey: onSettingsKey);
                SetStatus(setupWindow.ManagedResult?.Message ??
                    $"{harness.DisplayName} 已完成程序托管配置。");
                return true;
            case DeepSeekSetupWindowChoice.ExistingHarness:
                ShowHarnessActionStatus(
                    _localization.IsEnglish ? "USE EXISTING DSH" : "使用已有 DSH",
                    MicroHarnessDispatchStage.Background,
                    autoHide: true,
                    onSettingsKey: onSettingsKey);
                SetStatus(
                    $"请填写已有 {harness.DisplayName} 的控制地址；官方默认 Web 地址为 http://127.0.0.1:3080。若使用自定义端口，请填写实际端口。");
                ShowSoftwareSettings();
                _settingsWindow?.FocusHarnessSetup();
                return false;
            case DeepSeekSetupWindowChoice.RestartRequired:
                ShowHarnessActionStatus(
                    _localization.IsEnglish ? "3/8 RESTART WINDOWS" : "3/8 等待重启",
                    MicroHarnessDispatchStage.Background,
                    autoHide: false,
                    onSettingsKey: onSettingsKey);
                SetStatus(setupWindow.ManagedResult?.Message ??
                    "请重启 Windows，之后再次点击 DeepSeek 键继续配置。");
                return false;
            default:
                var result = setupWindow.ManagedResult;
                ShowHarnessActionStatus(
                    result?.Disposition == DeepSeekSetupDisposition.Failed
                        ? _localization.IsEnglish
                            ? $"{result.Step}/8 SETUP NEEDS ATTENTION"
                            : $"{result.Step}/8 配置需要处理"
                        : _localization.IsEnglish
                            ? "SETUP PAUSED"
                            : "配置已暂停",
                    MicroHarnessDispatchStage.Background,
                    autoHide: true,
                    onSettingsKey: onSettingsKey);
                SetStatus(result?.Message ??
                    $"{harness.DisplayName} 尚未配置；再次点击 DeepSeek 键即可继续。");
                return false;
        }
    }

    private void ShowDeepSeekSetupProgress(
        DeepSeekSetupProgress value,
        bool onSettingsKey)
    {
        var label = value.Step switch
        {
            1 => _localization.IsEnglish ? "DETECT HARNESS" : "检测已有 Harness",
            2 => _localization.IsEnglish ? "CHOOSE SETUP" : "确认配置方式",
            3 => _localization.IsEnglish ? "CHECK WSL" : "检查 WSL",
            4 => _localization.IsEnglish ? "VERIFY PAYLOAD" : "验证安装载荷",
            5 => _localization.IsEnglish ? "PREPARE RUNTIME" : "准备运行环境",
            6 => _localization.IsEnglish ? "INSTALL BRIDGE" : "安装桥接插件",
            7 => _localization.IsEnglish ? "SAVE AND START" : "保存并启动",
            _ => _localization.IsEnglish ? "HEALTH CHECK" : "健康检查",
        };
        ShowHarnessActionStatus(
            $"{value.Step}/{value.TotalSteps} {label}",
            value.Step == value.TotalSteps
                ? MicroHarnessDispatchStage.Completed
                : MicroHarnessDispatchStage.Connecting,
            autoHide: false,
            onSettingsKey: onSettingsKey,
            detail: value.Message);
        SetStatus(value.Message);
    }

    private void ShowHarnessActionProgress(
        MicroHarnessDispatchProgress value,
        bool onVoiceKey = false,
        bool onSettingsKey = false) =>
        ShowHarnessActionStatus(
            value.Step is { } step && value.TotalSteps is { } totalSteps
                ? ProgressLabel(step, totalSteps, includeFraction: true)
                : HarnessProgressLabel(value.Stage),
            value.Stage,
            autoHide: false,
            onVoiceKey: onVoiceKey,
            onSettingsKey: onSettingsKey,
            detail: value.Message);

    internal void ApplyHarnessProgressForVisualTest(
        int step,
        int totalSteps,
        bool onVoiceKey = false,
        bool onSettingsKey = false) =>
        ShowHarnessActionProgress(
            new(
                MicroHarnessDispatchStage.Connecting,
                "Visual progress test.",
                step,
                totalSteps),
            onVoiceKey,
            onSettingsKey);

    private string ProgressLabel(
        int step,
        int totalSteps,
        bool includeFraction)
    {
        var label = totalSteps switch
        {
            7 => ActivationProgressLabel(step, includeFraction: false),
            8 or 4 => VoiceProgressLabel(
                step,
                totalSteps,
                includeFraction: false),
            _ => _localization.IsEnglish ? "WORKING" : "处理中",
        };
        return includeFraction ? $"{step}/{totalSteps} {label}" : label;
    }

    private string ActivationProgressLabel(
        int step,
        bool includeFraction) =>
        (includeFraction ? $"{step}/7 " : string.Empty) +
        (step switch
        {
            0 => _localization.IsEnglish ? "PREPARING" : "准备开始",
            1 => _localization.IsEnglish ? "CHECK ADAPTER" : "检查适配器",
            2 => _localization.IsEnglish ? "START SERVICE" : "启动服务",
            3 => _localization.IsEnglish ? "WAIT ADAPTER" : "等待适配器",
            4 => _localization.IsEnglish ? "OPEN WINDOW" : "请求打开窗口",
            5 => _localization.IsEnglish ? "WAIT WEB BRIDGE" : "等待网页桥接",
            6 => _localization.IsEnglish ? "BRING TO FRONT" : "将窗口置前",
            _ => _localization.IsEnglish ? "READY" : "已就绪",
        });

    private string VoiceProgressLabel(
        int step,
        int totalSteps,
        bool includeFraction = true)
    {
        var label = totalSteps == 8
            ? step switch
            {
                1 => _localization.IsEnglish ? "CHECK VOICE" : "检查语音设置",
                2 => _localization.IsEnglish ? "CHECK ADAPTER" : "检查适配器",
                3 => _localization.IsEnglish ? "START SERVICE" : "启动并等待服务",
                4 => _localization.IsEnglish ? "CONNECT WEB" : "打开并连接网页",
                5 => _localization.IsEnglish ? "PREPARE ASR" : "准备识别引擎",
                6 => _localization.IsEnglish ? "VOICE CHANNEL" : "连接语音通道",
                7 => _localization.IsEnglish ? "GET MICROPHONE" : "获取麦克风",
                _ => _localization.IsEnglish ? "LISTENING" : "正在聆听",
            }
            : step switch
            {
                1 => _localization.IsEnglish ? "STOP CAPTURE" : "停止采集",
                2 => _localization.IsEnglish ? "FINAL TRANSCRIPT" : "等待最终转写",
                3 => _localization.IsEnglish ? "WRITE COMPOSER" : "写入输入框",
                _ => _localization.IsEnglish ? "COMPLETE" : "转写完成",
            };
        return includeFraction ? $"{step}/{totalSteps} {label}" : label;
    }

    private string VoiceFailureLabel(int step, int totalSteps) =>
        ProgressAttentionLabel(step, totalSteps);

    private string ProgressAttentionLabel(int step, int totalSteps)
    {
        var stage = ProgressLabel(step, totalSteps, includeFraction: false);
        return _localization.IsEnglish
            ? $"NEEDS ATTENTION AT {step}/{totalSteps}: {stage}"
            : $"停在 {step}/{totalSteps}，需要处理：{stage}";
    }

    private string HarnessProgressLabel(MicroHarnessDispatchStage stage) =>
        stage switch
        {
            MicroHarnessDispatchStage.Connecting =>
                _localization.IsEnglish ? "CHECKING" : "检查服务",
            MicroHarnessDispatchStage.Starting =>
                _localization.IsEnglish ? "STARTING SERVICE" : "启动服务",
            MicroHarnessDispatchStage.WaitingForAdapter =>
                _localization.IsEnglish ? "WAITING FOR PLUGIN" : "等待插件",
            MicroHarnessDispatchStage.Opening =>
                _localization.IsEnglish ? "OPENING WEB" : "正在打开网页",
            MicroHarnessDispatchStage.Foreground =>
                _localization.IsEnglish ? "IN FRONT" : "已置前",
            MicroHarnessDispatchStage.Background =>
                _localization.IsEnglish ? "BRINGING FRONT" : "正在置前",
            MicroHarnessDispatchStage.Failed =>
                _localization.IsEnglish ? "NEEDS ATTENTION" : "需要处理",
            _ => _localization.IsEnglish ? "READY" : "已完成",
        };

    private async Task<bool> TryActivateHarnessWindowAsync(
        MicroHarnessDefinition harness,
        TimeSpan timeout,
        int? preferredProcessId = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var deadline = Stopwatch.GetTimestamp() +
            checked((long)(Stopwatch.Frequency * timeout.TotalSeconds));
        do
        {
            try
            {
                if (HarnessWindowActivator.TryActivate(
                    harness,
                    preferredProcessId))
                {
                    return true;
                }
            }
            catch (Exception exception) when (
                exception is Win32Exception or
                    EntryPointNotFoundException or
                    DllNotFoundException)
            {
                SetStatus($"{harness.DisplayName} 原生窗口置前失败：{exception.Message}");
                return false;
            }

            await Task.Delay(160);
        }
        while (Stopwatch.GetTimestamp() < deadline && !_windowClosed);
        return false;
    }

    private void ShowHarnessActionStatus(
        string text,
        MicroHarnessDispatchStage stage,
        bool autoHide,
        bool onVoiceKey = false,
        bool onSettingsKey = false,
        string? detail = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
                ShowHarnessActionStatus(
                    text,
                    stage,
                    autoHide,
                    onVoiceKey,
                    onSettingsKey,
                    detail)));
            return;
        }

        var version = ++_harnessActionStatusVersion;
        if (!onVoiceKey &&
            IsDeepSeekHarness(ActiveHarness()) &&
            TryReadProgressFraction(text, out var step, out var totalSteps))
        {
            ShowSettingsDisplayProgress(new(
                version,
                step,
                totalSteps,
                text,
                detail ?? string.Empty,
                stage),
                autoHide);
            return;
        }

        if (_settingsDisplayProgress is not null)
        {
            _settingsDisplayProgress = null;
            _harnessActionElapsedTimer.Stop();
            HideHarnessProgressStatus();
            UpdateQuotaPresentation();
        }

        var structuredProgress = text.Contains('/', StringComparison.Ordinal);
        Grid.SetColumn(
            HarnessActionStatusBadge,
            onVoiceKey
                ? 1
                : onSettingsKey
                    ? 0
                    : structuredProgress ? 2 : 3);
        Grid.SetColumnSpan(
            HarnessActionStatusBadge,
            onVoiceKey || onSettingsKey || structuredProgress ? 2 : 1);
        HarnessActionStatusBadge.Width = onVoiceKey
            ? 178
            : onSettingsKey || structuredProgress
                ? 160
                : 84;
        HarnessActionStatusBadge.Height =
            onVoiceKey || onSettingsKey || structuredProgress ? 24 : 20;
        Grid.SetColumn(
            HarnessActionProgressRing,
            onVoiceKey ? 1 : onSettingsKey ? 0 : 3);
        Grid.SetColumnSpan(HarnessActionProgressRing, onVoiceKey ? 2 : 1);
        var busy = !autoHide && stage is not (
            MicroHarnessDispatchStage.Failed or
            MicroHarnessDispatchStage.Foreground or
            MicroHarnessDispatchStage.Completed);
        if (busy)
        {
            if (!_harnessActionElapsedTimer.IsEnabled)
            {
                _harnessActionStartedAt = DateTimeOffset.UtcNow;
                _harnessActionElapsedTimer.Start();
            }

            _harnessActionBaseText = text;
            StartHarnessActionProgressRing(stage);
        }
        else
        {
            _harnessActionElapsedTimer.Stop();
            _harnessActionBaseText = text;
            StopHarnessActionProgressRing();
        }

        var colors = stage switch
        {
            MicroHarnessDispatchStage.Failed =>
                (Background: "#F5FFF0F2", Border: "#C8F0A4AF", Text: "#FF8B4055"),
            MicroHarnessDispatchStage.Foreground or
                MicroHarnessDispatchStage.Completed =>
                (Background: "#F3EDF9FF", Border: "#B8B8D8FF", Text: "#FF315A86"),
            MicroHarnessDispatchStage.Background =>
                (Background: "#F5FFF9E9", Border: "#C8E8CC89", Text: "#FF725B23"),
            _ =>
                (Background: "#F2EEF4FF", Border: "#B8C7D2FF", Text: "#FF34415B"),
        };
        HarnessActionStatusText.Text = FormatHarnessActionStatusText(text);
        HarnessActionStatusText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(colors.Text));
        HarnessActionStatusBadge.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(colors.Background));
        HarnessActionStatusBadge.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(colors.Border));
        HarnessActionStatusBadge.BeginAnimation(OpacityProperty, null);
        HarnessActionStatusBadge.Visibility = Visibility.Visible;
        HarnessActionStatusBadge.Opacity = 1;
        AutomationProperties.SetItemStatus(
            onVoiceKey
                ? ActionKey10
                : onSettingsKey ? SettingsKey : ActionKey12,
            Localize(text));
        if (autoHide)
        {
            _ = HideHarnessActionStatusAsync(version);
        }
    }

    private void ShowSettingsDisplayProgress(
        SettingsDisplayProgress progress,
        bool autoHide)
    {
        _settingsDisplayProgress = progress;
        HarnessActionStatusBadge.BeginAnimation(OpacityProperty, null);
        HarnessActionStatusBadge.Visibility = Visibility.Collapsed;
        HarnessActionStatusBadge.Opacity = 0;
        HarnessActionStatusText.Text = progress.Text;
        StopHarnessActionProgressRing();

        var busy = !autoHide && progress.Stage is not (
            MicroHarnessDispatchStage.Failed or
            MicroHarnessDispatchStage.Foreground or
            MicroHarnessDispatchStage.Completed);
        if (busy)
        {
            if (!_harnessActionElapsedTimer.IsEnabled)
            {
                _harnessActionStartedAt = DateTimeOffset.UtcNow;
                _harnessActionElapsedTimer.Start();
            }
        }
        else
        {
            _harnessActionElapsedTimer.Stop();
        }

        _harnessActionBaseText = progress.Text;
        ApplySettingsDisplayProgress(progress);
        AutomationProperties.SetItemStatus(SettingsKey, Localize(progress.Text));
        AutomationProperties.SetItemStatus(ActionKey12, Localize(progress.Text));
        if (autoHide)
        {
            _ = HideHarnessActionStatusAsync(progress.Version);
        }
    }

    private void ApplySettingsDisplayProgress(SettingsDisplayProgress progress)
    {
        ApplySettingsDisplayTheme(light: true);
        var step = Math.Clamp(progress.Step, 0, progress.TotalSteps);
        QuotaCaptionText.Visibility = Visibility.Collapsed;
        QuotaValueText.Text = $"{step}/{progress.TotalSteps}";
        QuotaValueText.FontSize = 16;
        QuotaGauge.Opacity = 1;
        QuotaProgressRing.Data = CreateQuotaArcGeometry(
            100d * step / progress.TotalSteps);
        QuotaProgressRing.Stroke = new SolidColorBrush(
            progress.Stage == MicroHarnessDispatchStage.Failed
                ? Color.FromRgb(0xA7, 0x42, 0x56)
                : progress.Stage is MicroHarnessDispatchStage.Foreground or
                    MicroHarnessDispatchStage.Completed
                    ? Color.FromRgb(0x28, 0x66, 0x4A)
                    : Color.FromRgb(0x22, 0x22, 0x22));

        BrandWordmarkPanel.Visibility = Visibility.Collapsed;
        HarnessProgressStatusText.Text = FormatHarnessActionStatusText(
            ProgressStageText(progress.Text));
        HarnessProgressStatusText.Foreground = new SolidColorBrush(
            progress.Stage == MicroHarnessDispatchStage.Failed
                ? Color.FromRgb(0x8B, 0x40, 0x55)
                : progress.Stage is MicroHarnessDispatchStage.Foreground or
                    MicroHarnessDispatchStage.Completed
                    ? Color.FromRgb(0x31, 0x5A, 0x86)
                    : progress.Stage == MicroHarnessDispatchStage.Background
                        ? Color.FromRgb(0x72, 0x5B, 0x23)
                        : Color.FromRgb(0x34, 0x41, 0x5B));
        HarnessProgressStatusText.Visibility = Visibility.Visible;

        var detail = FormatHarnessActionStatusText(progress.Text);
        if (!string.IsNullOrWhiteSpace(progress.Detail) &&
            !string.Equals(
                progress.Detail.Trim(),
                progress.Text.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            detail += $"\n{progress.Detail.Trim()}";
        }

        ApplyHelp(
            SettingsKey,
            $"{ActiveHarness().DisplayName} · {step}/{progress.TotalSteps}",
            detail);
    }

    private static string ProgressStageText(string text)
    {
        if (!TryReadProgressFraction(text, out var step, out var totalSteps))
        {
            return text.Trim();
        }

        var fraction = $"{step}/{totalSteps}";
        if (!text.StartsWith(fraction, StringComparison.Ordinal))
        {
            return text.Trim();
        }

        return text[fraction.Length..].TrimStart(' ', '\t', ':', '：');
    }

    private void HideHarnessProgressStatus()
    {
        HarnessProgressStatusText.Visibility = Visibility.Collapsed;
        HarnessProgressStatusText.Text = string.Empty;
        BrandWordmarkPanel.Visibility = Visibility.Visible;
    }

    private static bool TryReadProgressFraction(
        string text,
        out int step,
        out int totalSteps)
    {
        step = 0;
        totalSteps = 0;
        for (var slash = text.IndexOf('/');
             slash >= 0;
             slash = text.IndexOf('/', slash + 1))
        {
            var left = slash - 1;
            while (left >= 0 && char.IsDigit(text[left]))
            {
                left--;
            }

            var right = slash + 1;
            while (right < text.Length && char.IsDigit(text[right]))
            {
                right++;
            }

            var stepSpan = text.AsSpan(left + 1, slash - left - 1);
            var totalSpan = text.AsSpan(slash + 1, right - slash - 1);
            if (stepSpan.Length > 0 &&
                totalSpan.Length > 0 &&
                int.TryParse(stepSpan, out step) &&
                int.TryParse(totalSpan, out totalSteps) &&
                step >= 0 &&
                totalSteps > 0)
            {
                return true;
            }
        }

        step = 0;
        totalSteps = 0;
        return false;
    }

    private void HarnessActionElapsedTimer_Tick(object? sender, EventArgs e)
    {
        if (_settingsDisplayProgress is { } settingsProgress)
        {
            ApplySettingsDisplayProgress(settingsProgress);
            return;
        }

        if (HarnessActionStatusBadge.Visibility == Visibility.Visible &&
            _harnessActionBaseText.Length > 0)
        {
            HarnessActionStatusText.Text = FormatHarnessActionStatusText(
                _harnessActionBaseText);
        }
    }

    private string FormatHarnessActionStatusText(string text)
    {
        if (!_harnessActionElapsedTimer.IsEnabled)
        {
            return text;
        }

        var elapsed = Math.Max(
            0,
            (int)(DateTimeOffset.UtcNow - _harnessActionStartedAt).TotalSeconds);
        return elapsed < 2 ? text : $"{text} · {elapsed}s";
    }

    private void StartHarnessActionProgressRing(MicroHarnessDispatchStage stage)
    {
        HarnessActionProgressRing.Stroke = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(
                stage == MicroHarnessDispatchStage.Starting ||
                stage == MicroHarnessDispatchStage.WaitingForAdapter
                    ? "#FFE8B44B"
                    : "#FF7EA5F7"));
        if (HarnessActionProgressRing.Visibility == Visibility.Visible)
        {
            return;
        }

        HarnessActionProgressRing.Visibility = Visibility.Visible;
        HarnessActionProgressRotate.BeginAnimation(
            RotateTransform.AngleProperty,
            new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(900),
                RepeatBehavior = RepeatBehavior.Forever,
            });
    }

    private void StopHarnessActionProgressRing()
    {
        HarnessActionProgressRotate.BeginAnimation(
            RotateTransform.AngleProperty,
            null);
        HarnessActionProgressRing.Visibility = Visibility.Collapsed;
    }

    private async Task HideHarnessActionStatusAsync(int version)
    {
        await Task.Delay(3200);
        if (_windowClosed || version != _harnessActionStatusVersion)
        {
            return;
        }

        if (_settingsDisplayProgress is { Version: var progressVersion } &&
            progressVersion == version)
        {
            _settingsDisplayProgress = null;
            _harnessActionElapsedTimer.Stop();
            HideHarnessProgressStatus();
            UpdateQuotaPresentation();
            return;
        }

        var fade = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            FillBehavior = FillBehavior.HoldEnd,
        };
        fade.Completed += (_, _) =>
        {
            if (version == _harnessActionStatusVersion)
            {
                HarnessActionStatusBadge.Visibility = Visibility.Collapsed;
                RefreshHarnessPresentation();
            }
        };
        HarnessActionStatusBadge.BeginAnimation(
            OpacityProperty,
            fade,
            HandoffBehavior.SnapshotAndReplace);
    }

    private async Task ActivateHarnessSessionSlotAsync(
        MicroHarnessDefinition harness,
        int slotId)
    {
        var session = _harnessStateSnapshot?.HarnessId == harness.Id &&
            slotId >= 0 &&
            slotId < _harnessStateSnapshot.Sessions.Count
                ? _harnessStateSnapshot.Sessions[slotId]
                : null;
        if (session is null)
        {
            var activation = await _harnessRegistry
                .ActivateUntilSurfaceReadyAsync(harness.Id);
            SetLed(
                ActivityLed,
                activation.Success ? "#9EBDFF" : "#FFD66E",
                activation.Message);
            SetStatus(activation.Message);
            if (activation.Success)
            {
                _ = RefreshHarnessStateAsync();
            }

            return;
        }

        _selectedHarnessSessionId = session.Id;
        _currentAgentSlotId = slotId;
        RefreshAgentSlotPresentation();
        var alreadyCurrent = string.Equals(
            session.Id,
            _harnessStateSnapshot?.CurrentSessionId,
            StringComparison.Ordinal);
        ShowHarnessActionStatus(
            alreadyCurrent
                ? _localization.IsEnglish ? "BRINGING FRONT" : "正在置前"
                : _localization.IsEnglish ? "OPENING SESSION" : "正在打开会话",
            alreadyCurrent
                ? MicroHarnessDispatchStage.Background
                : MicroHarnessDispatchStage.Opening,
            autoHide: false);
        SetLed(
            ActivityLed,
            "#9EBDFF",
            alreadyCurrent
                ? $"正在置前 {session.DisplayTitle}"
                : $"正在打开 {session.DisplayTitle}");
        var result = alreadyCurrent
            ? await _harnessRegistry.ActivateUntilSurfaceReadyAsync(harness.Id)
            : await _harnessRegistry.ActivateSessionAsync(
                harness.Id,
                session.Id);
        var foreground = result.Success &&
            result.Stage == MicroHarnessDispatchStage.Foreground;
        // Re-selecting the current Agent is a focus gesture, not a second
        // session-open command.  A browser can briefly miss its ACK while a
        // page reconnects; if the guarded native Harness window was raised,
        // that focus gesture still succeeded and must not surface as an
        // activation error.
        if (!foreground && (result.Success || alreadyCurrent))
        {
            foreground = await TryActivateHarnessWindowAsync(
                harness,
                result.Stage == MicroHarnessDispatchStage.Opening
                    ? TimeSpan.FromSeconds(5)
                    : TimeSpan.FromSeconds(1.6),
                result.WindowProcessId);
        }
        var succeeded = result.Success || (alreadyCurrent && foreground);
        ShowHarnessActionStatus(
            succeeded
                ? foreground
                    ? _localization.IsEnglish ? "IN FRONT" : "已置前"
                    : _localization.IsEnglish ? "CHECK TASKBAR" : "请从任务栏打开"
                : _localization.IsEnglish ? "NEEDS ATTENTION" : "需要处理",
            succeeded
                ? foreground
                    ? MicroHarnessDispatchStage.Foreground
                    : MicroHarnessDispatchStage.Background
                : MicroHarnessDispatchStage.Background,
            autoHide: true);
        SetLed(
            ActivityLed,
            succeeded ? "#9EBDFF" : "#FFD66E",
            succeeded && alreadyCurrent
                ? $"{session.DisplayTitle} 已是当前会话；已将 {harness.DisplayName} 置前。"
                : result.Message);
        SetStatus(succeeded
            ? alreadyCurrent
                ? $"{session.DisplayTitle} 已是当前会话；已将 {harness.DisplayName} 置前。"
                : $"已通过 {harness.DisplayName} 直连插件打开会话：{session.DisplayTitle}。"
            : result.Message);
        if (succeeded)
        {
            _ = RefreshHarnessStateAsync();
        }
    }

    private void SelectHarnessSessionSlot(
        MicroHarnessDefinition harness,
        int slotId)
    {
        var session = _harnessStateSnapshot?.HarnessId == harness.Id &&
            slotId >= 0 &&
            slotId < _harnessStateSnapshot.Sessions.Count
                ? _harnessStateSnapshot.Sessions[slotId]
                : null;
        _currentAgentSlotId = session is null ? null : slotId;
        _selectedHarnessSessionId = session?.Id;
        RefreshAgentSlotPresentation();
    }

    private string? CurrentHarnessSessionId() =>
        _selectedHarnessSessionId ?? _harnessStateSnapshot?.CurrentSessionId;

    private int? CurrentHarnessSessionSlot()
    {
        if (_currentAgentSlotId is int slotId)
        {
            return slotId;
        }

        var currentId = CurrentHarnessSessionId();
        var sessions = _harnessStateSnapshot?.Sessions;
        if (currentId is null || sessions is null)
        {
            return null;
        }

        for (var index = 0; index < sessions.Count; index++)
        {
            if (string.Equals(
                sessions[index].Id,
                currentId,
                StringComparison.Ordinal))
            {
                return index;
            }
        }

        return null;
    }

    private string HarnessActionLabel(string actionId) => actionId switch
    {
        MicroHarnessActionIds.None => _localization.IsEnglish ? "Unassigned" : "未分配",
        MicroHarnessActionIds.NewSession => _localization.IsEnglish ? "New session" : "新建会话",
        MicroHarnessActionIds.ForkSession => _localization.IsEnglish ? "Fork current session" : "Fork 当前会话",
        MicroHarnessActionIds.ArchiveSession => _localization.IsEnglish ? "Archive current session" : "归档当前会话",
        MicroHarnessActionIds.CancelTurn => _localization.IsEnglish ? "Stop current generation" : "停止当前生成",
        MicroHarnessActionIds.ToggleConversationView => _localization.IsEnglish ? "Conversation / trajectory" : "对话 / 轨迹",
        MicroHarnessActionIds.ApproveInteraction => _localization.IsEnglish ? "Allow once / approve plan" : "允许一次 / 批准计划",
        MicroHarnessActionIds.RejectInteraction => _localization.IsEnglish ? "Reject / decline plan" : "拒绝 / 否决计划",
        MicroHarnessActionIds.LoadOlderHistory => _localization.IsEnglish ? "Load older history" : "加载更早历史",
        MicroHarnessActionIds.ToggleSidebar => _localization.IsEnglish ? "Toggle sidebar" : "切换侧边栏",
        MicroHarnessActionIds.OpenDetails => _localization.IsEnglish ? "Open details" : "打开详情栏",
        MicroHarnessActionIds.CloseDetails => _localization.IsEnglish ? "Close details" : "关闭详情栏",
        MicroHarnessActionIds.PreviousSession => _localization.IsEnglish ? "Previous session" : "上一个会话",
        MicroHarnessActionIds.NextSession => _localization.IsEnglish ? "Next session" : "下一个会话",
        MicroHarnessActionIds.OpenSelectedSession => _localization.IsEnglish ? "Open selected session" : "打开已选会话",
        MicroHarnessActionIds.ActivateSurface => _localization.IsEnglish ? "Open / focus Harness" : "打开 / 聚焦 Harness",
        MicroHarnessActionIds.VoiceDictation => _localization.IsEnglish ? "Push to talk" : "按住说话",
        MicroHarnessActionIds.ComposerSelectPrevious => _localization.IsEnglish ? "Previous composer control" : "输入区上一控件",
        MicroHarnessActionIds.ComposerSelectNext => _localization.IsEnglish ? "Next composer control" : "输入区下一控件",
        MicroHarnessActionIds.ComposerActivateSelection => _localization.IsEnglish ? "Activate highlighted composer control" : "执行高亮输入区控件",
        MicroHarnessActionIds.ComposerBack => _localization.IsEnglish ? "Back one menu level" : "返回上一级",
        MicroHarnessActionIds.ComposerSubmit => _localization.IsEnglish ? "Send current input" : "发送当前输入",
        MicroHarnessActionIds.ReasoningDecrease => _localization.IsEnglish ? "Decrease reasoning effort" : "降低推理强度",
        MicroHarnessActionIds.ReasoningIncrease => _localization.IsEnglish ? "Increase reasoning effort" : "提高推理强度",
        MicroHarnessActionIds.ToggleQuickModel => _localization.IsEnglish ? "Toggle quick model" : "切换快捷模型",
        MicroHarnessActionIds.OpenGoal => _localization.IsEnglish ? "Set or view Goal" : "设置或查看 Goal",
        _ => actionId,
    };

    private async Task ExecuteHarnessActionAsync(
        MicroHarnessDefinition harness,
        string actionId,
        string label,
        string? sessionId = null,
        bool onSettingsKey = false)
    {
        var capabilities = _harnessStateSnapshot?.HarnessId == harness.Id
            ? _harnessStateSnapshot.Capabilities
            : null;
        if (capabilities is not null && !capabilities.Supports(actionId))
        {
            ShowUnavailableHarnessAction(
                harness,
                label,
                "适配器没有声明此能力");
            return;
        }

        if (HarnessActionRequiresSession(actionId) &&
            string.IsNullOrWhiteSpace(sessionId))
        {
            ShowUnavailableHarnessAction(
                harness,
                label,
                "当前没有可操作的会话");
            return;
        }

        if (onSettingsKey)
        {
            if (_harnessModelSwitching)
            {
                return;
            }

            _settingsDisplayProgress = null;
            _harnessModelSwitching = true;
            _harnessActionElapsedTimer.Stop();
            HideHarnessProgressStatus();
            UpdateQuotaPresentation();
        }

        try
        {
            SetLed(ActivityLed, "#9EBDFF", $"正在执行 {label}");
            var result = await _harnessRegistry.ExecuteActionAsync(
                harness.Id,
                actionId,
                sessionId);
            SetLed(
                ActivityLed,
                result.Success ? "#9EBDFF" : "#FFD66E",
                result.Message);
            SetStatus(result.Success
                ? $"{harness.DisplayName} · {label} 已通过直连插件执行。\n{result.Message}"
                : $"{harness.DisplayName} · {label} 未执行。\n{result.Message}");
            if (result.Success)
            {
                if (onSettingsKey ||
                    actionId is MicroHarnessActionIds.ComposerActivateSelection or
                        MicroHarnessActionIds.ComposerBack)
                {
                    await RefreshHarnessStateAsync();
                }
                else
                {
                    _ = RefreshHarnessStateAsync();
                }
            }
        }
        finally
        {
            if (onSettingsKey)
            {
                _harnessModelSwitching = false;
                UpdateQuotaPresentation();
            }
        }
    }

    private static bool HarnessActionRequiresSession(string actionId) =>
        actionId is MicroHarnessActionIds.ForkSession or
            MicroHarnessActionIds.ArchiveSession or
            MicroHarnessActionIds.CancelTurn or
            MicroHarnessActionIds.ToggleConversationView or
            MicroHarnessActionIds.ApproveInteraction or
            MicroHarnessActionIds.RejectInteraction or
            MicroHarnessActionIds.LoadOlderHistory or
            MicroHarnessActionIds.ComposerSubmit or
            MicroHarnessActionIds.ReasoningDecrease or
            MicroHarnessActionIds.ReasoningIncrease or
            MicroHarnessActionIds.ToggleQuickModel or
            MicroHarnessActionIds.OpenGoal;

    private void ShowUnavailableHarnessAction(
        MicroHarnessDefinition harness,
        string label,
        string reason)
    {
        SetLed(ActivityLed, "#FFD66E", $"{label} 在当前 Harness 中不可用");
        SetStatus(
            $"{harness.DisplayName} · {label} 未发送：{reason}。\n" +
            "为避免串台，本次不会降级发送 Codex HID。");
    }

    private bool ShouldFocusAgentAfterTap(string key)
    {
        if (_profileSettings.Current.SingleTapAgentKeys)
        {
            _lastAgentTapKey = null;
            _lastAgentTapTimestamp = 0;
            return true;
        }

        var now = Stopwatch.GetTimestamp();
        var elapsed = _lastAgentTapTimestamp == 0
            ? TimeSpan.MaxValue
            : Stopwatch.GetElapsedTime(_lastAgentTapTimestamp, now);
        var isDoubleTap = key == _lastAgentTapKey &&
            elapsed <= AgentDoubleTapThreshold;
        _lastAgentTapKey = isDoubleTap ? null : key;
        _lastAgentTapTimestamp = isDoubleTap ? 0 : now;
        return isDoubleTap;
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
        var brush = new SolidColorBrush(
            recording
                ? Color.FromRgb(0x0C, 0x8E, 0x7E)
                : Color.FromRgb(0x17, 0x17, 0x17));
        ActionIcon10.IconBrush = brush;
        ActionIcon10Split.IconBrush = brush;
        ActionIcon11Split.IconBrush = brush;
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
                    Localize($"滚轮路由已接收 · #{routeSequence}"));
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
                if (IsCodexHarnessActive() && !_broker.IsReady)
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
        if (IsCodexHarnessActive() && !_broker.IsReady)
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

        if (IsCodexHarnessActive() && !_broker.IsReady)
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
        if (!IsCodexHarnessActive())
        {
            await StepHarnessDialSelectionAsync(intent.Direction);
            return;
        }

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
            var physicalClockwise = step > 0;
            var reportedClockwise =
                _dialDirectionSettings.ToReportedClockwise(physicalClockwise);
            var routesDialog = _cachedDialSelection is
            {
                Surface: CodexSelectionSurface.Dialog,
            };
            Exception? animationError = null;
            try
            {
                AnimateDialStep(physicalClockwise);
            }
            catch (Exception exception)
            {
                animationError = exception;
            }

            Func<Task<MicroSendResult>> sendStep = routesDialog
                ? () => _broker.TapDialogKeyAsync(
                    BrokerKeyboardKey.Tab,
                    shift: reportedClockwise)
                : () => _broker.StepEncoderAsync(reportedClockwise);
            var stepLabel = routesDialog
                ? reportedClockwise
                    ? "确认框向上选择 · VHF Shift+Tab"
                    : "确认框向下选择 · VHF Tab"
                : reportedClockwise
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
                    Localize($"{(routesDialog
                        ? reportedClockwise ? "VHF Shift+Tab" : "VHF Tab"
                        : reportedClockwise ? "ENC_CW" : "ENC_CC")} 已交付 · #{sequence}"));
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
        if (!IsCodexHarnessActive())
        {
            var harness = ActiveHarness();
            var mode = _harnessRegistry.ResolveKnobMode(harness.Id);
            if (mode == MicroHarnessKnobModes.ComposerNavigation)
            {
                await ExecuteHarnessActionAsync(
                    harness,
                    MicroHarnessActionIds.ComposerActivateSelection,
                    HarnessActionLabel(
                        MicroHarnessActionIds.ComposerActivateSelection),
                    CurrentHarnessSessionId());
            }
            else if (mode == MicroHarnessKnobModes.ReasoningOnly)
            {
                await ExecuteHarnessActionAsync(
                    harness,
                    MicroHarnessActionIds.ToggleQuickModel,
                    HarnessActionLabel(MicroHarnessActionIds.ToggleQuickModel),
                    CurrentHarnessSessionId());
            }
            else if (_harnessStateSnapshot?.Sessions.Count > 0)
            {
                await ActivateHarnessSessionSlotAsync(
                    harness,
                    _currentAgentSlotId ?? 0);
            }
            else
            {
                await ActivateHarnessAsync(harness);
            }
            return;
        }

        if (_layoutObserver.Current.EncoderMode == "reasoning")
        {
            await ToggleQuickModelAsync();
            return;
        }

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

    private void StepHarnessSessionSelection(int steps)
    {
        var snapshot = _harnessStateSnapshot;
        if (snapshot is null || snapshot.Sessions.Count == 0)
        {
            SetStatus($"{ActiveHarness().DisplayName} 尚未返回可选择的会话；按下旋钮可打开 Harness。");
            return;
        }

        var current = Math.Clamp(_currentAgentSlotId ?? 0, 0, snapshot.Sessions.Count - 1);
        var physicalClockwise = steps > 0;
        var delta = (physicalClockwise ? -1 : 1) * Math.Abs(steps);
        var next = (current + delta) % snapshot.Sessions.Count;
        if (next < 0)
        {
            next += snapshot.Sessions.Count;
        }

        _currentAgentSlotId = next;
        _selectedHarnessSessionId = snapshot.Sessions[next].Id;
        AnimateDialStep(physicalClockwise);
        RefreshAgentSlotPresentation();
        ShowDialSelectionFeedback(snapshot.Sessions[next].DisplayTitle);
    }

    private async Task StepHarnessDialSelectionAsync(int steps)
    {
        var harness = ActiveHarness();
        var mode = _harnessRegistry.ResolveKnobMode(harness.Id);
        if (mode == MicroHarnessKnobModes.RecentSessions)
        {
            StepHarnessSessionSelection(steps);
            return;
        }

        var physicalClockwise = steps > 0;
        var actionId = mode == MicroHarnessKnobModes.ReasoningOnly
            ? physicalClockwise
                ? MicroHarnessActionIds.ReasoningIncrease
                : MicroHarnessActionIds.ReasoningDecrease
            : physicalClockwise
                ? MicroHarnessActionIds.ComposerSelectPrevious
                : MicroHarnessActionIds.ComposerSelectNext;
        AnimateDialStep(physicalClockwise);
        ShowDialSelectionFeedback(HarnessActionLabel(actionId));
        await ExecuteHarnessActionAsync(
            harness,
            actionId,
            HarnessActionLabel(actionId),
            CurrentHarnessSessionId());
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
                Localize($"菜单位置读取已跳过 · {exception.Message}"));
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
        _dialSelectionText = text;
        var localizedText = Localize(text);
        DialSelectionText.Text = localizedText;
        DialSelectionHud.Visibility = Visibility.Visible;
        DialSelectionHud.BeginAnimation(OpacityProperty, null);
        DialSelectionHud.Opacity = 1;
        AutomationProperties.SetItemStatus(DialButton, localizedText);
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

    private void Settings_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _settingsPointerDownTimestamp = Stopwatch.GetTimestamp();
    }

    private void Settings_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
        ShowSoftwareSettings();
        _settingsWindow?.FocusActiveAgentSettings();
    }

    private void Settings_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        e.Handled = true;
        ShowSoftwareSettings();
        _settingsWindow?.FocusActiveAgentSettings();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!IsCodexHarnessActive())
        {
            _settingsPointerDownTimestamp = 0;
            var harness = ActiveHarness();
            var sessionId = CurrentHarnessSessionId();
            if (IsDeepSeekHarness(harness) &&
                !string.IsNullOrWhiteSpace(sessionId))
            {
                await ExecuteHarnessActionAsync(
                    harness,
                    MicroHarnessActionIds.ToggleQuickModel,
                    HarnessActionLabel(MicroHarnessActionIds.ToggleQuickModel),
                    sessionId,
                    onSettingsKey: true);
            }
            else
            {
                await ActivateHarnessAsync(harness, onSettingsKey: true);
            }
            return;
        }

        var pressedAt = _settingsPointerDownTimestamp;
        _settingsPointerDownTimestamp = 0;
        if (
            pressedAt != 0 &&
            Stopwatch.GetElapsedTime(pressedAt) >=
                SettingsLongPressThreshold)
        {
            await OpenCodexMicroSettingsAsync("打开 Codex Micro 设置");
            return;
        }

        await ToggleQuickModelAsync();
    }

    private async Task ToggleQuickModelAsync()
    {
        if (_quickModelSwitching ||
            _windowClosed ||
            !IsCodexHarnessActive())
        {
            return;
        }

        _quickModelSwitching = true;
        _modelRefreshCancellation?.Cancel();
        UpdateQuotaPresentation();
        try
        {
            if (!await ActivateCodexAsync(initialDelayMilliseconds: 0))
            {
                return;
            }

            var action = new CancellationTokenSource(
                TimeSpan.FromSeconds(8));
            _modelActionCancellation = action;
            var quickModels = _profileSettings.Current;
            var result = await _modelToggleService.ToggleAsync(
                quickModels.QuickModelA,
                quickModels.QuickModelB,
                action.Token);
            if (_windowClosed)
            {
                return;
            }

            if (result.Previous != CodexQuickModel.Unknown)
            {
                _quickModel = result.Previous;
            }

            if (result.Succeeded)
            {
                _quickModel = result.Current;
                var name = FormatQuickModelName(result.Current);
                SetLed(ActivityLed, "#9EBDFF", $"已切换到 {name}");
                SetStatus($"当前任务的下一轮已切换到 {name}；全局默认模型未更改。");
            }
            else
            {
                SetLed(ActivityLed, "#FF7994", "快捷模型切换失败");
                SetStatus(DescribeQuickModelError(result.Error));
            }
        }
        catch (OperationCanceledException)
        {
            if (!_windowClosed)
            {
                SetLed(ActivityLed, "#FFD66E", "快捷模型切换超时");
                SetStatus("快捷模型切换超时；模型保持不变，请再试一次。");
            }
        }
        finally
        {
            _modelActionCancellation?.Dispose();
            _modelActionCancellation = null;
            _quickModelSwitching = false;
            if (!_windowClosed)
            {
                UpdateQuotaPresentation();
            }
        }
    }

    private static string DescribeQuickModelError(string? error) =>
        error switch
        {
            "composer-model-button" or
            "composer-model-button-expand" =>
                "没有找到当前任务的模型按钮；请先打开一个可输入的 Codex 任务。",
            "advanced-view" or
            "model-category" or
            "model-category-expand" =>
                "无法打开 Codex 的 Advanced 模型菜单；模型保持不变。",
            "model-option-sol" =>
                "当前账户的模型菜单中没有找到 Sol；模型保持不变。",
            "model-option-luna" =>
                "当前账户的模型菜单中没有找到 Luna；模型保持不变。",
            "model-option-terra" =>
                "当前账户的模型菜单中没有找到 Terra；模型保持不变。",
            "model-readback" =>
                "模型选择已发送，但无法确认结果；请查看 Codex 输入框下方的模型按钮。",
            "codex-window" =>
                "没有找到 Codex 主窗口；模型保持不变。",
            _ => "快捷模型切换失败；模型保持不变，请再试一次。",
        };

    private static string FormatQuickModelName(CodexQuickModel model) =>
        model switch
        {
            CodexQuickModel.Sol => "Sol",
            CodexQuickModel.Terra => "Terra",
            CodexQuickModel.Luna => "Luna",
            _ => "快捷模型",
        };

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
            if (IsCodexHarnessActive())
            {
                await RunActionAsync(
                    () => _broker.MoveJoystickAsync(angle, 1, parts[1]),
                    $"analog {parts[1]}");
            }
            else
            {
                await RouteHarnessJoystickDirectionAsync(
                    ActiveHarness(),
                    parts[1]);
            }
        }
        finally
        {
            if (feedbackVersion == _joystickFeedbackVersion)
            {
                AnimateJoystickReturn();
            }
        }
    }

    private async Task RouteHarnessJoystickDirectionAsync(
        MicroHarnessDefinition harness,
        string direction)
    {
        var controlId = direction switch
        {
            "up" => MicroHarnessControlIds.JoystickUp,
            "down" => MicroHarnessControlIds.JoystickDown,
            "left" => MicroHarnessControlIds.JoystickLeft,
            "right" => MicroHarnessControlIds.JoystickRight,
            _ => null,
        };
        if (controlId is null)
        {
            ShowUnavailableHarnessAction(
                harness,
                $"摇杆 {direction}",
                "此方向没有 Harness 映射");
            return;
        }

        await ExecuteHarnessMappedControlAsync(harness, controlId);
    }

    private bool SelectAdjacentHarnessSession(int delta)
    {
        var snapshot = _harnessStateSnapshot;
        if (snapshot is null || snapshot.Sessions.Count == 0)
        {
            return false;
        }

        var current = Math.Clamp(
            _currentAgentSlotId ?? 0,
            0,
            snapshot.Sessions.Count - 1);
        var next = (current + delta) % snapshot.Sessions.Count;
        if (next < 0)
        {
            next += snapshot.Sessions.Count;
        }

        _currentAgentSlotId = next;
        _selectedHarnessSessionId = snapshot.Sessions[next].Id;
        RefreshAgentSlotPresentation();
        ShowDialSelectionFeedback(snapshot.Sessions[next].DisplayTitle);
        return true;
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
        if (IsCodexHarnessActive() && !_broker.IsReady)
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
        if (!IsCodexHarnessActive() && vector.Direction is not null)
        {
            _ = RouteHarnessJoystickDirectionAsync(
                ActiveHarness(),
                vector.Direction);
        }
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
        if (!IsCodexHarnessActive() || !_broker.IsReady)
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
                if (!IsCodexHarnessActive())
                {
                    _joystickReportQueue.Clear();
                    return;
                }

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

        return IntPtr.Zero;
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

        _windowMoving = true;
        _windowMoveStartScreen = PointToScreen(e.GetPosition(this));
        _windowMoveStartPosition = new Point(Left, Top);
        _windowMoveDpi = VisualTreeHelper.GetDpi(this);
        _ = CaptureMouse();
        e.Handled = true;
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_windowMoving || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var screen = PointToScreen(e.GetPosition(this));
        Left = _windowMoveStartPosition.X +
            (screen.X - _windowMoveStartScreen.X) /
            Math.Max(_windowMoveDpi.DpiScaleX, 1);
        Top = _windowMoveStartPosition.Y +
            (screen.Y - _windowMoveStartScreen.Y) /
            Math.Max(_windowMoveDpi.DpiScaleY, 1);
        e.Handled = true;
    }

    private void Window_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        EndWindowMove();
    }

    private void Window_LostMouseCapture(
        object sender,
        MouseEventArgs e) => _windowMoving = false;

    private void EndWindowMove()
    {
        if (!_windowMoving)
        {
            return;
        }

        _windowMoving = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        _profileSettings.SetWindowPlacement(Left, Top, Topmost);
    }

    private void DeviceFrame_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        if (
            FindAncestor<Button>(e.OriginalSource as DependencyObject) is
                { ContextMenu: null })
        {
            e.Handled = true;
        }
    }

    private void DeviceContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        TopmostMenuItem.IsChecked = Topmost;
    }

    private void HarnessContextMenu_Opened(object sender, RoutedEventArgs e)
        => PopulateHarnessContextMenu();

    internal void PopulateHarnessContextMenu()
    {
        // A fresh menu opening is a new selection gesture. Any suppression
        // left by the previous "+" gesture must not affect an intentional
        // click made after the menu is reopened.
        _suppressedHarnessSelectionId = null;
        HarnessContextMenu.Items.Clear();
        var activeHarness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        foreach (var harness in _harnessRegistry.Definitions)
        {
            var item = new MenuItem
            {
                Tag = harness.Id,
                IsCheckable = true,
                IsChecked = harness.Id == activeHarness.Id,
                IsEnabled = harness.IsAvailable,
            };
            item.Header = CreateHarnessMenuHeader(
                item,
                harness.DisplayName,
                LocalizeHarnessDescription(harness),
                GetHarnessIconId(harness),
                harness.Id);
            item.Click += HarnessTargetMenuItem_Click;
            HarnessContextMenu.Items.Add(item);
        }

        HarnessContextMenu.Items.Add(new Separator());
        var manageItem = new MenuItem
        {
            Header = null,
        };
        manageItem.Header = CreateHarnessMenuHeader(
            manageItem,
            _localization.IsEnglish
                ? "Manage Agent / Harness…"
                : "管理 Agent / Harness…",
            _localization.IsEnglish
                ? "Open the full settings page"
                : "打开完整设置页",
            "SETUP");
        manageItem.Click += ManageHarnessMenuItem_Click;
        HarnessContextMenu.Items.Add(manageItem);
        if (_canCloseKeypad)
        {
            HarnessContextMenu.Items.Add(new Separator());
            var closeItem = new MenuItem();
            closeItem.Header = CreateHarnessMenuHeader(
                closeItem,
                _localization.IsEnglish
                    ? "Close this keypad"
                    : "关闭此小键盘",
                _localization.IsEnglish
                    ? "Remove this window and its independent keypad profile"
                    : "移除此窗口及其独立小键盘配置",
                "REJ");
            closeItem.Click += CloseKeypadMenuItem_Click;
            HarnessContextMenu.Items.Add(closeItem);
        }
    }

    private FrameworkElement CreateHarnessMenuHeader(
        MenuItem owner,
        string title,
        string description,
        string iconId,
        string? newKeypadHarnessId = null)
    {
        var grid = new Grid
        {
            Width = 34,
            Height = 34,
            IsHitTestVisible = true,
        };
        var icon = new KeycapIcon
        {
            Width = 25,
            Height = 25,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            KeycapId = iconId,
            IconBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x23, 0x26)),
            IsHitTestVisible = false,
        };
        grid.Children.Add(icon);
        if (_openHarnessInNewKeypad is not null &&
            !string.IsNullOrWhiteSpace(newKeypadHarnessId))
        {
            var badge = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, -3, -3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(
                    Color.FromRgb(0x31, 0xB9, 0xFF)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = "+",
                    Margin = new Thickness(0, -2, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    IsHitTestVisible = false,
                },
                ToolTip = _localization.IsEnglish
                    ? $"Add a new {title} keypad; keep this keypad unchanged"
                    : $"新增 {title} 小键盘；当前小键盘保持不变",
            };
            AutomationProperties.SetName(
                badge,
                _localization.IsEnglish
                    ? $"Add a new {title} keypad without changing this keypad"
                    : $"新增 {title} 小键盘（当前小键盘不变）");
            badge.PreviewMouseLeftButtonDown += (_, args) =>
            {
                args.Handled = true;
                AddHarnessInNewKeypad(newKeypadHarnessId, title);
            };
            Panel.SetZIndex(badge, 3);
            grid.Children.Add(badge);
        }
        var tooltipContent = new StackPanel
        {
            MaxWidth = 260,
        };
        tooltipContent.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x28, 0x2B)),
        });
        tooltipContent.Children.Add(new TextBlock
        {
            Text = description,
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 11,
            LineHeight = 16,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x70, 0x74, 0x78)),
        });
        owner.ToolTip = new ToolTip
        {
            Content = tooltipContent,
            Placement = PlacementMode.Right,
            HorizontalOffset = 8,
            IsHitTestVisible = false,
        };
        ToolTipService.SetInitialShowDelay(owner, 260);
        ToolTipService.SetShowDuration(owner, 12000);
        AutomationProperties.SetName(owner, title);
        AutomationProperties.SetHelpText(owner, description);
        return grid;
    }

    private string LocalizeHarnessDescription(MicroHarnessDefinition harness)
    {
        if (!_localization.IsEnglish)
        {
            return harness.Id switch
            {
                "codex" => "原生 Micro HID 与任务路由",
                "deepseek-harness" => "直连插件 · 不模拟键鼠",
                _ => "已注册的直连 Harness 适配器",
            };
        }

        return harness.Description;
    }

    private static string GetHarnessIconId(MicroHarnessDefinition harness)
    {
        if (harness.Id == "codex")
        {
            return "CODEX";
        }

        return harness.Id.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ||
            harness.DisplayName.Contains("deepseek", StringComparison.OrdinalIgnoreCase)
                ? "DEEPSEEK"
                : "HARNESS";
    }

    private void HarnessTargetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string harnessId })
        {
            return;
        }

        if (string.Equals(
                _suppressedHarnessSelectionId,
                harnessId,
                StringComparison.OrdinalIgnoreCase))
        {
            // The "+" badge lives inside this MenuItem. Consume any parent
            // Click raised by the same pointer gesture so the source keypad
            // keeps its current Harness selection.
            _suppressedHarnessSelectionId = null;
            e.Handled = true;
            return;
        }

        _suppressedHarnessSelectionId = null;

        var harness = _harnessRegistry.Resolve(harnessId);
        _profileSettings.SetActiveHarness(harness.Id);
        RefreshHarnessPresentation();
        SetStatus($"Codex 键现已指向 {harness.DisplayName}。右键 Codex 键可随时切换。");
    }

    internal void AddHarnessInNewKeypad(string harnessId, string title)
    {
        if (_openHarnessInNewKeypad is null ||
            string.IsNullOrWhiteSpace(harnessId))
        {
            return;
        }

        var currentHarnessId = _profileSettings.Current.ActiveHarnessId;
        _suppressedHarnessSelectionId = harnessId;
        HarnessContextMenu.IsOpen = false;
        _openHarnessInNewKeypad(harnessId);

        // The controller callback is expected to create a separate profile.
        // Preserve the source window even if a future callback accidentally
        // mutates the profile while doing so.
        if (!string.Equals(
                _profileSettings.Current.ActiveHarnessId,
                currentHarnessId,
                StringComparison.OrdinalIgnoreCase))
        {
            _profileSettings.SetActiveHarness(currentHarnessId);
            RefreshHarnessPresentation();
        }

        SetStatus(_localization.IsEnglish
            ? $"Added a new {title} keypad; this keypad is unchanged."
            : $"已新增 {title} 小键盘；当前小键盘未改变。");
    }

    private void ManageHarnessMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ShowSoftwareSettings();
        _settingsWindow?.FocusHarnessOptions();
    }

    private void TopmostMenuItem_Click(object sender, RoutedEventArgs e) =>
        SetTopmostState(TopmostMenuItem.IsChecked);

    private async void ReconnectMenuItem_Click(object sender, RoutedEventArgs e) =>
        await ReconnectAsync();

    private async Task ReconnectAsync()
    {
        if (!_broker.IsReady)
        {
            await ConnectAsync();
            return;
        }

        if (_connecting)
        {
            return;
        }

        _connecting = true;
        try
        {
            var info = await _broker.RecoverCodexLinkAsync();
            _transportName = info.TransportName;
            ApplyTransportReadyState();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                Win32Exception or
                InvalidDataException or
                IOException)
        {
            SetStatus(LocalizeDriverError(exception));
        }
        finally
        {
            _connecting = false;
        }
    }

    private void OpenSoftwareSettingsMenuItem_Click(
        object sender,
        RoutedEventArgs e) => ShowSoftwareSettings();

    private async void OpenOfficialSettingsMenuItem_Click(
        object sender,
        RoutedEventArgs e) =>
        await OpenCodexMicroSettingsAsync("打开 Codex Micro 设置");

    private void ShowSoftwareSettings()
    {
        if (_settingsWindow is not null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
            {
                _settingsWindow.WindowState = WindowState.Normal;
            }

            _settingsWindow.Topmost = Topmost;
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        var settings = new MicroSettingsWindow(
            _localization,
            _profileSettings,
            DesignSurface,
            _layoutObserver,
            _configWriter,
            _harnessRegistry,
            _voiceInput,
            () => OpenCodexMicroSettingsAsync("打开 Codex Micro 设置"),
            ReconnectAsync,
            () => _broker.IsReady)
        {
            Owner = this,
            Topmost = Topmost,
        };
        _settingsWindow = settings;
        settings.Closed += (_, _) =>
        {
            if (ReferenceEquals(_settingsWindow, settings))
            {
                _settingsWindow = null;
            }
        };
        settings.Show();
        settings.Activate();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Hide();

    private void CloseKeypadMenuItem_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_canCloseKeypad)
        {
            _closeKeypad?.Invoke();
        }
        else
        {
            Hide();
        }
    }

    private void SetTopmostState(bool value)
    {
        Topmost = value;
        TopmostMenuItem.IsChecked = value;
        _profileSettings.SetWindowPlacement(Left, Top, value);
        if (value)
        {
            _lastForegroundWindow = IntPtr.Zero;
            RefreshTopmostContinuity();
        }

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
            if (!IsCodexHarnessActive())
            {
                return;
            }

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
            if (!IsCodexHarnessActive())
            {
                return;
            }

            ResolveCurrentAgentSlot();
            RefreshAgentSlotPresentation();
        });
    }

    private void ResolveCurrentAgentSlot()
    {
        if (!IsCodexHarnessActive())
        {
            return;
        }

        _currentAgentSlotId = AgentLightingAppearance.ResolveCurrentSessionSlot(
            _latestSlotLighting?.Slots ?? [],
            _currentAgentSlotId,
            _latestAgentRoster?.Entries.Select(entry => entry.SlotId));
    }

    private void RefreshAgentSlotPresentation()
    {
        if (!IsCodexHarnessActive())
        {
            RefreshHarnessSessionPresentation();
            return;
        }

        AgentBackGlyph.Visibility = Visibility.Collapsed;

        var lightingBySlot = _latestSlotLighting?.Slots
            .Where(slot => slot.SlotId is >= 0 and < 6)
            .ToDictionary(slot => slot.SlotId) ?? [];

        for (var slotId = 0; slotId < _agentKeys.Length; slotId++)
        {
            _agentKeys[slotId].IsEnabled = true;
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

    private void RefreshHarnessSessionPresentation()
    {
        var harness = ActiveHarness();
        if (IsHarnessMenuNavigationActive(harness))
        {
            RefreshHarnessMenuNavigationPresentation(harness);
            return;
        }

        AgentBackGlyph.Visibility = Visibility.Collapsed;
        var sessions = _harnessStateSnapshot?.HarnessId == harness.Id
            ? _harnessStateSnapshot.Sessions
            : [];
        for (var slotId = 0; slotId < _agentKeys.Length; slotId++)
        {
            var session = slotId < sessions.Count ? sessions[slotId] : null;
            if (session is null)
            {
                _agentKeys[slotId].IsEnabled = false;
                ToolTipService.SetShowOnDisabled(_agentKeys[slotId], true);
                ApplyAgentLightingAppearance(
                    slotId,
                    AgentLightingAppearance.From(null));
                SetHelp(
                    _agentKeys[slotId],
                    $"{harness.DisplayName} · 槽位 {slotId + 1}",
                    _harnessStateSnapshot is null
                        ? "适配器离线或尚未返回状态；单击可打开 Harness。"
                        : "当前 Harness 没有为此槽位返回会话。");
                continue;
            }

            _agentKeys[slotId].IsEnabled = true;
            var current = slotId == _currentAgentSlotId;
            var appearance = AgentLightingAppearance.FromHarnessSession(
                session.Running,
                current);
            ApplyAgentLightingAppearance(slotId, appearance);
            SetHelp(
                _agentKeys[slotId],
                session.DisplayTitle,
                $"{harness.DisplayName} 会话 · AG{slotId:00} · " +
                $"{(session.Running
                    ? "运行中"
                    : current
                        ? "当前会话 · 空闲"
                        : "最近会话 · 空闲")} · 单击通过插件直连打开。\n" +
                "不会发送 Codex HID。" );
        }
    }

    private bool IsHarnessMenuNavigationActive(MicroHarnessDefinition harness) =>
        harness.Id != "codex" &&
        _harnessStateSnapshot?.HarnessId == harness.Id &&
        _harnessStateSnapshot.NavigationDepth > 0;

    private void RefreshHarnessMenuNavigationPresentation(
        MicroHarnessDefinition harness)
    {
        var depth = _harnessStateSnapshot?.NavigationDepth ?? 0;
        AgentBackGlyph.Visibility = Visibility.Visible;
        for (var slotId = 0; slotId < _agentKeys.Length; slotId++)
        {
            var isBack = slotId == 0;
            _agentKeys[slotId].IsEnabled = isBack;
            ToolTipService.SetShowOnDisabled(_agentKeys[slotId], true);
            ApplyAgentLightingAppearance(
                slotId,
                isBack
                    ? new AgentLightingAppearance(
                        true,
                        true,
                        false,
                        Color.FromRgb(0xE8, 0x4F, 0x67),
                        1,
                        0.78,
                        0.58,
                        0.24,
                        0.56,
                        0.50,
                        "返回上一级",
                        "navigation back")
                    : AgentLightingAppearance.From(null));

            SetHelp(
                _agentKeys[slotId],
                isBack
                    ? (_localization.IsEnglish ? "Back one level" : "返回上一级")
                    : $"{harness.DisplayName} · Agent {slotId + 1}",
                isBack
                    ? (_localization.IsEnglish
                        ? $"Composer menu level {depth} · click to return one level."
                        : $"当前位于 {harness.DisplayName} 选择菜单第 {depth} 层 · 单击返回上一级。")
                    : (_localization.IsEnglish
                        ? "Temporarily locked while a composer menu is open. Use the red Back key first."
                        : "选择菜单打开期间暂时锁定；请先使用红色返回键。"));
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
        ActionKey10.Visibility = snapshot.SeparateMicrophoneKeys
            ? Visibility.Collapsed
            : Visibility.Visible;
        ActionKey10Split.Visibility = snapshot.SeparateMicrophoneKeys
            ? Visibility.Visible
            : Visibility.Collapsed;
        ActionKey11Split.Visibility = snapshot.SeparateMicrophoneKeys
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (var (slotId, presentation) in _actionKeys)
        {
            var binding = snapshot.GetSlot(slotId);
            var definition = CodexKeycapCatalog.Get(binding.KeycapId);
            presentation.Icon.KeycapId = binding.KeycapId;
            var action = binding.ResolvedAction;
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

        RefreshDialHelp(snapshot);

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

        RefreshHarnessPresentation();
    }

    private void RefreshDialHelp(CodexMicroLayoutSnapshot snapshot)
    {
        var harness = ActiveHarness();
        if (harness.Id != "codex")
        {
            var mode = _harnessRegistry.ResolveKnobMode(harness.Id);
            SetHelp(
                DialButton,
                mode switch
                {
                    MicroHarnessKnobModes.ComposerNavigation =>
                        $"{harness.DisplayName} 输入区旋钮",
                    MicroHarnessKnobModes.ReasoningOnly =>
                        $"{harness.DisplayName} 推理强度旋钮",
                    _ => $"{harness.DisplayName} 会话旋钮",
                },
                mode switch
                {
                    MicroHarnessKnobModes.ComposerNavigation =>
                        "滚轮或拖动：只选择当前输入框内可见、可用的控件；其他插件新增的输入区按钮会自动加入。\n" +
                        "短按：执行网页中蓝色高亮的控件。",
                    MicroHarnessKnobModes.ReasoningOnly =>
                        "滚轮或拖动：只调节当前模型的推理强度。\n" +
                        "短按：在设置的两个快捷模型间切换。",
                    _ =>
                        "滚轮或拖动：在此 Harness 返回的六个最近会话间选择。\n" +
                        "短按：通过直连插件打开选中的会话；不会发送 Codex HID。",
                });
            return;
        }

        SetHelp(
            DialButton,
            snapshot.EncoderMode == "reasoning"
                ? "推理强度旋钮"
                : "选择旋钮",
            snapshot.EncoderMode == "reasoning"
                ? "滚轮/按住左键上下或左右拖动：只调节推理强度。\n短按：在快捷模型 A/B 间切换。"
                : "滚轮/按住左键上下或左右拖动：移动输入区控件或菜单选项。\n短按：打开或确认。");
    }

    private MicroHarnessDefinition ActiveHarness() =>
        _harnessRegistry.Resolve(_profileSettings.Current.ActiveHarnessId);

    private bool IsCodexHarnessActive() => ActiveHarness().Id == "codex";

    private static bool IsHarnessForeground(MicroHarnessDefinition harness) =>
        harness.Id == "codex"
            ? CodexWindowActivator.IsForeground(packageRoot: null)
            : HarnessWindowActivator.IsForeground(harness);

    private bool SupportsHarnessComposerSubmit(
        MicroHarnessDefinition harness) =>
        harness.Id == "codex" ||
        (_harnessStateSnapshot?.HarnessId == harness.Id &&
            _harnessStateSnapshot.Capabilities.Supports(
                MicroHarnessActionIds.ComposerSubmit));

    private void ApplyHarnessContext()
    {
        var harness = ActiveHarness();
        var changed = harness.Id != _activeHarnessContextId;
        _activeHarnessContextId = harness.Id;
        ApplyHarnessTheme(harness);
        if (changed)
        {
            _actionTargetIsForeground = false;
            _harnessActionStatusVersion++;
            _settingsDisplayProgress = null;
            _harnessActionElapsedTimer.Stop();
            HideHarnessProgressStatus();
            _harnessStateCancellation?.Cancel();
            _harnessStateSnapshot = null;
            _selectedHarnessSessionId = null;
            _currentAgentSlotId = null;
            _encoderSteps.Clear();
            _joystickReportQueue.Clear();
            _cachedDialSelection = null;
            _pendingHarnessSetupId = harness.Id != "codex" &&
                !_harnessRegistry.IsSetupCompleted(harness.Id)
                    ? harness.Id
                    : null;
        }

        if (harness.Id == "codex")
        {
            PauseHarnessStateRefresh();
            ResolveCurrentAgentSlot();
            StartQuotaRefresh();
        }
        else
        {
            PauseQuotaRefresh();
            StartHarnessStateRefresh();
        }

        RefreshHarnessPresentation();
        var knobMode = _harnessRegistry.ResolveKnobMode(harness.Id);
        HarnessDialModeLabel.Text = knobMode switch
        {
            MicroHarnessKnobModes.ComposerNavigation => "INPUT",
            MicroHarnessKnobModes.ReasoningOnly => "MIND",
            _ => string.Empty,
        };
        HarnessDialModeLabel.Visibility = harness.Id != "codex" &&
            knobMode != MicroHarnessKnobModes.RecentSessions
                ? Visibility.Visible
                : Visibility.Collapsed;
        RefreshDialHelp(_layoutObserver.Current);
        RefreshAgentSlotPresentation();
        UpdateQuotaPresentation();
        if (changed && IsLoaded)
        {
            _ = Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(PromptForHarnessSetupIfNeeded));
        }
    }

    private void PromptForHarnessSetupIfNeeded()
    {
        var harness = ActiveHarness();
        if (_pendingHarnessSetupId is not { } pendingId ||
            harness.Id == "codex" ||
            !string.Equals(harness.Id, pendingId, StringComparison.OrdinalIgnoreCase) ||
            _harnessRegistry.IsSetupCompleted(harness.Id))
        {
            return;
        }

        _pendingHarnessSetupId = null;
        ShowHarnessActionStatus(
            _localization.IsEnglish ? "SET UP FIRST" : "首次配置",
            MicroHarnessDispatchStage.Background,
            autoHide: true);
        SetStatus(
            $"{harness.DisplayName} 尚未配置。点击 DeepSeek 键可选择“在专用 WSL 中安装 DSH”或“使用我已有的 DSH”。");
    }

    private void RefreshHarnessPresentation()
    {
        var harness = ActiveHarness();
        var english = _localization.IsEnglish;
        var connected = _harnessStateSnapshot?.HarnessId == harness.Id;
        ActionIcon12.KeycapId = harness.Id == "codex"
            ? _layoutObserver.Current.GetSlot("ACT12").KeycapId
            : GetHarnessIconId(harness);
        SetHelp(
            ActionKey12,
            harness.Id == "codex"
                ? "Codex"
                : harness.DisplayName,
            english
                ? $"ACT12 · current target: {harness.DisplayName}. Left-click activates it; right-click switches Agent / Harness.{(harness.Id == "codex" ? string.Empty : connected ? " Adapter connected." : " Adapter offline; click starts it.")}"
                : $"ACT12 · 当前目标：{harness.DisplayName}。左键激活；右键切换 Agent / Harness。{(harness.Id == "codex" ? string.Empty : connected ? "适配器已连接。" : "适配器离线，点击即可启动。")}" );
        AutomationProperties.SetItemStatus(
            ActionKey12,
            english
                ? $"Current target: {harness.DisplayName}"
                : $"当前目标：{harness.DisplayName}");
        RefreshHarnessStatusLeds(harness);
        RefreshHarnessControlPresentation(harness);
        RefreshActionKeyPresentation();
    }

    private void RefreshActionKeyPresentation()
    {
        var harness = ActiveHarness();
        var canSend = SupportsHarnessComposerSubmit(harness);
        var sends = canSend && _actionTargetIsForeground;
        ActionSendBadge.Visibility = sends
            ? Visibility.Visible
            : Visibility.Collapsed;

        var english = _localization.IsEnglish;
        var displayName = harness.Id == "codex" ? "Codex" : harness.DisplayName;
        SetHelp(
            ActionKey12,
            displayName,
            sends
                ? english
                    ? $"ACT12 · {displayName} is in the foreground. The paper-plane badge means this key sends the current input. Right-click switches Agent / Harness."
                    : $"ACT12 · {displayName} 正在前台。纸飞机角标表示此键会发送当前输入；右键可切换 Agent / Harness。"
                : canSend
                    ? english
                        ? $"ACT12 · Brings {displayName} to the foreground. Once it is in front, a paper-plane badge appears and this key sends the current input. Right-click switches Agent / Harness."
                        : $"ACT12 · 将 {displayName} 置于前台。置前后会出现纸飞机角标，此键随即用于发送当前输入；右键可切换 Agent / Harness。"
                    : english
                        ? $"ACT12 · Opens or focuses {displayName}. Its adapter has not advertised direct composer submit."
                        : $"ACT12 · 打开或置前 {displayName}。当前适配器尚未声明直接发送能力。");
        AutomationProperties.SetItemStatus(
            ActionKey12,
            sends
                ? english
                    ? $"{displayName} is in the foreground; send key"
                    : $"{displayName} 正在前台；发送键"
                : canSend
                    ? english
                        ? $"{displayName} is not in the foreground; activate key"
                        : $"{displayName} 不在前台；置前键"
                    : english
                        ? $"{displayName} direct send unavailable; activate key"
                        : $"{displayName} 直接发送不可用；置前键");
    }

    private void RefreshHarnessStatusLeds(MicroHarnessDefinition harness)
    {
        if (harness.Id == "codex")
        {
            return;
        }

        var snapshot = _harnessStateSnapshot?.HarnessId == harness.Id
            ? _harnessStateSnapshot
            : null;
        var pluginEndpointTitle = _localization.IsEnglish
            ? $"{harness.DisplayName} plugin endpoint"
            : $"{harness.DisplayName}插件端";
        if (snapshot is null)
        {
            SetLed(RuntimeLed, NeutralStatusLed, "Harness 适配器离线");
            SetLed(
                DriverLed,
                NeutralStatusLed,
                "网页桥接未连接",
                title: pluginEndpointTitle);
            RefreshVoiceServiceLed();
            return;
        }

        SetLed(
            RuntimeLed,
            HealthyStatusLed,
            $"{harness.DisplayName} 适配器已连接");
        var browserConnected = snapshot.Components?.Browser == "connected";
        SetLed(
            DriverLed,
            browserConnected ? HealthyStatusLed : WaitingStatusLed,
            browserConnected ? "Harness 网页桥接已连接" : "等待 Harness 网页桥接",
            title: pluginEndpointTitle);
        RefreshVoiceServiceLed();
    }

    private void SetVoiceServiceState(
        KeypadVoiceServiceState state,
        string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
                SetVoiceServiceState(state, message)));
            return;
        }

        _voiceServiceState = state;
        _voiceServiceMessage = string.IsNullOrWhiteSpace(message)
            ? _localization.IsEnglish
                ? "Keypad voice status is unavailable."
                : "小键盘语音状态不可用。"
            : message.Trim();
        if (!IsCodexHarnessActive())
        {
            RefreshVoiceServiceLed();
        }
    }

    private void RefreshVoiceServiceLed()
    {
        var appearance = _voiceServiceState switch
        {
            KeypadVoiceServiceState.Ready => HealthyStatusLed,
            KeypadVoiceServiceState.Listening => ActiveStatusLed,
            KeypadVoiceServiceState.Checking => WaitingStatusLed,
            KeypadVoiceServiceState.Error => ErrorStatusLed,
            _ => NeutralStatusLed,
        };
        SetLed(
            ActivityLed,
            appearance,
            _voiceServiceMessage,
            _localization.IsEnglish ? "Voice service" : "语音服务");
    }

    private void ApplyHarnessTheme(MicroHarnessDefinition harness)
    {
        if (harness.Id == "codex")
        {
            RestoreCodexGlassTheme();
            LeftSilkScreen.Text = "CODEX  /  MICRO  /  CRYSTAL HID";
            BrandCodexIcon.KeycapId = "CODEX";
            BrandWordmarkText.Text = "OPENAI  CODEX";
            HarnessThemeWash.Opacity = 0;
            return;
        }

        var deepSeek = IsDeepSeekHarness(harness);
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.52, 0.44),
            GradientOrigin = new Point(0.52, 0.44),
            RadiusX = 0.78,
            RadiusY = 0.72,
        };
        if (deepSeek)
        {
            LeftSilkScreen.Text =
                "DEEPSEEK  /  MICRO  /  DIRECT BRIDGE";
            BrandCodexIcon.KeycapId = "DEEPSEEK";
            BrandWordmarkText.Text = "DEEPSEEK  HARNESS";
            // DeepSeek uses the same restrained glass treatment as Codex,
            // shifted to a clean low-saturation blue. Replacing the green
            // base avoids cyan created by additive color mixing.
            DeviceFrame.Background = CreateVerticalGradient(
                (Color.FromArgb(0xE4, 0xFF, 0xFF, 0xFF), 0),
                (Color.FromArgb(0xD6, 0xF1, 0xF4, 0xFB), 0.48),
                (Color.FromArgb(0xC8, 0xE4, 0xE9, 0xF4), 0.78),
                (Color.FromArgb(0xB8, 0xC8, 0xCF, 0xDC), 1));
            PearlLightGuide.Background = CreateVerticalGradient(
                (Color.FromArgb(0xF4, 0xFF, 0xFF, 0xFF), 0),
                (Color.FromArgb(0xEE, 0xF2, 0xF5, 0xFC), 0.5),
                (Color.FromArgb(0xE9, 0xEA, 0xEF, 0xF9), 0.82),
                (Color.FromArgb(0xE3, 0xDF, 0xE6, 0xF2), 1));
            CrystalDepthPlate.Background = new SolidColorBrush(
                Color.FromArgb(0x14, 0x5E, 0x70, 0x94));
            CrystalPrismRim.BorderBrush = CreateDiagonalGradient(
                (Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0),
                (Color.FromArgb(0xB8, 0xDE, 0xE8, 0xFA), 0.16),
                (Color.FromArgb(0x76, 0xFF, 0xFF, 0xFF), 0.46),
                (Color.FromArgb(0x8B, 0xC9, 0xD7, 0xF1), 0.78),
                (Color.FromArgb(0xD6, 0xFF, 0xFF, 0xFF), 1));
            CrystalLowerRefraction.Background = CreateHorizontalGradient(
                (Color.FromArgb(0, 0x7C, 0xA2, 0xE2), 0),
                (Color.FromArgb(0xA5, 0x7C, 0xA2, 0xE2), 0.48),
                (Color.FromArgb(0, 0x7C, 0xA2, 0xE2), 1));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x38, 0x75, 0x9D, 0xE8),
                0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x20, 0x6C, 0x88, 0xD2),
                0.58));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x08, 0x4B, 0x5E, 0x9E),
                1));
            HarnessThemeWash.Opacity = 0.28;
        }
        else
        {
            RestoreCodexGlassTheme();
            LeftSilkScreen.Text = "HARNESS  /  MICRO  /  DIRECT BRIDGE";
            BrandCodexIcon.KeycapId = "HARNESS";
            BrandWordmarkText.Text = harness.DisplayName.ToUpperInvariant();
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x48, 0x9B, 0x86, 0xFF),
                0));
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb(0x16, 0x52, 0x46, 0xA8),
                1));
            HarnessThemeWash.Opacity = 0.55;
        }

        HarnessThemeWash.Background = brush;
    }

    private void RestoreCodexGlassTheme()
    {
        DeviceFrame.Background = _codexDeviceFrameBackground;
        PearlLightGuide.Background = _codexPearlLightGuideBackground;
        CrystalDepthPlate.Background = _codexCrystalDepthBackground;
        CrystalPrismRim.BorderBrush = _codexCrystalPrismBorder;
        CrystalLowerRefraction.Background =
            _codexLowerRefractionBackground;
    }

    private static LinearGradientBrush CreateVerticalGradient(
        params (Color Color, double Offset)[] stops) =>
        CreateLinearGradient(new Point(0, 0), new Point(0, 1), stops);

    private static LinearGradientBrush CreateHorizontalGradient(
        params (Color Color, double Offset)[] stops) =>
        CreateLinearGradient(new Point(0, 0), new Point(1, 0), stops);

    private static LinearGradientBrush CreateDiagonalGradient(
        params (Color Color, double Offset)[] stops) =>
        CreateLinearGradient(new Point(0, 0), new Point(1, 1), stops);

    private static LinearGradientBrush CreateLinearGradient(
        Point start,
        Point end,
        IReadOnlyList<(Color Color, double Offset)> stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = start,
            EndPoint = end,
        };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(color, offset));
        }

        return brush;
    }

    private void RefreshHarnessControlPresentation(
        MicroHarnessDefinition harness)
    {
        var snapshot = _layoutObserver.Current;
        foreach (var presentation in _actionKeys.Values)
        {
            presentation.Button.IsEnabled = true;
            ToolTipService.SetShowOnDisabled(presentation.Button, true);
        }

        foreach (var button in _joystickButtons.Values)
        {
            button.IsEnabled = true;
            ToolTipService.SetShowOnDisabled(button, true);
        }

        if (harness.Id == "codex")
        {
            foreach (var (slotId, presentation) in _actionKeys)
            {
                var binding = snapshot.GetSlot(slotId);
                presentation.Icon.KeycapId = binding.KeycapId;
                var physicalKeys = slotId == "ACT10_ACT11"
                    ? "ACT10 / ACT11"
                    : slotId;
                var gesture = slotId == "ACT10_ACT11"
                    ? "按住说话，松开结束。"
                    : "单击执行。";
                SetHelp(
                    presentation.Button,
                    CodexKeycapCatalog.Get(binding.KeycapId).Label,
                    $"{physicalKeys} · {binding.ResolvedAction}\n" +
                    $"{gesture}键帽图标随 Codex Micro 设置同步。");
            }

            var defaults = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["up"] = "composer.togglePlanMode",
                ["right"] = "navigateForward",
                ["down"] = "toggleSidebar",
                ["left"] = "navigateBack",
            };
            foreach (var (direction, button) in _joystickButtons)
            {
                var action = snapshot.AnalogActions.TryGetValue(
                    direction,
                    out var configured)
                        ? configured
                        : defaults[direction];
                SetHelp(
                    button,
                    $"摇杆方向 · {direction}",
                    $"{action} · 单击触发并自动回中。");
            }

            return;
        }

        var capabilities = _harnessStateSnapshot?.HarnessId == harness.Id
            ? _harnessStateSnapshot.Capabilities
            : null;
        var map = _harnessRegistry.ResolveKeyMap(harness.Id);
        var mappedKeys = new (Button Button, KeycapIcon Icon, string ControlId)[]
        {
            (ActionKey06, ActionIcon06, MicroHarnessControlIds.Action06),
            (ActionKey07, ActionIcon07, MicroHarnessControlIds.Action07),
            (ActionKey08, ActionIcon08, MicroHarnessControlIds.Action08),
            (ActionKey09, ActionIcon09, MicroHarnessControlIds.Action09),
            (ActionKey10, ActionIcon10, MicroHarnessControlIds.VoiceWide),
            (ActionKey10Split, ActionIcon10Split, MicroHarnessControlIds.VoiceLeft),
            (ActionKey11Split, ActionIcon11Split, MicroHarnessControlIds.VoiceRight),
        };
        foreach (var (button, icon, controlId) in mappedKeys)
        {
            var actionId = map.Resolve(controlId);
            var actionLabel = HarnessActionLabel(actionId);
            icon.KeycapId = GetHarnessActionIconId(harness, actionId);
            button.IsEnabled = IsHarnessMappedActionAvailable(
                harness,
                actionId,
                capabilities);
            SetHelp(
                button,
                actionLabel,
                $"{controlId} · {harness.DisplayName} 独立键位：{actionLabel}。" +
                (button.IsEnabled
                    ? "通过直连适配器执行；不会发送 Codex HID。"
                    : "当前未分配、缺少会话，或适配器未声明此能力。"));
        }

        ActionKey12.IsEnabled = true;
        var mappedDirections = new (Button Button, string ControlId, string Arrow)[]
        {
            (JoystickUp, MicroHarnessControlIds.JoystickUp, "↑"),
            (JoystickDown, MicroHarnessControlIds.JoystickDown, "↓"),
            (JoystickLeft, MicroHarnessControlIds.JoystickLeft, "←"),
            (JoystickRight, MicroHarnessControlIds.JoystickRight, "→"),
        };
        foreach (var (button, controlId, arrow) in mappedDirections)
        {
            var actionId = map.Resolve(controlId);
            var actionLabel = HarnessActionLabel(actionId);
            button.IsEnabled = IsHarnessMappedActionAvailable(
                harness,
                actionId,
                capabilities);
            SetHelp(
                button,
                $"{arrow} {actionLabel}",
                $"{controlId} · {harness.DisplayName} 独立键位：{actionLabel}。" +
                (button.IsEnabled
                    ? "通过直连适配器执行。"
                    : "当前未分配、缺少会话，或适配器未声明此能力。"));
        }

        SetHelp(
            JoystickSurface,
            $"{harness.DisplayName} 快捷操作",
            string.Join(" · ", mappedDirections.Select(item =>
                $"{item.Arrow} {HarnessActionLabel(map.Resolve(item.ControlId))}")) +
            "。在软件设置中按 Harness 独立配置；不会发送 Codex HID。");
    }

    private bool IsHarnessMappedActionAvailable(
        MicroHarnessDefinition harness,
        string actionId,
        MicroHarnessCapabilities? capabilities)
    {
        if (actionId == MicroHarnessActionIds.None)
        {
            return false;
        }

        if (MicroHarnessActionIds.IsNative(actionId))
        {
            if (capabilities is not null && !capabilities.Supports(actionId))
            {
                return false;
            }

            return !HarnessActionRequiresSession(actionId) ||
                !string.IsNullOrWhiteSpace(CurrentHarnessSessionId());
        }

        if (MicroHarnessActionIds.IsVoice(actionId))
        {
            return capabilities?.VoiceInput != false;
        }

        return actionId switch
        {
            MicroHarnessActionIds.PreviousSession or
            MicroHarnessActionIds.NextSession =>
                _harnessStateSnapshot?.HarnessId == harness.Id &&
                _harnessStateSnapshot.Sessions.Count > 0,
            MicroHarnessActionIds.OpenSelectedSession =>
                CurrentHarnessSessionSlot() is not null,
            MicroHarnessActionIds.ActivateSurface => true,
            _ => false,
        };
    }

    private static string GetHarnessActionIconId(
        MicroHarnessDefinition harness,
        string actionId) => actionId switch
    {
        MicroHarnessActionIds.NewSession => "NEW",
        MicroHarnessActionIds.ForkSession => "SPLIT",
        MicroHarnessActionIds.ArchiveSession => "DEL",
        MicroHarnessActionIds.CancelTurn => "REJ",
        MicroHarnessActionIds.ToggleConversationView => "DIFF",
        MicroHarnessActionIds.ApproveInteraction => "APPR",
        MicroHarnessActionIds.RejectInteraction => "REJ",
        MicroHarnessActionIds.LoadOlderHistory => "TIME",
        MicroHarnessActionIds.ToggleSidebar => "NAV",
        MicroHarnessActionIds.OpenDetails or
            MicroHarnessActionIds.CloseDetails => "DIFF",
        MicroHarnessActionIds.PreviousSession or
            MicroHarnessActionIds.NextSession => "NAV",
        MicroHarnessActionIds.OpenSelectedSession or
            MicroHarnessActionIds.ActivateSurface => GetHarnessIconId(harness),
        MicroHarnessActionIds.VoiceDictation => "MIC",
        MicroHarnessActionIds.OpenGoal => "GOAL",
        _ => "EMPT1",
    };

    private void ApplyPackageAssets(string? packageRoot)
    {
        foreach (var icon in _brandAwareIcons)
        {
            icon.PackageRoot = packageRoot;
        }
    }

    internal void ApplyQuotaSnapshot(
        CodexQuotaSnapshot? snapshot,
        bool refreshFailed = false)
    {
        _quotaSnapshot = snapshot;
        _quotaRefreshFailed = refreshFailed;
        UpdateQuotaPresentation();
    }

    /// <summary>
    /// Applies one adapter snapshot without starting a real Harness. Off-screen
    /// visual QA uses the exact production mapping for running sessions and
    /// the Harness component status LEDs.
    /// </summary>
    internal void ApplyHarnessStateForVisualTest(
        MicroHarnessStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _harnessStateSnapshot = snapshot;
        _selectedHarnessSessionId = snapshot.CurrentSessionId;
        _currentAgentSlotId = snapshot.CurrentSessionId is null
            ? null
            : snapshot.Sessions
                .Select((session, index) => (session, index))
                .Where(item => item.session.Id == snapshot.CurrentSessionId)
                .Select(item => (int?)item.index)
                .FirstOrDefault();
        RefreshAgentSlotPresentation();
        RefreshHarnessPresentation();
        UpdateQuotaPresentation();
    }

    internal void ApplyVoiceServiceStateForVisualTest(
        bool ready,
        bool checking = false,
        bool error = false,
        bool listening = false)
    {
        var state = error
            ? KeypadVoiceServiceState.Error
            : checking
                ? KeypadVoiceServiceState.Checking
                : listening
                    ? KeypadVoiceServiceState.Listening
                    : ready
                        ? KeypadVoiceServiceState.Ready
                        : KeypadVoiceServiceState.Unavailable;
        SetVoiceServiceState(
            state,
            state switch
            {
                KeypadVoiceServiceState.Ready => "小键盘语音服务已就绪。",
                KeypadVoiceServiceState.Listening => "小键盘正在聆听。",
                KeypadVoiceServiceState.Checking => "正在检查小键盘语音服务。",
                KeypadVoiceServiceState.Error => "小键盘语音服务错误。",
                _ => "小键盘语音服务尚未启动。",
            });
    }

    internal void ApplyQuickModel(CodexQuickModel model)
    {
        _quickModel = model;
        UpdateQuotaPresentation();
    }

    private void UpdateQuotaPresentation()
    {
        var english = _localization.IsEnglish;
        var harness = ActiveHarness();
        var deepSeek = IsDeepSeekHarness(harness);
        ApplySettingsDisplayTheme(light: deepSeek);
        if (deepSeek && _settingsDisplayProgress is { } progress)
        {
            ApplySettingsDisplayProgress(progress);
            return;
        }

        if (harness.Id != "codex")
        {
            if (deepSeek)
            {
                QuotaCaptionText.Visibility = Visibility.Collapsed;
                var currentModel = _harnessStateSnapshot?.HarnessId == harness.Id
                    ? _harnessStateSnapshot.Components?.CurrentModel
                    : null;
                var modelLabel = FormatDeepSeekModel(currentModel);
                QuotaValueText.Text = _harnessModelSwitching
                    ? "···"
                    : modelLabel;
                QuotaValueText.FontSize = modelLabel.Length switch
                {
                    <= 5 => 12.5,
                    <= 7 => 10.5,
                    _ => 9.5,
                };
                QuotaGauge.Opacity = 1;
                QuotaProgressRing.Data = Geometry.Empty;
                QuotaProgressRing.Stroke = new SolidColorBrush(
                    Color.FromRgb(0x22, 0x22, 0x22));
                var hasSession = !string.IsNullOrWhiteSpace(
                    CurrentHarnessSessionId());
                var modelDescription = string.IsNullOrWhiteSpace(currentModel)
                    ? (english
                        ? "Waiting for the browser bridge to report the current model."
                        : "正在等待网页桥接回报当前模型。")
                    : english
                        ? $"Current model: {currentModel}."
                        : $"当前模型：{currentModel}。";
                var controls = hasSession
                    ? english
                        ? "Left-click switches between the two quick models configured in DeepSeek Harness. Right-click opens this Agent's adapter and key-map settings."
                        : "左键在 DeepSeek Harness 中配置的两个快捷模型间切换；右键直达此 Agent 的适配器与独立键位设置。"
                    : english
                        ? "Left-click opens and connects DeepSeek Harness first. Right-click opens this Agent's adapter and key-map settings."
                        : "当前尚无可切换会话；左键先打开并连接 DeepSeek Harness，右键直达此 Agent 的适配器与独立键位设置。";
                if (_harnessModelSwitching)
                {
                    modelDescription = english
                        ? "Switching the current DeepSeek session's quick model…"
                        : "正在切换当前 DeepSeek 会话的快捷模型…";
                }
                AutomationProperties.SetItemStatus(
                    SettingsKey,
                    _harnessModelSwitching
                        ? english ? "Switching DeepSeek model" : "正在切换 DeepSeek 模型"
                        : english
                            ? $"DeepSeek current model {currentModel ?? "unknown"}"
                            : $"DeepSeek 当前模型 {currentModel ?? "未知"}");
                ApplyHelp(
                    SettingsKey,
                    string.IsNullOrWhiteSpace(currentModel)
                        ? harness.DisplayName
                        : $"{harness.DisplayName} · {currentModel}",
                    $"{modelDescription}\n\n{controls}");
                return;
            }

            QuotaCaptionText.Visibility = Visibility.Visible;
            QuotaCaptionText.Text = harness.Id == "deepseek-harness"
                ? "DSH"
                : string.Concat(harness.DisplayName
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(word => char.ToUpperInvariant(word[0])))
                    .PadRight(2, '·')[..2];
            QuotaValueText.Text = "—";
            QuotaValueText.FontSize = 15;
            QuotaGauge.Opacity = 0.76;
            QuotaProgressRing.Data = Geometry.Empty;
            QuotaProgressRing.Stroke = new SolidColorBrush(
                Color.FromRgb(0x86, 0x9B, 0xFF));
            var sessionCount = _harnessStateSnapshot?.HarnessId == harness.Id
                ? _harnessStateSnapshot.Sessions.Count
                : 0;
            AutomationProperties.SetItemStatus(
                SettingsKey,
                english
                    ? $"{harness.DisplayName} · {sessionCount} sessions"
                    : $"{harness.DisplayName} · {sessionCount} 个会话");
            ApplyHelp(
                SettingsKey,
                harness.DisplayName,
                english
                    ? "This Harness does not expose a quota reading. Left-click opens and focuses it; right-click opens this Agent's adapter and key-map settings."
                    : "此 Harness 尚未提供额度读取能力。左键打开并置前；右键直达此 Agent 的适配器与独立键位设置。");
            return;
        }

        QuotaCaptionText.Visibility = Visibility.Visible;
        var quickModels = _profileSettings.Current;
        var quickModelPair = FormatQuickModelPair(quickModels);
        QuotaCaptionText.Text = _quickModelSwitching
            ? "···"
            : _quickModel switch
            {
                CodexQuickModel.Sol => "SOL",
                CodexQuickModel.Terra => "TERRA",
                CodexQuickModel.Luna => "LUNA",
                _ => FormatQuickModelPairCaption(quickModels),
            };
        var modelName = FormatQuickModelName(_quickModel);
        var modelStatus = _quickModel == CodexQuickModel.Unknown
            ? english
                ? $"{quickModelPair} quick switch ready"
                : $"{quickModelPair} 快速切换已就绪"
            : english
                ? $"current model {modelName}"
                : $"当前模型 {modelName}";
        if (_quickModelSwitching)
        {
            modelStatus = english
                ? $"switching between {quickModelPair}"
                : $"正在切换 {quickModelPair}";
        }

        if (_quotaSnapshot is null)
        {
            QuotaValueText.Text = "—";
            QuotaValueText.FontSize = 15;
            QuotaGauge.Opacity = 0.76;
            QuotaProgressRing.Data = Geometry.Empty;
            QuotaProgressRing.Stroke = new SolidColorBrush(
                Color.FromRgb(0xA7, 0xAF, 0xB8));
            AutomationProperties.SetItemStatus(
                SettingsKey,
                english
                    ? $"Quota unavailable · {modelStatus}"
                    : $"额度暂不可用 · {modelStatus}");

            var title = english ? "Codex quota" : "Codex 剩余额度";
            var state = _quotaRefreshFailed
                ? english
                    ? "Quota is temporarily unavailable. It remains unknown rather than being shown as 0%, and will retry while the panel is visible."
                    : "暂时无法读取额度。当前保持未知状态，不会误显示为 0%；面板显示时会自动重试。"
                : english
                    ? "Reading the remaining Codex quota."
                    : "正在读取 Codex 剩余额度。";
            var controls = english
                ? $"Click: switch {quickModelPair} for this task's next turn · Hold: open official Micro settings · Right-click: open the current Agent's software settings."
                : $"短按：为当前任务的下一轮切换 {quickModelPair} · 长按：打开官方 Micro 设置 · 右键：直达右下角当前 Agent 的软件设置。";
            ApplyHelp(SettingsKey, title, $"{state}\n\n{controls}");
            return;
        }

        var displayWindow = _quotaSnapshot.DisplayWindow;
        var remaining = displayWindow.RemainingPercent;
        var roundedRemaining = (int)Math.Round(
            remaining,
            MidpointRounding.AwayFromZero);
        var accent = GetQuotaAccent(remaining);

        QuotaValueText.Text = $"{roundedRemaining}%";
        QuotaValueText.FontSize = roundedRemaining == 100 ? 13.5 : 15;
        QuotaGauge.Opacity = 1;
        QuotaProgressRing.Data = CreateQuotaArcGeometry(remaining);
        QuotaProgressRing.Stroke = new SolidColorBrush(accent);
        AutomationProperties.SetItemStatus(
            SettingsKey,
            english
                ? $"{roundedRemaining}% quota remaining · {modelStatus}"
                : $"剩余额度 {roundedRemaining}% · {modelStatus}");

        var titleText = english
            ? $"Codex quota · {roundedRemaining}% left · {modelName}"
            : $"Codex 剩余额度 · {roundedRemaining}% · {modelName}";
        ApplyHelp(
            SettingsKey,
            titleText,
            BuildQuotaHelpDetail(_quotaSnapshot, english));
    }

    private static bool IsDeepSeekHarness(MicroHarnessDefinition harness) =>
        harness.Id.Contains("deepseek", StringComparison.OrdinalIgnoreCase) ||
        harness.DisplayName.Contains("deepseek", StringComparison.OrdinalIgnoreCase);

    private void ApplySettingsDisplayTheme(bool light)
    {
        SettingsKey.Background = new SolidColorBrush(light
            ? Color.FromRgb(0xFA, 0xFA, 0xF8)
            : Color.FromRgb(0x2D, 0x29, 0x25));
        QuotaTrackRing.Stroke = new SolidColorBrush(light
            ? Color.FromRgb(0xC9, 0xCC, 0xCA)
            : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        QuotaValueText.Foreground = new SolidColorBrush(light
            ? Color.FromRgb(0x1D, 0x1D, 0x1B)
            : Color.FromRgb(0xF7, 0xFA, 0xFF));
        QuotaCaptionText.Foreground = new SolidColorBrush(light
            ? Color.FromRgb(0x4F, 0x53, 0x50)
            : Color.FromArgb(0xBF, 0xDD, 0xE7, 0xF2));
    }

    private static string FormatDeepSeekModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return "—";
        }

        if (model.Contains("flash", StringComparison.OrdinalIgnoreCase))
        {
            return "FLASH";
        }

        if (model.Contains("pro", StringComparison.OrdinalIgnoreCase))
        {
            return "PRO";
        }

        var candidates = model.Trim().Split(
            ['-', '_', '/', '\\', ':', ' ', '·'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var compact = candidates
            .Reverse()
            .FirstOrDefault(candidate =>
                !candidate.Equals("deepseek", StringComparison.OrdinalIgnoreCase) &&
                !candidate.Equals("model", StringComparison.OrdinalIgnoreCase)) ??
            model.Trim();
        compact = compact.ToUpperInvariant();
        return compact.Length <= 8 ? compact : $"{compact[..7]}…";
    }

    private string BuildQuotaHelpDetail(
        CodexQuotaSnapshot snapshot,
        bool english)
    {
        var displayWindow = snapshot.DisplayWindow;
        var culture = CultureInfo.GetCultureInfo(english ? "en-US" : "zh-CN");
        var lines = snapshot.Windows
            .OrderBy(window => window.WindowDurationMinutes)
            .Select(window =>
            {
                var marker = ReferenceEquals(window, displayWindow) ? "●" : "○";
                var label = FormatQuotaWindowLabel(
                    window.WindowDurationMinutes,
                    english);
                var remaining = (int)Math.Round(
                    window.RemainingPercent,
                    MidpointRounding.AwayFromZero);
                var reset = window.ResetsAt.ToLocalTime().ToString(
                    english ? "MMM d, h:mm tt" : "MM/dd HH:mm",
                    culture);
                return english
                    ? $"{marker} {label}: {remaining}% left · resets {reset}"
                    : $"{marker} {label}：剩余 {remaining}% · {reset} 重置";
            })
            .ToList();

        var updated = snapshot.ReadAt.ToLocalTime().ToString(
            english ? "h:mm tt" : "HH:mm",
            culture);
        lines.Add(english ? $"Updated {updated}" : $"更新于 {updated}");
        if (_quotaRefreshFailed)
        {
            lines.Add(english
                ? "The latest refresh failed; showing the last successful reading."
                : "最近一次刷新失败，当前显示上次成功读取的结果。");
        }

        lines.Add(string.Empty);
        var quickModelPair = FormatQuickModelPair(_profileSettings.Current);
        lines.Add(english
            ? $"The ring shows the tighter window. Click switches {quickModelPair} for this task's next turn; hold opens official Micro settings; right-click opens the current Agent's software settings."
            : $"圆环显示当前更紧张的额度窗口。短按为当前任务的下一轮切换 {quickModelPair}；长按打开官方 Micro 设置；右键直达右下角当前 Agent 的软件设置。");
        return string.Join('\n', lines);
    }

    private static string FormatQuickModelPair(
        MicroProfileSnapshot snapshot) =>
        $"{FormatQuickModelName(snapshot.QuickModelA)} / " +
        FormatQuickModelName(snapshot.QuickModelB);

    private static string FormatQuickModelPairCaption(
        MicroProfileSnapshot snapshot) =>
        $"{QuickModelAbbreviation(snapshot.QuickModelA)}↔" +
        QuickModelAbbreviation(snapshot.QuickModelB);

    private static string QuickModelAbbreviation(CodexQuickModel model) =>
        model switch
        {
            CodexQuickModel.Sol => "S",
            CodexQuickModel.Terra => "T",
            CodexQuickModel.Luna => "L",
            _ => "?",
        };

    private static string FormatQuotaWindowLabel(
        int durationMinutes,
        bool english)
    {
        const int minutesPerWeek = 7 * 24 * 60;
        const int minutesPerDay = 24 * 60;

        if (durationMinutes % minutesPerWeek == 0)
        {
            var weeks = durationMinutes / minutesPerWeek;
            if (english)
            {
                return weeks == 1 ? "Weekly limit" : $"{weeks}-week limit";
            }

            return weeks == 1 ? "周额度" : $"{weeks} 周额度";
        }

        if (durationMinutes % minutesPerDay == 0)
        {
            var days = durationMinutes / minutesPerDay;
            return english ? $"{days}-day limit" : $"{days} 天额度";
        }

        if (durationMinutes % 60 == 0)
        {
            var hours = durationMinutes / 60;
            return english ? $"{hours}-hour limit" : $"{hours} 小时额度";
        }

        return english
            ? $"{durationMinutes}-minute limit"
            : $"{durationMinutes} 分钟额度";
    }

    private static Color GetQuotaAccent(double remainingPercent) =>
        remainingPercent <= 10
            ? Color.FromRgb(0xFF, 0x9E, 0x8B)
            : remainingPercent <= 30
                ? Color.FromRgb(0xFF, 0xD2, 0x7A)
                : Color.FromRgb(0xA8, 0xC7, 0xFF);

    internal static Geometry CreateQuotaArcGeometry(double remainingPercent)
    {
        var clamped = Math.Clamp(remainingPercent, 0, 100);
        if (clamped <= 0)
        {
            return Geometry.Empty;
        }

        const double center = 26;
        const double radius = 23.5;
        if (clamped >= 100)
        {
            var circle = new EllipseGeometry(
                new Point(center, center),
                radius,
                radius);
            circle.Freeze();
            return circle;
        }

        var sweepAngle = 360 * clamped / 100;
        var start = PointOnQuotaCircle(-90, center, radius);
        var end = PointOnQuotaCircle(-90 + sweepAngle, center, radius);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(
                end,
                new Size(radius, radius),
                rotationAngle: 0,
                isLargeArc: sweepAngle > 180,
                SweepDirection.Clockwise,
                isStroked: true,
                isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnQuotaCircle(
        double angleDegrees,
        double center,
        double radius)
    {
        var angleRadians = angleDegrees * Math.PI / 180;
        return new Point(
            center + (radius * Math.Cos(angleRadians)),
            center + (radius * Math.Sin(angleRadians)));
    }

    private void SetStatus(string value)
    {
        _status = value;
        AutomationProperties.SetHelpText(this, Localize(value));
        SetHelp(
            DeviceFrame,
            "Agent Controller · Micro Surface",
            $"{value}\n\n拖动机身移动 · 右击机身打开窗口菜单 · 关闭时收起到托盘");
    }

    private void Localization_LanguageChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                new Action(RefreshLocalizedChrome));
            return;
        }

        RefreshLocalizedChrome();
    }

    private void ProfileSettings_Changed(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                new Action(ApplyProfileSettingsChange));
            return;
        }

        ApplyProfileSettingsChange();
    }

    private void ApplyProfileSettingsChange()
    {
        _dialDirectionSettings.InvertDirection =
            _profileSettings.Current.InvertDialDirection;
        ApplyHarnessContext();
        RestartVoiceBridgeMonitor();
        RestartKeypadVoiceWarmUp();
    }

    private void RestartKeypadVoiceWarmUp()
    {
        _voiceWarmUpCancellation?.Cancel();
        _voiceHealthCancellation?.Cancel();
        if (_windowClosed)
        {
            return;
        }

        var settings = _profileSettings.Current.VoiceSettings;
        if (!settings.SetupCompleted)
        {
            SetVoiceServiceState(
                KeypadVoiceServiceState.Unconfigured,
                _localization.IsEnglish
                    ? "Configure voice in the keypad first."
                    : "请先在小键盘中完成语音配置。");
            return;
        }
        if (settings.Provider != MicroVoiceProviders.LocalQwen)
        {
            SetVoiceServiceState(
                KeypadVoiceServiceState.Ready,
                settings.Provider == MicroVoiceProviders.System
                    ? _localization.IsEnglish
                        ? "Windows speech recognition is configured."
                        : "Windows 系统语音识别已配置。"
                    : _localization.IsEnglish
                        ? "The configured remote voice service is available to the keypad."
                        : "远程语音服务已在小键盘中配置。");
            return;
        }

        SetVoiceServiceState(
            KeypadVoiceServiceState.Checking,
            settings.LocalStartMode == MicroLocalVoiceStartModes.KeypadStart
                ? _localization.IsEnglish
                    ? "Starting the keypad-owned Qwen voice service."
                    : "正在启动小键盘持有的 Qwen 语音服务。"
                : _localization.IsEnglish
                    ? "Checking the keypad-owned Qwen voice service."
                    : "正在检查小键盘持有的 Qwen 语音服务。");
        var cancellation = new CancellationTokenSource();
        _voiceWarmUpCancellation = cancellation;
        _ = WarmUpKeypadVoiceAsync(cancellation);
    }

    private async Task WarmUpKeypadVoiceAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            var settings = _profileSettings.Current.VoiceSettings;
            if (_windowClosed ||
                settings.Provider != MicroVoiceProviders.LocalQwen ||
                !settings.SetupCompleted)
            {
                return;
            }

            if (settings.LocalStartMode == MicroLocalVoiceStartModes.KeypadStart)
            {
                await _voiceInput.WarmUpAsync(cancellation.Token);
            }
            var ready = await _voiceInput.ProbeLocalReadyAsync(
                cancellation.Token);
            if (!_windowClosed)
            {
                SetVoiceServiceState(
                    ready
                        ? KeypadVoiceServiceState.Ready
                        : KeypadVoiceServiceState.Unavailable,
                    ready
                        ? _localization.IsEnglish
                            ? "The keypad-owned Qwen voice service is ready."
                            : "小键盘持有的 Qwen 语音服务已就绪。"
                        : _localization.IsEnglish
                            ? "The keypad-owned Qwen voice service is not running."
                            : "小键盘持有的 Qwen 语音服务尚未启动。");
            }
        }
        catch (OperationCanceledException)
        {
            // App shutdown cancels an in-flight keypad-owned warm-up.
        }
        catch (Exception) when (_windowClosed)
        {
            // The window and voice runtime have already been disposed.
        }
        catch (Exception exception)
        {
            SetVoiceServiceState(
                KeypadVoiceServiceState.Error,
                exception.Message);
            SetStatus(exception.Message);
        }
        finally
        {
            if (ReferenceEquals(_voiceWarmUpCancellation, cancellation))
            {
                _voiceWarmUpCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void HarnessRegistry_Changed(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(
                new Action(ApplyHarnessRegistryChange));
            return;
        }

        ApplyHarnessRegistryChange();
    }

    private void ApplyHarnessRegistryChange()
    {
        ApplyHarnessContext();
        RestartVoiceBridgeMonitor();
    }

    private void RefreshLocalizedChrome()
    {
        Title = _localization.IsEnglish
            ? $"Codex Micro · {_keypadDisplayName}"
            : $"Codex Micro · {_keypadDisplayName}";
        TopmostMenuItem.Header = Localize("窗口置顶");
        SettingsMenuItem.Header = _localization.IsEnglish ? "Settings" : "设置";
        KnobSettingsMenuItem.Header = SettingsMenuItem.Header;
        OpenSoftwareSettingsMenuItem.Header = _localization.IsEnglish
            ? "Software settings"
            : "软件设置";
        KnobOpenSoftwareSettingsMenuItem.Header =
            OpenSoftwareSettingsMenuItem.Header;
        OpenOfficialSettingsMenuItem.Header = _localization.IsEnglish
            ? "Official Codex Micro settings"
            : "Codex Micro 官方设置";
        KnobOpenOfficialSettingsMenuItem.Header =
            OpenOfficialSettingsMenuItem.Header;
        ReconnectMenuItem.Header = Localize("重新连接虚拟 HID");
        KnobReconnectMenuItem.Header = ReconnectMenuItem.Header;
        HidePanelMenuItem.Header = Localize("隐藏面板");
        CloseKeypadMenuItem.Header = _localization.IsEnglish
            ? "Close this keypad"
            : "关闭此小键盘";
        AutomationProperties.SetHelpText(this, Localize(_status));

        foreach (var (element, content) in _helpContent.ToArray())
        {
            ApplyHelp(element, content.Title, content.Detail);
        }

        if (_dialSelectionText is not null)
        {
            var localized = Localize(_dialSelectionText);
            DialSelectionText.Text = localized;
            AutomationProperties.SetItemStatus(DialButton, localized);
        }

        UpdateQuotaPresentation();
        RefreshHarnessPresentation();
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

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_voiceCloseReleasePending)
        {
            e.Cancel = true;
            return;
        }

        if (_voicePressed)
        {
            e.Cancel = true;
            var closeApplication = _allowApplicationClose;
            _voiceCloseReleasePending = true;
            try
            {
                await ReleaseVoiceAsync();
            }
            finally
            {
                _voiceCloseReleasePending = false;
            }

            if (closeApplication || _allowApplicationClose)
            {
                Close();
            }
            else
            {
                Hide();
            }
            return;
        }

        if (_allowApplicationClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void SetLed(
        Ellipse led,
        StatusLedAppearance appearance,
        string tooltip,
        string? title = null) =>
        SetLed(led, appearance.Color, tooltip, appearance.Glow, title);

    private void SetLed(
        Ellipse led,
        string color,
        string tooltip,
        bool glow = false,
        string? title = null) =>
        SetLed(
            led,
            (Color)ColorConverter.ConvertFromString(color),
            tooltip,
            glow,
            title);

    private void SetLed(
        Ellipse led,
        Color color,
        string tooltip,
        bool glow,
        string? title)
    {
        if (led == ActivityLed &&
            !IsCodexHarnessActive() &&
            string.IsNullOrWhiteSpace(title))
        {
            RefreshVoiceServiceLed();
            return;
        }

        led.Fill = new SolidColorBrush(color);
        led.Effect = glow
            ? new DropShadowEffect
            {
                Color = color,
                BlurRadius = StatusLedGlowBlurRadius,
                ShadowDepth = 0,
                Opacity = StatusLedGlowOpacity,
            }
            : null;
        var helpTitle = !string.IsNullOrWhiteSpace(title)
            ? title
            : led == RuntimeLed
                ? "Codex 运行时握手"
                : led == DriverLed
                    ? "虚拟 HID"
                    : "最近事件";
        SetHelp(led, helpTitle, tooltip);
    }

    private void SetHelp(
        FrameworkElement element,
        string title,
        string detail)
    {
        _helpContent[element] = (title, detail);
        ApplyHelp(element, title, detail);
    }

    private void ApplyHelp(
        FrameworkElement element,
        string title,
        string detail)
    {
        var localizedTitle = Localize(title);
        var localizedDetail = Localize(detail);
        var content = new StackPanel
        {
            MaxWidth = 360,
        };
        content.Children.Add(new TextBlock
        {
            Text = localizedTitle,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2F, 0x34, 0x38)),
        });
        content.Children.Add(new TextBlock
        {
            Text = localizedDetail,
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
        AutomationProperties.SetName(element, localizedTitle);
        AutomationProperties.SetHelpText(element, localizedDetail);
    }

    private string Localize(string value) => _localization.Text(value);

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
