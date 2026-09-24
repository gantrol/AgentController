using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexController.Models;
using CodexMicro.Desktop.Services;

namespace CodexMicro.Desktop;

public partial class MicroSurfaceWindow
{
    private sealed record MonitorTask(
        string HarnessId, string Id, string Title, MicroHarnessSessionStatus? Status);

    private readonly CodexTaskMonitorService _taskMonitor = new();
    private readonly DispatcherTimer _monitorRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2),
    };
    private const int MonitorTaskCapacity = CodexTaskMonitorService.Capacity - 2;
    private readonly Grid _sharedPageControls = new()
    {
        Width = 424,
        Height = 424,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button[] _monitorKeys = new Button[MonitorTaskCapacity];
    private readonly Border[] _monitorWideGlows = new Border[MonitorTaskCapacity];
    private readonly Border[] _monitorNearGlows = new Border[MonitorTaskCapacity];
    private IReadOnlyList<CodexMonitoredTask>? _monitoredTasks;
    private CancellationTokenSource? _monitorRefreshCancellation;
    private string? _monitorHarnessId;
    private bool _monitorPage;
    private bool _monitorAvailable;
    private bool _monitorRefreshRequested;
    private bool _pageSwitching;
    private bool _monitorOpening;
    private (string HarnessId, string Id, string Message)? _monitorOpenFailure;

    private void InitializeMonitorPage()
    {
        for (var dimension = 0; dimension < 4; dimension++)
        {
            MonitorGrid.RowDefinitions.Add(new() { Height = new GridLength(106) });
            MonitorGrid.ColumnDefinitions.Add(new() { Width = new GridLength(106) });
            _sharedPageControls.RowDefinitions.Add(new() { Height = new GridLength(106) });
            _sharedPageControls.ColumnDefinitions.Add(new() { Width = new GridLength(106) });
        }

        for (var index = 0; index < _monitorKeys.Length; index++)
        {
            var wide = new Border { Style = (Style)FindResource("AgentWideHalo") };
            var near = new Border { Style = (Style)FindResource("AgentNearHalo") };
            var key = new Button
            {
                Width = 96,
                Height = 96,
                Margin = new Thickness(5),
                Style = (Style)FindResource("AgentKey"),
                Focusable = false,
                IsEnabled = false,
            };
            key.Click += MonitorKey_Click;
            key.PreviewMouseRightButtonDown += MonitorKey_PreviewMouseRightButtonDown;
            ToolTipService.SetShowOnDisabled(key, true);
            foreach (var element in new FrameworkElement[] { wide, near, key })
            {
                var cell = index < 12 ? index : index + 1;
                Grid.SetRow(element, cell / 4);
                Grid.SetColumn(element, cell % 4);
                MonitorGrid.Children.Add(element);
            }

            Panel.SetZIndex(wide, -10);
            Panel.SetZIndex(near, -9);
            _monitorKeys[index] = key;
            _monitorWideGlows[index] = wide;
            _monitorNearGlows[index] = near;
        }

        foreach (var control in new FrameworkElement[] { ModelKnob, ActionKey12 })
        {
            ControlGrid.Children.Remove(control);
            _sharedPageControls.Children.Add(control);
        }
        ((Panel)ControlGrid.Parent).Children.Add(_sharedPageControls);

        _monitorRefreshTimer.Tick += MonitorRefreshTimer_Tick;
        MonitorGrid.MouseLeave += (_, _) => RefreshMonitorPresentation();
        RefreshPageHelp();
    }

    private void RefreshPageHelp()
    {
        var controls = _localization.IsEnglish ? "Controls" : "控制页";
        var monitor = _localization.IsEnglish ? "14 tasks" : "14 个任务";
        ControlPageButton.ToolTip = controls;
        MonitorPageButton.ToolTip = monitor;
        AutomationProperties.SetName(ControlPageButton, controls);
        AutomationProperties.SetName(MonitorPageButton, monitor);
    }

    private void MonitorRefreshTimer_Tick(object? sender, EventArgs e) =>
        _ = RefreshMonitorAsync();

    private void UpdateMonitorRefresh()
    {
        if (_windowClosed || !IsLoaded || !IsVisible ||
            (!IsCodexHarnessActive() && !_monitorPage))
        {
            _monitorRefreshTimer.Stop();
            _monitorRefreshCancellation?.Cancel();
            if (_windowClosed || !IsLoaded || !IsVisible)
            {
                _pageMotionCancellation?.Cancel();
            }
            return;
        }

        var harnessId = ActiveHarness().Id;
        if (_monitorHarnessId != harnessId)
        {
            _monitorRefreshCancellation?.Cancel();
            _monitorHarnessId = harnessId;
            _monitorAvailable = false;
            foreach (var key in _monitorKeys)
            {
                key.Tag = null;
            }
        }

        RefreshPageHelp();
        RefreshMonitorPresentation();
        _monitorRefreshTimer.Start();
        _ = RefreshMonitorAsync();
    }

    private async Task RefreshMonitorAsync()
    {
        if (_windowClosed || !IsLoaded || !IsVisible)
        {
            return;
        }

        if (!IsCodexHarnessActive())
        {
            RefreshMonitorPresentation();
            return;
        }

        if (_monitorRefreshCancellation is not null)
        {
            _monitorRefreshRequested = true;
            return;
        }

        _monitorRefreshRequested = false;
        using var cancellation = new CancellationTokenSource();
        _monitorRefreshCancellation = cancellation;
        try
        {
            var snapshot = await _taskMonitor.ReadAsync(cancellation.Token);
            if (_windowClosed || cancellation.IsCancellationRequested ||
                !IsVisible || !IsCodexHarnessActive())
            {
                return;
            }

            _monitorAvailable = snapshot is not null;
            if (snapshot is not null)
            {
                _monitoredTasks = snapshot.Tasks;
                _latestAgentRoster = snapshot.AgentRoster;
                foreach (var task in snapshot.Tasks)
                {
                    if (task.Status == ThreadStatus.CompleteUnread)
                    {
                        _manualUnreadThreads.ClearConfirmed(task.Id);
                    }
                }
            }

            ResolveCurrentAgentSlot();
            RefreshAgentSlotPresentation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _monitorAvailable = false;
            Debug.WriteLine($"Codex task monitor: {exception.Message}");
            RefreshAgentSlotPresentation();
        }
        finally
        {
            _monitorRefreshCancellation = null;
            if (_monitorRefreshRequested)
            {
                _monitorRefreshRequested = false;
                _ = RefreshMonitorAsync();
            }
        }
    }

    private void RefreshMonitorPresentation()
    {
        if (!_monitorPage || _windowClosed)
        {
            return;
        }

        var harness = ActiveHarness();
        var codex = harness.Id == "codex";
        var available = codex ? _monitorAvailable :
            _harnessStateSnapshot?.HarnessId == harness.Id;
        var currentId = codex ? CurrentCodexAgentThreadId() :
            _harnessStateSnapshot?.CurrentSessionId;
        var tasks = codex
            ? (_monitoredTasks ?? []).Select(task => new MonitorTask(
                harness.Id, task.Id, task.Title, ResolveMonitoredTaskStatus(task.Status))).ToArray()
            : (_harnessStateSnapshot?.HarnessId == harness.Id
                ? _harnessStateSnapshot.Sessions : [])
                .Take(CodexTaskMonitorService.Capacity)
                .Select(task => new MonitorTask(
                    harness.Id, task.Id, task.DisplayTitle, task.Status)).ToArray();
        tasks = tasks.DistinctBy(task => task.Id, StringComparer.Ordinal).ToArray();
        var byId = tasks.ToDictionary(task => task.Id, StringComparer.Ordinal);
        // Restore row-major recency order when the pointer leaves the keys.
        // Never replace the identity of a hovered or captured key.
        var freezeAssignments = _pageMotionActive || _monitorOpening ||
            _monitorKeys.Any(key => key.Tag is not null &&
                (key.IsMouseOver || key.IsMouseCaptured));
        for (var index = 0; index < _monitorKeys.Length; index++)
        {
            var key = _monitorKeys[index];
            if (!freezeAssignments)
            {
                key.Tag = index < tasks.Length ? tasks[index] : null;
            }
            else if (key.Tag is MonitorTask old && old.HarnessId == harness.Id &&
                byId.TryGetValue(old.Id, out var updated))
            {
                key.Tag = updated;
            }

            var task = key.Tag as MonitorTask;
            var fresh = available && task is not null &&
                task.HarnessId == harness.Id && byId.ContainsKey(task.Id);
            var status = task?.Status;
            if (fresh && codex &&
                status is null or MicroHarnessSessionStatus.Idle &&
                _manualUnreadThreads.IsUnread(task!.Id))
            {
                status = MicroHarnessSessionStatus.Completed;
            }

            var appearance = fresh && codex
                ? ResolveMonitoredCodexAppearance(task!.Id, status, task.Id == currentId)
                : fresh && status is { } knownStatus
                    ? AgentLightingAppearance.FromHarnessSession(knownStatus, task!.Id == currentId)
                    : AgentLightingAppearance.From(null);
            var state = !available
                ? (_localization.IsEnglish ? "Status unavailable" : "状态暂不可用")
                : task is null
                    ? (_localization.IsEnglish ? "Unassigned" : "未分配")
                    : !fresh || status is null
                        ? (_localization.IsEnglish ? "Status unknown" : "状态未知")
                        : codex && status == MicroHarnessSessionStatus.Running
                            ? (_localization.IsEnglish ? "Turn open (may be waiting for input)" : "任务进行中（可能在等待输入）")
                            : codex && status == MicroHarnessSessionStatus.Completed
                                ? (_localization.IsEnglish ? "Unread" : "未读")
                                : Localize(appearance.StatusName);

            key.IsEnabled = fresh && !_monitorOpening &&
                (codex || !IsHarnessMenuNavigationActive(harness));
            key.Opacity = task is null ? 0.42 : fresh ? 1 : 0.58;
            appearance = ApplyAgentLightingAppearance(key, appearance);
            SetTemplatePartOpacity(key, "GlowWide", 0);
            SetTemplatePartOpacity(key, "Glow", 0);
            ApplyAgentGlowAppearance(
                _monitorWideGlows[index],
                _monitorNearGlows[index],
                key.BorderBrush,
                appearance);
            key.ToolTip = task is null ? state : $"{task.Title}\n{state}";
            if (task is not null && _monitorOpenFailure is { } failure &&
                failure.HarnessId == task.HarnessId && failure.Id == task.Id)
            {
                key.ToolTip = $"{task.Title}\n{failure.Message}";
                state = failure.Message;
            }
            AutomationProperties.SetName(key, task?.Title ?? $"{index + 1}");
            AutomationProperties.SetItemStatus(key, state);
        }
    }

    private static MicroHarnessSessionStatus? ResolveMonitoredTaskStatus(ThreadStatus status) =>
        status switch
        {
            ThreadStatus.Thinking => MicroHarnessSessionStatus.Running,
            ThreadStatus.CompleteUnread => MicroHarnessSessionStatus.Completed,
            ThreadStatus.Idle => MicroHarnessSessionStatus.Idle,
            ThreadStatus.RequiresInput => MicroHarnessSessionStatus.WaitingForInput,
            ThreadStatus.Error => MicroHarnessSessionStatus.Error,
            _ => null,
        };

    private AgentLightingAppearance ResolveMonitoredCodexAppearance(
        string threadId,
        MicroHarnessSessionStatus? status,
        bool isCurrentSession)
    {
        // The rollout marks an open turn; Micro can additionally identify a
        // pending request. Apply that detail to the same task on either page.
        if (status == MicroHarnessSessionStatus.Running &&
            _latestAgentRoster?.Entries.FirstOrDefault(entry => entry.ThreadId == threadId)
                is { } entry &&
            _latestSlotLighting?.Slots.FirstOrDefault(slot => slot.SlotId == entry.SlotId)
                is { Color: 0xFF6D00 } lighting &&
            AgentLightingAppearance.From(lighting).IsActive)
        {
            status = MicroHarnessSessionStatus.WaitingForInput;
        }

        return status is null or MicroHarnessSessionStatus.Idle &&
            _manualUnreadThreads.IsUnread(threadId)
            ? AgentLightingAppearance.ManualUnread(isCurrentSession)
            : AgentLightingAppearance.FromCodexSession(status, isCurrentSession);
    }

    private string? CurrentCodexAgentThreadId()
    {
        // A known foreground renderer with no thread is a draft/navigation
        // state. The service already handles the no-foreground fallback;
        // falling back again here would select another window's task.
        var threadId = _modelToggleService.CurrentForegroundVisibleThreadId(
            CodexWindowActivator.CaptureForegroundWindow());
        return CodexDraftModelToggleService.IsDraftThreadId(threadId)
            ? null
            : threadId;
    }

    private void OpenCodexTask(Guid id)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = $"codex://threads/{id:D}",
            UseShellExecute = true,
        })?.Dispose();
        var threadId = id.ToString("D");
        _manualUnreadThreads.Clear(threadId);
        _currentAgentSlotId = _latestAgentRoster?.Entries
            .FirstOrDefault(entry => entry.ThreadId == threadId)?.SlotId;
        RefreshAgentSlotPresentation();
    }

    private async void MonitorKey_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!IsCodexHarnessActive() ||
            sender is not Button { Tag: MonitorTask { HarnessId: "codex" } task })
        {
            return;
        }

        e.Handled = true;
        if (_monitorOpening || !_monitorAvailable)
        {
            return;
        }

        await MarkCodexThreadUnreadAsync(
            task.Id,
            task.Title,
            task.Status is not null and not MicroHarnessSessionStatus.Idle);
    }

    private async void MonitorKey_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorOpening || sender is not Button { Tag: MonitorTask task } ||
            task.HarnessId != ActiveHarness().Id)
        {
            return;
        }

        _monitorOpening = true;
        _monitorOpenFailure = null;
        RefreshMonitorPresentation();
        try
        {
            if (task.HarnessId == "codex")
            {
                if (!Guid.TryParse(task.Id, out var id))
                {
                    return;
                }

                OpenCodexTask(id);
            }
            else
            {
                var result = await _harnessRegistry.ActivateSessionAsync(task.HarnessId, task.Id);
                if (!_windowClosed && ActiveHarness().Id == task.HarnessId)
                {
                    if (!result.Success)
                    {
                        _monitorOpenFailure = (task.HarnessId, task.Id, result.Message);
                    }
                    SetStatus(result.Message);
                    await RefreshHarnessStateAsync();
                }
            }
        }
        catch (Exception exception)
        {
            if (!_windowClosed)
            {
                SetStatus(exception.Message);
                _monitorOpenFailure = (task.HarnessId, task.Id, exception.Message);
            }
        }
        finally
        {
            _monitorOpening = false;
            RefreshMonitorPresentation();
        }
    }

    private void StopMonitorPage()
    {
        _pageMotionCancellation?.Cancel();
        _monitorRefreshTimer.Stop();
        _monitorRefreshTimer.Tick -= MonitorRefreshTimer_Tick;
        _monitorRefreshCancellation?.Cancel();
    }
}
