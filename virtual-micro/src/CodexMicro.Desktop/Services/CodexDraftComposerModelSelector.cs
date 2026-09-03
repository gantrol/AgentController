using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

namespace CodexMicro.Desktop.Services;

internal sealed class CodexDraftComposerModelSelector
{
    private const ushort VirtualKeyEscape = 0x1B;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;

    private static readonly TimeSpan PollInterval =
        TimeSpan.FromMilliseconds(70);
    private static readonly TimeSpan MenuTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan SelectionTimeout =
        TimeSpan.FromSeconds(5);
    private static readonly TimeSpan UltraWarningAppearanceTimeout =
        TimeSpan.FromSeconds(8);
    private static readonly Regex PowerPositionPattern = new(
        @"(?<position>\d+)\s+of\s+(?<count>\d+)",
        RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);

    private readonly record struct ComposerSelection(
        CodexQuickModel Model,
        string? Effort,
        int Position = 0,
        int Count = 0)
    {
        internal bool Matches(CodexQuickModel model, string effort) =>
            Model == model &&
            string.Equals(Effort, effort, StringComparison.Ordinal);
    }

    private readonly record struct TriggerCandidate(
        AutomationElement Element,
        int Score,
        double Area);

    private readonly record struct MenuCandidate(
        AutomationElement Element,
        int Score,
        double Distance);

    private readonly record struct PowerTrackCandidate(
        AutomationElement Element,
        int Score,
        double Area);

    private sealed class DraftUiException(string error) : Exception(error)
    {
        internal string Error { get; } = error;
    }

    internal Task<CodexModelToggleResult> ToggleAsync(
        IntPtr foregroundWindow,
        CodexQuickModel first,
        string? firstEffort,
        CodexQuickModel second,
        string? secondEffort,
        string draftOperationId,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        if (foregroundWindow == IntPtr.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(foregroundWindow));
        }

        if (!CodexDraftModelToggleService.IsDraftOperationId(
                draftOperationId))
        {
            throw new ArgumentException(
                "The composer selector cannot target a real Codex task.",
                nameof(draftOperationId));
        }

        if (first == CodexQuickModel.Unknown ||
            second == CodexQuickModel.Unknown ||
            first == second)
        {
            throw new ArgumentException(
                "Quick-model profiles must contain two distinct models.");
        }

        ArgumentNullException.ThrowIfNull(isDraftCurrent);
        return Task.Run(
            () => ToggleCore(
                foregroundWindow,
                first,
                firstEffort,
                second,
                secondEffort,
                draftOperationId,
                isDraftCurrent,
                cancellationToken),
            CancellationToken.None);
    }

    private static CodexModelToggleResult ToggleCore(
        IntPtr foregroundWindow,
        CodexQuickModel first,
        string? firstEffort,
        CodexQuickModel second,
        string? secondEffort,
        string draftOperationId,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        var previous = CodexQuickModel.Unknown;
        string? previousEffort = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = RequireRoot(foregroundWindow);
            if (HasUltraWarning(root))
            {
                throw new DraftUiException(
                    "draft-ui-decision-already-pending");
            }

            EnsureCurrent(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            root = RequireRoot(foregroundWindow);
            EnsureNoUnexpectedDialog(root);

            var trigger = RequireTrigger(root);
            var initial = ReadTriggerSelection(trigger);
            var menu = EnsureMenuOpen(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            if (FindPowerItem(menu) is { } initialPower)
            {
                initial = PreferPowerSelection(
                    initial,
                    ReadPowerSelection(initialPower, menu));
            }
            else if (initial.Model == CodexQuickModel.Unknown)
            {
                initial = initial with
                {
                    Model = ReadSelectedModel(menu),
                };
            }

            previous = initial.Model;
            previousEffort = initial.Effort;
            if (previous == CodexQuickModel.Unknown)
            {
                throw new DraftUiException(
                    "draft-ui-selection-unavailable");
            }

            var target = CodexModelToggleService.ResolveToggleTarget(
                previous,
                first,
                second);
            var rememberedEffort = target == first
                ? firstEffort
                : secondEffort;
            var targetEffort = CodexModelToggleService.ResolveTargetEffort(
                CodexModelToggleService.ToModelId(target),
                rememberedEffort);

#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-ui-target-resolved",
                new
                {
                    draftOperationId,
                    previous = previous.ToString(),
                    previousEffort,
                    target = target.ToString(),
                    targetEffort,
                });
#endif

            SelectModel(
                foregroundWindow,
                target,
                isDraftCurrent,
                cancellationToken);
            SelectEffort(
                foregroundWindow,
                target,
                targetEffort,
                isDraftCurrent,
                cancellationToken);
            VerifyFinalSelection(
                foregroundWindow,
                target,
                targetEffort,
                isDraftCurrent,
                cancellationToken);

            return new(
                Succeeded: true,
                Previous: previous,
                Current: target,
                ThreadId: draftOperationId,
                PreviousEffort: previousEffort,
                CurrentEffort: targetEffort,
                Detail: CodexDraftModelToggleService
                    .NativeTargetConfirmationReceipt);
        }
        catch (DraftUiException exception)
        {
#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-ui-toggle-failed",
                new
                {
                    draftOperationId,
                    exception.Error,
                });
#endif
            return new(
                Succeeded: false,
                Previous: previous,
                Current: previous,
                ThreadId: draftOperationId,
                PreviousEffort: previousEffort,
                CurrentEffort: previousEffort,
                Error: exception.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
                InvalidOperationException or
                COMException or
                Win32Exception)
        {
#if DEBUG
            CodexModelToggleDiagnostics.RecordStage(
                "draft-ui-automation-failed",
                new
                {
                    draftOperationId,
                    exception = exception.GetType().Name,
                    exception.Message,
                    exception.StackTrace,
                });
#endif
            return new(
                Succeeded: false,
                Previous: previous,
                Current: previous,
                ThreadId: draftOperationId,
                PreviousEffort: previousEffort,
                CurrentEffort: previousEffort,
                Error: "draft-ui-automation-failed");
        }
    }

    private static void SelectModel(
        IntPtr foregroundWindow,
        CodexQuickModel target,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        var menu = EnsureMenuOpen(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        if (!HasModelRows(menu))
        {
            var selectModel = FindMenuItem(menu, "Select model") ??
                throw new DraftUiException("draft-ui-model-menu-unavailable");
            Invoke(selectModel);
            menu = WaitForMenu(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken,
                HasModelRows,
                "draft-ui-model-menu-unavailable");
        }

        var label = ModelLabel(target);
        var rows = FindElements(
                menu,
                ControlType.RadioButton,
                visibleOnly: true)
            .Where(row => string.Equals(
                SafeRead(() => row.Current.Name, string.Empty),
                label,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rows.Length != 1)
        {
            throw new DraftUiException("draft-ui-model-unavailable");
        }

        var row = rows[0];
        if (!SafeRead(() => row.Current.IsEnabled, false) ||
            IsLocked(row))
        {
            throw new DraftUiException("draft-ui-model-locked");
        }

        EnsureCurrent(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        Invoke(row);

        _ = WaitForMenu(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken,
            currentMenu =>
            {
                var power = FindPowerItem(currentMenu);
                if (power is null)
                {
                    return false;
                }

                var selection = ReadPowerSelection(power, currentMenu);
                return selection is null || selection.Value.Model == target;
            },
            "draft-ui-model-not-applied");

        var root = RequireRoot(foregroundWindow);
        EnsureNoUnexpectedDialog(root);
#if DEBUG
        CodexModelToggleDiagnostics.RecordStage(
            "draft-ui-model-selected",
            new { target = target.ToString() });
#endif
    }

    private static void SelectEffort(
        IntPtr foregroundWindow,
        CodexQuickModel target,
        string targetEffort,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        _ = EnsurePowerMenu(
            foregroundWindow,
            target,
            isDraftCurrent,
            cancellationToken);
        var current = WaitForPowerSelection(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken,
            selection =>
                selection.Model == target &&
                (selection.Matches(target, targetEffort) ||
                    (selection.Position > 0 &&
                        selection.Count > 1 &&
                        selection.Position <= selection.Count)),
            "draft-ui-power-state-unavailable");
#if DEBUG
        RecordPowerDiagnostics(
            "draft-ui-power-ready",
            foregroundWindow,
            current);
#endif

        if (!current.Matches(target, targetEffort))
        {
            current = SetPowerDirectly(
                foregroundWindow,
                current,
                target,
                targetEffort,
                isDraftCurrent,
                cancellationToken);
        }

        if (!current.Matches(target, targetEffort))
        {
            throw new DraftUiException("draft-ui-target-unavailable");
        }

        CloseMenu(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
#if DEBUG
        CodexModelToggleDiagnostics.RecordStage(
            "draft-ui-effort-selected",
            new
            {
                target = target.ToString(),
                targetEffort,
                current.Position,
                current.Count,
            });
#endif
    }

    private static ComposerSelection SetPowerDirectly(
        IntPtr foregroundWindow,
        ComposerSelection current,
        CodexQuickModel target,
        string targetEffort,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        if (current.Count <= 1 ||
            current.Position <= 0 ||
            current.Position > current.Count)
        {
            throw new DraftUiException("draft-ui-power-position-unavailable");
        }

        var targetPosition = ResolvePowerPosition(
            targetEffort,
            current.Count);
        EnsureCurrent(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        var root = RequireRoot(foregroundWindow);
        EnsureNoUnexpectedDialog(root);
        var permissionBefore = TryReadPermissionMode(root);
        _ = EnsureMenuOpen(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        var menu = WaitForMenu(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken,
            candidate =>
                FindPowerItem(candidate) is not null &&
                HasDirectPowerTarget(candidate, target, current.Count),
            "draft-ui-power-track-unavailable");
        var mechanism = "track-click";
        var minimum = double.NaN;
        var maximum = double.NaN;
        var targetValue = double.NaN;
        var slider = FindPowerSlider(menu);
        if (slider is not null &&
            slider.TryGetCurrentPattern(
                RangeValuePattern.Pattern,
                out var rawPattern) &&
            rawPattern is RangeValuePattern rangePattern)
        {
            var range = rangePattern.Current;
            if (!range.IsReadOnly &&
                double.IsFinite(range.Minimum) &&
                double.IsFinite(range.Maximum) &&
                range.Maximum > range.Minimum)
            {
                minimum = range.Minimum;
                maximum = range.Maximum;
                targetValue = range.Minimum +
                    ((range.Maximum - range.Minimum) *
                        (targetPosition - 1d) /
                        (current.Count - 1d));
                mechanism = "range-value";
                rangePattern.SetValue(targetValue);
            }
        }

        Rect trackRectangle = Rect.Empty;
        Point targetPoint = default;
        if (mechanism == "track-click")
        {
            var track = FindPowerTrack(menu, target, current.Count) ??
                throw new DraftUiException(
                    "draft-ui-power-track-unavailable");
            trackRectangle = SafeRead(
                () => track.Current.BoundingRectangle,
                Rect.Empty);
            targetPoint = ResolvePowerTrackPoint(
                trackRectangle,
                targetPosition,
                current.Count);
            ClickPowerTrack(
                foregroundWindow,
                targetPoint,
                isDraftCurrent,
                cancellationToken);
        }

#if DEBUG
        CodexModelToggleDiagnostics.RecordStage(
            "draft-ui-power-direct-set",
            new
            {
                target = target.ToString(),
                targetEffort,
                targetPosition,
                current.Count,
                mechanism,
                minimum,
                maximum,
                targetValue,
                track = trackRectangle.IsEmpty
                    ? null
                    : new
                    {
                        trackRectangle.Left,
                        trackRectangle.Top,
                        trackRectangle.Width,
                        trackRectangle.Height,
                        x = targetPoint.X,
                        y = targetPoint.Y,
                    },
            });
#endif
        return WaitForDirectPowerSelection(
            foregroundWindow,
            current,
            target,
            targetEffort,
            permissionBefore,
            isDraftCurrent,
            cancellationToken);
    }

    private static ComposerSelection WaitForDirectPowerSelection(
        IntPtr foregroundWindow,
        ComposerSelection before,
        CodexQuickModel target,
        string targetEffort,
        string? permissionBefore,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        var targetsUltra = string.Equals(
            targetEffort,
            "ultra",
            StringComparison.Ordinal);
        var waitForWarning = targetsUltra &&
            !string.Equals(
                permissionBefore,
                "Full access",
                StringComparison.OrdinalIgnoreCase);
        var deadline = DateTimeOffset.UtcNow +
            (waitForWarning
                ? UltraWarningAppearanceTimeout
                : SelectionTimeout);
        var latest = before;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = RequireRoot(foregroundWindow);
            if (HasUltraWarning(root))
            {
                if (!targetsUltra)
                {
                    throw new DraftUiException(
                        "draft-ui-unexpected-ultra-warning");
                }

                WaitForUltraDecision(
                    foregroundWindow,
                    isDraftCurrent,
                    cancellationToken);
                latest = ReadVerifiedSelection(
                    foregroundWindow,
                    isDraftCurrent,
                    cancellationToken);
                if (!latest.Matches(target, targetEffort))
                {
                    throw new DraftUiException("draft-ui-user-declined");
                }

                return latest;
            }

            EnsureCurrent(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            EnsureNoUnexpectedDialog(root);
            var trigger = FindTrigger(root);
            var menu = FindMenu(root, trigger);
            var power = menu is null ? null : FindPowerItem(menu);
            var triggerSelection = trigger is null
                ? default
                : ReadTriggerSelection(trigger);
            var selection = power is null
                ? triggerSelection
                : PreferPowerSelection(
                    triggerSelection,
                    ReadPowerSelection(power, menu!));
            if (selection.Model != CodexQuickModel.Unknown)
            {
                latest = selection;
                if (selection.Matches(target, targetEffort) &&
                    !waitForWarning)
                {
                    return selection;
                }
            }

            Thread.Sleep(PollInterval);
        }

        if (latest.Matches(target, targetEffort))
        {
            return latest;
        }

        throw new DraftUiException(targetsUltra
            ? "draft-ui-ultra-transition-unconfirmed"
            : "draft-ui-transition-unconfirmed");
    }

#if DEBUG
    private static void RecordPowerDiagnostics(
        string stage,
        IntPtr foregroundWindow,
        ComposerSelection selection)
    {
        try
        {
            var root = RequireRoot(foregroundWindow);
            var trigger = RequireTrigger(root);
            var menu = FindMenu(root, trigger);
            var power = menu is null ? null : FindPowerItem(menu);
            var focused = AutomationElement.FocusedElement;
            CodexModelToggleDiagnostics.RecordStage(
                stage,
                new
                {
                    selection.Model,
                    selection.Effort,
                    selection.Position,
                    selection.Count,
                    trigger = ReadAccessibleStrings(trigger)
                        .Take(12)
                        .ToArray(),
                    power = power is null
                        ? []
                        : ReadAccessibleStrings(power)
                            .Take(30)
                            .ToArray(),
                    menu = menu is null
                        ? []
                        : ReadAccessibleStrings(menu)
                            .Take(60)
                            .ToArray(),
                    focusedName = focused is null
                        ? null
                        : SafeRead(
                            () => focused.Current.Name,
                            string.Empty),
                    focusedType = focused is null
                        ? null
                        : SafeRead(
                            () => focused.Current.LocalizedControlType,
                            string.Empty),
                });
        }
        catch
        {
        }
    }
#endif

    private static AutomationElement EnsurePowerMenu(
        IntPtr foregroundWindow,
        CodexQuickModel target,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var menu = EnsureMenuOpen(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            if (FindPowerItem(menu) is not null)
            {
                return menu;
            }

            if (!HasModelRows(menu))
            {
                throw new DraftUiException("draft-ui-power-unavailable");
            }

            var row = FindElements(
                    menu,
                    ControlType.RadioButton,
                    visibleOnly: true)
                .SingleOrDefault(item => string.Equals(
                    SafeRead(() => item.Current.Name, string.Empty),
                    ModelLabel(target),
                    StringComparison.OrdinalIgnoreCase));
            if (row is null || IsLocked(row))
            {
                throw new DraftUiException("draft-ui-model-locked");
            }

            Invoke(row);
            Thread.Sleep(PollInterval);
        }

        throw new DraftUiException("draft-ui-power-unavailable");
    }

    private static ComposerSelection WaitForPowerSelection(
        IntPtr foregroundWindow,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken,
        Func<ComposerSelection, bool> predicate,
        string error)
    {
        var deadline = DateTimeOffset.UtcNow + SelectionTimeout;
        ComposerSelection? latest = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            EnsureCurrent(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            var root = RequireRoot(foregroundWindow);
            if (!HasUltraWarning(root))
            {
                EnsureNoUnexpectedDialog(root);
            }

            var trigger = FindTrigger(root);
            var menu = FindMenu(root, trigger);
            if (trigger is null && menu is null)
            {
                Thread.Sleep(PollInterval);
                continue;
            }

            var power = menu is null ? null : FindPowerItem(menu);
            var triggerSelection = trigger is null
                ? default
                : ReadTriggerSelection(trigger);
            var selection = power is null
                ? triggerSelection
                : PreferPowerSelection(
                    triggerSelection,
                    ReadPowerSelection(power, menu!));
            if (selection.Model != CodexQuickModel.Unknown)
            {
                latest = selection;
                if (predicate(selection))
                {
                    return selection;
                }
            }

            Thread.Sleep(PollInterval);
        }

        if (latest is { } observed && predicate(observed))
        {
            return observed;
        }

        throw new DraftUiException(error);
    }

    private static void VerifyFinalSelection(
        IntPtr foregroundWindow,
        CodexQuickModel target,
        string targetEffort,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        var selection = ReadVerifiedSelection(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        if (!selection.Matches(target, targetEffort))
        {
            throw new DraftUiException("draft-ui-final-state-mismatch");
        }

#if DEBUG
        CodexModelToggleDiagnostics.RecordStage(
            "draft-ui-final-state-confirmed",
            new
            {
                target = target.ToString(),
                targetEffort,
                permission = TryReadPermissionMode(
                    RequireRoot(foregroundWindow)),
            });
#endif
    }

    private static ComposerSelection ReadVerifiedSelection(
        IntPtr foregroundWindow,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = RequireRoot(foregroundWindow);
        if (HasUltraWarning(root))
        {
            WaitForUltraDecision(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            root = RequireRoot(foregroundWindow);
        }

        EnsureCurrent(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        EnsureNoUnexpectedDialog(root);
        var trigger = RequireTrigger(root);
        var triggerSelection = ReadTriggerSelection(trigger);
        if (triggerSelection.Model != CodexQuickModel.Unknown &&
            triggerSelection.Effort is not null)
        {
            return triggerSelection;
        }

        var menu = EnsureMenuOpen(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        var power = FindPowerItem(menu) ??
            throw new DraftUiException("draft-ui-power-unavailable");
        var selection = PreferPowerSelection(
            triggerSelection,
            ReadPowerSelection(power, menu));
        CloseMenu(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        return selection;
    }

    private static AutomationElement EnsureMenuOpen(
        IntPtr foregroundWindow,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        EnsureCurrent(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        var root = RequireRoot(foregroundWindow);
        var existing = FindMenu(root, trigger: null);
        if (existing is not null)
        {
            return existing;
        }

        var trigger = RequireTrigger(root);
        var pattern = GetExpandCollapsePattern(trigger) ??
            throw new DraftUiException("draft-ui-trigger-unavailable");
        pattern.Expand();
        return WaitForMenu(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken,
            _ => true,
            "draft-ui-menu-unavailable");
    }

    private static AutomationElement WaitForMenu(
        IntPtr foregroundWindow,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken,
        Func<AutomationElement, bool> predicate,
        string error)
    {
        var deadline = DateTimeOffset.UtcNow + MenuTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            EnsureCurrent(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            var root = RequireRoot(foregroundWindow);
            if (!HasUltraWarning(root))
            {
                EnsureNoUnexpectedDialog(root);
            }

            var menu = FindMenu(root, FindTrigger(root), predicate);
            if (menu is not null)
            {
                return menu;
            }

            Thread.Sleep(PollInterval);
        }

        throw new DraftUiException(error);
    }

    private static void CloseMenu(
        IntPtr foregroundWindow,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        EnsureCurrent(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        var root = RequireRoot(foregroundWindow);
        if (HasUltraWarning(root))
        {
            throw new DraftUiException("draft-ui-decision-pending");
        }

        if (FindMenu(root, FindTrigger(root)) is null)
        {
            return;
        }

        SendVirtualKey(VirtualKeyEscape);
        var deadline = DateTimeOffset.UtcNow + MenuTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            EnsureCurrent(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            root = RequireRoot(foregroundWindow);
            if (FindMenu(root, FindTrigger(root)) is null)
            {
                return;
            }

            Thread.Sleep(PollInterval);
        }

        throw new DraftUiException("draft-ui-menu-close-unconfirmed");
    }

    private static void WaitForUltraDecision(
        IntPtr foregroundWindow,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
#if DEBUG
        CodexModelToggleDiagnostics.RecordStage(
            "draft-ui-waiting-for-ultra-decision");
#endif
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = RequireRoot(foregroundWindow);
            if (!HasUltraWarning(root))
            {
                EnsureCurrent(
                    foregroundWindow,
                    isDraftCurrent,
                    cancellationToken);
                root = RequireRoot(foregroundWindow);
                EnsureNoUnexpectedDialog(root);
#if DEBUG
                CodexModelToggleDiagnostics.RecordStage(
                    "draft-ui-ultra-decision-complete",
                    new
                    {
                        permission = TryReadPermissionMode(root),
                    });
#endif
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(120));
        }
    }

    private static bool HasUltraWarning(AutomationElement root)
    {
        var processId = SafeRead(() => root.Current.ProcessId, 0);
        if (processId == 0)
        {
            return false;
        }

        return HasUltraWarning(root, processId);
    }

    private static bool HasUltraWarning(
        AutomationElement scope,
        int processId)
    {
        var title = FindFirstByExactName(
            scope,
            "Use Ultra with Full access?",
            processId);
        if (title is null || !IsRendered(title))
        {
            return false;
        }

        return new[] { "Use Full access", "Continue" }
            .Select(name => FindFirstByExactName(scope, name, processId))
            .Any(element => element is not null && IsRendered(element));
    }

    private static AutomationElement? FindFirstByExactName(
        AutomationElement scope,
        string name,
        int processId) =>
        SafeRead<AutomationElement?>(
            () => scope.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(
                        AutomationElement.NameProperty,
                        name),
                    new PropertyCondition(
                        AutomationElement.ProcessIdProperty,
                        processId))),
            null);

    private static void EnsureNoUnexpectedDialog(AutomationElement root)
    {
        foreach (var element in FindAll(root))
        {
            if (!IsRendered(element))
            {
                continue;
            }

            var localizedType = SafeRead(
                () => element.Current.LocalizedControlType,
                string.Empty);
            if (!string.Equals(
                    localizedType,
                    "dialog",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = SafeRead(
                () => element.Current.Name,
                string.Empty);
            if (string.Equals(
                    name,
                    "Breadcrumb",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ContainsAccessibleText(
                    element,
                    "Use Ultra with Full access?"))
            {
#if DEBUG
                CodexModelToggleDiagnostics.RecordStage(
                    "draft-ui-unexpected-dialog",
                    new
                    {
                        name,
                        localizedType,
                    });
#endif
                throw new DraftUiException("draft-ui-dialog-blocked");
            }
        }
    }

    private static string? TryReadPermissionMode(AutomationElement root)
    {
        foreach (var name in new[]
                 {
                     "Ask for approval",
                     "Approve for me",
                     "Full access",
                 })
        {
            var candidates = FindAll(root)
                .Where(IsRendered)
                .Where(element =>
                    SafeRead(
                        () => element.Current.ControlType,
                        ControlType.Custom) is var type &&
                    (type == ControlType.Button ||
                        type == ControlType.MenuItem))
                .Where(element => string.Equals(
                    SafeRead(
                        () => element.Current.Name,
                        string.Empty),
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (candidates.Any())
            {
                return name;
            }
        }

        return null;
    }

    private static AutomationElement RequireRoot(IntPtr foregroundWindow)
    {
        var root = AutomationElement.FromHandle(foregroundWindow);
        if (root is null ||
            SafeRead(() => root.Current.ProcessId, 0) == 0)
        {
            throw new DraftUiException("draft-ui-window-unavailable");
        }

        return root;
    }

    private static AutomationElement RequireTrigger(AutomationElement root) =>
        FindTrigger(root) ??
            throw new DraftUiException("draft-ui-trigger-unavailable");

    private static AutomationElement? FindTrigger(AutomationElement root)
    {
        var rootRectangle = SafeRead(
            () => root.Current.BoundingRectangle,
            Rect.Empty);
        var candidates = new List<TriggerCandidate>();
        foreach (var button in FindElements(
                     root,
                     ControlType.Button,
                     visibleOnly: true))
        {
            var pattern = GetExpandCollapsePattern(button);
            if (pattern is null ||
                !SafeRead(() => button.Current.IsEnabled, false))
            {
                continue;
            }

            var text = string.Join(" ", ReadAccessibleStrings(button));
            var selection = ParseSelection(text);
            var isKnownLabel =
                text.Contains("Select model", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Select effort", StringComparison.OrdinalIgnoreCase) ||
                text.Contains(
                    "Select ChatGPT model",
                    StringComparison.OrdinalIgnoreCase);
            if (selection.Model == CodexQuickModel.Unknown && !isKnownLabel)
            {
                continue;
            }

            var rectangle = SafeRead(
                () => button.Current.BoundingRectangle,
                Rect.Empty);
            var score = selection.Model == CodexQuickModel.Unknown ? 50 : 100;
            if (!rootRectangle.IsEmpty &&
                rectangle.Top >= rootRectangle.Top +
                    (rootRectangle.Height * 0.45))
            {
                score += 20;
            }

            candidates.Add(new(
                button,
                score,
                rectangle.IsEmpty
                    ? double.MaxValue
                    : rectangle.Width * rectangle.Height));
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Area)
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        if (ordered.Length > 1 &&
            ordered[0].Score == ordered[1].Score &&
            Math.Abs(ordered[0].Area - ordered[1].Area) < 0.5)
        {
            return null;
        }

        return ordered[0].Element;
    }

    private static AutomationElement? FindMenu(
        AutomationElement root,
        AutomationElement? trigger,
        Func<AutomationElement, bool>? predicate = null)
    {
        var triggerRectangle = trigger is null
            ? Rect.Empty
            : SafeRead(
                () => trigger.Current.BoundingRectangle,
                Rect.Empty);
        var candidates = new List<MenuCandidate>();
        foreach (var menu in FindElements(
                     root,
                     ControlType.Menu,
                     visibleOnly: true))
        {
            if (predicate is not null && !predicate(menu))
            {
                continue;
            }

            var score = 0;
            if (string.Equals(
                    SafeRead(() => menu.Current.Name, string.Empty),
                    "Select model",
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }

            if (FindPowerItem(menu) is not null)
            {
                score += 100;
            }

            if (HasModelRows(menu))
            {
                score += 80;
            }

            if (score == 0)
            {
                continue;
            }

            var rectangle = SafeRead(
                () => menu.Current.BoundingRectangle,
                Rect.Empty);
            var distance = triggerRectangle.IsEmpty || rectangle.IsEmpty
                ? double.MaxValue
                : Math.Abs(rectangle.Right - triggerRectangle.Right) +
                    Math.Abs(rectangle.Bottom - triggerRectangle.Top);
            candidates.Add(new(menu, score, distance));
        }

        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Distance)
            .Select(candidate => candidate.Element)
            .FirstOrDefault();
    }

    private static AutomationElement? FindPowerItem(AutomationElement menu) =>
        FindMenuItem(menu, "Power");

    private static AutomationElement? FindPowerSlider(
        AutomationElement menu)
    {
        var sliders = FindElements(
                menu,
                ControlType.Slider,
                visibleOnly: true)
            .Where(element =>
                IsRendered(element) &&
                SafeRead(() => element.Current.IsEnabled, false) &&
                element.TryGetCurrentPattern(
                    RangeValuePattern.Pattern,
                    out var pattern) &&
                pattern is RangeValuePattern)
            .ToArray();
        if (sliders.Length != 0)
        {
            return sliders.Length == 1 ? sliders[0] : null;
        }

        var ranged = FindAll(menu)
            .Where(element =>
                IsRendered(element) &&
                SafeRead(() => element.Current.IsEnabled, false) &&
                element.TryGetCurrentPattern(
                    RangeValuePattern.Pattern,
                    out var pattern) &&
                pattern is RangeValuePattern)
            .ToArray();
        return ranged.Length == 1 ? ranged[0] : null;
    }

    private static bool HasDirectPowerTarget(
        AutomationElement menu,
        CodexQuickModel target,
        int count)
    {
        if (FindPowerTrack(menu, target, count) is not null)
        {
            return true;
        }

        var slider = FindPowerSlider(menu);
        return slider is not null && SafeRead(
            () =>
            {
                if (!slider.TryGetCurrentPattern(
                        RangeValuePattern.Pattern,
                        out var rawPattern) ||
                    rawPattern is not RangeValuePattern rangePattern)
                {
                    return false;
                }

                var range = rangePattern.Current;
                return !range.IsReadOnly &&
                    double.IsFinite(range.Minimum) &&
                    double.IsFinite(range.Maximum) &&
                    range.Maximum > range.Minimum;
            },
            false);
    }

    private static AutomationElement? FindPowerTrack(
        AutomationElement menu,
        CodexQuickModel target,
        int count)
    {
        var power = FindPowerItem(menu);
        if (power is not null &&
            SelectPowerTrack(
                FindAll(power),
                target,
                count,
                scopedToPowerItem: true) is { } scopedTrack)
        {
            return scopedTrack;
        }

        return SelectPowerTrack(
            FindAll(menu),
            target,
            count,
            scopedToPowerItem: false);
    }

    private static AutomationElement? SelectPowerTrack(
        IReadOnlyList<AutomationElement> elements,
        CodexQuickModel target,
        int count,
        bool scopedToPowerItem)
    {
        var candidates = new List<PowerTrackCandidate>();
        foreach (var element in elements)
        {
            if (!IsRendered(element) ||
                !SafeRead(() => element.Current.IsEnabled, false))
            {
                continue;
            }

            var rectangle = SafeRead(
                () => element.Current.BoundingRectangle,
                Rect.Empty);
            if (rectangle.IsEmpty ||
                rectangle.Width <= rectangle.Height * 3 ||
                rectangle.Width < count * 20)
            {
                continue;
            }

            var text = string.Join(
                " ",
                ReadOwnAccessibleStrings(element));
            var selection = ParseSelection(text);
            var isSlider = SafeRead(
                () => element.Current.ControlType == ControlType.Slider,
                false);
            var hasMatchingState = selection.Model == target &&
                selection.Position > 0 &&
                selection.Count == count;
            if (!scopedToPowerItem && !isSlider && !hasMatchingState)
            {
                continue;
            }

            var score = scopedToPowerItem ? 400 : 0;
            if (isSlider)
            {
                score += 200;
            }

            if (hasMatchingState)
            {
                score += 300;
            }

            if (text.Contains(
                    "arrow keys to adjust power",
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 100;
            }

            candidates.Add(new(
                element,
                score,
                rectangle.Width * rectangle.Height));
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Area)
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        if (ordered.Length > 1 &&
            ordered[0].Score == ordered[1].Score &&
            Math.Abs(ordered[0].Area - ordered[1].Area) < 0.5)
        {
            return null;
        }

        return ordered[0].Element;
    }

    private static Point ResolvePowerTrackPoint(
        Rect rectangle,
        int position,
        int count)
    {
        if (rectangle.IsEmpty ||
            rectangle.Width <= 0 ||
            rectangle.Height <= 0 ||
            position <= 0 ||
            position > count ||
            count <= 1)
        {
            throw new DraftUiException("draft-ui-power-track-unavailable");
        }

        var endpointInset = Math.Min(
            rectangle.Height / 2d,
            rectangle.Width / (count * 2d));
        var usableWidth = rectangle.Width - (endpointInset * 2d);
        if (usableWidth <= 0)
        {
            throw new DraftUiException("draft-ui-power-track-unavailable");
        }

        return new(
            rectangle.Left + endpointInset +
                (usableWidth * (position - 1d) / (count - 1d)),
            rectangle.Top + (rectangle.Height / 2d));
    }

    private static void ClickPowerTrack(
        IntPtr foregroundWindow,
        Point target,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        EnsureCurrent(
            foregroundWindow,
            isDraftCurrent,
            cancellationToken);
        var root = RequireRoot(foregroundWindow);
        var rootRectangle = SafeRead(
            () => root.Current.BoundingRectangle,
            Rect.Empty);
        if (rootRectangle.IsEmpty || !rootRectangle.Contains(target))
        {
            throw new DraftUiException("draft-ui-power-track-unavailable");
        }

        if (!GetCursorPos(out var original))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var x = checked((int)Math.Round(target.X));
            var y = checked((int)Math.Round(target.Y));
            if (!SetCursorPos(x, y))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            EnsureCurrent(
                foregroundWindow,
                isDraftCurrent,
                cancellationToken);
            root = RequireRoot(foregroundWindow);
            rootRectangle = SafeRead(
                () => root.Current.BoundingRectangle,
                Rect.Empty);
            if (rootRectangle.IsEmpty || !rootRectangle.Contains(target))
            {
                throw new DraftUiException(
                    "draft-ui-power-track-unavailable");
            }

            var inputs = new[]
            {
                MouseButtonInput(MouseEventLeftDown),
                MouseButtonInput(MouseEventLeftUp),
            };
            var sent = SendInput(
                checked((uint)inputs.Length),
                inputs,
                Marshal.SizeOf<NativeInput>());
            if (sent != inputs.Length)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            _ = SetCursorPos(original.X, original.Y);
        }
    }

    private static AutomationElement? FindMenuItem(
        AutomationElement root,
        string name) =>
        FindElements(root, ControlType.MenuItem, visibleOnly: true)
            .SingleOrDefault(element => string.Equals(
                SafeRead(() => element.Current.Name, string.Empty),
                name,
                StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<AutomationElement> FindByExactName(
        AutomationElement root,
        string name)
    {
        var found = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.NameProperty,
                name));
        var result = new List<AutomationElement>(found.Count);
        for (var index = 0; index < found.Count; index++)
        {
            result.Add(found[index]);
        }

        return result;
    }

    private static IReadOnlyList<AutomationElement> FindElements(
        AutomationElement root,
        ControlType controlType,
        bool visibleOnly)
    {
        var found = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                controlType));
        var result = new List<AutomationElement>(found.Count);
        for (var index = 0; index < found.Count; index++)
        {
            var element = found[index];
            if (!visibleOnly || IsRendered(element))
            {
                result.Add(element);
            }
        }

        return result;
    }

    private static IReadOnlyList<AutomationElement> FindAll(
        AutomationElement root)
    {
        var found = root.FindAll(
            TreeScope.Descendants,
            System.Windows.Automation.Condition.TrueCondition);
        var result = new List<AutomationElement>(found.Count);
        for (var index = 0; index < found.Count; index++)
        {
            result.Add(found[index]);
        }

        return result;
    }

    private static bool HasModelRows(AutomationElement menu) =>
        FindElements(menu, ControlType.RadioButton, visibleOnly: true)
            .Any(row =>
                ParseModel(
                    SafeRead(
                        () => row.Current.Name,
                        string.Empty)) != CodexQuickModel.Unknown);

    private static CodexQuickModel ReadSelectedModel(AutomationElement menu)
    {
        foreach (var row in FindElements(
                     menu,
                     ControlType.RadioButton,
                     visibleOnly: true))
        {
            if (row.TryGetCurrentPattern(
                    SelectionItemPattern.Pattern,
                    out var rawPattern) &&
                rawPattern is SelectionItemPattern pattern &&
                SafeRead(() => pattern.Current.IsSelected, false))
            {
                var model = ParseModel(
                    SafeRead(() => row.Current.Name, string.Empty));
                if (model != CodexQuickModel.Unknown)
                {
                    return model;
                }
            }
        }

        return CodexQuickModel.Unknown;
    }

    private static ComposerSelection ReadTriggerSelection(
        AutomationElement trigger) =>
        ParseSelection(string.Join(" ", ReadAccessibleStrings(trigger)));

    private static ComposerSelection? ReadPowerSelection(
        AutomationElement power,
        AutomationElement menu)
    {
        var candidates = new List<(ComposerSelection Selection, int Score)>();
        AddSelectionCandidates(power, candidates, baseScore: 20);
        AddSelectionCandidates(menu, candidates, baseScore: 0);
        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Selection.Count)
            .Select(candidate => (ComposerSelection?)candidate.Selection)
            .FirstOrDefault();
    }

    private static void AddSelectionCandidates(
        AutomationElement root,
        ICollection<(ComposerSelection Selection, int Score)> candidates,
        int baseScore)
    {
        foreach (var text in ReadAccessibleStrings(root))
        {
            var selection = ParseSelection(text);
            if (selection.Model == CodexQuickModel.Unknown ||
                selection.Effort is null)
            {
                continue;
            }

            candidates.Add((
                selection,
                baseScore + (selection.Position > 0 ? 100 : 0)));
        }
    }

    private static IEnumerable<string> ReadAccessibleStrings(
        AutomationElement root)
    {
        var elements = new List<AutomationElement> { root };
        elements.AddRange(FindAll(root));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in elements)
        {
            foreach (var value in ReadOwnAccessibleStrings(element))
            {
                if (seen.Add(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IEnumerable<string> ReadOwnAccessibleStrings(
        AutomationElement element)
    {
        foreach (var value in new[]
                 {
                     SafeRead(
                         () => element.Current.Name,
                         string.Empty),
                     SafeRead(
                         () => element.Current.HelpText,
                         string.Empty),
                     SafeRead(
                         () => element.Current.ItemStatus,
                         string.Empty),
                 })
        {
            var normalized = value.Trim();
            if (normalized.Length > 0)
            {
                yield return normalized;
            }
        }
    }

    private static ComposerSelection ParseSelection(string text)
    {
        var model = ParseModel(text);
        var effort = ParseEffort(text);
        var position = 0;
        var count = 0;
        var match = PowerPositionPattern.Match(text);
        if (match.Success)
        {
            _ = int.TryParse(
                match.Groups["position"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out position);
            _ = int.TryParse(
                match.Groups["count"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out count);
        }

        return new(model, effort, position, count);
    }

    private static CodexQuickModel ParseModel(string value) =>
        CodexModelToggleService.ParseModelId(value);

    private static string? ParseEffort(string value)
    {
        foreach (var (label, effort) in new[]
                 {
                     ("Extra High", "xhigh"),
                     ("Extended", "high"),
                     ("Standard", "medium"),
                     ("Ultra", "ultra"),
                     ("Max", "max"),
                     ("XHigh", "xhigh"),
                     ("High", "high"),
                     ("Medium", "medium"),
                     ("Light", "low"),
                     ("Low", "low"),
                     ("Minimal", "low"),
                 })
        {
            if (value.Contains(label, StringComparison.OrdinalIgnoreCase))
            {
                return effort;
            }
        }

        return null;
    }

    private static int ResolvePowerPosition(string effort, int count)
    {
        var position = effort switch
        {
            "low" => 1,
            "medium" => 2,
            "high" => 3,
            "xhigh" => 4,
            "max" => 5,
            "ultra" => 6,
            _ => 0,
        };
        if (position <= 0 || position > count)
        {
            throw new DraftUiException("draft-ui-target-unavailable");
        }

        return position;
    }

    private static ComposerSelection PreferPowerSelection(
        ComposerSelection fallback,
        ComposerSelection? power) =>
        power is { } value &&
        value.Model != CodexQuickModel.Unknown &&
        value.Effort is not null
            ? value
            : fallback;

    private static bool IsLocked(AutomationElement element) =>
        ReadAccessibleStrings(element).Any(value =>
            value.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(
                "opens access options",
                StringComparison.OrdinalIgnoreCase));

    private static bool ContainsAccessibleText(
        AutomationElement element,
        string value) =>
        ReadAccessibleStrings(element).Any(text =>
            text.Contains(value, StringComparison.OrdinalIgnoreCase));

    private static string ModelLabel(CodexQuickModel model) =>
        model switch
        {
            CodexQuickModel.Sol => "5.6 Sol",
            CodexQuickModel.Terra => "5.6 Terra",
            CodexQuickModel.Luna => "5.6 Luna",
            _ => throw new ArgumentOutOfRangeException(nameof(model)),
        };

    private static ExpandCollapsePattern? GetExpandCollapsePattern(
        AutomationElement element) =>
        element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out var pattern) &&
            pattern is ExpandCollapsePattern expandCollapse
                ? expandCollapse
                : null;

    private static void Invoke(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out var invokePattern) &&
            invokePattern is InvokePattern invoke)
        {
            invoke.Invoke();
            return;
        }

        if (element.TryGetCurrentPattern(
                SelectionItemPattern.Pattern,
                out var selectionPattern) &&
            selectionPattern is SelectionItemPattern selection)
        {
            selection.Select();
            return;
        }

        throw new DraftUiException("draft-ui-action-unavailable");
    }

    private static bool IsRendered(AutomationElement element)
    {
        var rectangle = SafeRead(
            () => element.Current.BoundingRectangle,
            Rect.Empty);
        return !rectangle.IsEmpty &&
            rectangle.Width > 0 &&
            rectangle.Height > 0 &&
            !SafeRead(() => element.Current.IsOffscreen, true);
    }

    private static void EnsureCurrent(
        IntPtr foregroundWindow,
        Func<bool> isDraftCurrent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CodexWindowActivator.IsForegroundWindow(foregroundWindow) ||
            !isDraftCurrent())
        {
            throw new DraftUiException("visible-thread-changed");
        }
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
                InvalidOperationException or
                COMException)
        {
            return fallback;
        }
    }

    private static void SendVirtualKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            new NativeInput
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = virtualKey,
                    },
                },
            },
            new NativeInput
            {
                Type = InputKeyboard,
                Data = new NativeInputUnion
                {
                    Keyboard = new NativeKeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = KeyEventKeyUp,
                    },
                },
            },
        };
        var sent = SendInput(
            checked((uint)inputs.Length),
            inputs,
            Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static NativeInput MouseButtonInput(uint flags) =>
        new()
        {
            Type = InputMouse,
            Data = new NativeInputUnion
            {
                Mouse = new NativeMouseInput
                {
                    Flags = flags,
                },
            },
        };

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public NativeInputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct NativeInputUnion
    {
        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;

        [FieldOffset(0)]
        public NativeMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint count,
        [In] NativeInput[] inputs,
        int size);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);
}
