using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CodexController.Core.Bridge;
using CodexController.Models;
using CodexController.Services;
using Forms = System.Windows.Forms;

namespace CodexController;

public partial class MainWindow : Window
{
    private static readonly SidebarScope[] RootSidebarScopes =
    [
        SidebarScope.PinnedTasks,
        SidebarScope.PinnedProjects,
        SidebarScope.Projects,
        SidebarScope.ProjectlessTasks,
    ];

    private readonly SettingsService _settingsService;
    private readonly CodexDataService _codexDataService;
    private readonly CodexCommandService _codexCommandService;
    private readonly CodexKeybindingService _codexKeybindingService;
    private readonly CodexComposerService _codexComposerService;
    private readonly CodexSidebarService _codexSidebarService;
    private readonly XInputService _xInputService;
    private readonly AxisRepeater _axisRepeater;
    private readonly StickGestureRouter _leftStickRouter;
    private readonly StickGestureRouter _rightStickRouter;
    private readonly SemaphoreSlim _sidebarFocusGate = new(1, 1);
    private readonly SemaphoreSlim _dataRefreshGate = new(1, 1);
    private readonly ObservableCollection<SidebarEntry> _sidebarEntries = [];
    private readonly ObservableCollection<EventRow> _events = [];
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _dataTimer;
    private readonly object _controllerStateSync = new();
    private readonly ControllerSession _controllerSession = new();

    private AppSettings _settings = new();
    private CodexSnapshot _snapshot = new();
    private SidebarScope _scope = SidebarScope.Projects;
    private RightControlMode _rightMode = RightControlMode.Reasoning;
    private ControllerButtons _previousButtons;
    private string? _selectedProjectPath;
    private int _selectedIndex;
    private bool _projectTasksPinnedOnly;
    private readonly Dictionary<SidebarScope, string> _rootCursorIds = [];
    private readonly Dictionary<string, string> _projectTaskCursorIds =
        new(StringComparer.OrdinalIgnoreCase);
    private SidebarReturnFrame? _sidebarReturnFrame;
    private ProjectDisclosureLease? _projectDisclosureLease;
    private bool _dictationInjected;
    private bool _controllerWasConnected;
    private bool _hasSeenController;
    private bool _wakeInProgress;
    private bool _initializing = true;
    private bool _suppressSelectionActivation;
    private bool _exitRequested;
    private long _leftNavigationBlockedUntil;
    private long _rightAdjustmentBlockedUntil;
    private CancellationTokenSource? _sidebarFocusCancellation;
    private ControllerState _latestControllerState;
    private int _controllerDispatchPending;
    private ComposerCatalog? _composerCatalog;
    private int _modelIndex;
    private int _committedModelIndex;
    private int _reasoningIndex;
    private int _committedReasoningIndex;
    private int _speedIndex;
    private int _committedSpeedIndex;
    private ComposerSettingKind? _pendingComposerKind;
    private string? _pendingComposerTarget;
    private CancellationTokenSource? _composerCommitCancellation;
    private CancellationTokenSource? _dictationStopCancellation;
    private CancellationTokenSource? _navigationConfirmCancellation;
    private NavigationUndoState? _navigationUndo;
    private Forms.NotifyIcon? _trayIcon;
    private System.Drawing.Icon? _trayIconImage;
    private OverlayWindow? _overlayWindow;

    public MainWindow(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _settingsService = services.Settings;
        _codexDataService = services.CodexData;
        _codexCommandService = services.CodexCommand;
        _codexKeybindingService = services.CodexKeybindings;
        _codexComposerService = services.CodexComposer;
        _codexSidebarService = services.CodexSidebar;
        _xInputService = services.Controller;
        _axisRepeater = services.AxisRepeater;
        _leftStickRouter = services.LeftStickRouter;
        _rightStickRouter = services.RightStickRouter;

        InitializeComponent();
        SidebarList.ItemsSource = _sidebarEntries;
        EventList.ItemsSource = _events;

        _statusTimer = new DispatcherTimer
        {
            Interval = BridgeTimings.StatusPoll,
        };
        _statusTimer.Tick += (_, _) => UpdateCodexStatus();

        _dataTimer = new DispatcherTimer
        {
            Interval = BridgeTimings.DataRefresh,
        };
        _dataTimer.Tick += (_, _) => RefreshCodexData(preserveSelection: true);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();
        ApplySettingsToControls();
        SetupTrayIcon();
        _overlayWindow = new OverlayWindow();

        AddEvent("正式桥接已启动");
        ConfigureCodexKeybindings();
        InitializeComposerControls();
        RefreshCodexData(preserveSelection: false);
        ShowPage(DevicePage);
        SetSelectedNav(DeviceNavButton);

        _xInputService.StateChanged += XInputService_StateChanged;
        _xInputService.Start();
        _statusTimer.Start();
        _dataTimer.Start();
        _initializing = false;

        FooterStatusText.Text =
            "Menu 首次唤醒并解锁 · 左摇杆 ↑↓ 焦点、→ 进入 / 打开、← 返回、L3 切根区域 · Y 项目上下文 · 右摇杆调节 · A 按住说话 · X 发送 · B 取消 / 撤回";
    }

    private void XInputService_StateChanged(
        object? sender,
        ControllerState state)
    {
        lock (_controllerStateSync)
        {
            _latestControllerState = state;
        }

        if (Interlocked.Exchange(ref _controllerDispatchPending, 1) != 0)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(ProcessLatestControllerState);
    }

    private void ProcessLatestControllerState()
    {
        ControllerState state;
        lock (_controllerStateSync)
        {
            state = _latestControllerState;
            Interlocked.Exchange(ref _controllerDispatchPending, 0);
        }

        ProcessControllerState(state);
    }

    private void ConfigureCodexKeybindings()
    {
        if (!_settings.BridgeEnabled)
        {
            return;
        }

        var result =
            _codexKeybindingService.EnsureBridgeBindings(_settings);
        if (!result.Succeeded)
        {
            AddEvent($"Codex 快捷键未写入 · {result.Error}");
            return;
        }

        if (result.Conflicts.Count > 0)
        {
            AddEvent($"Codex 快捷键冲突 · {result.Conflicts[0]}");
        }
        else if (result.Changed)
        {
            AddEvent("降级快捷键已写入 · 重启 Codex 后生效");
        }
    }

    private void ProcessControllerState(ControllerState state)
    {
        UpdateControllerVisual(state);

        if (!state.IsConnected)
        {
            if (_controllerWasConnected)
            {
                _controllerWasConnected = false;
                AddEvent("手柄连接已暂停 · 重连后自动恢复");
            }

            PauseControllerInput();
            _previousButtons = ControllerButtons.None;
            return;
        }

        if (!_controllerWasConnected)
        {
            _controllerWasConnected = true;
            _controllerSession.Pause(requireNeutral: true);
            if (_hasSeenController)
            {
                AddEvent("手柄已重新连接 · 松开按键后自动恢复");
            }

            _hasSeenController = true;
        }

        var pressed = state.Buttons;
        HandleButtonEdge(
            pressed,
            ControllerButtons.Start,
            onDown: WakeCodex);

        var foreground = _codexCommandService.IsCodexForeground;
        ObserveCodexForeground(foreground);
        if (
            !_settings.BridgeEnabled ||
            !_controllerSession.IsArmed ||
            (_settings.OnlyWhenCodexForeground && !foreground))
        {
            PauseControllerInput(state);
            _previousButtons = pressed;
            return;
        }

        if (!TryResumeControllerInput(state))
        {
            _previousButtons = pressed;
            return;
        }

        HandleButtonEdge(
            pressed,
            ControllerButtons.LeftThumb,
            onDown: CycleRootSidebarScope);
        HandleButtonEdge(
            pressed,
            ControllerButtons.RightThumb,
            onDown: CycleRightControlMode);
        HandleButtonEdge(
            pressed,
            ControllerButtons.DPadLeft,
            onDown: () =>
            {
                _leftStickRouter.RequireNeutral();
                NavigateSidebarHorizontal(-1);
            });
        HandleButtonEdge(
            pressed,
            ControllerButtons.DPadRight,
            onDown: () =>
            {
                _leftStickRouter.RequireNeutral();
                NavigateSidebarHorizontal(1);
            });
        HandleButtonEdge(
            pressed,
            ControllerButtons.Y,
            onDown: JumpToProjectContext);
        HandleButtonEdge(
            pressed,
            ControllerButtons.A,
            onDown: StartDictation,
            onUp: StopDictation);
        HandleButtonEdge(
            pressed,
            ControllerButtons.X,
            onDown: SendPrompt);
        HandleButtonEdge(
            pressed,
            ControllerButtons.B,
            onDown: CancelAction);
        _previousButtons = pressed;

        var deadZone = _settings.DeadZone;
        var leftGesture = _leftStickRouter.Update(
            state.LeftX,
            state.LeftY,
            deadZone,
            invertVertical: true,
            blocked:
                pressed.HasFlag(ControllerButtons.LeftThumb) ||
                Environment.TickCount64 < _leftNavigationBlockedUntil);
        var rightGesture = _rightStickRouter.Update(
            state.RightX,
            state.RightY,
            deadZone,
            invertVertical: false,
            blocked:
                pressed.HasFlag(ControllerButtons.RightThumb) ||
                Environment.TickCount64 < _rightAdjustmentBlockedUntil);

        _axisRepeater.Update(
            "left-y",
            leftGesture.VerticalDirection,
            _settings.RepeatDelayMs,
            _settings.RepeatIntervalMs,
            MoveSidebarSelection);
        if (leftGesture.HorizontalStarted)
        {
            NavigateSidebarHorizontal(leftGesture.HorizontalDirection);
        }
        _axisRepeater.Update(
            "right-y",
            rightGesture.VerticalDirection,
            _settings.RepeatDelayMs,
            _settings.RepeatIntervalMs,
            AdjustRightMode);
        if (rightGesture.HorizontalStarted)
        {
            SwitchRightControlMode(rightGesture.HorizontalDirection);
        }
    }

    private void HandleButtonEdge(
        ControllerButtons current,
        ControllerButtons button,
        Action onDown,
        Action? onUp = null)
    {
        var isPressed = current.HasFlag(button);
        var wasPressed = _previousButtons.HasFlag(button);
        if (isPressed && !wasPressed)
        {
            onDown();
        }
        else if (!isPressed && wasPressed)
        {
            onUp?.Invoke();
        }
    }

    private async void WakeCodex()
    {
        if (_wakeInProgress)
        {
            return;
        }

        _wakeInProgress = true;
        AddEvent("Menu · 正在启动或唤醒 Codex");
        ShowFeedback("Menu · Codex", "正在启动或置于前台");
        try
        {
            var woke = await Task.Run(_codexCommandService.WakeCodex);
            if (!woke)
            {
                _controllerSession.Lock();
                AddEvent("Menu · 未找到或无法启动 Codex");
                ShowFeedback("Menu · Codex", "启动 / 唤醒失败");
                return;
            }

            _controllerSession.Arm();
            _axisRepeater.Reset();
            _leftStickRouter.RequireNeutral();
            _rightStickRouter.RequireNeutral();
            _leftNavigationBlockedUntil =
                Environment.TickCount64 + BridgeTimings.WakeInputGuardMs;
            _rightAdjustmentBlockedUntil =
                Environment.TickCount64 + BridgeTimings.WakeInputGuardMs;
            AddEvent("Menu · Codex 已唤醒 · 手柄控制已解锁");
            ShowFeedback("Menu · Codex", "已置于前台 · 可控制");
            Pulse(strength: 0.3);
            UpdateCodexStatus();
        }
        finally
        {
            _wakeInProgress = false;
        }
    }

    private void ObserveCodexForeground(bool foreground)
    {
        if (
            !_settings.OnlyWhenCodexForeground ||
            foreground ||
            !_controllerSession.IsArmed)
        {
            return;
        }

        // Losing foreground pauses the armed session, but does not lock it.
        // This avoids requiring Menu again after Codex has been open for a while.
        PauseControllerInput(_xInputService.LastState);
    }

    private void PauseControllerInput(
        ControllerState? state = null)
    {
        if (_controllerSession.IsActive)
        {
            CancelPendingSidebarFocus();
            CancelPendingComposerSelection();
        }

        _controllerSession.Pause(
            requireNeutral:
                state is null ||
                !IsControllerNeutral(state.Value));

        _axisRepeater.Reset();
        _leftStickRouter.Reset();
        _rightStickRouter.Reset();
    }

    private bool TryResumeControllerInput(ControllerState state)
    {
        var wasActive = _controllerSession.IsActive;
        if (!_controllerSession.TryActivate(IsControllerNeutral(state)))
        {
            return false;
        }

        if (!wasActive)
        {
            _previousButtons = ControllerButtons.None;
            _axisRepeater.Reset();
            _leftStickRouter.Reset();
            _rightStickRouter.Reset();
            if (_dictationInjected)
            {
                StopDictation();
            }
        }

        return true;
    }

    private bool IsControllerNeutral(ControllerState state)
    {
        var stickZone = Math.Clamp(
            _settings.DeadZone * 0.45,
            0.18,
            0.34);
        return
            state.IsConnected &&
            state.Buttons == ControllerButtons.None &&
            Math.Abs(state.LeftX) < stickZone &&
            Math.Abs(state.LeftY) < stickZone &&
            Math.Abs(state.RightX) < stickZone &&
            Math.Abs(state.RightY) < stickZone &&
            state.LeftTrigger < 0.12 &&
            state.RightTrigger < 0.12;
    }

    private void NavigateSidebarHorizontal(int direction)
    {
        _leftStickRouter.RequireNeutral();
        _leftNavigationBlockedUntil =
            Environment.TickCount64 + BridgeTimings.GestureInputGuardMs;
        if (direction < 0)
        {
            if (_scope == SidebarScope.ProjectTasks)
            {
                ExitProjectTasks();
            }
            else
            {
                ShowFeedback("Codex 侧边栏", "当前已在根区域");
            }

            return;
        }

        EnterSelectedSidebarEntry();
    }

    private void CycleRootSidebarScope()
    {
        _leftStickRouter.RequireNeutral();
        _leftNavigationBlockedUntil =
            Environment.TickCount64 + BridgeTimings.GestureInputGuardMs;
        var rootScope = _scope == SidebarScope.ProjectTasks
            ? _sidebarReturnFrame?.Scope ?? SidebarScope.Projects
            : _scope;
        var current = Array.IndexOf(RootSidebarScopes, rootScope);
        var next = (Math.Max(0, current) + 1) % RootSidebarScopes.Length;
        SetSidebarScope(RootSidebarScopes[next], showFeedback: true);
    }

    private void SetSidebarScope(
        SidebarScope scope,
        bool showFeedback,
        string? preferredId = null)
    {
        if (_scope == SidebarScope.ProjectTasks)
        {
            RememberCurrentSidebarCursor();
            RestoreProjectDisclosureLease();
            _sidebarReturnFrame = null;
        }
        else
        {
            RememberCurrentSidebarCursor();
        }

        _scope = scope;
        _projectTasksPinnedOnly = false;
        _selectedIndex = 0;
        RebuildSidebarEntries();

        preferredId ??= _rootCursorIds.GetValueOrDefault(scope);
        RestoreSidebarSelection(preferredId);
        UpdateLayerTabs();
        FocusCurrentSidebarEntry();

        if (showFeedback)
        {
            var label = ScopeLabel(scope);
            AddEvent($"侧边栏区域 · {label}");
            ShowFeedback("Codex 侧边栏", label);
            Pulse();
        }
    }

    private void RestoreSidebarSelection(string? preferredId)
    {
        if (_sidebarEntries.Count == 0)
        {
            _selectedIndex = -1;
            SidebarList.SelectedIndex = -1;
            return;
        }

        var restored = string.IsNullOrWhiteSpace(preferredId)
            ? -1
            : _sidebarEntries
                .Select((entry, index) => new { entry.Id, index })
                .FirstOrDefault(item =>
                    string.Equals(
                        item.Id,
                        preferredId,
                        StringComparison.OrdinalIgnoreCase))?.index ?? -1;
        _selectedIndex = restored >= 0
            ? restored
            : Math.Clamp(_selectedIndex, 0, _sidebarEntries.Count - 1);
        SelectSidebarIndex(_selectedIndex);

        var selected = _sidebarEntries[_selectedIndex];
        if (selected.Layer == SidebarLayer.Projects)
        {
            _selectedProjectPath = selected.ProjectPath;
        }
    }

    private void FocusCurrentSidebarEntry()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _sidebarEntries.Count)
        {
            FocusCodexSidebarEntry(_sidebarEntries[_selectedIndex]);
        }
    }

    private void EnterSelectedSidebarEntry()
    {
        if (SidebarList.SelectedItem is not SidebarEntry entry)
        {
            ShowFeedback("Codex 侧边栏", "当前区域没有可用条目");
            return;
        }

        if (entry.Layer == SidebarLayer.Projects)
        {
            EnterProjectTasks(entry.ProjectPath, preferredThreadId: null);
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.ThreadId))
        {
            return;
        }

        OpenThreadNow(
            entry.ThreadId,
            entry.Title,
            entry.NativeTitle ?? entry.Title);
    }

    private void EnterProjectTasks(
        string? projectPath,
        string? preferredThreadId)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            ShowFeedback("项目任务", "该任务未归属项目");
            return;
        }

        var project = _snapshot.Projects.FirstOrDefault(item =>
            string.Equals(
                item.Path,
                projectPath,
                StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            ShowFeedback("项目任务", "项目当前不可用");
            return;
        }

        if (
            _scope != SidebarScope.ProjectTasks ||
            !string.Equals(
                _selectedProjectPath,
                projectPath,
                StringComparison.OrdinalIgnoreCase))
        {
            RememberCurrentSidebarCursor();
            RestoreProjectDisclosureLease();
            _sidebarReturnFrame = new SidebarReturnFrame(
                _scope == SidebarScope.ProjectTasks
                    ? SidebarScope.Projects
                    : _scope,
                SidebarList.SelectedItem is SidebarEntry selected
                    ? selected.Id
                    : null,
                _selectedProjectPath);
        }

        _selectedProjectPath = project.Path;
        _scope = SidebarScope.ProjectTasks;
        _projectTasksPinnedOnly = false;
        _projectDisclosureLease = new ProjectDisclosureLease(
            project.Name,
            project.IsPinned);
        RebuildSidebarEntries();

        preferredThreadId ??=
            _projectTaskCursorIds.GetValueOrDefault(project.Path);
        RestoreSidebarSelection(preferredThreadId);
        UpdateLayerTabs();
        FocusCurrentSidebarEntry();

        var position = _selectedIndex >= 0
            ? $"{_selectedIndex + 1} / {_sidebarEntries.Count}"
            : "暂无可用任务";
        AddEvent($"项目任务 · {project.Name} · {position}");
        ShowFeedback($"项目 › {project.Name}", position);
        Pulse();
    }

    private void ExitProjectTasks()
    {
        if (_scope != SidebarScope.ProjectTasks)
        {
            return;
        }

        RememberCurrentSidebarCursor();
        var frame = _sidebarReturnFrame;
        RestoreProjectDisclosureLease();
        _sidebarReturnFrame = null;
        _selectedProjectPath = frame?.ProjectPath ?? _selectedProjectPath;
        SetSidebarScope(
            frame?.Scope ?? SidebarScope.Projects,
            showFeedback: true,
            preferredId: frame?.EntryId);
    }

    private void JumpToProjectContext()
    {
        if (_scope == SidebarScope.ProjectTasks)
        {
            ToggleProjectPinnedFilter();
            return;
        }

        if (SidebarList.SelectedItem is not SidebarEntry entry)
        {
            ShowFeedback("Y · 项目任务", "当前没有可定位条目");
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.ProjectPath))
        {
            ShowFeedback("Y · 项目任务", "该任务未归属项目");
            return;
        }

        EnterProjectTasks(entry.ProjectPath, entry.ThreadId);
    }

    private void ToggleProjectPinnedFilter()
    {
        if (string.IsNullOrWhiteSpace(_selectedProjectPath))
        {
            return;
        }

        var previousId = SidebarList.SelectedItem is SidebarEntry entry
            ? entry.Id
            : null;
        _projectTasksPinnedOnly = !_projectTasksPinnedOnly;
        RebuildSidebarEntries();
        if (_projectTasksPinnedOnly && _sidebarEntries.Count == 0)
        {
            _projectTasksPinnedOnly = false;
            RebuildSidebarEntries();
            RestoreSidebarSelection(previousId);
            ShowFeedback("Y · 项目任务", "该项目没有置顶任务");
            return;
        }

        RestoreSidebarSelection(previousId);
        FocusCurrentSidebarEntry();
        var label = _projectTasksPinnedOnly ? "仅该项目置顶" : "全部任务";
        AddEvent($"项目任务筛选 · {label}");
        ShowFeedback("Y · 项目任务", label);
        Pulse();
    }

    private void RememberCurrentSidebarCursor()
    {
        if (SidebarList.SelectedItem is not SidebarEntry selected)
        {
            return;
        }

        if (
            _scope == SidebarScope.ProjectTasks &&
            !string.IsNullOrWhiteSpace(_selectedProjectPath))
        {
            _projectTaskCursorIds[_selectedProjectPath] = selected.Id;
            return;
        }

        if (RootSidebarScopes.Contains(_scope))
        {
            _rootCursorIds[_scope] = selected.Id;
        }
    }

    private void MoveSidebarSelection(int direction)
    {
        if (_sidebarEntries.Count == 0)
        {
            AddEvent($"{ScopeLabel(_scope)}中暂无可用条目");
            return;
        }

        var nextIndex = Math.Clamp(
            _selectedIndex + Math.Sign(direction),
            0,
            _sidebarEntries.Count - 1);
        if (nextIndex == _selectedIndex)
        {
            return;
        }

        _selectedIndex = nextIndex;
        SelectSidebarIndex(_selectedIndex);
        ActivateSelectedEntry(_sidebarEntries[_selectedIndex]);
    }

    private void SelectSidebarIndex(int index)
    {
        _suppressSelectionActivation = true;
        SidebarList.SelectedIndex = index;
        SidebarList.ScrollIntoView(SidebarList.SelectedItem);
        _suppressSelectionActivation = false;
    }

    private void ActivateSelectedEntry(SidebarEntry entry)
    {
        RememberCurrentSidebarCursor();
        FocusCodexSidebarEntry(entry);
        if (entry.Layer == SidebarLayer.Projects)
        {
            _selectedProjectPath = entry.ProjectPath;
            SelectedProjectText.Text = entry.Title;
        }

        RememberCurrentSidebarCursor();
        AddEvent($"{ScopeLabel(_scope)} · {entry.Title}");
        ShowFeedback(ScopeLabel(_scope), entry.Title);
        Pulse();
    }

    private void FocusCodexSidebarEntry(SidebarEntry entry)
    {
        CancelPendingSidebarFocus();
        if (!_settings.BridgeEnabled)
        {
            return;
        }

        var projectName = entry.NativeListIndex is not null
            ? null
            : _snapshot.Projects
                .FirstOrDefault(project =>
                    string.Equals(
                        project.Path,
                        entry.ProjectPath ?? _selectedProjectPath,
                        StringComparison.OrdinalIgnoreCase))?.Name;
        var cancellation = new CancellationTokenSource();
        _sidebarFocusCancellation = cancellation;
        var disclosureLease =
            _scope == SidebarScope.ProjectTasks
                ? _projectDisclosureLease
                : null;
        _ = FocusCodexSidebarEntryAsync(
            entry,
            projectName,
            disclosureLease,
            cancellation);
    }

    private async Task FocusCodexSidebarEntryAsync(
        SidebarEntry entry,
        string? projectName,
        ProjectDisclosureLease? disclosureLease,
        CancellationTokenSource cancellation)
    {
        var gateEntered = false;
        try
        {
            await _sidebarFocusGate.WaitAsync(cancellation.Token)
                .ConfigureAwait(true);
            gateEntered = true;
            var result = await Task.Run(
                    () => _codexSidebarService.FocusEntry(
                        entry,
                        projectName,
                        _settings,
                        cancellation.Token,
                        disclosureLease),
                    cancellation.Token)
                .ConfigureAwait(true);
            if (
                !result.Succeeded &&
                !cancellation.IsCancellationRequested &&
                !string.Equals(
                    result.Error,
                    "已取消",
                    StringComparison.Ordinal))
            {
                AddEvent($"侧边栏焦点未同步 · {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            // A newer stick movement replaced this focus target.
        }
        finally
        {
            if (gateEntered)
            {
                _sidebarFocusGate.Release();
            }

            if (ReferenceEquals(_sidebarFocusCancellation, cancellation))
            {
                _sidebarFocusCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelPendingSidebarFocus()
    {
        var cancellation = _sidebarFocusCancellation;
        _sidebarFocusCancellation = null;
        cancellation?.Cancel();
    }

    private void RestoreProjectDisclosureLease()
    {
        var lease = _projectDisclosureLease;
        _projectDisclosureLease = null;
        if (lease is null || !_settings.BridgeEnabled)
        {
            return;
        }

        CancelPendingSidebarFocus();
        var result = _codexSidebarService.RestoreDisclosure(lease);
        if (!result.Succeeded)
        {
            AddEvent($"项目折叠状态未恢复 · {result.Error}");
        }
    }

    private void OpenThreadNow(
        string threadId,
        string threadTitle,
        string nativeThreadTitle)
    {
        if (
            _settings.OnlyWhenCodexForeground &&
            !_codexCommandService.IsCodexForeground &&
            !IsActive)
        {
            return;
        }

        if (!_codexDataService.IsThreadAvailable(threadId))
        {
            AddEvent("任务已归档或不可用 · 已跳过");
            RefreshCodexData(preserveSelection: true);
            return;
        }

        ClearNavigationUndo();
        var previousTitle = _codexSidebarService.TryGetCurrentThreadTitle();
        if (CodexCommandService.OpenThread(threadId))
        {
            RegisterNavigationUndo(
                threadTitle,
                nativeThreadTitle,
                previousTitle);
            AddEvent($"正在打开 · {threadTitle}");
            ShowFeedback("正在打开任务", threadTitle);
        }
        else
        {
            AddEvent($"打开任务失败 · {threadTitle}");
        }
    }

    private void RegisterNavigationUndo(
        string threadTitle,
        string nativeThreadTitle,
        string? previousTitle)
    {
        if (
            string.IsNullOrWhiteSpace(nativeThreadTitle) ||
            string.Equals(
                previousTitle,
                nativeThreadTitle,
                StringComparison.Ordinal))
        {
            return;
        }

        var matchingTitles = _snapshot.Threads.Count(thread =>
            string.Equals(
                thread.NativeTitle ?? thread.Title,
                nativeThreadTitle,
                StringComparison.Ordinal));
        if (matchingTitles != 1)
        {
            AddEvent($"撤回未启用 · 无法唯一确认 {threadTitle}");
            return;
        }

        var state = new NavigationUndoState(
            threadTitle,
            nativeThreadTitle,
            previousTitle);
        _navigationUndo = state;
        var cancellation = new CancellationTokenSource();
        _navigationConfirmCancellation = cancellation;
        _ = ConfirmNavigationUndoAsync(state, cancellation);
    }

    private async Task ConfirmNavigationUndoAsync(
        NavigationUndoState state,
        CancellationTokenSource cancellation)
    {
        var consecutiveMatches = 0;
        var deadline =
            Environment.TickCount64 +
            BridgeTimings.NavigationConfirmTimeoutMs;
        try
        {
            while (Environment.TickCount64 < deadline)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var currentTitle = await Task.Run(
                        _codexSidebarService.TryGetCurrentThreadTitle,
                        cancellation.Token)
                    .ConfigureAwait(true);
                if (
                    string.Equals(
                        currentTitle,
                        state.TargetNativeTitle,
                        StringComparison.Ordinal))
                {
                    consecutiveMatches++;
                    if (consecutiveMatches >= 2)
                    {
                        if (!ReferenceEquals(_navigationUndo, state))
                        {
                            return;
                        }

                        state.Confirmed = true;
                        state.ExpiresAt =
                            DateTimeOffset.UtcNow +
                            BridgeTimings.NavigationUndoWindow;
                        if (_scope == SidebarScope.ProjectlessTasks)
                        {
                            RefreshCodexData(preserveSelection: true);
                        }

                        if (state.UndoRequested)
                        {
                            ExecuteNavigationUndo(state);
                            return;
                        }

                        AddEvent(
                            $"已打开 · {state.TargetDisplayTitle} · B 可撤回");
                        ShowFeedback(
                            "已打开任务",
                            $"{state.TargetDisplayTitle} · 10 秒内按 B 撤回");
                        return;
                    }
                }
                else
                {
                    consecutiveMatches = 0;
                }

                await Task.Delay(
                        BridgeTimings.NavigationConfirmPollMs,
                        cancellation.Token)
                    .ConfigureAwait(true);
            }

            if (ReferenceEquals(_navigationUndo, state))
            {
                _navigationUndo = null;
                AddEvent(
                    $"撤回未启用 · 未确认已到达 {state.TargetDisplayTitle}");
                if (state.UndoRequested)
                {
                    ShowFeedback(
                        "B · 撤回",
                        "未确认任务已打开，未执行返回");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A newer navigation or an intentional command invalidated this frame.
        }
        finally
        {
            if (ReferenceEquals(_navigationConfirmCancellation, cancellation))
            {
                _navigationConfirmCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ClearNavigationUndo()
    {
        _navigationUndo = null;
        var cancellation = _navigationConfirmCancellation;
        _navigationConfirmCancellation = null;
        cancellation?.Cancel();
    }

    private void CycleRightControlMode()
    {
        _rightStickRouter.RequireNeutral();
        SwitchRightControlMode(1, source: "R3");
    }

    private void SwitchRightControlMode(
        int direction,
        string? source = null)
    {
        CancelPendingComposerSelection();
        _rightAdjustmentBlockedUntil =
            Environment.TickCount64 + BridgeTimings.GestureInputGuardMs;
        var values = Enum.GetValues<RightControlMode>();
        var current = Array.IndexOf(values, _rightMode);
        var next =
            (current + Math.Sign(direction) + values.Length) %
            values.Length;
        _rightMode = values[next];
        UpdateRightModeUi();
        var gesture =
            source ??
            $"右摇杆 {Arrow(direction, horizontal: true)}";
        AddEvent($"{gesture} · {ModeLabel(_rightMode)}");
        ShowFeedback("右摇杆模式", ModeLabel(_rightMode));
        Pulse();
    }

    private void AdjustRightMode(int direction)
    {
        switch (_rightMode)
        {
            case RightControlMode.Reasoning:
                AdjustReasoning(direction);
                break;
            case RightControlMode.Model:
                AdjustModel(direction);
                break;
            case RightControlMode.Speed:
                AdjustSpeed(direction);
                break;
        }
    }

    private void AdjustReasoning(int direction)
    {
        var efforts = CurrentEfforts();
        var next = Math.Clamp(
            _reasoningIndex + direction,
            0,
            efforts.Count - 1);
        if (next == _reasoningIndex)
        {
            RefreshPendingComposerDeadline(ComposerSettingKind.Effort);
            return;
        }

        _reasoningIndex = next;
        var target = efforts[_reasoningIndex];
        RightModeValue.Text = $"{target} · 预选";
        ScheduleComposerCommit(ComposerSettingKind.Effort, target);
        AddEvent($"思考强度预选 · {target}");
        ShowFeedback("思考强度预选", $"{target} · 停稳后确认");
        Pulse();
    }

    private void AdjustModel(int direction)
    {
        if (_composerCatalog is null || _composerCatalog.Models.Count == 0)
        {
            return;
        }

        var next = Math.Clamp(
            _modelIndex + direction,
            0,
            _composerCatalog.Models.Count - 1);
        if (next == _modelIndex)
        {
            RefreshPendingComposerDeadline(ComposerSettingKind.Model);
            return;
        }

        _modelIndex = next;
        var target = _composerCatalog.Models[_modelIndex].DisplayName;
        RightModeValue.Text = $"{target} · 预选";
        ScheduleComposerCommit(ComposerSettingKind.Model, target);
        AddEvent($"模型预选 · {target}");
        ShowFeedback("模型预选", $"{target} · 停稳后确认");
        Pulse();
    }

    private void AdjustSpeed(int direction)
    {
        var next = direction > 0 ? 1 : 0;
        if (next == _speedIndex)
        {
            RefreshPendingComposerDeadline(ComposerSettingKind.Speed);
            return;
        }

        _speedIndex = next;
        var target = SpeedLabel(_speedIndex);
        RightModeValue.Text = $"{target} · 预选";
        ScheduleComposerCommit(ComposerSettingKind.Speed, target);
        AddEvent($"速度预选 · {target}");
        ShowFeedback("速度预选", $"{target} · 停稳后确认");
        Pulse();
    }

    private void ScheduleComposerCommit(
        ComposerSettingKind kind,
        string target)
    {
        _composerCommitCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _composerCommitCancellation = cancellation;
        _pendingComposerKind = kind;
        _pendingComposerTarget = target;
        _ = CommitComposerAfterSettleAsync(kind, target, cancellation);
    }

    private void RefreshPendingComposerDeadline(ComposerSettingKind kind)
    {
        if (
            _pendingComposerKind == kind &&
            !string.IsNullOrWhiteSpace(_pendingComposerTarget))
        {
            ScheduleComposerCommit(kind, _pendingComposerTarget);
        }
    }

    private async Task CommitComposerAfterSettleAsync(
        ComposerSettingKind kind,
        string target,
        CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(
                    BridgeTimings.ComposerSettleMs,
                    cancellation.Token)
                .ConfigureAwait(true);
            RightModeValue.Text = $"{target} · 正在应用…";
            var automation = await _codexComposerService
                .SelectAsync(kind, target, _settings, cancellation.Token)
                .ConfigureAwait(true);
            var fallback = false;
            if (!automation.Succeeded)
            {
                fallback = await ExecuteComposerFallbackAsync(
                        kind,
                        cancellation.Token)
                    .ConfigureAwait(true);
            }

            if (cancellation.IsCancellationRequested)
            {
                return;
            }

            if (automation.Succeeded || fallback)
            {
                ClearNavigationUndo();
                if (kind == ComposerSettingKind.Model)
                {
                    await Task.Delay(
                            BridgeTimings.ComposerFallbackSettleMs,
                            cancellation.Token)
                        .ConfigureAwait(true);
                    InitializeComposerControls();
                }
                else
                {
                    MarkComposerCommitted(kind);
                }

                RightModeValue.Text =
                    automation.Succeeded
                        ? target
                        : $"{target} · 快捷键已发送";
                var channel =
                    automation.Succeeded
                        ? "精确选择"
                        : "快捷键已发送（新绑定需重启 Codex）";
                AddEvent($"{ComposerKindLabel(kind)} → {target} · {channel}");
                ShowFeedback(
                    ComposerKindLabel(kind),
                    automation.Succeeded ? target : "快捷键已发送");
            }
            else
            {
                RightModeValue.Text = $"{target} · 未执行";
                AddEvent(
                    $"{ComposerKindLabel(kind)} · 未执行 · {automation.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            // A newer stick input replaced this pending selection.
        }
        finally
        {
            if (ReferenceEquals(_composerCommitCancellation, cancellation))
            {
                _composerCommitCancellation = null;
                _pendingComposerKind = null;
                _pendingComposerTarget = null;
            }

            cancellation.Dispose();
        }
    }

    private async Task<bool> ExecuteComposerFallbackAsync(
        ComposerSettingKind kind,
        CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case ComposerSettingKind.Model:
            {
                var steps = _modelIndex - _committedModelIndex;
                return steps != 0 &&
                       await _codexCommandService
                           .StepModelAsync(
                               steps,
                               _settings,
                               cancellationToken)
                           .ConfigureAwait(true);
            }
            case ComposerSettingKind.Effort:
            {
                var steps = _reasoningIndex - _committedReasoningIndex;
                if (steps == 0)
                {
                    return true;
                }

                var shortcut =
                    steps > 0
                        ? _settings.ReasoningUpShortcut
                        : _settings.ReasoningDownShortcut;
                for (var index = 0; index < Math.Abs(steps); index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_codexCommandService.ExecuteShortcut(
                            shortcut,
                            _settings))
                    {
                        return false;
                    }

                    await Task.Delay(
                            BridgeTimings.ComposerMenuPollMs,
                            cancellationToken)
                        .ConfigureAwait(true);
                }

                return true;
            }
            case ComposerSettingKind.Speed:
                return
                    _speedIndex == _committedSpeedIndex ||
                    _codexCommandService.ExecuteShortcut(
                        _settings.FastToggleShortcut,
                        _settings);
            default:
                return false;
        }
    }

    private void MarkComposerCommitted(ComposerSettingKind kind)
    {
        switch (kind)
        {
            case ComposerSettingKind.Model:
                _committedModelIndex = _modelIndex;
                var efforts = CurrentEfforts();
                _reasoningIndex = Math.Clamp(
                    _reasoningIndex,
                    0,
                    efforts.Count - 1);
                _committedReasoningIndex = _reasoningIndex;
                break;
            case ComposerSettingKind.Effort:
                _committedReasoningIndex = _reasoningIndex;
                break;
            case ComposerSettingKind.Speed:
                _committedSpeedIndex = _speedIndex;
                break;
        }
    }

    private void CancelPendingComposerSelection()
    {
        var pendingKind = _pendingComposerKind;
        _composerCommitCancellation?.Cancel();
        _composerCommitCancellation = null;
        _pendingComposerKind = null;
        _pendingComposerTarget = null;

        switch (pendingKind)
        {
            case ComposerSettingKind.Model:
                _modelIndex = _committedModelIndex;
                break;
            case ComposerSettingKind.Effort:
                _reasoningIndex = _committedReasoningIndex;
                break;
            case ComposerSettingKind.Speed:
                _speedIndex = _committedSpeedIndex;
                break;
        }

        if (_composerCatalog is not null)
        {
            UpdateRightModeUi();
        }
    }

    private void StartDictation()
    {
        _dictationStopCancellation?.Cancel();
        _dictationStopCancellation = null;
        ButtonAHalo.Opacity = 1;
        var automation = _codexComposerService.InvokeComposerAction(
            _settings,
            "Dictate",
            "Start dictation");
        _dictationInjected =
            automation.Succeeded ||
            _codexCommandService.ExecuteShortcut(
                _settings.DictationShortcut,
                _settings);
        if (_dictationInjected)
        {
            ClearNavigationUndo();
        }
        AddEvent(
            $"A · 开始语音识别" +
            ExecutionSuffix(_dictationInjected, automation.Error));
        ShowFeedback(
            "A · 按住说话",
            _dictationInjected
                ? "正在录音 · 松开 A 停止"
                : ExecutionFailureLabel(automation.Error));
        Pulse();
    }

    private void StopDictation()
    {
        ButtonAHalo.Opacity = 0;
        var shouldStop = _dictationInjected;
        _dictationInjected = false;
        if (!shouldStop)
        {
            AddEvent("A 松开 · 未发现活动录音");
            ShowFeedback("A · 按住说话", "未发现活动录音");
            return;
        }

        _dictationStopCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _dictationStopCancellation = cancellation;
        AddEvent("A 松开 · 正在结束语音识别");
        ShowFeedback("A · 按住说话", "松开 · 正在结束录音");
        _ = StopDictationAfterReleaseAsync(cancellation);
    }

    private async Task StopDictationAfterReleaseAsync(
        CancellationTokenSource cancellation)
    {
        try
        {
            var automation = await _codexComposerService
                .InvokeComposerActionAsync(
                    _settings,
                    timeoutMs: 1200,
                    cancellation.Token,
                    "Stop dictation",
                    "Stop recording",
                    "Stop listening")
                .ConfigureAwait(true);
            var executed =
                automation.Succeeded ||
                _codexCommandService.ExecuteShortcut(
                    _settings.DictationShortcut,
                    _settings);
            AddEvent(
                $"A 松开 · 结束语音识别" +
                ExecutionSuffix(executed, automation.Error));
            ShowFeedback(
                "A · 按住说话",
                executed
                    ? "录音已结束"
                    : ExecutionFailureLabel(automation.Error));
        }
        catch (OperationCanceledException)
        {
            // A new dictation session replaced this stop request.
        }
        finally
        {
            if (ReferenceEquals(_dictationStopCancellation, cancellation))
            {
                _dictationStopCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void SendPrompt()
    {
        var automation = _codexComposerService.SubmitComposer(_settings);
        if (automation.Succeeded)
        {
            ClearNavigationUndo();
        }

        AddEvent(
            $"X · 发送提示词" +
            ExecutionSuffix(automation.Succeeded, automation.Error));
        ShowFeedback(
            "X · 发送",
            automation.Succeeded
                ? "已发送"
                : ExecutionFailureLabel(automation.Error));
        Pulse(strength: 0.28);
    }

    private void CancelAction()
    {
        if (_dictationInjected)
        {
            CancelPendingSidebarFocus();
            CancelPendingComposerSelection();
            _dictationInjected = false;
            ButtonAHalo.Opacity = 0;
            _dictationStopCancellation?.Cancel();
            _dictationStopCancellation = null;
            var stop = _codexComposerService.InvokeComposerAction(
                _settings,
                "Stop dictation",
                "Stop recording",
                "Stop listening");
            var stopped =
                stop.Succeeded ||
                _codexCommandService.ExecuteShortcut(
                    _settings.DictationShortcut,
                    _settings);
            ClearNavigationUndo();
            AddEvent(
                $"B · 中止语音" +
                ExecutionSuffix(stopped, stop.Error));
            ShowFeedback(
                "B · 取消",
                stopped
                    ? "已中止语音识别"
                    : ExecutionFailureLabel(stop.Error));
            Pulse(strength: 0.18);
            return;
        }

        var hadPendingSelection =
            _pendingComposerKind is not null ||
            _composerCommitCancellation is not null;
        CancelPendingSidebarFocus();
        CancelPendingComposerSelection();
        if (hadPendingSelection)
        {
            AddEvent("B · 已撤销待执行选择");
            ShowFeedback("B · 撤回", "已撤销待执行选择");
            Pulse(strength: 0.18);
            return;
        }

        var active = _codexComposerService.InvokeComposerAction(
            _settings,
            "Stop",
            "Cancel",
            "Cancel request");
        if (active.Succeeded)
        {
            ClearNavigationUndo();
            AddEvent("B · 已中止当前操作");
            ShowFeedback("B · 取消", "已中止当前操作");
            Pulse(strength: 0.18);
            return;
        }

        if (_navigationUndo is { } undo)
        {
            if (
                !undo.Confirmed ||
                undo.ExpiresAt is null)
            {
                undo.UndoRequested = true;
                AddEvent("B · 已排队撤回");
                ShowFeedback("B · 撤回", "任务打开后将自动返回");
                Pulse(strength: 0.12);
                return;
            }

            if (DateTimeOffset.UtcNow > undo.ExpiresAt)
            {
                ClearNavigationUndo();
            }
            else
            {
                ExecuteNavigationUndo(undo);
                return;
            }
        }

        var automation = _codexComposerService.CancelComposer(_settings);
        var executed = automation.Succeeded;
        AddEvent(
            $"B · 取消" +
            ExecutionSuffix(executed, automation.Error));
        ShowFeedback(
            "B · 取消",
            executed
                ? "已取消"
                : ExecutionFailureLabel(automation.Error));
        Pulse(strength: 0.18);
    }

    private void ExecuteNavigationUndo(NavigationUndoState undo)
    {
        if (!ReferenceEquals(_navigationUndo, undo))
        {
            return;
        }

        var currentTitle =
            _codexSidebarService.TryGetCurrentThreadTitle();
        if (
            !string.Equals(
                currentTitle,
                undo.TargetNativeTitle,
                StringComparison.Ordinal))
        {
            ClearNavigationUndo();
            AddEvent("B · 页面已变化，未执行导航撤回");
            ShowFeedback("B · 撤回", "页面已变化，未执行返回");
            Pulse(strength: 0.12);
            return;
        }

        var navigation =
            _codexSidebarService.GoBack(_settings);
        if (navigation.Succeeded)
        {
            var target = undo.TargetDisplayTitle;
            ClearNavigationUndo();
            AddEvent($"B · 已撤回 · {target}");
            ShowFeedback("B · 撤回", $"已返回上一任务 · {target}");
            Pulse(strength: 0.18);
            return;
        }

        AddEvent($"B · 撤回失败 · {navigation.Error}");
        ShowFeedback(
            "B · 撤回",
            ExecutionFailureLabel(navigation.Error));
        Pulse(strength: 0.12);
    }

    private void UpdateControllerVisual(ControllerState state)
    {
        ControllerStatusDot.SetResourceReference(
            System.Windows.Shapes.Shape.FillProperty,
            state.IsConnected
                ? "Brush.Status.Success"
                : "Brush.Status.Idle");
        ControllerStatusText.Text =
            state.IsConnected
                ? $"手柄已连接 · {state.Backend}"
                : "等待手柄";
        ControllerLiveBadge.Text = state.IsConnected ? "LIVE" : "IDLE";
        ControllerLiveBadge.SetResourceReference(
            TextBlock.ForegroundProperty,
            state.IsConnected
                ? "Brush.Status.Success"
                : "Brush.Status.Idle");

        LeftStickTransform.X = state.LeftX * 8;
        LeftStickTransform.Y = -state.LeftY * 8;
        RightStickTransform.X = state.RightX * 8;
        RightStickTransform.Y = -state.RightY * 8;
        LeftStickHalo.Opacity =
            state.IsConnected &&
            Math.Max(Math.Abs(state.LeftX), Math.Abs(state.LeftY)) >
            _settings.DeadZone
                ? 1
                : 0;
        RightStickHalo.Opacity =
            state.IsConnected &&
            Math.Max(Math.Abs(state.RightX), Math.Abs(state.RightY)) >
            _settings.DeadZone
                ? 1
                : 0;
        ButtonAHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.A) ? 1 : 0;
        ButtonXHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.X) ? 1 : 0;
        ButtonBHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.B) ? 1 : 0;
        ButtonYHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.Y) ? 1 : 0;
        MenuButtonHalo.Opacity =
            state.Buttons.HasFlag(ControllerButtons.Start) ? 1 : 0;
    }

    private async void RefreshCodexData(bool preserveSelection)
    {
        if (!await _dataRefreshGate.WaitAsync(0).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            var snapshot = await Task.Run(_codexDataService.LoadSnapshot)
                .ConfigureAwait(true);
            var selectedId =
                preserveSelection &&
                SidebarList.SelectedItem is SidebarEntry selected
                    ? selected.Id
                    : null;

            _snapshot = snapshot;
            if (string.IsNullOrWhiteSpace(_selectedProjectPath))
            {
                _selectedProjectPath =
                    _snapshot.Projects.FirstOrDefault()?.Path;
            }

            RebuildSidebarEntries();
            if (selectedId is not null)
            {
                var index = _sidebarEntries
                    .Select((entry, index) => new { entry.Id, index })
                    .FirstOrDefault(item =>
                        item.Id.Equals(
                            selectedId,
                            StringComparison.OrdinalIgnoreCase))?.index;
                if (index is not null)
                {
                    _selectedIndex = index.Value;
                    SelectSidebarIndex(_selectedIndex);
                }
            }

            FooterStatusText.Text =
                $"已读取 {_snapshot.Threads.Count} 个任务、{_snapshot.Projects.Count} 个项目" +
                $" · 已过滤 {_snapshot.ArchivedThreadCount} 个归档、{_snapshot.UnavailableThreadCount} 个不可用";
        }
        catch (Exception exception)
        {
            FooterStatusText.Text = "读取 Codex 本机任务失败";
            AddEvent($"数据读取失败 · {exception.Message}");
        }
        finally
        {
            _dataRefreshGate.Release();
        }
    }

    private void RebuildSidebarEntries()
    {
        var entries = _codexDataService.BuildEntries(
                _snapshot,
                _scope,
                _selectedProjectPath)
            .ToList();
        if (
            _scope == SidebarScope.ProjectTasks &&
            _projectTasksPinnedOnly)
        {
            entries = entries
                .Where(entry => entry.IsPinned)
                .ToList();
        }

        if (_scope == SidebarScope.ProjectlessTasks)
        {
            var visibleCount = _codexSidebarService.TryGetBottomTaskCount();
            if (visibleCount is > 0 && entries.Count > visibleCount.Value)
            {
                entries = entries.Take(visibleCount.Value).ToList();
            }
        }

        _sidebarEntries.Clear();
        foreach (var entry in entries)
        {
            _sidebarEntries.Add(entry);
        }

        if (_sidebarEntries.Count == 0)
        {
            _selectedIndex = -1;
            SidebarList.SelectedIndex = -1;
        }
        else
        {
            _selectedIndex = Math.Clamp(
                _selectedIndex,
                0,
                _sidebarEntries.Count - 1);
            SelectSidebarIndex(_selectedIndex);
        }

        SelectedProjectText.Text = _scope switch
        {
            SidebarScope.PinnedTasks => "置顶任务",
            SidebarScope.PinnedProjects => "置顶项目",
            SidebarScope.Projects => "普通项目",
            SidebarScope.ProjectlessTasks => "未归项目任务",
            SidebarScope.ProjectTasks =>
                $"{_snapshot.Projects.FirstOrDefault(project =>
                    string.Equals(
                        project.Path,
                        _selectedProjectPath,
                        StringComparison.OrdinalIgnoreCase))?.Name ??
                  "项目"} › " +
                (_projectTasksPinnedOnly ? "仅置顶任务" : "全部任务"),
            _ => "Codex 侧边栏",
        };
    }

    private void AddEvent(string text)
    {
        _events.Insert(0, new EventRow(DateTime.Now.ToString("HH:mm:ss"), text));
        while (_events.Count > 4)
        {
            _events.RemoveAt(_events.Count - 1);
        }
    }

    private void ShowFeedback(string title, string value)
    {
        if (!_settings.ShowOverlay)
        {
            return;
        }

        _overlayWindow?.ShowMessage(title, value);
    }

    private void Pulse(double strength = 0.22)
    {
        if (_settings.HapticFeedback)
        {
            _xInputService.Pulse(strength);
        }
    }

    private void InitializeComposerControls()
    {
        _composerCatalog = _codexComposerService.LoadCatalog();
        _modelIndex = Math.Clamp(
            _composerCatalog.InitialModelIndex,
            0,
            Math.Max(0, _composerCatalog.Models.Count - 1));
        _committedModelIndex = _modelIndex;

        var efforts = CurrentEfforts();
        _reasoningIndex = FindValueIndex(
            efforts,
            _composerCatalog.InitialEffort);
        _committedReasoningIndex = _reasoningIndex;
        _speedIndex =
            string.Equals(
                _composerCatalog.InitialSpeed,
                "Fast",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        _committedSpeedIndex = _speedIndex;
        UpdateRightModeUi();
    }

    private IReadOnlyList<string> CurrentEfforts()
    {
        return _composerCatalog?.EffortsForModel(_modelIndex)
               ?? ["Light", "Medium", "High", "Extra High", "Max", "Ultra"];
    }

    private static int FindValueIndex(
        IReadOnlyList<string> values,
        string target)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(
                    values[index],
                    target,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    private void UpdateRightModeUi()
    {
        SetModeTab(ReasoningModeTab, _rightMode == RightControlMode.Reasoning);
        SetModeTab(ModelModeTab, _rightMode == RightControlMode.Model);
        SetModeTab(SpeedModeTab, _rightMode == RightControlMode.Speed);
        RightModeLabel.Text = ModeLabel(_rightMode);
        RightModeValue.Text = _rightMode switch
        {
            RightControlMode.Reasoning =>
                CurrentEfforts()[Math.Clamp(
                    _reasoningIndex,
                    0,
                    CurrentEfforts().Count - 1)],
            RightControlMode.Model =>
                _composerCatalog is not null &&
                _composerCatalog.Models.Count > 0
                    ? _composerCatalog.Models[Math.Clamp(
                        _modelIndex,
                        0,
                        _composerCatalog.Models.Count - 1)].DisplayName
                    : "等待 Codex",
            RightControlMode.Speed => SpeedLabel(_speedIndex),
            _ => string.Empty,
        };
    }

    private static string SpeedLabel(int index)
    {
        return index > 0 ? "Fast" : "Standard";
    }

    private static string ComposerKindLabel(ComposerSettingKind kind)
    {
        return kind switch
        {
            ComposerSettingKind.Model => "模型",
            ComposerSettingKind.Effort => "思考强度",
            ComposerSettingKind.Speed => "速度",
            _ => string.Empty,
        };
    }

    private void UpdateLayerTabs()
    {
        var activeScope = _scope;
        if (_scope == SidebarScope.ProjectTasks)
        {
            activeScope = _snapshot.Projects.FirstOrDefault(project =>
                    string.Equals(
                        project.Path,
                        _selectedProjectPath,
                        StringComparison.OrdinalIgnoreCase))?.IsPinned == true
                ? SidebarScope.PinnedProjects
                : SidebarScope.Projects;
        }

        SetLayerTab(
            PinnedLayerButton,
            activeScope == SidebarScope.PinnedTasks);
        SetLayerTab(
            PinnedProjectsLayerButton,
            activeScope == SidebarScope.PinnedProjects);
        SetLayerTab(
            ProjectsLayerButton,
            activeScope == SidebarScope.Projects);
        SetLayerTab(
            TasksLayerButton,
            activeScope == SidebarScope.ProjectlessTasks);
    }

    private static void SetModeTab(
        System.Windows.Controls.RadioButton button,
        bool active)
    {
        button.IsChecked = active;
    }

    private static void SetLayerTab(
        System.Windows.Controls.RadioButton button,
        bool active)
    {
        button.IsChecked = active;
    }

    private void ApplySettingsToControls()
    {
        BridgeEnabledCheckBox.IsChecked = _settings.BridgeEnabled;
        OnlyForegroundCheckBox.IsChecked = _settings.OnlyWhenCodexForeground;
        HapticFeedbackCheckBox.IsChecked = _settings.HapticFeedback;
        ShowOverlayCheckBox.IsChecked = _settings.ShowOverlay;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTray;
        DeadZoneSlider.Value = _settings.DeadZone;
        RepeatDelaySlider.Value = _settings.RepeatDelayMs;
        RepeatIntervalSlider.Value = _settings.RepeatIntervalMs;

        ReasoningDownTextBox.Text = _settings.ReasoningDownShortcut;
        ReasoningUpTextBox.Text = _settings.ReasoningUpShortcut;
        ModelPickerTextBox.Text = _settings.ModelPickerShortcut;
        FastToggleTextBox.Text = _settings.FastToggleShortcut;
        DictationTextBox.Text = _settings.DictationShortcut;
        SubmitTextBox.Text = _settings.SubmitShortcut;
    }

    private void ReadControlsIntoSettings()
    {
        _settings.BridgeEnabled = BridgeEnabledCheckBox.IsChecked == true;
        _settings.OnlyWhenCodexForeground =
            OnlyForegroundCheckBox.IsChecked == true;
        _settings.HapticFeedback = HapticFeedbackCheckBox.IsChecked == true;
        _settings.ShowOverlay = ShowOverlayCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
        _settings.DeadZone = Math.Round(DeadZoneSlider.Value, 2);
        _settings.RepeatDelayMs = (int)Math.Round(RepeatDelaySlider.Value);
        _settings.RepeatIntervalMs =
            (int)Math.Round(RepeatIntervalSlider.Value);

        _settings.ReasoningDownShortcut = ReasoningDownTextBox.Text.Trim();
        _settings.ReasoningUpShortcut = ReasoningUpTextBox.Text.Trim();
        _settings.ModelPickerShortcut = ModelPickerTextBox.Text.Trim();
        _settings.FastToggleShortcut = FastToggleTextBox.Text.Trim();
        _settings.DictationShortcut = DictationTextBox.Text.Trim();
        _settings.SubmitShortcut = SubmitTextBox.Text.Trim();
    }

    private void SaveSettings(string eventText)
    {
        ReadControlsIntoSettings();
        _settingsService.Save(_settings);
        ConfigureCodexKeybindings();
        AddEvent(eventText);
        FooterStatusText.Text = eventText;
    }

    private void UpdateCodexStatus()
    {
        var foreground = _codexCommandService.IsCodexForeground;
        ObserveCodexForeground(foreground);
        if (
            _controllerSession.IsArmed &&
            _settings.OnlyWhenCodexForeground &&
            !foreground)
        {
            PauseControllerInput(_xInputService.LastState);
        }

        if (
            _controllerSession.IsArmed &&
            _controllerWasConnected &&
            (!_settings.OnlyWhenCodexForeground || foreground))
        {
            _ = TryResumeControllerInput(_xInputService.LastState);
        }

        CodexForegroundText.Text =
            !_controllerWasConnected
                ? "等待手柄重新连接"
                : foreground
                    ? !_controllerSession.IsArmed
                        ? "Codex 位于前台 · 按 Menu 解锁"
                        : !_controllerSession.IsActive
                            ? "Codex 位于前台 · 松开按键后恢复"
                            : "Codex 位于前台 · 已解锁"
                    : !_settings.OnlyWhenCodexForeground
                        ? _controllerSession.IsArmed
                            ? "后台控制 · 已解锁"
                            : "后台控制 · 按 Menu 解锁"
                        : _controllerSession.IsArmed
                            ? "Codex 暂离前台 · 控制已暂停"
                            : "Codex 未在前台 · 按 Menu 唤醒";
        CodexForegroundText.SetResourceReference(
            TextBlock.ForegroundProperty,
            _controllerSession.IsArmed &&
            _controllerWasConnected &&
            _controllerSession.IsActive &&
            (!_settings.OnlyWhenCodexForeground || foreground)
                ? "Brush.Status.Success"
                : "Brush.Text.Secondary");
    }

    private void ShowPage(FrameworkElement page)
    {
        DevicePage.Visibility =
            page == DevicePage ? Visibility.Visible : Visibility.Collapsed;
        ConfigPage.Visibility =
            page == ConfigPage ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility =
            page == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void SetSelectedNav(
        System.Windows.Controls.RadioButton selected)
    {
        selected.IsChecked = true;
    }

    private static string ScopeLabel(SidebarScope scope)
    {
        return scope switch
        {
            SidebarScope.PinnedTasks => "置顶任务",
            SidebarScope.PinnedProjects => "置顶项目",
            SidebarScope.Projects => "普通项目",
            SidebarScope.ProjectTasks => "项目任务",
            SidebarScope.ProjectlessTasks => "未归项目任务",
            _ => string.Empty,
        };
    }

    private static string ModeLabel(RightControlMode mode)
    {
        return mode switch
        {
            RightControlMode.Reasoning => "思考强度",
            RightControlMode.Model => "模型",
            RightControlMode.Speed => "速度",
            _ => string.Empty,
        };
    }

    private static string Arrow(int direction, bool horizontal)
    {
        return horizontal
            ? direction > 0 ? "→" : "←"
            : direction > 0 ? "↑" : "↓";
    }

    private string ExecutionSuffix(
        bool executed,
        string? error = null)
    {
        if (executed)
        {
            return " · 已执行";
        }

        return $" · {ExecutionFailureLabel(error)}";
    }

    private string ExecutionFailureLabel(string? error)
    {
        if (!_settings.BridgeEnabled)
        {
            return "安全预览";
        }

        if (
            _settings.OnlyWhenCodexForeground &&
            !_codexCommandService.IsCodexForeground)
        {
            return "等待 Codex 前台";
        }

        return string.IsNullOrWhiteSpace(error)
            ? "未执行"
            : error;
    }

    private void SetupTrayIcon()
    {
        _trayIconImage = LoadTrayIcon();
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Agent Controller",
            Icon = _trayIconImage ?? System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 Agent Controller", null, (_, _) => RestoreWindow());
        menu.Items.Add("打开 Codex", null, (_, _) =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "codex://settings",
                UseShellExecute = true,
            });
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        _trayIcon.ContextMenuStrip = menu;
    }

    private static System.Drawing.Icon? LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/AgentController.ico"));
        if (resource is null)
        {
            return null;
        }

        using (resource.Stream)
        using (var icon = new System.Drawing.Icon(resource.Stream))
        {
            return (System.Drawing.Icon)icon.Clone();
        }
    }

    private void RestoreWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _trayIconImage?.Dispose();
        _trayIconImage = null;
        _overlayWindow?.Close();
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void DeviceNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(DevicePage);
        SetSelectedNav(DeviceNavButton);
    }

    private void ConfigNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(ConfigPage);
        SetSelectedNav(ConfigNavButton);
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowPage(SettingsPage);
        SetSelectedNav(SettingsNavButton);
    }

    private void BridgeEnabledCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (_initializing)
        {
            return;
        }

        _settings.BridgeEnabled = BridgeEnabledCheckBox.IsChecked == true;
        _settingsService.Save(_settings);
        ConfigureCodexKeybindings();
        AddEvent(
            _settings.BridgeEnabled
                ? "桥接已启用"
                : "桥接已切换为安全预览");
    }

    private void RefreshDataButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshCodexData(preserveSelection: true);
        AddEvent("已刷新 Codex 本机任务");
    }

    private void PinnedLayerButton_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarScope(SidebarScope.PinnedTasks, showFeedback: false);
    }

    private void PinnedProjectsLayerButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetSidebarScope(SidebarScope.PinnedProjects, showFeedback: false);
    }

    private void ProjectsLayerButton_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarScope(SidebarScope.Projects, showFeedback: false);
    }

    private void TasksLayerButton_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarScope(SidebarScope.ProjectlessTasks, showFeedback: false);
    }

    private void SidebarList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (
            _suppressSelectionActivation ||
            SidebarList.SelectedItem is not SidebarEntry entry)
        {
            return;
        }

        _selectedIndex = SidebarList.SelectedIndex;
        ActivateSelectedEntry(entry);
    }

    private void SidebarList_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        EnterSelectedSidebarEntry();
        e.Handled = true;
    }

    private void SidebarList_PreviewKeyDown(
        object sender,
        System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Right)
        {
            EnterSelectedSidebarEntry();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            NavigateSidebarHorizontal(-1);
            e.Handled = true;
        }
    }

    private void OpenCodexShortcutsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CodexCommandService.OpenCodexKeyboardShortcuts();
        AddEvent("已打开 Codex 快捷键设置");
    }

    private void SaveBindingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings("快捷键配置已保存");
    }

    private void ResetBindingsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        ReasoningDownTextBox.Text = defaults.ReasoningDownShortcut;
        ReasoningUpTextBox.Text = defaults.ReasoningUpShortcut;
        ModelPickerTextBox.Text = defaults.ModelPickerShortcut;
        FastToggleTextBox.Text = defaults.FastToggleShortcut;
        DictationTextBox.Text = defaults.DictationShortcut;
        SubmitTextBox.Text = defaults.SubmitShortcut;
    }

    private void DeadZoneSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (DeadZoneValueText is not null)
        {
            DeadZoneValueText.Text = e.NewValue.ToString("0.00");
        }
    }

    private void RepeatDelaySlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (RepeatDelayValueText is not null)
        {
            RepeatDelayValueText.Text = $"{e.NewValue:0} ms";
        }
    }

    private void RepeatIntervalSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (RepeatIntervalValueText is not null)
        {
            RepeatIntervalValueText.Text = $"{e.NewValue:0} ms";
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings("设置已保存");
    }

    private void OpenUltimateSoftwareButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CodexCommandService.OpenUltimateSoftware();
    }

    private void OpenCodexSettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        CodexCommandService.OpenCodexSettings();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (
            WindowState == WindowState.Minimized &&
            _settings.MinimizeToTray)
        {
            ShowInTaskbar = false;
            Hide();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            ShowInTaskbar = false;
            Hide();
            AddEvent("窗口已隐藏，桥接继续在后台运行");
            return;
        }

        _statusTimer.Stop();
        _dataTimer.Stop();
        CancelPendingSidebarFocus();
        CancelPendingComposerSelection();
        _dictationStopCancellation?.Cancel();
        _dictationStopCancellation = null;
        ClearNavigationUndo();
        _xInputService.StateChanged -= XInputService_StateChanged;
        _trayIcon?.Dispose();
        _trayIconImage?.Dispose();
        _overlayWindow?.Close();

        if (!_exitRequested)
        {
            _exitRequested = true;
            _ = Dispatcher.BeginInvoke(
                System.Windows.Application.Current.Shutdown);
        }
    }

    private sealed class NavigationUndoState
    {
        public NavigationUndoState(
            string targetDisplayTitle,
            string targetNativeTitle,
            string? previousTitle)
        {
            TargetDisplayTitle = targetDisplayTitle;
            TargetNativeTitle = targetNativeTitle;
            PreviousTitle = previousTitle;
        }

        public string TargetDisplayTitle { get; }
        public string TargetNativeTitle { get; }
        public string? PreviousTitle { get; }
        public bool Confirmed { get; set; }
        public bool UndoRequested { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private sealed record SidebarReturnFrame(
        SidebarScope Scope,
        string? EntryId,
        string? ProjectPath);

    private sealed record EventRow(string Time, string Text);
}
