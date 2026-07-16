using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using CodexController.Agents;
using CodexController.Models;
using CodexController.Native;

[assembly: InternalsVisibleTo("AgentController.Tests")]

namespace CodexController.Services;

public enum ComposerSettingKind
{
    Model,
    Effort,
    Speed,
}

public sealed record ComposerModelOption(
    string Slug,
    string DisplayName,
    IReadOnlyList<string> Efforts);

public sealed class ComposerCatalog
{
    private static readonly string[] FallbackEfforts =
        ["Light", "Medium", "High", "Extra High", "Max", "Ultra"];

    public required IReadOnlyList<ComposerModelOption> Models { get; init; }
    public required int InitialModelIndex { get; init; }
    public required string InitialEffort { get; init; }
    public required string InitialSpeed { get; init; }

    public IReadOnlyList<string> EffortsForModel(int modelIndex)
    {
        if (Models.Count == 0)
        {
            return FallbackEfforts;
        }

        var safeIndex = Math.Clamp(modelIndex, 0, Models.Count - 1);
        var values = Models[safeIndex].Efforts;
        return values.Count > 0 ? values : FallbackEfforts;
    }
}

public sealed record ComposerAutomationResult(
    bool Succeeded,
    string? Error = null,
    string? ErrorDetail = null)
{
    public AgentAutomationError? Failure =>
        Error is null
            ? null
            : new AgentAutomationError(Error, ErrorDetail);
}

public sealed record ComposerDialResult(
    bool Succeeded,
    string? ControlName = null,
    bool IsMenuOpen = false,
    string? Error = null,
    string? ErrorDetail = null);

public sealed partial class CodexComposerService
{
    private const int DialPopupMountTimeoutMs = 240;
    private const int DialPopupPollIntervalMs = 24;

    private static readonly IReadOnlyList<ComposerModelOption> FallbackModels =
    [
        new("gpt-5.5", "5.5",
            ["Light", "Medium", "High", "Extra High"]),
        new("gpt-5.6-sol", "5.6 Sol",
            ["Light", "Medium", "High", "Extra High", "Max", "Ultra"]),
        new("gpt-5.6-terra", "5.6 Terra",
            ["Light", "Medium", "High", "Extra High", "Max", "Ultra"]),
        new("gpt-5.6-luna", "5.6 Luna",
            ["Light", "Medium", "High", "Extra High", "Max"]),
        new("gpt-5.4", "5.4",
            ["Light", "Medium", "High", "Extra High"]),
        new("gpt-5.4-mini", "5.4 Mini",
            ["Light", "Medium", "High", "Extra High"]),
        new("gpt-5.3-codex-spark", "5.3 Codex Spark",
            ["Light", "Medium", "High", "Extra High"]),
    ];
    private readonly object _dialSync = new();
    private readonly ComposerDialCursor _dialCursor = new();
    private bool _dialMenuOpen;
    private string? _dialControlKey;
    private string? _dialControlName;

    /// <summary>
    /// Reads the currently mounted Codex composer popup without focusing the
    /// window or injecting input. A normal closed state is a successful probe.
    /// </summary>
    public ComposerDialResult ProbeDialState()
    {
        lock (_dialSync)
        {
            try
            {
                var context = FindCodexWindow();
                if (context is null)
                {
                    return new(
                        false,
                        Error:
                            AgentAutomationErrorCodes.AgentWindowNotFound);
                }

                var probe = ProbeDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                if (!OwnsDialPopup(context.Value.Window, probe))
                {
                    ClearOwnedDialPopup();
                    return ComposerDialPolicy.CreateProbeResult(
                        isOpen: false,
                        focusedName: null);
                }

                UpdateOwnedDialPopup(probe);
                return ComposerDialPolicy.CreateProbeResult(
                    probe.IsOpen,
                    probe.FocusedName ?? _dialControlName);
            }
            catch (ElementNotAvailableException)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.AutomationStale,
                    ErrorDetail: "composer-dial-popup");
            }
            catch (Exception exception)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.Unexpected,
                    ErrorDetail: exception.Message);
            }
        }
    }

    public ComposerDialResult DialStep(
        int delta,
        AppSettings settings)
    {
        if (delta == 0)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-delta");
        }

        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return gate;
        }

        lock (_dialSync)
        {
            try
            {
                var context = FindCodexWindow();
                if (context is null)
                {
                    return new(
                        false,
                        Error:
                            AgentAutomationErrorCodes.AgentWindowNotFound);
                }

                var before = ProbeDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                if (_dialMenuOpen)
                {
                    if (!OwnsDialPopup(context.Value.Window, before))
                    {
                        ClearOwnedDialPopup();
                        return new(
                            false,
                            ControlName: _dialControlName,
                            IsMenuOpen: false,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-popup-focus-lost");
                    }

                    if (
                        !TryMoveDialPopupSelection(
                            before,
                            delta,
                            out var selectedName))
                    {
                        UpdateOwnedDialPopup(before);
                        return new(
                            false,
                            ControlName:
                                before.FocusedName ??
                                _dialControlName,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-step-no-selection-change");
                    }

                    var after = WaitForDialSelectionChange(
                        context.Value.Window,
                        context.Value.ProcessId,
                        before,
                        DialPopupMountTimeoutMs);
                    if (!OwnsDialPopup(context.Value.Window, after))
                    {
                        ClearOwnedDialPopup();
                        return new(
                            false,
                            ControlName:
                                selectedName ??
                                after.FocusedName ??
                                _dialControlName,
                            IsMenuOpen: false,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-popup-focus-lost");
                    }

                    UpdateOwnedDialPopup(after);
                    if (
                        after.IsOpen &&
                        !ComposerDialPolicy.HasFocusedSelectionChanged(
                            before.FocusedName,
                            after.FocusedName))
                    {
                        return new(
                            false,
                            ControlName:
                                selectedName ??
                                after.FocusedName ??
                                before.FocusedName ??
                                _dialControlName,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes.ElementUnsupported,
                            ErrorDetail: "dial-step-no-selection-change");
                    }

                    return new(
                        true,
                        ControlName:
                            selectedName ??
                            after.FocusedName ??
                            _dialControlName,
                        IsMenuOpen: after.IsOpen);
                }

                if (before.IsOpen)
                {
                    return new(
                        false,
                        ControlName: before.FocusedName,
                        IsMenuOpen: false,
                        Error:
                            AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail: "dial-popup-not-owned");
                }

                var controls = FindDialControls(context.Value.Window);
                var keys = controls
                    .Select(control => control.Key)
                    .ToArray();
                var index = _dialCursor.Move(keys, delta);
                if (index < 0)
                {
                    return new(
                        false,
                        Error:
                            AgentAutomationErrorCodes.ElementNotFound,
                        ErrorDetail: "composer-dial-controls");
                }

                var selected = controls[index];
                selected.Element.SetFocus();
                _dialControlName = selected.Name;
                return new(
                    true,
                    selected.Name,
                    IsMenuOpen: false);
            }
            catch (ElementNotAvailableException)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.AutomationStale,
                    ErrorDetail: "composer-dial-control");
            }
            catch (Exception exception)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.Unexpected,
                    ErrorDetail: exception.Message);
            }
        }
    }

    public ComposerDialResult DialPress(AppSettings settings)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return gate;
        }

        lock (_dialSync)
        {
            try
            {
                var context = FindCodexWindow();
                if (context is null)
                {
                    return new(
                        false,
                        Error:
                            AgentAutomationErrorCodes.AgentWindowNotFound);
                }

                var before = ProbeDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                if (_dialMenuOpen)
                {
                    if (!OwnsDialPopup(context.Value.Window, before))
                    {
                        ClearOwnedDialPopup();
                        return new(
                            false,
                            ControlName: _dialControlName,
                            IsMenuOpen: false,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-popup-focus-lost");
                    }

                    var selectedOption =
                        FindFocusedDialPopupOption(before);
                    var menuSelection = selectedOption?.Name;
                    var blockedReason =
                        ComposerDialActionPolicy.BlockReason(
                            menuSelection);
                    if (blockedReason is not null)
                    {
                        return new(
                            false,
                            menuSelection ?? _dialControlName,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes.ElementUnsupported,
                            ErrorDetail: blockedReason);
                    }

                    if (
                        selectedOption is null ||
                        !TryActivateDialPopupOption(
                            selectedOption.Element))
                    {
                        return new(
                            false,
                            menuSelection,
                            IsMenuOpen: true,
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                            "dial-selection-unverified");
                    }

                    var afterEnter = WaitForDialPopupTransition(
                        context.Value.Window,
                        context.Value.ProcessId,
                        before,
                        DialPopupMountTimeoutMs);
                    if (afterEnter.IsOpen)
                    {
                        UpdateOwnedDialPopup(afterEnter);
                    }
                    else
                    {
                        ClearOwnedDialPopup();
                        _dialControlName = menuSelection;
                    }

                    return new(
                        true,
                        afterEnter.FocusedName ?? menuSelection,
                        afterEnter.IsOpen);
                }

                if (before.IsOpen)
                {
                    return new(
                        false,
                        before.FocusedName,
                        IsMenuOpen: false,
                        Error:
                            AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail: "dial-popup-not-owned");
                }

                var controls = FindDialControls(context.Value.Window);
                if (controls.Count == 0)
                {
                    return new(
                        false,
                        Error:
                            AgentAutomationErrorCodes.ElementNotFound,
                        ErrorDetail: "composer-dial-controls");
                }

                var keys = controls
                    .Select(control => control.Key)
                    .ToArray();
                var index = _dialCursor.FindSelectedIndex(keys);
                if (index < 0)
                {
                    index = 0;
                    _dialCursor.Select(keys[index]);
                }

                var selected = controls[index];
                selected.Element.SetFocus();
                _dialControlName = selected.Name;
                if (
                    TryOpenDialControl(
                        selected.Element,
                        selected.AllowInvoke))
                {
                    var afterOpen = WaitForDialPopupOpen(
                        context.Value.Window,
                        context.Value.ProcessId,
                        DialPopupMountTimeoutMs);
                    if (!afterOpen.IsOpen)
                    {
                        ClearOwnedDialPopup();
                        return new(
                            false,
                            selected.Name,
                            IsMenuOpen: false,
                            Error:
                                AgentAutomationErrorCodes.ElementNotFound,
                            ErrorDetail: "composer-dial-popup");
                    }

                    _dialMenuOpen = true;
                    _dialControlKey = selected.Key;
                    UpdateOwnedDialPopup(afterOpen);
                    return new(
                        true,
                        afterOpen.FocusedName ?? selected.Name,
                        IsMenuOpen: true);
                }

                var afterFailure = ProbeDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                ClearOwnedDialPopup();
                return new(
                    false,
                    selected.Name,
                    afterFailure.IsOpen,
                    Error: AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "composer-dial-control:open");
            }
            catch (ElementNotAvailableException)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.AutomationStale,
                    ErrorDetail: "composer-dial-control");
            }
            catch (Exception exception)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.Unexpected,
                    ErrorDetail: exception.Message);
            }
        }
    }

    public ComposerDialResult DialCancel(AppSettings settings)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return gate;
        }

        lock (_dialSync)
        {
            try
            {
                var context = FindCodexWindow();
                if (context is null)
                {
                    return new(
                        false,
                        Error:
                            AgentAutomationErrorCodes.AgentWindowNotFound);
                }

                var before = ProbeDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                var selected =
                    before.FocusedName ??
                    _dialControlName;
                if (!_dialMenuOpen)
                {
                    return new(
                        true,
                        selected,
                        IsMenuOpen: false);
                }

                if (!OwnsDialPopup(context.Value.Window, before))
                {
                    ClearOwnedDialPopup();
                    return new(
                        false,
                        selected,
                        IsMenuOpen: false,
                        AgentAutomationErrorCodes.ElementUnsupported,
                        "dial-popup-focus-lost");
                }

                if (!TryCloseOwnedDialControl(context.Value.Window))
                {
                    return new(
                        false,
                        selected,
                        IsMenuOpen: true,
                        AgentAutomationErrorCodes.ElementUnsupported,
                        "dial-popup-close");
                }

                var after = WaitForDialPopupTransition(
                    context.Value.Window,
                    context.Value.ProcessId,
                    before,
                    DialPopupMountTimeoutMs);
                if (after.IsOpen)
                {
                    UpdateOwnedDialPopup(after);
                }
                else
                {
                    ClearOwnedDialPopup();
                }

                return new(
                    true,
                    after.FocusedName ?? selected,
                    after.IsOpen);
            }
            catch (ElementNotAvailableException)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.AutomationStale,
                    ErrorDetail: "composer-dial-popup");
            }
            catch (Exception exception)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.Unexpected,
                    ErrorDetail: exception.Message);
            }
        }
    }

    public ComposerCatalog LoadCatalog()
    {
        var codexHome = ResolveCodexHome();
        var models = LoadModels(Path.Combine(codexHome, "models_cache.json"));
        if (models.Count == 0)
        {
            models = FallbackModels;
        }

        var preferences = ReadConfig(Path.Combine(codexHome, "config.toml"));
        var buttonName = TryReadComposerButtonName();

        var modelIndex = FindModelIndex(
            models,
            buttonName,
            preferences.ModelSlug);
        var modelEfforts = models[modelIndex].Efforts;
        var effort = FindEffort(
            modelEfforts,
            buttonName,
            EffortLabel(preferences.Effort));

        return new ComposerCatalog
        {
            Models = models,
            InitialModelIndex = modelIndex,
            InitialEffort = effort,
            InitialSpeed = FindSpeed(
                buttonName,
                preferences.ServiceTier),
        };
    }

    public Task<ComposerAutomationResult> SelectAsync(
        ComposerSettingKind kind,
        string target,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => SelectCore(kind, target, settings, cancellationToken),
            cancellationToken);
    }

    public string? TryReadComposerButtonName()
    {
        try
        {
            var context = FindCodexWindow();
            var button =
                context is null
                    ? null
                    : FindComposerButton(context.Value.Window);
            return button?.Current.Name;
        }
        catch
        {
            return null;
        }
    }

    public string? TryReadDispatchButtonName()
    {
        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return null;
            }

            return FindVisibleNamedButton(
                    context.Value.Window,
                    [
                        "Steer",
                        "Steer current turn",
                        "Queue",
                        "Queue next turn",
                        "Send",
                        "Send message",
                        "Submit",
                        "Submit prompt",
                        "加入当前运行",
                        "排到下一轮",
                        "发送",
                        "提交",
                    ])
                ?.Current.Name;
        }
        catch
        {
            return null;
        }
    }

    public bool IsComposerActionAvailable(params string[] actionNames)
    {
        try
        {
            var context = FindCodexWindow();
            return
                context is not null &&
                FindVisibleNamedButton(
                    context.Value.Window,
                    actionNames) is not null;
        }
        catch
        {
            return false;
        }
    }

    public ComposerAutomationResult InvokeComposerAction(
        AppSettings settings,
        params string[] actionNames)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var targets = actionNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                AutomationElement? button;
                try
                {
                    button = context.Value.Window.FindFirst(
                        TreeScope.Descendants,
                        new AndCondition(
                            new PropertyCondition(
                                AutomationElement.ControlTypeProperty,
                                ControlType.Button),
                            new PropertyCondition(
                                AutomationElement.NameProperty,
                                target,
                                PropertyConditionFlags.IgnoreCase)));
                    if (button is null)
                    {
                        continue;
                    }

                    if (
                        !button.Current.IsEnabled ||
                        button.Current.IsOffscreen ||
                        button.Current.BoundingRectangle.IsEmpty)
                    {
                        continue;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    continue;
                }

                if (
                    !button.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out var patternObject) ||
                    patternObject is not InvokePattern pattern)
                {
                    continue;
                }

                pattern.Invoke();
                return new(true);
            }

            return new(
                false,
                AgentAutomationErrorCodes.ElementNotFound,
                $"action:{string.Join("|", actionNames)}");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    public Task<ComposerAutomationResult> InvokeComposerActionAsync(
        AppSettings settings,
        int timeoutMs,
        CancellationToken cancellationToken,
        params string[] actionNames)
    {
        return Task.Run(() =>
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            ComposerAutomationResult result;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = InvokeComposerAction(settings, actionNames);
                if (result.Succeeded)
                {
                    return result;
                }

                if (AgentAutomationErrorCodes.IsImmediateFailure(
                        result.Error))
                {
                    return result;
                }

                Thread.Sleep(45);
            }
            while (Environment.TickCount64 < deadline);

            return result;
        }, cancellationToken);
    }

    public ComposerAutomationResult SubmitComposer(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var namedSubmit = InvokeNamedButton(
                context.Value.Window,
                ["Send", "Send message", "Submit", "Submit prompt",
                 "Transcribe and send"]);
            if (namedSubmit.Succeeded)
            {
                return namedSubmit;
            }

            var editor = FindComposerEditor(context.Value.Window);
            if (editor is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    "composer-editor");
            }

            if (
                editor.TryGetCurrentPattern(
                    TextPattern.Pattern,
                    out var textObject) &&
                textObject is TextPattern textPattern &&
                string.IsNullOrWhiteSpace(
                    textPattern.DocumentRange.GetText(-1)))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ComposerEmpty);
            }

            editor.SetFocus();
            Thread.Sleep(45);
            if (
                !Win32Input.IsCodexForeground() &&
                !Win32Input.FocusCodexAndWait())
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.FocusRejected,
                    "composer-editor");
            }

            editor.SetFocus();
            Thread.Sleep(25);
            return Win32Input.SendKey(0x0D)
                ? new(true)
                : new(
                    false,
                    AgentAutomationErrorCodes.InputInjectionFailed,
                    "Enter");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    public ComposerAutomationResult CancelComposer(AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        try
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var namedCancel = InvokeNamedButton(
                context.Value.Window,
                ["Stop", "Cancel", "Cancel request"]);
            if (namedCancel.Succeeded)
            {
                return namedCancel;
            }

            var editor = FindComposerEditor(context.Value.Window);
            if (editor is not null)
            {
                editor.SetFocus();
                Thread.Sleep(45);
            }
            else if (!Win32Input.FocusCodexAndWait())
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.FocusRejected,
                    "composer-editor");
            }

            return Win32Input.SendKey(0x1B)
                ? new(true)
                : new(
                    false,
                    AgentAutomationErrorCodes.InputInjectionFailed,
                    "Escape");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
    }

    private static ComposerAutomationResult InvokeNamedButton(
        AutomationElement window,
        IReadOnlyCollection<string> actionNames)
    {
        var button = FindVisibleNamedButton(window, actionNames);
        if (
            button is not null &&
            button.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out var patternObject) &&
            patternObject is InvokePattern pattern)
        {
            try
            {
                pattern.Invoke();
                return new(true);
            }
            catch (ElementNotAvailableException)
            {
                // Chromium replaced the button between discovery and invoke.
            }
        }

        return new(
            false,
            AgentAutomationErrorCodes.ElementNotFound,
            $"action:{string.Join("|", actionNames)}");
    }

    private static AutomationElement? FindVisibleNamedButton(
        AutomationElement window,
        IReadOnlyCollection<string> actionNames)
    {
        var targets = actionNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(NormalizeChoice)
            .ToHashSet(StringComparer.Ordinal);
        if (targets.Count == 0)
        {
            return null;
        }

        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            try
            {
                if (
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    button.Current.BoundingRectangle.IsEmpty ||
                    !targets.Contains(NormalizeChoice(button.Current.Name)))
                {
                    continue;
                }

                return button;
            }
            catch (ElementNotAvailableException)
            {
                // Continue looking if Chromium replaced a button mid-query.
            }
        }

        return null;
    }

    private static AutomationElement? FindComposerEditor(
        AutomationElement window)
    {
        var groups = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Group));
        foreach (AutomationElement group in groups)
        {
            try
            {
                var className = group.Current.ClassName ?? string.Empty;
                if (
                    group.Current.IsEnabled &&
                    !group.Current.IsOffscreen &&
                    !group.Current.BoundingRectangle.IsEmpty &&
                    className
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Contains("ProseMirror", StringComparer.Ordinal))
                {
                    return group;
                }
            }
            catch (ElementNotAvailableException)
            {
                // Continue looking for the live editor.
            }
        }

        return null;
    }

    private static IReadOnlyList<DialControl> FindDialControls(
        AutomationElement window)
    {
        var editor = FindComposerEditor(window);
        if (editor is null)
        {
            return [];
        }

        System.Windows.Rect editorBounds;
        try
        {
            editorBounds = editor.Current.BoundingRectangle;
        }
        catch (ElementNotAvailableException)
        {
            return [];
        }

        var composerRegion = new System.Windows.Rect(
            editorBounds.Left - 80,
            editorBounds.Top - 180,
            editorBounds.Width + 160,
            editorBounds.Height + 360);
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        var controls = new List<DialControl>();
        foreach (AutomationElement button in buttons)
        {
            try
            {
                var name = button.Current.Name?.Trim() ?? string.Empty;
                var className = button.Current.ClassName ?? string.Empty;
                var bounds = button.Current.BoundingRectangle;
                if (
                    name.Length == 0 ||
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    bounds.IsEmpty ||
                    !composerRegion.IntersectsWith(bounds) ||
                    !ComposerDialPolicy.HasComposerButtonClassToken(
                        className) ||
                    ComposerDialPolicy.HasClassToken(
                        className,
                        "aspect-square") ||
                    !ComposerDialActionPolicy.IsPickerControl(name) ||
                    !CanOpenDialControl(
                        button,
                        className,
                        out var allowInvoke))
                {
                    continue;
                }

                var automationId =
                    button.Current.AutomationId?.Trim() ?? string.Empty;
                var stableKey = automationId.Length > 0
                    ? $"id:{automationId}"
                    : $"name:{NormalizeChoice(name)}";
                controls.Add(new DialControl(
                    stableKey,
                    name,
                    bounds.Left,
                    bounds.Top,
                    allowInvoke,
                    button));
            }
            catch (ElementNotAvailableException)
            {
                // Chromium may replace one composer button mid-query.
            }
        }

        return controls
            .OrderBy(control => control.Left)
            .ThenBy(control => control.Top)
            .ToArray();
    }

    private static bool CanOpenDialControl(
        AutomationElement element,
        string className,
        out bool allowInvoke)
    {
        allowInvoke = false;
        if (
            element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out _))
        {
            return true;
        }

        bool isKeyboardFocusable;
        try
        {
            isKeyboardFocusable =
                element.Current.IsKeyboardFocusable;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }

        allowInvoke =
            ComposerDialPolicy.IsConservativeInvokeTrigger(
                className,
                isKeyboardFocusable) &&
            element.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out _);
        return allowInvoke;
    }

    private static bool TryOpenDialControl(
        AutomationElement element,
        bool allowInvoke)
    {
        if (
            element.TryGetCurrentPattern(
                ExpandCollapsePattern.Pattern,
                out var expandObject) &&
            expandObject is ExpandCollapsePattern expand)
        {
            if (
                expand.Current.ExpandCollapseState !=
                ExpandCollapseState.Expanded)
            {
                expand.Expand();
            }

            return true;
        }

        if (
            allowInvoke &&
            element.TryGetCurrentPattern(
                InvokePattern.Pattern,
                out var invokeObject) &&
            invokeObject is InvokePattern invoke)
        {
            invoke.Invoke();
            return true;
        }

        return false;
    }

    private bool OwnsDialPopup(
        AutomationElement window,
        DialPopupProbe probe)
    {
        if (
            !_dialMenuOpen ||
            string.IsNullOrWhiteSpace(_dialControlKey) ||
            !probe.IsOpen)
        {
            return false;
        }

        var control = FindDialControls(window)
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Key,
                    _dialControlKey,
                    StringComparison.Ordinal));
        return
            control is not null &&
            IsDialControlExpanded(control.Element);
    }

    private void UpdateOwnedDialPopup(DialPopupProbe probe)
    {
        _dialMenuOpen = probe.IsOpen;
        if (!string.IsNullOrWhiteSpace(probe.FocusedName))
        {
            _dialControlName = probe.FocusedName;
        }
    }

    private void ClearOwnedDialPopup()
    {
        _dialMenuOpen = false;
        _dialControlKey = null;
    }

    private static bool IsDialControlExpanded(
        AutomationElement element)
    {
        try
        {
            return
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) &&
                patternObject is ExpandCollapsePattern pattern &&
                pattern.Current.ExpandCollapseState is
                    ExpandCollapseState.Expanded or
                    ExpandCollapseState.PartiallyExpanded;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private bool TryCloseOwnedDialControl(
        AutomationElement window)
    {
        if (string.IsNullOrWhiteSpace(_dialControlKey))
        {
            return false;
        }

        var control = FindDialControls(window)
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Key,
                    _dialControlKey,
                    StringComparison.Ordinal));
        if (control is null)
        {
            return false;
        }

        try
        {
            if (
                !control.Element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) ||
                patternObject is not ExpandCollapsePattern pattern)
            {
                return false;
            }

            if (
                pattern.Current.ExpandCollapseState is
                    ExpandCollapseState.Expanded or
                    ExpandCollapseState.PartiallyExpanded)
            {
                pattern.Collapse();
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static DialPopupProbe WaitForDialPopupOpen(
        AutomationElement window,
        int processId,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var last = ProbeDialPopup(window, processId);
        var stableOpenSamples = 0;
        while (Environment.TickCount64 < deadline)
        {
            if (
                last.IsOpen &&
                (
                    !string.IsNullOrWhiteSpace(last.FocusedName) ||
                    stableOpenSamples >= 1
                ))
            {
                return last;
            }

            Thread.Sleep(DialPopupPollIntervalMs);
            var current = ProbeDialPopup(window, processId);
            stableOpenSamples =
                current.IsOpen &&
                ComposerDialPolicy.IsSamePopupState(
                    last.IsOpen,
                    last.Signature,
                    current.IsOpen,
                    current.Signature)
                    ? stableOpenSamples + 1
                    : 0;
            last = current;
        }

        return last;
    }

    private static DialPopupProbe WaitForDialPopupTransition(
        AutomationElement window,
        int processId,
        DialPopupProbe before,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var last = ProbeDialPopup(window, processId);
        var observedTransition =
            !ComposerDialPolicy.IsSamePopupState(
                before.IsOpen,
                before.Signature,
                last.IsOpen,
                last.Signature);
        var stableSamples = 0;
        while (Environment.TickCount64 < deadline)
        {
            if (observedTransition && stableSamples >= 1)
            {
                return last;
            }

            Thread.Sleep(DialPopupPollIntervalMs);
            var current = ProbeDialPopup(window, processId);
            if (
                !ComposerDialPolicy.IsSamePopupState(
                    before.IsOpen,
                    before.Signature,
                    current.IsOpen,
                    current.Signature))
            {
                observedTransition = true;
            }

            stableSamples =
                observedTransition &&
                ComposerDialPolicy.IsSamePopupState(
                    last.IsOpen,
                    last.Signature,
                    current.IsOpen,
                    current.Signature)
                    ? stableSamples + 1
                    : 0;
            last = current;
        }

        return last;
    }

    private static DialPopupProbe WaitForDialSelectionChange(
        AutomationElement window,
        int processId,
        DialPopupProbe before,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var last = ProbeDialPopup(window, processId);
        while (Environment.TickCount64 < deadline)
        {
            if (
                !last.IsOpen ||
                ComposerDialPolicy.HasFocusedSelectionChanged(
                    before.FocusedName,
                    last.FocusedName))
            {
                return last;
            }

            Thread.Sleep(DialPopupPollIntervalMs);
            last = ProbeDialPopup(window, processId);
        }

        return last;
    }

    private static bool TryMoveDialPopupSelection(
        DialPopupProbe probe,
        int delta,
        out string? selectedName)
    {
        selectedName = null;
        if (probe.ActiveOptions.Count == 0)
        {
            return false;
        }

        var currentIndex = -1;
        for (var index = 0; index < probe.ActiveOptions.Count; index++)
        {
            try
            {
                if (probe.ActiveOptions[index]
                    .Element.Current.HasKeyboardFocus)
                {
                    currentIndex = index;
                    break;
                }
            }
            catch (ElementNotAvailableException)
            {
                // The option list is re-probed after every detent.
            }
        }

        var targetIndex = ComposerDialPolicy.ResolveVisualOptionIndex(
            probe.ActiveOptions.Count,
            currentIndex,
            delta);
        if (targetIndex < 0)
        {
            return false;
        }

        var target = probe.ActiveOptions[targetIndex];
        try
        {
            target.Element.SetFocus();
            selectedName = target.Name;
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static DialPopupOption? FindFocusedDialPopupOption(
        DialPopupProbe probe)
    {
        foreach (var option in probe.ActiveOptions)
        {
            try
            {
                if (option.Element.Current.HasKeyboardFocus)
                {
                    return option;
                }
            }
            catch (ElementNotAvailableException)
            {
                // A replaced Chromium node is not a confirmable selection.
            }
        }

        return null;
    }

    private static bool TryActivateDialPopupOption(
        AutomationElement element)
    {
        try
        {
            if (
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var expandObject) &&
                expandObject is ExpandCollapsePattern expand)
            {
                var state = expand.Current.ExpandCollapseState;
                if (state == ExpandCollapseState.Collapsed)
                {
                    expand.Expand();
                    return true;
                }

                if (
                    state is
                        ExpandCollapseState.Expanded or
                        ExpandCollapseState.PartiallyExpanded)
                {
                    return true;
                }
            }

            if (
                element.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var invokeObject) &&
                invokeObject is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }

            if (
                element.TryGetCurrentPattern(
                    SelectionItemPattern.Pattern,
                    out var selectionObject) &&
                selectionObject is SelectionItemPattern selection)
            {
                selection.Select();
                return true;
            }

            if (
                element.TryGetCurrentPattern(
                    TogglePattern.Pattern,
                    out var toggleObject) &&
                toggleObject is TogglePattern toggle)
            {
                toggle.Toggle();
                return true;
            }
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        return false;
    }

    private static DialPopupProbe ProbeDialPopup(
        AutomationElement window,
        int processId)
    {
        var composerRegion = TryGetComposerPopupRegion(window);
        var snapshots = new List<string>();
        var popupOptions = new List<DialPopupOption>();
        string? focusedName = null;
        foreach (var root in FindDialPopupRoots(window, processId))
        {
            var elements = new List<AutomationElement>();
            try
            {
                elements.Add(root.Element);
                elements.AddRange(
                    root.Element.FindAll(
                            TreeScope.Descendants,
                            new OrCondition(
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.Menu),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.List),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.MenuItem),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.ListItem),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.DataItem),
                                new PropertyCondition(
                                    AutomationElement.ControlTypeProperty,
                                    ControlType.Edit)))
                        .Cast<AutomationElement>());
            }
            catch
            {
                continue;
            }

            foreach (var element in elements)
            {
                try
                {
                    var current = element.Current;
                    var bounds = current.BoundingRectangle;
                    var kind = DialPopupKind(current.ControlType);
                    if (kind is null)
                    {
                        continue;
                    }

                    var nearComposer =
                        composerRegion is { } region &&
                        region.Contains(
                            new System.Windows.Point(
                                bounds.Left + bounds.Width / 2,
                                bounds.Top + bounds.Height / 2));
                    var hasPopupAncestor =
                        HasPopupAncestor(
                            element,
                            root.Element,
                            includeList:
                                kind is
                                    ComposerDialPopupElementKind.Edit or
                                    ComposerDialPopupElementKind.OptionItem);
                    var semanticSearch =
                        kind == ComposerDialPopupElementKind.Edit &&
                        ComposerDialPolicy.LooksLikeSearchEdit(
                            current.Name,
                            current.AutomationId,
                            current.ClassName,
                            current.HelpText);
                    var evidence = new ComposerDialPopupEvidence(
                        kind.Value,
                        current.IsEnabled,
                        current.IsOffscreen,
                        bounds.IsEmpty,
                        nearComposer,
                        current.HasKeyboardFocus,
                        current.IsKeyboardFocusable,
                        root.IsPopupWindow,
                        hasPopupAncestor,
                        semanticSearch,
                        element.TryGetCurrentPattern(
                            SelectionPattern.Pattern,
                            out _) ||
                        element.TryGetCurrentPattern(
                            SelectionItemPattern.Pattern,
                            out _));
                    if (!ComposerDialPolicy.IsPopupEvidence(evidence))
                    {
                        continue;
                    }

                    var name = current.Name?.Trim() ?? string.Empty;
                    var automationId =
                        current.AutomationId?.Trim() ?? string.Empty;
                    snapshots.Add(
                        string.Join(
                            ':',
                            kind.Value,
                            NormalizeChoice(name),
                            NormalizeChoice(automationId),
                            Math.Round(bounds.Left),
                            Math.Round(bounds.Top),
                            Math.Round(bounds.Width),
                            Math.Round(bounds.Height)));
                    if (
                        (
                            kind is
                                ComposerDialPopupElementKind.MenuItem or
                                ComposerDialPopupElementKind.OptionItem
                        ) &&
                        name.Length > 0 &&
                        current.IsKeyboardFocusable &&
                        TryGetDialPopupContainer(
                            element,
                            root.Element,
                            out var containerKey,
                            out var containerBounds))
                    {
                        popupOptions.Add(
                            new DialPopupOption(
                                name,
                                containerKey,
                                containerBounds.Left,
                                containerBounds.Top,
                                bounds.Left,
                                bounds.Top,
                                element));
                    }

                    if (
                        current.HasKeyboardFocus &&
                        name.Length > 0)
                    {
                        focusedName = name;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // Chromium can replace popup nodes while they mount.
                }
            }
        }

        var isOpen = snapshots.Count > 0;
        if (isOpen && string.IsNullOrWhiteSpace(focusedName))
        {
            focusedName = TryReadFocusedPopupName(
                processId,
                composerRegion);
        }

        var activeOptions = popupOptions
            .GroupBy(
                option => option.ContainerKey,
                StringComparer.Ordinal)
            .OrderByDescending(group =>
                group.First().ContainerLeft)
            .ThenByDescending(group =>
                group.First().ContainerTop)
            .Select(group =>
                group
                    .OrderBy(option => option.Top)
                    .ThenBy(option => option.Left)
                    .ToArray())
            .FirstOrDefault() ??
            [];
        return new DialPopupProbe(
            isOpen,
            focusedName,
            string.Join(
                '|',
                snapshots
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)),
            activeOptions);
    }

    private static bool TryGetDialPopupContainer(
        AutomationElement element,
        AutomationElement root,
        out string key,
        out System.Windows.Rect bounds)
    {
        key = string.Empty;
        bounds = System.Windows.Rect.Empty;
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = walker.GetParent(element);
            for (
                var depth = 0;
                current is not null &&
                depth < 16;
                depth++)
            {
                var controlType = current.Current.ControlType;
                if (
                    controlType == ControlType.Menu ||
                    controlType == ControlType.List)
                {
                    bounds = current.Current.BoundingRectangle;
                    if (bounds.IsEmpty)
                    {
                        return false;
                    }

                    var runtimeId = current.GetRuntimeId();
                    key = runtimeId is { Length: > 0 }
                        ? string.Join('.', runtimeId)
                        : string.Join(
                            ':',
                            controlType.ProgrammaticName,
                            current.Current.Name,
                            Math.Round(bounds.Left),
                            Math.Round(bounds.Top));
                    return true;
                }

                if (current.Equals(root))
                {
                    break;
                }

                current = walker.GetParent(current);
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static System.Windows.Rect? TryGetComposerPopupRegion(
        AutomationElement window)
    {
        var editor = FindComposerEditor(window);
        if (editor is null)
        {
            return null;
        }

        try
        {
            var bounds = editor.Current.BoundingRectangle;
            return new System.Windows.Rect(
                bounds.Left - 160,
                bounds.Top - 720,
                bounds.Width + 320,
                bounds.Height + 900);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static IReadOnlyList<DialPopupRoot> FindDialPopupRoots(
        AutomationElement mainWindow,
        int processId)
    {
        var roots = new List<DialPopupRoot>
        {
            new(mainWindow, IsPopupWindow: false),
        };
        try
        {
            var processWindows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(
                    AutomationElement.ProcessIdProperty,
                    processId));
            foreach (AutomationElement processWindow in processWindows)
            {
                if (!processWindow.Equals(mainWindow))
                {
                    roots.Add(
                        new(
                            processWindow,
                            IsPopupWindow: true));
                }
            }
        }
        catch
        {
            // The main window still provides the normal Electron popup tree.
        }

        return roots;
    }

    private static bool HasPopupAncestor(
        AutomationElement element,
        AutomationElement root,
        bool includeList)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var current = walker.GetParent(element);
            for (
                var depth = 0;
                current is not null &&
                depth < 16 &&
                !current.Equals(root);
                depth++)
            {
                var type = current.Current.ControlType;
                if (
                    type == ControlType.Menu ||
                    type == ControlType.MenuItem ||
                    (includeList && type == ControlType.List))
                {
                    return true;
                }

                current = walker.GetParent(current);
            }
        }
        catch
        {
            // A focused localized Search edit is sufficient without ancestry.
        }

        return false;
    }

    private static string? TryReadFocusedPopupName(
        int processId,
        System.Windows.Rect? composerRegion)
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (
                focused is null ||
                focused.Current.ProcessId != processId ||
                focused.Current.IsOffscreen ||
                focused.Current.BoundingRectangle.IsEmpty)
            {
                return null;
            }

            if (DialPopupKind(focused.Current.ControlType) is null)
            {
                return null;
            }

            var bounds = focused.Current.BoundingRectangle;
            if (
                composerRegion is not { } region ||
                !region.Contains(
                    new System.Windows.Point(
                        bounds.Left + bounds.Width / 2,
                        bounds.Top + bounds.Height / 2)))
            {
                return null;
            }

            var name = focused.Current.Name?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static ComposerDialPopupElementKind? DialPopupKind(
        ControlType type)
    {
        if (type == ControlType.Menu)
        {
            return ComposerDialPopupElementKind.Menu;
        }

        if (type == ControlType.List)
        {
            return ComposerDialPopupElementKind.ListBox;
        }

        if (type == ControlType.MenuItem)
        {
            return ComposerDialPopupElementKind.MenuItem;
        }

        if (
            type == ControlType.ListItem ||
            type == ControlType.DataItem)
        {
            return ComposerDialPopupElementKind.OptionItem;
        }

        if (type == ControlType.Edit)
        {
            return ComposerDialPopupElementKind.Edit;
        }

        return null;
    }

    private static ComposerDialResult? ValidateInteractiveBridge(
        AppSettings settings)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.AgentNotForeground);
        }

        if (
            !Win32Input.IsCodexForeground() &&
            !Win32Input.FocusCodexAndWait())
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.FocusRejected,
                ErrorDetail: "composer-dial");
        }

        return null;
    }

    private sealed record DialControl(
        string Key,
        string Name,
        double Left,
        double Top,
        bool AllowInvoke,
        AutomationElement Element);

    private sealed record DialPopupRoot(
        AutomationElement Element,
        bool IsPopupWindow);

    private sealed record DialPopupOption(
        string Name,
        string ContainerKey,
        double ContainerLeft,
        double ContainerTop,
        double Left,
        double Top,
        AutomationElement Element);

    private sealed record DialPopupProbe(
        bool IsOpen,
        string? FocusedName,
        string Signature,
        IReadOnlyList<DialPopupOption> ActiveOptions);

    private static ComposerAutomationResult SelectCore(
        ComposerSettingKind kind,
        string target,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.BridgeEnabled)
        {
            return new(
                false,
                AgentAutomationErrorCodes.BridgeSafePreview);
        }

        if (
            settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            return new(
                false,
                AgentAutomationErrorCodes.AgentNotForeground);
        }

        if (
            !settings.OnlyWhenCodexForeground &&
            !Win32Input.IsCodexForeground())
        {
            _ = Win32Input.FocusCodex();
            Thread.Sleep(90);
        }

        AutomationElement? composerButton = null;
        AutomationElement? categoryItem = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            composerButton = FindComposerButton(context.Value.Window);
            if (composerButton is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    "composer-model-button");
            }

            if (!TryExpand(composerButton))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementUnsupported,
                    "composer-model-button:expand");
            }

            var category = CategoryLabel(kind);
            categoryItem = WaitForMenuItem(
                context.Value.Window,
                context.Value.ProcessId,
                name =>
                    name.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith(
                        $"{category} ",
                        StringComparison.OrdinalIgnoreCase),
                cancellationToken);
            if (categoryItem is null || !TryExpand(categoryItem))
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    $"composer-submenu:{category}");
            }

            var option = WaitForBestOption(
                context.Value.Window,
                context.Value.ProcessId,
                category,
                target,
                cancellationToken);
            if (option is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    $"composer-option:{target}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (
                !option.TryGetCurrentPattern(
                    InvokePattern.Pattern,
                    out var invokeObject) ||
                invokeObject is not InvokePattern invoke)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementUnsupported,
                    $"composer-option:{target}:select");
            }

            // Only the final settled choice is invoked; previews never touch Codex.
            invoke.Invoke();
            return new(true);
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.OperationCanceled);
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                AgentAutomationErrorCodes.AutomationStale,
                "composer-menu");
        }
        catch (Exception exception)
        {
            return new(
                false,
                AgentAutomationErrorCodes.Unexpected,
                exception.Message);
        }
        finally
        {
            TryCollapse(categoryItem);
            TryCollapse(composerButton);
        }
    }

    private static (AutomationElement Window, int ProcessId)? FindCodexWindow()
    {
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            if (process.MainWindowHandle == nint.Zero)
            {
                process.Dispose();
                continue;
            }

            var processId = process.Id;
            var handle = process.MainWindowHandle;
            process.Dispose();
            var window = AutomationElement.FromHandle(handle);
            if (window is not null)
            {
                return (window, processId);
            }
        }

        return null;
    }

    private static AutomationElement? FindComposerButton(
        AutomationElement window)
    {
        var buttons = window.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            string name;
            string className;
            try
            {
                name = button.Current.Name?.Trim() ?? string.Empty;
                className = button.Current.ClassName ?? string.Empty;
                if (
                    !button.Current.IsEnabled ||
                    button.Current.IsOffscreen ||
                    button.Current.BoundingRectangle.IsEmpty)
                {
                    continue;
                }
            }
            catch (ElementNotAvailableException)
            {
                continue;
            }

            if (
                name.Length > 0 &&
                char.IsDigit(name[0]) &&
                ComposerDialPolicy.HasComposerButtonClassToken(
                    className))
            {
                return button;
            }
        }

        return null;
    }

    private static AutomationElement? WaitForMenuItem(
        AutomationElement window,
        int processId,
        Func<string, bool> predicate,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = FindMenuItems(window, processId)
                .Where(IsUsableMenuElement)
                .FirstOrDefault(element => predicate(SafeName(element)));
            if (item is not null)
            {
                return item;
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static AutomationElement? WaitForBestOption(
        AutomationElement window,
        int processId,
        string category,
        string target,
        CancellationToken cancellationToken)
    {
        var normalizedTarget = NormalizeChoice(target);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = FindMenuItems(window, processId)
                .Where(IsUsableMenuElement)
                .Select(element => new
                {
                    Element = element,
                    Name = SafeName(element),
                })
                .Where(item =>
                    !item.Name.StartsWith(
                        $"{category} ",
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => new
                {
                    item.Element,
                    item.Name,
                    Normalized = NormalizeChoice(item.Name),
                })
                .Where(item =>
                    item.Normalized.Equals(
                        normalizedTarget,
                        StringComparison.Ordinal) ||
                    item.Normalized.StartsWith(
                        normalizedTarget,
                        StringComparison.Ordinal))
                .OrderBy(item =>
                    item.Normalized.Equals(
                        normalizedTarget,
                        StringComparison.Ordinal)
                        ? 0
                        : 1)
                .ThenBy(item => item.Normalized.Length)
                .ToArray();

            foreach (var candidate in candidates)
            {
                if (
                    candidate.Element.TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out var pattern) &&
                    pattern is InvokePattern)
                {
                    return candidate.Element;
                }
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static IEnumerable<AutomationElement> FindMenuItems(
        AutomationElement mainWindow,
        int processId)
    {
        var menuItemCondition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            ControlType.MenuItem);
        var processWindowCondition = new PropertyCondition(
            AutomationElement.ProcessIdProperty,
            processId);
        var roots = new List<AutomationElement> { mainWindow };
        var processWindows = AutomationElement.RootElement.FindAll(
            TreeScope.Children,
            processWindowCondition);
        roots.AddRange(
            processWindows
                .Cast<AutomationElement>()
                .Where(item => !item.Equals(mainWindow)));

        foreach (var root in roots)
        {
            AutomationElementCollection collection;
            try
            {
                collection = root.FindAll(
                    TreeScope.Descendants,
                    menuItemCondition);
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement item in collection)
            {
                yield return item;
            }
        }
    }

    private static bool TryExpand(AutomationElement element)
    {
        try
        {
            if (
                !element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) ||
                patternObject is not ExpandCollapsePattern pattern)
            {
                return false;
            }

            if (
                pattern.Current.ExpandCollapseState !=
                ExpandCollapseState.Expanded)
            {
                pattern.Expand();
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryCollapse(AutomationElement? element)
    {
        if (element is null)
        {
            return;
        }

        try
        {
            if (
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) &&
                patternObject is ExpandCollapsePattern pattern &&
                pattern.Current.ExpandCollapseState ==
                ExpandCollapseState.Expanded)
            {
                pattern.Collapse();
            }
        }
        catch
        {
            // Invoking a menu option commonly destroys the popup immediately.
        }
    }

    private static string SafeName(AutomationElement element)
    {
        try
        {
            return element.Current.Name?.Trim() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsUsableMenuElement(AutomationElement element)
    {
        try
        {
            return element.Current.IsEnabled && !element.Current.IsOffscreen;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<ComposerModelOption> LoadModels(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (
                !document.RootElement.TryGetProperty(
                    "models",
                    out var modelsElement) ||
                modelsElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var models = new List<(
                ComposerModelOption Option,
                int Priority,
                int SourceOrder)>();
            var sourceOrder = 0;
            foreach (var model in modelsElement.EnumerateArray())
            {
                if (
                    GetString(model, "visibility") != "list" ||
                    GetString(model, "slug") is not { Length: > 0 } slug ||
                    GetString(model, "display_name") is not { Length: > 0 }
                        displayName)
                {
                    continue;
                }

                var efforts = new List<string>();
                if (
                    model.TryGetProperty(
                        "supported_reasoning_levels",
                        out var effortElement) &&
                    effortElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var effort in effortElement.EnumerateArray())
                    {
                        var label = EffortLabel(GetString(effort, "effort"));
                        if (
                            label.Length > 0 &&
                            !efforts.Contains(
                                label,
                                StringComparer.OrdinalIgnoreCase))
                        {
                            efforts.Add(label);
                        }
                    }
                }

                models.Add((
                    new ComposerModelOption(
                        slug,
                        ModelLabel(displayName),
                        efforts),
                    GetInt(model, "priority") ?? int.MaxValue,
                    sourceOrder++));
            }

            return models
                .OrderBy(item => item.Priority)
                .ThenBy(item => item.SourceOrder)
                .Select(item => item.Option)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static ConfigPreferences ReadConfig(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return new(
                MatchTomlString(text, "model"),
                MatchTomlString(text, "model_reasoning_effort"),
                MatchTomlString(text, "service_tier"));
        }
        catch
        {
            return new(null, null, null);
        }
    }

    private static string? MatchTomlString(string text, string key)
    {
        var match = Regex.Match(
            text,
            $@"(?m)^\s*{Regex.Escape(key)}\s*=\s*[""']([^""']+)[""']");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int FindModelIndex(
        IReadOnlyList<ComposerModelOption> models,
        string? buttonName,
        string? configuredSlug)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalizedButton = NormalizeChoice(buttonName);
            var match = models
                .Select((model, index) => new
                {
                    Index = index,
                    Length = NormalizeChoice(model.DisplayName).Length,
                    Matches = normalizedButton.StartsWith(
                        NormalizeChoice(model.DisplayName),
                        StringComparison.Ordinal),
                })
                .Where(item => item.Matches)
                .OrderByDescending(item => item.Length)
                .FirstOrDefault();
            if (match is not null)
            {
                return match.Index;
            }
        }

        var configuredIndex = models
            .Select((model, index) => new { model, index })
            .FirstOrDefault(item =>
                string.Equals(
                    item.model.Slug,
                    configuredSlug,
                    StringComparison.OrdinalIgnoreCase))?.index;
        return configuredIndex ?? 0;
    }

    private static string FindEffort(
        IReadOnlyList<string> efforts,
        string? buttonName,
        string configuredEffort)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalizedButton = NormalizeChoice(buttonName);
            var fromButton = efforts
                .OrderByDescending(value => value.Length)
                .FirstOrDefault(value =>
                    normalizedButton.EndsWith(
                        NormalizeChoice(value),
                        StringComparison.Ordinal));
            if (fromButton is not null)
            {
                return fromButton;
            }
        }

        return efforts.FirstOrDefault(value =>
                   string.Equals(
                       value,
                       configuredEffort,
                       StringComparison.OrdinalIgnoreCase))
               ?? efforts.FirstOrDefault()
               ?? "Medium";
    }

    private static string FindSpeed(
        string? buttonName,
        string? configuredServiceTier)
    {
        if (!string.IsNullOrWhiteSpace(buttonName))
        {
            var normalized = NormalizeChoice(buttonName);
            if (normalized.EndsWith("standard", StringComparison.Ordinal))
            {
                return "Standard";
            }

            if (normalized.EndsWith("fast", StringComparison.Ordinal))
            {
                return "Fast";
            }
        }

        return string.Equals(
            configuredServiceTier,
            "priority",
            StringComparison.OrdinalIgnoreCase)
            ? "Fast"
            : "Standard";
    }

    private static string ModelLabel(string displayName)
    {
        var value = displayName.StartsWith(
            "GPT-",
            StringComparison.OrdinalIgnoreCase)
            ? displayName[4..]
            : displayName;
        return value.Replace('-', ' ');
    }

    private static string EffortLabel(string? effort)
    {
        return effort?.ToLowerInvariant() switch
        {
            "low" => "Light",
            "medium" => "Medium",
            "high" => "High",
            "xhigh" => "Extra High",
            "max" => "Max",
            "ultra" => "Ultra",
            _ => string.Empty,
        };
    }

    private static string CategoryLabel(ComposerSettingKind kind)
    {
        return kind switch
        {
            ComposerSettingKind.Model => "Model",
            ComposerSettingKind.Effort => "Effort",
            ComposerSettingKind.Speed => "Speed",
            _ => string.Empty,
        };
    }

    private static string NormalizeChoice(string value)
    {
        return new string(
            value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
    }

    private static string? GetString(JsonElement element, string property)
    {
        return
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        return
            element.TryGetProperty(property, out var value) &&
            value.TryGetInt32(out var number)
                ? number
                : null;
    }

    private static string ResolveCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile),
                ".codex");
    }

    private sealed record ConfigPreferences(
        string? ModelSlug,
        string? Effort,
        string? ServiceTier);
}

internal enum ComposerDialPopupElementKind
{
    Menu,
    ListBox,
    MenuItem,
    OptionItem,
    Edit,
}

internal readonly record struct ComposerDialPopupEvidence(
    ComposerDialPopupElementKind Kind,
    bool IsEnabled,
    bool IsOffscreen,
    bool IsBoundsEmpty,
    bool IsNearComposer,
    bool HasKeyboardFocus,
    bool IsKeyboardFocusable,
    bool IsInPopupWindow,
    bool HasPopupAncestor,
    bool IsSemanticSearchEdit,
    bool SupportsSelection);

internal static class ComposerDialPolicy
{
    private const string ComposerButtonClassToken =
        "h-token-button-composer";
    private const string ComposerSmallButtonClassToken =
        "h-token-button-composer-sm";

    private static readonly string[] SearchSemanticTokens =
    [
        "search",
        "filter",
        "find",
        "query",
        "搜索",
        "搜尋",
        "查找",
        "筛选",
        "篩選",
        "検索",
        "찾기",
        "검색",
    ];

    internal static bool HasComposerButtonClassToken(
        string? className)
    {
        return
            HasClassToken(
                className,
                ComposerButtonClassToken) ||
            HasClassToken(
                className,
                ComposerSmallButtonClassToken);
    }

    internal static bool HasClassToken(
        string? className,
        string token)
    {
        if (
            string.IsNullOrWhiteSpace(className) ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return className
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(token, StringComparer.Ordinal);
    }

    internal static bool IsConservativeInvokeTrigger(
        string? className,
        bool isKeyboardFocusable)
    {
        if (
            !isKeyboardFocusable ||
            !HasComposerButtonClassToken(className) ||
            HasClassToken(className, "aspect-square"))
        {
            return false;
        }

        return
            HasClassToken(
                className,
                ComposerSmallButtonClassToken) ||
            HasClassToken(className, "min-w-0");
    }

    internal static bool LooksLikeSearchEdit(
        string? name,
        string? automationId,
        string? className,
        string? helpText)
    {
        var semanticText = string.Concat(
            name,
            " ",
            automationId,
            " ",
            className,
            " ",
            helpText);
        return SearchSemanticTokens.Any(token =>
            semanticText.Contains(
                token,
                StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsPopupEvidence(
        ComposerDialPopupEvidence evidence)
    {
        if (
            !evidence.IsEnabled ||
            evidence.IsOffscreen ||
            evidence.IsBoundsEmpty)
        {
            return false;
        }

        var isInDialArea =
            evidence.IsNearComposer ||
            evidence.HasKeyboardFocus ||
            evidence.IsInPopupWindow;
        if (!isInDialArea)
        {
            return false;
        }

        if (evidence.Kind == ComposerDialPopupElementKind.ListBox)
        {
            return
                evidence.SupportsSelection ||
                evidence.IsKeyboardFocusable ||
                evidence.HasKeyboardFocus ||
                evidence.HasPopupAncestor ||
                evidence.IsInPopupWindow;
        }

        if (evidence.Kind == ComposerDialPopupElementKind.OptionItem)
        {
            return
                evidence.HasKeyboardFocus ||
                evidence.HasPopupAncestor ||
                evidence.IsInPopupWindow;
        }

        if (evidence.Kind != ComposerDialPopupElementKind.Edit)
        {
            return true;
        }

        return
            evidence.HasKeyboardFocus ||
            evidence.HasPopupAncestor ||
            evidence.IsSemanticSearchEdit;
    }

    internal static bool HasFocusedSelectionChanged(
        string? before,
        string? after)
    {
        if (string.IsNullOrWhiteSpace(after))
        {
            return false;
        }

        return
            string.IsNullOrWhiteSpace(before) ||
            !string.Equals(
                before.Trim(),
                after.Trim(),
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSamePopupState(
        bool leftIsOpen,
        string? leftSignature,
        bool rightIsOpen,
        string? rightSignature)
    {
        return
            leftIsOpen == rightIsOpen &&
            string.Equals(
                leftSignature ?? string.Empty,
                rightSignature ?? string.Empty,
                StringComparison.Ordinal);
    }

    internal static int ResolveVisualOptionIndex(
        int optionCount,
        int currentIndex,
        int delta)
    {
        if (optionCount <= 0 || delta == 0)
        {
            return -1;
        }

        var direction = Math.Sign(delta);
        if (currentIndex < 0 || currentIndex >= optionCount)
        {
            return direction > 0 ? 0 : optionCount - 1;
        }

        return (
            currentIndex +
            direction +
            optionCount
        ) % optionCount;
    }

    internal static ComposerDialResult CreateProbeResult(
        bool isOpen,
        string? focusedName)
    {
        return new(
            true,
            ControlName:
                isOpen && !string.IsNullOrWhiteSpace(focusedName)
                    ? focusedName
                    : null,
            IsMenuOpen: isOpen);
    }
}
