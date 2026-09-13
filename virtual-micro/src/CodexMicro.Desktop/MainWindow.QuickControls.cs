using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CodexMicro.Desktop.Services;

namespace CodexMicro.Desktop;

public partial class MicroSurfaceWindow
{
    private sealed record MonitorComposerTarget(
        string ThreadId, IntPtr Window, string? ModelId,
        CodexModelToggleService.ForegroundDraftLease? Draft = null);

    private bool _monitorQuickReady;
    private bool _monitorQuickBusy;
    private ContextMenu? _monitorEffortMenu;
    private MonitorComposerTarget? _monitorQuickTarget;
    private CancellationTokenSource? _monitorQuickCancellation;
    private CancellationTokenSource? _monitorDraftReadCancellation;
    private CodexModelCatalog? _monitorModelCatalog;
    private (string Id, CodexComposerQuickSelection Selection)? _monitorDraftSelection;
    private DateTimeOffset _monitorLastDraftRead;
    private string? _monitorQuickError;
    private string? _monitorQuickContext;

    private void InitializeMonitorQuickControls()
    {
        _monitorQuickReady = true;
        foreach (var button in new[] { MonitorModelButton, MonitorEffortButton,
                     MonitorWeekQuotaButton, MonitorShortQuotaButton })
        {
            ToolTipService.SetShowOnDisabled(button, true);
        }
        UpdateMonitorQuickControls();
    }

    private MonitorComposerTarget? CaptureMonitorComposerTarget()
    {
        if (!IsLoaded || !IsVisible || !_monitorPage || !IsCodexHarnessActive())
        {
            return null;
        }
        var window = CodexWindowActivator.CaptureForegroundWindow();
        var id = _modelToggleService.CurrentForegroundVisibleThreadId(window);
        var state = _modelToggleService.CurrentThreadState;
        if (id is not null && state?.ThreadId == id &&
            !CodexDraftModelToggleService.IsDraftThreadId(id))
        {
            return new(id, window, state.ModelId);
        }
        var lease = window == IntPtr.Zero ? null
            : _modelToggleService.TryCaptureForegroundDraftLeaseForReasoningStep(window);
        return lease is { } draft
            ? new(draft.OperationId, window,
                _monitorDraftSelection is { } read && read.Id == draft.OperationId
                    ? read.Selection.ModelId : null, draft)
            : null;
    }

    private bool IsMonitorComposerTargetCurrent(MonitorComposerTarget target)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            {
                return false;
            }
            try
            {
                return Dispatcher.Invoke(() => IsMonitorComposerTargetCurrent(target));
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        if (_windowClosed || !IsVisible || !_monitorPage || _pageSwitching ||
            !IsCodexHarnessActive())
        {
            return false;
        }
        if (target.Draft is { } draft)
        {
            return _modelToggleService.IsForegroundDraftLeaseCurrent(draft);
        }
        var state = _modelToggleService.CurrentThreadState;
        return _modelToggleService.CurrentForegroundVisibleThreadId(target.Window) == target.ThreadId &&
            state?.ThreadId == target.ThreadId && state.ModelId == target.ModelId;
    }

    private void UpdateMonitorQuickControls()
    {
        if (!_monitorQuickReady || _windowClosed)
        {
            return;
        }
        var english = _localization.IsEnglish;
        var codex = IsCodexHarnessActive();
        var harness = ActiveHarness();
        if (_monitorModelCatalog is not { IsFresh: true })
        {
            _monitorModelCatalog = CodexModelCatalog.Load();
            if (!_monitorModelCatalog.IsFresh)
            {
                _monitorDraftSelection = null;
            }
        }
        var target = CaptureMonitorComposerTarget();
        var context = $"{harness.Id}:{target?.ThreadId ?? CurrentHarnessSessionId()}";
        if (_monitorQuickContext != context)
        {
            _monitorQuickContext = context;
            _monitorQuickError = null;
        }
        if (_monitorQuickTarget is { } pending && !IsMonitorComposerTargetCurrent(pending))
        {
            _monitorQuickCancellation?.Cancel();
            CloseMonitorEffortMenu();
        }

        var state = _modelToggleService.CurrentThreadState;
        var modelId = target?.ModelId;
        var effort = target?.Draft is null && state?.ThreadId == target?.ThreadId
            ? state?.Effort
            : _monitorDraftSelection is { } draftRead && draftRead.Id == target?.ThreadId
                ? draftRead.Selection.Effort : null;
        if (codex && modelId is null && !string.IsNullOrWhiteSpace(_quickModelThreadId) &&
            _quickModel != CodexQuickModel.Unknown &&
            _quickModelThreadId == _modelToggleService.CurrentForegroundVisibleThreadId(
                CodexWindowActivator.CaptureForegroundWindow()))
        {
            modelId = _quickModel.Id;
        }
        var external = _harnessStateSnapshot?.HarnessId == harness.Id
            ? _harnessStateSnapshot : null;
        var modelLabel = codex
            ? modelId is null ? "—" : _monitorModelCatalog.Find(modelId)?.Label
                ?? CodexModelCatalog.ModelLabel(modelId)
            : external?.Components?.CurrentModel ?? "—";
        var busy = _monitorQuickBusy || _quickModelSwitching || _harnessModelSwitching;
        var available = IsLoaded && IsVisible && _monitorPage && !_pageSwitching && !busy &&
            !_monitorOpening && !_voicePressed;
        var externalAvailable = external is { NavigationDepth: 0 } &&
            !string.IsNullOrWhiteSpace(CurrentHarnessSessionId());
        MonitorModelValue.Text = modelLabel;
        MonitorEffortValue.Text = busy ? "…" : QuickEffortLabel(effort, english);
        MonitorModelButton.IsEnabled = available && (codex ||
            externalAvailable &&
            external?.Capabilities.Supports(MicroHarnessActionIds.ToggleQuickModel) == true);
        MonitorEffortButton.IsEnabled = available && (codex ? target is not null :
            externalAvailable &&
            (external?.Capabilities.Supports(MicroHarnessActionIds.ReasoningDecrease) == true ||
             external?.Capabilities.Supports(MicroHarnessActionIds.ReasoningIncrease) == true));

        var pair = _profileSettings.Current;
        var modelHelp = codex
            ? $"{modelLabel}\n{(english ? "Switch models" : "切换模型")} · " +
                $"{CodexModelCatalog.ShortLabel(pair.QuickModelA.Id)} ↔ " +
                CodexModelCatalog.ShortLabel(pair.QuickModelB.Id)
            : $"{modelLabel}\n{(english ? "Switch models" : "切换模型")}";
        var effortHelp = target is null && codex
            ? (english ? "Open a Codex task to choose its reasoning effort." : "打开 Codex 任务后选择思考档位。")
            : (english ? "Choose reasoning effort for this task" : "选择当前任务的思考档位");
        SetMonitorHelp(MonitorModelButton, modelHelp);
        SetMonitorHelp(MonitorEffortButton, effortHelp +
            (_monitorQuickError is null ? string.Empty : $"\n{_monitorQuickError}"));
        AutomationProperties.SetItemStatus(MonitorEffortButton,
            _monitorQuickError ?? MonitorEffortValue.Text);

        UpdateMonitorQuota(codex, english);
        if (available && target?.Draft is not null && _monitorEffortMenu is null)
        {
            _ = RefreshMonitorDraftSelectionAsync(target);
        }
    }

    private void UpdateMonitorQuota(bool codex, bool english)
    {
        MonitorQuotaRow.Visibility = codex ? Visibility.Visible : Visibility.Collapsed;
        if (!codex)
        {
            return;
        }
        var windows = _quotaSnapshot?.Windows ?? [];
        var week = windows.FirstOrDefault(window => window.WindowDurationMinutes == 10080);
        var shortWindow = windows.FirstOrDefault(window => window.WindowDurationMinutes == 300);
        // A provider can expose a different rolling window. Label that actual
        // duration instead of silently presenting it as a weekly allowance.
        var first = week ?? windows.OrderByDescending(window => window.WindowDurationMinutes).FirstOrDefault();
        var second = shortWindow != first ? shortWindow : null;
        if (second is null && first is not null)
        {
            second = windows.FirstOrDefault(window => window != first);
        }
        var stale = _quotaSnapshot is { } snapshot && (_quotaRefreshFailed ||
            DateTimeOffset.Now - snapshot.ReadAt > TimeSpan.FromMinutes(4));
        ApplyMonitorQuotaWindow(MonitorWeekQuotaButton, MonitorWeekQuotaText,
            MonitorWeekQuotaBar, first, stale, english);
        ApplyMonitorQuotaWindow(MonitorShortQuotaButton, MonitorShortQuotaText,
            MonitorShortQuotaBar, second, stale, english);
        MonitorShortQuotaButton.Visibility = second is null ? Visibility.Collapsed : Visibility.Visible;
        MonitorShortQuotaColumn.Width = second is null ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MonitorQuotaGap.Width = second is null ? new GridLength(0) : new GridLength(16);
    }

    private void ApplyMonitorQuotaWindow(Button button, TextBlock text, ProgressBar bar,
        CodexQuotaWindow? window, bool stale, bool english)
    {
        var duration = window?.WindowDurationMinutes switch
        {
            int minutes when minutes % 1440 == 0 => $"{minutes / 1440}d",
            int minutes when minutes % 60 == 0 => $"{minutes / 60}h",
            int minutes => $"{minutes}m",
            _ => null,
        };
        var expired = window is not null && window.ResetsAt <= DateTimeOffset.Now;
        stale |= expired;
        var value = window is null ? "—" : $"{window.RemainingPercent:0}%";
        text.Text = (duration is null ? value : $"{duration}  {value}") +
            (stale ? "  ↺" : string.Empty);
        bar.Value = window?.RemainingPercent ?? 0;
        bar.Visibility = window is null ? Visibility.Hidden : Visibility.Visible;
        bar.Opacity = stale ? 0.45 : 0.85;
        bar.Foreground = new SolidColorBrush(window?.RemainingPercent switch
        {
            <= 5 => Color.FromRgb(0xBA, 0x53, 0x66),
            <= 20 => Color.FromRgb(0xAC, 0x7C, 0x35),
            _ => Color.FromRgb(0x8F, 0x86, 0xB8),
        });
        var help = window is null
            ? english ? "Quota unavailable · click to refresh" : "额度暂不可用 · 点击刷新"
            : $"{duration} · {(english ? "Remaining" : "剩余")} {value}" +
                (stale ? english ? " · old" : " · 旧" : string.Empty) +
                $"\n{(english ? "Resets" : "重置时间")} · " +
                window.ResetsAt.ToLocalTime().ToString("g") +
                $"\n{(english ? "Read" : "读取时间")} · {_quotaSnapshot!.ReadAt.ToLocalTime():g}" +
                (stale ? english ? "\nLast known value; not a live balance." : "\n这是上次读数，不代表当前剩余额度。" : string.Empty) +
                (english ? "\nClick to refresh" : "\n点击刷新");
        button.IsEnabled = _quotaRefreshCancellation is null;
        SetMonitorHelp(button, help);
    }

    private static string QuickEffortLabel(string? effort, bool english) => effort switch
    {
        null or "" => "—",
        "none" => english ? "None" : "关闭",
        "minimal" => english ? "Minimal" : "最低",
        "low" => english ? "Low" : "低",
        "medium" => english ? "Medium" : "中",
        "high" => english ? "High" : "高",
        "xhigh" => english ? "Extra high" : "极高",
        _ => effort,
    };

    private static void SetMonitorHelp(FrameworkElement element, string text)
    {
        element.ToolTip = text;
        AutomationProperties.SetName(element, text.Replace('\n', ' '));
    }

    private async void MonitorQuota_Click(object sender, RoutedEventArgs e)
    {
        if (!IsCodexHarnessActive() || !_monitorPage)
        {
            return;
        }
        try
        {
            var refresh = RefreshQuotaAsync();
            UpdateMonitorQuickControls();
            await refresh;
        }
        catch (Exception exception)
        {
            SetStatus(exception.Message);
        }
        finally
        {
            UpdateMonitorQuickControls();
        }
    }

    private async void MonitorModel_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorQuickBusy || _quickModelSwitching || _harnessModelSwitching ||
            _monitorOpening || _pageSwitching || !_monitorPage || _windowClosed || !IsVisible)
        {
            return;
        }
        CloseMonitorEffortMenu();
        CancelMonitorQuickRead();
        _monitorQuickBusy = true;
        _monitorQuickError = null;
        UpdateMonitorQuickControls();
        try
        {
            if (IsCodexHarnessActive())
            {
                await ToggleQuickModelAsync();
            }
            else
            {
                await RunMonitorHarnessActionAsync(MicroHarnessActionIds.ToggleQuickModel);
            }
        }
        catch (Exception exception)
        {
            _monitorQuickError = exception.Message;
            SetStatus(exception.Message);
        }
        finally
        {
            _monitorDraftSelection = null;
            _monitorLastDraftRead = default;
            _monitorQuickBusy = false;
            UpdateMonitorQuickControls();
        }
    }

    private async void MonitorEffort_Click(object sender, RoutedEventArgs e)
    {
        if (_monitorQuickBusy || _quickModelSwitching || _harnessModelSwitching ||
            _monitorOpening || _pageSwitching || !_monitorPage || _windowClosed || !IsVisible)
        {
            return;
        }
        CloseMonitorEffortMenu();
        if (!IsCodexHarnessActive())
        {
            OpenMonitorHarnessEffortMenu();
            return;
        }
        var target = CaptureMonitorComposerTarget();
        if (target is null)
        {
            return;
        }
        CancelMonitorQuickRead();
        _monitorQuickBusy = true;
        _monitorQuickError = null;
        _monitorQuickTarget = target;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _monitorQuickCancellation = cancellation;
        UpdateMonitorQuickControls();
        try
        {
            var catalog = _monitorModelCatalog is { IsFresh: true } cached ? cached
                : await CodexDraftModelToggleService.FetchModelCatalogAsync(cancellation.Token);
            _monitorModelCatalog = catalog;
            string? selectedEffort;
            if (target.Draft is not null)
            {
                var selection = await _draftComposerModelSelector.ReadQuickSelectionAsync(
                    target.Window, catalog, () => IsMonitorComposerTargetCurrent(target), cancellation.Token);
                if (selection is null)
                {
                    throw new InvalidOperationException(_localization.IsEnglish
                        ? "The current composer could not be read." : "暂时无法读取当前输入区的模型和档位。");
                }
                target = target with { ModelId = selection.ModelId };
                _monitorQuickTarget = target;
                _monitorDraftSelection = (target.ThreadId, selection);
                selectedEffort = selection.Effort;
            }
            else
            {
                selectedEffort = _modelToggleService.CurrentThreadState?.Effort;
            }
            if (!IsMonitorComposerTargetCurrent(target))
            {
                return;
            }
            var model = target.ModelId is null ? null : catalog.Find(target.ModelId);
            if (!catalog.IsFresh || model is not { Hidden: false } || model.SupportedEfforts.Count == 0)
            {
                throw new InvalidOperationException(_localization.IsEnglish
                    ? "No verified reasoning options are available for this model."
                    : "当前模型没有可确认的思考档位。");
            }
            var menu = CreateMonitorEffortMenu();
            foreach (var effort in model.SupportedEfforts)
            {
                var item = new MenuItem
                {
                    Header = QuickEffortLabel(effort, _localization.IsEnglish),
                    IsCheckable = true,
                    IsChecked = effort == selectedEffort,
                };
                AutomationProperties.SetName(item,
                    $"{(_localization.IsEnglish ? "Reasoning" : "思考")} · {item.Header}");
                item.Click += async (_, _) =>
                {
                    menu.IsOpen = false;
                    await SelectMonitorEffortAsync(target, effort, catalog);
                };
                menu.Items.Add(item);
            }
            _monitorEffortMenu = menu;
            menu.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            _monitorQuickError = _localization.IsEnglish ? "Read cancelled or timed out." : "读取已取消或超时。";
        }
        catch (Exception exception)
        {
            _monitorQuickError = exception.Message;
            SetStatus(exception.Message);
        }
        finally
        {
            _monitorQuickCancellation = null;
            _monitorQuickBusy = false;
            if (_monitorEffortMenu is null)
            {
                _monitorQuickTarget = null;
            }
            UpdateMonitorQuickControls();
        }
    }

    private async Task SelectMonitorEffortAsync(MonitorComposerTarget target,
        string effort, CodexModelCatalog catalog)
    {
        if (_monitorQuickBusy || target.ModelId is null || !IsMonitorComposerTargetCurrent(target))
        {
            return;
        }
        _monitorQuickBusy = true;
        _monitorQuickTarget = target;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        _monitorQuickCancellation = cancellation;
        UpdateMonitorQuickControls();
        try
        {
            // Let the popup release mouse capture before native draft controls
            // verify that the original Codex renderer is still foreground.
            await Task.Delay(75, cancellation.Token);
            var result = target.Draft is null
                ? await _modelToggleService.SelectEffortAsync(target.ThreadId,
                    target.ModelId, effort, () => IsMonitorComposerTargetCurrent(target), cancellation.Token)
                : await _draftComposerModelSelector.SelectQuickEffortAsync(target.Window,
                    target.ModelId, effort, target.ThreadId, catalog,
                    () => IsMonitorComposerTargetCurrent(target), cancellation.Token);
            if (!IsMonitorComposerTargetCurrent(target))
            {
                return;
            }
            if (result.Succeeded)
            {
                _monitorQuickError = null;
                if (target.Draft is { } draft)
                {
                    _modelToggleService.TryPreserveForegroundDraftAfterReasoningStep(draft);
                    _monitorDraftSelection = (target.ThreadId,
                        new(target.ModelId, result.CurrentEffort));
                }
            }
            else
            {
                _monitorQuickError = (_localization.IsEnglish
                    ? "Change not confirmed" : "未确认切换成功") + $" · {result.Error}";
            }
        }
        catch (OperationCanceledException)
        {
            _monitorQuickError = _localization.IsEnglish
                ? "Change not confirmed; showing the last verified state."
                : "切换未确认；仍显示已确认的状态。";
        }
        catch (Exception exception)
        {
            _monitorQuickError = exception.Message;
            SetStatus(exception.Message);
        }
        finally
        {
            _monitorQuickCancellation = null;
            _monitorQuickTarget = null;
            _monitorQuickBusy = false;
            UpdateMonitorQuickControls();
        }
    }

    private ContextMenu CreateMonitorEffortMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = MonitorEffortButton,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 5,
            Style = (Style)FindResource("Monitor.ChoiceMenu"),
        };
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_monitorEffortMenu, menu))
            {
                _monitorEffortMenu = null;
                if (!_monitorQuickBusy)
                {
                    _monitorQuickTarget = null;
                }
            }
        };
        return menu;
    }

    private void OpenMonitorHarnessEffortMenu()
    {
        var harnessId = ActiveHarness().Id;
        var sessionId = CurrentHarnessSessionId();
        var capabilities = _harnessStateSnapshot?.HarnessId == harnessId
            ? _harnessStateSnapshot.Capabilities : null;
        var menu = CreateMonitorEffortMenu();
        foreach (var action in new[] { MicroHarnessActionIds.ReasoningDecrease,
                     MicroHarnessActionIds.ReasoningIncrease })
        {
            if (capabilities?.Supports(action) != true)
            {
                continue;
            }
            var item = new MenuItem { Header = HarnessActionLabel(action) };
            item.Click += async (_, _) =>
            {
                menu.IsOpen = false;
                if (_monitorQuickBusy || ActiveHarness().Id != harnessId ||
                    CurrentHarnessSessionId() != sessionId || !_monitorPage)
                {
                    return;
                }
                _monitorQuickBusy = true;
                UpdateMonitorQuickControls();
                try
                {
                    await RunMonitorHarnessActionAsync(action);
                }
                catch (Exception exception)
                {
                    SetStatus(exception.Message);
                }
                finally
                {
                    _monitorQuickBusy = false;
                    UpdateMonitorQuickControls();
                }
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0)
        {
            _monitorEffortMenu = menu;
            menu.IsOpen = true;
        }
    }

    private async Task RunMonitorHarnessActionAsync(string action)
    {
        var harness = ActiveHarness();
        if (harness.Id == "codex" || _harnessStateSnapshot?.HarnessId != harness.Id ||
            !_harnessStateSnapshot.Capabilities.Supports(action) ||
            _harnessStateSnapshot.NavigationDepth != 0)
        {
            return;
        }
        await ExecuteHarnessActionAsync(harness, action,
            HarnessActionLabel(action), CurrentHarnessSessionId());
        if (!_windowClosed && ActiveHarness().Id == harness.Id)
        {
            await RefreshHarnessStateAsync();
        }
    }

    private async Task RefreshMonitorDraftSelectionAsync(MonitorComposerTarget target)
    {
        if (_monitorDraftReadCancellation is not null ||
            DateTimeOffset.UtcNow - _monitorLastDraftRead < TimeSpan.FromSeconds(2) ||
            _monitorModelCatalog is not { IsFresh: true } catalog)
        {
            return;
        }
        _monitorLastDraftRead = DateTimeOffset.UtcNow;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        _monitorDraftReadCancellation = cancellation;
        try
        {
            var selection = await _draftComposerModelSelector.ReadQuickSelectionAsync(
                target.Window, catalog, () => IsMonitorComposerTargetCurrent(target), cancellation.Token);
            if (!cancellation.IsCancellationRequested && IsMonitorComposerTargetCurrent(target))
            {
                _monitorDraftSelection = selection is null ? null : (target.ThreadId, selection);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _monitorDraftReadCancellation = null;
            UpdateMonitorQuickControls();
        }
    }

    private void CloseMonitorEffortMenu()
    {
        var menu = _monitorEffortMenu;
        _monitorEffortMenu = null;
        if (!_monitorQuickBusy)
        {
            _monitorQuickTarget = null;
        }
        if (menu is not null)
        {
            menu.IsOpen = false;
        }
    }

    private void CancelMonitorQuickRead() => _monitorDraftReadCancellation?.Cancel();

    private void StopMonitorQuickControls()
    {
        CloseMonitorEffortMenu();
        CancelMonitorQuickRead();
        _monitorQuickCancellation?.Cancel();
    }
}
