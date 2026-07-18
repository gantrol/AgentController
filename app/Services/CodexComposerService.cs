using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Automation;
using CodexController.Agents;
using CodexController.Models;
using CodexController.Native;
using CodexController.Services.Micro;
using static CodexController.Services.CodexAutomationLocator;
using static CodexController.Services.CodexComposerDialProbe;
using static CodexController.Services.CodexComposerStateVerifier;

[assembly: InternalsVisibleTo("AgentController.Tests")]

namespace CodexController.Services;

public enum ComposerSettingKind
{
    Model,
    Effort,
    Speed,
}

public enum ComposerAutomationChannel
{
    Unknown,
    UiAutomation,
    KeyboardInput,
}

public sealed record ComposerAutomationResult(
    bool Succeeded,
    string? Error = null,
    string? ErrorDetail = null,
    ComposerAutomationChannel Channel = ComposerAutomationChannel.Unknown,
    bool StateVerified = false)
{
    public AgentAutomationError? Failure =>
        Error is null
            ? null
            : new AgentAutomationError(Error, ErrorDetail);
}

public sealed record ComposerPlanToggleResult(
    bool Succeeded,
    bool? IsPlanMode = null,
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
    string? ErrorDetail = null,
    bool MenuWasPresent = false,
    bool RequiresConfirmation = false,
    long? ElapsedMilliseconds = null);

public enum ComposerDialNavigation
{
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 4,
}

public enum ComposerPickerView
{
    Unknown,
    Simple,
    Advanced,
    Model,
}

public sealed record ComposerPickerResult(
    bool Succeeded,
    string? Value = null,
    bool IsMenuOpen = false,
    string? Error = null,
    string? ErrorDetail = null);

public sealed partial class CodexComposerService
{
    internal const string NativeSubmitShortcut = "Ctrl+Enter";

    private const int DialPopupMountTimeoutMs = 240;
    private const int DialConfirmationTimeoutMs = 650;
    private const int DialPopupPollIntervalMs = 24;
    private const int DialPopupCloseAttempts = 3;
    private const int NativePickerRefocusSettleMs = 55;
    private const int PlanStateChangeTimeoutMs = 900;
    private const int PlanStatePollIntervalMs = 40;

    private readonly object _dialSync = new();
    private readonly ComposerDialCursor _dialCursor = new();
    private readonly ComposerShortcutHealth _powerShortcutHealth = new();
    private readonly ComposerShortcutHealth _speedShortcutHealth = new();
    private readonly CodexComposerCatalogService _catalog;
    private readonly CodexComposerAutomationExecutor _commands;
    private readonly MicroInputService _microInput;
    private readonly Func<string, bool> _sendShortcut;
    private readonly Func<ushort, bool> _sendKey;
    private readonly Dictionary<string, OwnedDialSurface>
        _dialOwnedSurfaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>
        _dialSurfaceOptionKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>
        _dialSurfaceOptionNames = new(StringComparer.Ordinal);
    private bool _dialMenuOpen;
    private bool _dialSelectionRequiresExplicitDismiss;
    private string? _dialControlKey;
    private string? _dialControlName;
    private string? _dialActiveSurfaceKey;
    private ComposerDialMenuContainerGeometry? _dialControlGeometry;
    private System.Windows.Rect? _dialComposerRegion;
    private long _dialSurfaceMountSequence;

    public CodexComposerService()
        : this(
            MicroInputService.Unavailable,
            Win32Input.SendShortcut,
            Win32Input.SendKey)
    {
    }

    public CodexComposerService(MicroInputService microInput)
        : this(
            microInput,
            Win32Input.SendShortcut,
            Win32Input.SendKey)
    {
    }

    internal CodexComposerService(
        MicroInputService microInput,
        Func<string, bool> sendShortcut)
        : this(microInput, sendShortcut, Win32Input.SendKey)
    {
    }

    internal CodexComposerService(
        MicroInputService microInput,
        Func<string, bool> sendShortcut,
        Func<ushort, bool> sendKey)
    {
        _microInput = microInput ??
            throw new ArgumentNullException(nameof(microInput));
        _sendShortcut = sendShortcut ??
            throw new ArgumentNullException(nameof(sendShortcut));
        _sendKey = sendKey ??
            throw new ArgumentNullException(nameof(sendKey));
        _commands = new CodexComposerAutomationExecutor(_sendShortcut);
        _catalog = new CodexComposerCatalogService(
            TryReadComposerButtonName);
    }

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

                var confirmation =
                    FindDialConfirmation(
                        context.Value.Window);
                if (confirmation is not null)
                {
                    ClearOwnedDialPopup();
                    return new(
                        true,
                        confirmation.Title,
                        IsMenuOpen: true,
                        MenuWasPresent: true,
                        RequiresConfirmation: true);
                }

                var probe = ProbeOwnedDialPopup(
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
                    CurrentDialOptionName(probe) ??
                    probe.FocusedName ??
                    _dialControlName);
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

                var confirmation =
                    FindDialConfirmation(
                        context.Value.Window);
                if (confirmation is not null)
                {
                    ClearOwnedDialPopup();
                    return new(
                        true,
                        confirmation.Title,
                        IsMenuOpen: true,
                        MenuWasPresent: true,
                        RequiresConfirmation: true);
                }

                var before = ProbeOwnedDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                var expectedOwnedPopup = _dialMenuOpen;
                if (
                    before.IsOpen &&
                    !OwnsDialPopup(
                        context.Value.Window,
                        before) &&
                    !TryAdoptOpenDialPopup(
                        context.Value.Window,
                        context.Value.ProcessId,
                        before))
                {
                    if (expectedOwnedPopup)
                    {
                        ClearOwnedDialPopup();
                    }

                    return new(
                        false,
                        ControlName: _dialControlName,
                        IsMenuOpen: false,
                        Error:
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                        ErrorDetail:
                            expectedOwnedPopup
                                ? "dial-popup-focus-lost"
                                : "dial-popup-not-owned");
                }

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

                    UpdateOwnedDialPopup(before);
                    var surfaces =
                        BuildOwnedSurfaceSnapshots(before);
                    _dialActiveSurfaceKey =
                        ComposerDialMenuSelectionPolicy
                            .ResolveActiveSurface(
                                surfaces,
                                _dialActiveSurfaceKey);
                    var options = BuildOptionSnapshots(before);
                    var previousOptionKey =
                        CurrentDialOptionKey();
                    var target =
                        ComposerDialMenuSelectionPolicy.ResolveTarget(
                            options,
                            _dialActiveSurfaceKey,
                            previousOptionKey,
                            delta);
                    var targetOption =
                        target is { } resolvedTarget
                            ? FindDialPopupOption(
                                before,
                                resolvedTarget)
                            : null;
                    if (
                        target is null ||
                        targetOption is null ||
                        !TryFocusDialPopupOption(targetOption))
                    {
                        return new(
                            false,
                            ControlName:
                                CurrentDialOptionName(before) ??
                                _dialControlName,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-step-no-selection-change");
                    }

                    RememberDialOption(
                        target.Value.SurfaceKey,
                        target.Value.OptionKey,
                        target.Value.Name);
                    var after = WaitForDialTargetFocus(
                        context.Value.Window,
                        context.Value.ProcessId,
                        target.Value.OptionKey,
                        DialPopupMountTimeoutMs);
                    if (!OwnsDialPopup(context.Value.Window, after))
                    {
                        ClearOwnedDialPopup();
                        return new(
                            false,
                            ControlName:
                                target.Value.Name ??
                                _dialControlName,
                            IsMenuOpen: false,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-popup-focus-lost");
                    }

                    UpdateOwnedDialPopup(after);
                    var updatedSurfaces =
                        BuildOwnedSurfaceSnapshots(after);
                    var updatedOptions =
                        BuildOptionSnapshots(after);
                    if (
                        !string.Equals(
                            after.FocusedOptionKey,
                            target.Value.OptionKey,
                            StringComparison.Ordinal) ||
                        !ComposerDialMenuSelectionPolicy
                            .IsTargetStillConfirmable(
                                target.Value,
                                updatedSurfaces,
                                updatedOptions))
                    {
                        RememberDialOption(
                            target.Value.SurfaceKey,
                            target.Value.OptionKey,
                            target.Value.Name);
                        return new(
                            false,
                            ControlName:
                                target.Value.Name ??
                                _dialControlName,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes.ElementUnsupported,
                            ErrorDetail: "dial-step-no-selection-change");
                    }

                    return new(
                        true,
                        ControlName:
                            CurrentDialOptionName(after) ??
                            target.Value.Name ??
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

    public ComposerDialResult DialNavigate(
        ComposerDialNavigation navigation,
        AppSettings settings)
    {
        var started = Stopwatch.GetTimestamp();
        var result = DialNavigateCore(navigation, settings);
        return result with
        {
            ElapsedMilliseconds = Math.Max(
                0,
                (long)Stopwatch
                    .GetElapsedTime(started)
                    .TotalMilliseconds),
        };
    }

    private ComposerDialResult DialNavigateCore(
        ComposerDialNavigation navigation,
        AppSettings settings)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return gate;
        }

        lock (_dialSync)
        {
            if (_dialMenuOpen)
            {
                if (
                    !ComposerDialNativeInputPolicy.TryGetNavigationKey(
                        navigation,
                        out var nativeKey) ||
                    !_sendKey(nativeKey))
                {
                    return new(
                        false,
                        _dialControlName,
                        IsMenuOpen: true,
                        Error:
                            AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail: "dial-native-input",
                        MenuWasPresent: true);
                }

                // Native Codex menu navigation is the hot path. The old
                // implementation rescanned the full UIA tree and waited up
                // to 240 ms after every direction; ownership is established
                // once when the menu opens, then keys are delivered directly.
                return new(
                    true,
                    _dialControlName,
                    IsMenuOpen: true,
                    MenuWasPresent: true);
            }
        }

        lock (_dialSync)
        {
            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    Error:
                        AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var confirmation =
                FindDialConfirmation(
                    context.Value.Window);
            if (confirmation is not null)
            {
                ClearOwnedDialPopup();
                return new(
                    true,
                    confirmation.Title,
                    IsMenuOpen: true,
                    MenuWasPresent: true,
                    RequiresConfirmation: true);
            }

            if (_dialMenuOpen)
            {
                return DialNavigateOwnedPopupByKeyboard(
                    context.Value.Window,
                    context.Value.ProcessId,
                    navigation,
                    settings);
            }
        }

        return navigation switch
        {
            ComposerDialNavigation.Left =>
                DialStep(-1, settings),
            ComposerDialNavigation.Right =>
                DialStep(1, settings),
            _ => new(
                false,
                ControlName: _dialControlName,
                IsMenuOpen: false,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-closed-horizontal-only"),
        };
    }

    private ComposerDialResult DialNavigateOwnedPopupByKeyboard(
        AutomationElement window,
        int processId,
        ComposerDialNavigation navigation,
        AppSettings settings)
    {
        var before = ProbeOwnedDialPopup(window, processId);
        if (!OwnsDialPopup(window, before))
        {
            var observedName = before.FocusedName ?? _dialControlName;
            if (!TryAdoptOpenDialPopup(window, processId, before))
            {
                ClearOwnedDialPopup();
                return new(
                    false,
                    observedName,
                    IsMenuOpen: before.IsOpen,
                    Error:
                        AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: before.IsOpen
                        ? "dial-popup-not-owned"
                        : "dial-menu-not-open",
                    MenuWasPresent: before.IsOpen);
            }

            before = ProbeOwnedDialPopup(window, processId);
            if (
                !OwnsDialPopup(window, before) ||
                !HasOwnedDialInputFocus(
                    processId,
                    before,
                    _dialActiveSurfaceKey))
            {
                ClearOwnedDialPopup();
                return new(
                    false,
                    observedName,
                    IsMenuOpen: before.IsOpen,
                    Error:
                        AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "dial-popup-focus-lost",
                    MenuWasPresent: before.IsOpen);
            }
        }

        UpdateOwnedDialPopup(before);
        var surfaces = BuildOwnedSurfaceSnapshots(before);
        _dialActiveSurfaceKey =
            ComposerDialMenuSelectionPolicy.ResolveActiveSurface(
                surfaces,
                _dialActiveSurfaceKey);
        var previousActiveSurfaceKey = _dialActiveSurfaceKey;
        var selectedOption = FindDialPopupOption(
            before,
            _dialActiveSurfaceKey,
            CurrentDialOptionKey());
        var selectedName =
            selectedOption?.Name ??
            before.FocusedName ??
            _dialControlName;

        if (RequiresMicroSemanticNavigation(selectedName))
        {
            return navigation switch
            {
                ComposerDialNavigation.Up =>
                    DialStep(-1, settings),
                ComposerDialNavigation.Down =>
                    DialStep(1, settings),
                ComposerDialNavigation.Right =>
                    DialPressCore(
                        settings,
                        DialActivationIntent.EnterSubmenu),
                ComposerDialNavigation.Left =>
                    DialBack(settings),
                _ => new(
                    false,
                    selectedName,
                    IsMenuOpen: true,
                    Error:
                        AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "dial-navigation",
                    MenuWasPresent: true),
            };
        }

        string? parentSurfaceKey = null;
        if (navigation == ComposerDialNavigation.Left)
        {
            if (
                string.IsNullOrWhiteSpace(_dialActiveSurfaceKey) ||
                !_dialOwnedSurfaces.TryGetValue(
                    _dialActiveSurfaceKey,
                    out var activeSurface))
            {
                return new(
                    false,
                    selectedName,
                    IsMenuOpen: true,
                    Error:
                        AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "dial-navigation",
                    MenuWasPresent: true);
            }

            parentSurfaceKey = activeSurface.ParentKey;
            if (string.IsNullOrWhiteSpace(parentSurfaceKey))
            {
                return new(
                    true,
                    selectedName,
                    IsMenuOpen: true,
                    MenuWasPresent: true);
            }
        }

        if (
            navigation == ComposerDialNavigation.Right &&
            selectedOption?.CanExpand != true)
        {
            return new(
                false,
                selectedName,
                IsMenuOpen: true,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-no-submenu",
                MenuWasPresent: true);
        }

        if (
            !ComposerDialNativeInputPolicy.TryGetNavigationKey(
                navigation,
                out var virtualKey) ||
            !TrySendOwnedDialKey(
                processId,
                before,
                _dialActiveSurfaceKey,
                virtualKey))
        {
            return new(
                false,
                selectedName,
                IsMenuOpen: true,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-native-input",
                MenuWasPresent: true);
        }

        var previousFocusedOptionKey = before.FocusedOptionKey;
        var after = WaitForOwnedDialObservation(
            window,
            processId,
            DialPopupMountTimeoutMs,
            current => IsExpectedNativeNavigationState(
                window,
                processId,
                before,
                current,
                navigation,
                previousActiveSurfaceKey,
                parentSurfaceKey,
                selectedOption,
                previousFocusedOptionKey));
        if (!after.IsOpen)
        {
            ClearOwnedDialPopup();
            return new(
                false,
                selectedName,
                IsMenuOpen: false,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-native-navigation-closed",
                MenuWasPresent: true);
        }

        if (
            !IsExpectedNativeNavigationState(
                window,
                processId,
                before,
                after,
                navigation,
                previousActiveSurfaceKey,
                parentSurfaceKey,
                selectedOption,
                previousFocusedOptionKey))
        {
            return new(
                false,
                after.FocusedName ?? selectedName,
                IsMenuOpen: true,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail:
                    navigation == ComposerDialNavigation.Right
                        ? "dial-submenu-not-open"
                        : "dial-native-navigation-unverified",
                MenuWasPresent: true);
        }

        DialPopupSurface? openedSurface = null;
        if (
            navigation == ComposerDialNavigation.Right &&
            selectedOption is not null)
        {
            openedSurface = FindNewDialPopupSurface(
                before,
                after,
                selectedOption);
            if (openedSurface is not null)
            {
                AddOwnedDialSurface(
                    openedSurface.Key,
                    _dialActiveSurfaceKey);
                _dialActiveSurfaceKey = openedSurface.Key;
            }
            else
            {
                return new(
                    false,
                    selectedName,
                    IsMenuOpen: true,
                    Error:
                        AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "dial-submenu-not-open",
                    MenuWasPresent: true);
            }
        }
        else if (
            navigation == ComposerDialNavigation.Left &&
            !string.IsNullOrWhiteSpace(parentSurfaceKey))
        {
            if (
                !string.IsNullOrWhiteSpace(previousActiveSurfaceKey) &&
                after.Surfaces.Any(surface =>
                    string.Equals(
                        surface.Key,
                        previousActiveSurfaceKey,
                        StringComparison.Ordinal)))
            {
                return new(
                    false,
                    selectedName,
                    IsMenuOpen: true,
                    Error:
                        AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "dial-back",
                    MenuWasPresent: true);
            }

            _dialActiveSurfaceKey = parentSurfaceKey;
        }

        if (!OwnsDialPopup(window, after))
        {
            ClearOwnedDialPopup();
            return new(
                false,
                selectedName,
                IsMenuOpen: true,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-popup-focus-lost",
                MenuWasPresent: true);
        }

        UpdateOwnedDialPopup(after);
        if (openedSurface is not null)
        {
            _dialActiveSurfaceKey = openedSurface.Key;
        }

        if (
            !HasOwnedDialInputFocus(
                processId,
                after,
                _dialActiveSurfaceKey))
        {
            return new(
                false,
                after.FocusedName ?? selectedName,
                IsMenuOpen: true,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-popup-focus-lost",
                MenuWasPresent: true);
        }
        if (
            openedSurface is not null &&
            !after.Options.Any(option =>
                string.Equals(
                    option.SurfaceKey,
                    openedSurface.Key,
                    StringComparison.Ordinal) &&
                string.Equals(
                    option.Key,
                    after.FocusedOptionKey,
                    StringComparison.Ordinal)) &&
            !FocusInitialDialOption(
                window,
                processId,
                after,
                openedSurface.Key,
                selectedName,
                preferFirst: false))
        {
            return new(
                false,
                selectedName,
                IsMenuOpen: true,
                Error:
                    AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "dial-initial-focus",
                MenuWasPresent: true);
        }

        return new(
            true,
            CurrentDialOptionName(after) ??
            after.FocusedName ??
            selectedName,
            IsMenuOpen: true,
            MenuWasPresent: true);
    }

    public ComposerDialResult DialPress(AppSettings settings)
    {
        return DialPressCore(
            settings,
            DialActivationIntent.OpenOnly);
    }

    public ComposerDialResult DialSelect(AppSettings settings)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return gate;
        }

        lock (_dialSync)
        {
            if (_dialMenuOpen)
            {
                if (!_sendKey(ComposerDialNativeInputPolicy.EnterKey))
                {
                    return new(
                        false,
                        _dialControlName,
                        IsMenuOpen: true,
                        Error:
                            AgentAutomationErrorCodes.InputInjectionFailed,
                        ErrorDetail: "Enter",
                        MenuWasPresent: true);
                }

                var selected = _dialControlName;
                if (_dialSelectionRequiresExplicitDismiss)
                {
                    // Codex's model picker is hierarchical. Enter can switch
                    // Compact/Advanced views, open Model/Effort/Speed, or
                    // commit a leaf. The VHF/keyboard acknowledgement only
                    // proves input delivery, so it cannot tell those cases
                    // apart. Keep native ownership until B/R3 explicitly
                    // dismisses the session; otherwise the next stick frame
                    // leaks into the Simple Power F17/F18 fallback.
                    return new(
                        true,
                        selected,
                        IsMenuOpen: true,
                        MenuWasPresent: true);
                }

                ClearOwnedDialPopup();
                return new(
                    true,
                    selected,
                    IsMenuOpen: false,
                    MenuWasPresent: true);
            }
        }

        return DialPressCore(
            settings,
            DialActivationIntent.SelectLeaf);
    }

    private ComposerDialResult DialPressCore(
        AppSettings settings,
        DialActivationIntent intent)
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

                var confirmation =
                    FindDialConfirmation(
                        context.Value.Window);
                if (confirmation is not null)
                {
                    ClearOwnedDialPopup();
                    if (
                        intent !=
                            DialActivationIntent.SelectLeaf)
                    {
                        return new(
                            true,
                            confirmation.Title,
                            IsMenuOpen: true,
                            MenuWasPresent: true,
                            RequiresConfirmation: true);
                    }

                    if (
                        !TryInvokeDialConfirmationButton(
                            confirmation.ConfirmButton) ||
                        !WaitForDialConfirmationClosed(
                            context.Value.Window,
                            DialConfirmationTimeoutMs))
                    {
                        return new(
                            false,
                            confirmation.Title,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail:
                                "dial-confirmation-confirm",
                            MenuWasPresent: true,
                            RequiresConfirmation: true);
                    }

                    return new(
                        true,
                        confirmation.Title,
                        IsMenuOpen: false,
                        MenuWasPresent: true);
                }

                var before = ProbeOwnedDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                var expectedOwnedPopup = _dialMenuOpen;
                if (
                    before.IsOpen &&
                    !OwnsDialPopup(
                        context.Value.Window,
                        before) &&
                    !TryAdoptOpenDialPopup(
                        context.Value.Window,
                        context.Value.ProcessId,
                        before))
                {
                    if (expectedOwnedPopup)
                    {
                        ClearOwnedDialPopup();
                    }

                    return new(
                        false,
                        ControlName: _dialControlName,
                        IsMenuOpen: false,
                        Error:
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                        ErrorDetail:
                            expectedOwnedPopup
                                ? "dial-popup-focus-lost"
                                : "dial-popup-not-owned");
                }

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

                    UpdateOwnedDialPopup(before);
                    var surfaces =
                        BuildOwnedSurfaceSnapshots(before);
                    _dialActiveSurfaceKey =
                        ComposerDialMenuSelectionPolicy
                            .ResolveActiveSurface(
                                surfaces,
                                _dialActiveSurfaceKey);
                    var optionKey = CurrentDialOptionKey();
                    var selectedOption =
                        FindDialPopupOption(
                            before,
                            _dialActiveSurfaceKey,
                            optionKey);
                    var menuSelection = selectedOption?.Name;
                    if (intent == DialActivationIntent.OpenOnly)
                    {
                        return new(
                            true,
                            menuSelection ?? _dialControlName,
                            IsMenuOpen: true,
                            MenuWasPresent: true);
                    }

                    if (
                        intent ==
                            DialActivationIntent.EnterSubmenu &&
                        selectedOption?.CanExpand != true)
                    {
                        return new(
                            false,
                            menuSelection ?? _dialControlName,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-no-submenu",
                            MenuWasPresent: true);
                    }

                    if (
                        intent ==
                            DialActivationIntent.SelectLeaf &&
                        selectedOption?.CanExpand == true)
                    {
                        return new(
                            false,
                            menuSelection ?? _dialControlName,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-enter-with-right",
                            MenuWasPresent: true);
                    }

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

                    var selectedTarget =
                        selectedOption is null
                            ? null
                            : CreateDialMenuTarget(
                                before,
                                selectedOption);
                    if (
                        selectedOption is null ||
                        selectedTarget is null ||
                        !ComposerDialMenuSelectionPolicy
                            .IsTargetStillConfirmable(
                                selectedTarget.Value,
                                surfaces,
                                BuildOptionSnapshots(before)))
                    {
                        return new(
                            false,
                            menuSelection,
                            IsMenuOpen: true,
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                            "dial-selection-unverified");
                    }

                    var activationSucceeded =
                        TryActivateDialPopupOption(
                            selectedOption.Element);
                    if (!activationSucceeded)
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
                    var mountedConfirmation =
                        FindDialConfirmation(
                            context.Value.Window);
                    if (mountedConfirmation is not null)
                    {
                        ClearOwnedDialPopup();
                        return new(
                            true,
                            mountedConfirmation.Title,
                            IsMenuOpen: true,
                            MenuWasPresent: true,
                            RequiresConfirmation: true);
                    }

                    var initialFocusSucceeded = true;
                    if (afterEnter.IsOpen)
                    {
                        var openedSurface =
                            FindNewDialPopupSurface(
                                before,
                                afterEnter,
                                selectedOption);
                        if (openedSurface is not null)
                        {
                            AddOwnedDialSurface(
                                openedSurface.Key,
                                _dialActiveSurfaceKey);
                        }

                        UpdateOwnedDialPopup(afterEnter);
                        if (openedSurface is not null)
                        {
                            _dialActiveSurfaceKey =
                                openedSurface.Key;
                            initialFocusSucceeded =
                                FocusInitialDialOption(
                                context.Value.Window,
                                context.Value.ProcessId,
                                afterEnter,
                                openedSurface.Key,
                                menuSelection,
                                preferFirst: false);
                        }
                    }
                    else
                    {
                        ClearOwnedDialPopup();
                    }

                    if (!initialFocusSucceeded)
                    {
                        return new(
                            false,
                            CurrentDialOptionName(afterEnter) ??
                            menuSelection ??
                            _dialControlName,
                            afterEnter.IsOpen,
                            AgentAutomationErrorCodes.ElementUnsupported,
                            "dial-initial-focus");
                    }

                    return new(
                        true,
                        CurrentDialOptionName(afterEnter) ??
                        menuSelection ??
                        _dialControlName,
                        afterEnter.IsOpen,
                        MenuWasPresent: true);
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
                    _dialControlGeometry = new(
                        selected.Left,
                        selected.Top,
                        selected.Width,
                        selected.Height);
                    var initialFocusSucceeded =
                        InitializeOwnedDialPopup(
                            context.Value.Window,
                            context.Value.ProcessId,
                            afterOpen,
                            selected.Element);
                    if (!initialFocusSucceeded)
                    {
                        return new(
                            false,
                            CurrentDialOptionName(afterOpen) ??
                            selected.Name,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail: "dial-initial-focus");
                    }

                    return new(
                        true,
                        CurrentDialOptionName(afterOpen) ??
                        selected.Name,
                        IsMenuOpen: true);
                }

                var afterFailure = ProbeOwnedDialPopup(
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

    private ComposerDialResult DialBack(AppSettings settings)
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

                var before = ProbeOwnedDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                if (
                    !OwnsDialPopup(
                        context.Value.Window,
                        before) &&
                    !TryAdoptOpenDialPopup(
                        context.Value.Window,
                        context.Value.ProcessId,
                        before))
                {
                    return new(
                        false,
                        ControlName:
                            before.FocusedName ??
                            _dialControlName,
                        IsMenuOpen: before.IsOpen,
                        Error:
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                        ErrorDetail: before.IsOpen
                            ? "dial-popup-not-owned"
                            : "dial-menu-not-open",
                        MenuWasPresent: before.IsOpen);
                }

                UpdateOwnedDialPopup(before);
                var surfaces =
                    BuildOwnedSurfaceSnapshots(before);
                _dialActiveSurfaceKey =
                    ComposerDialMenuSelectionPolicy
                        .ResolveActiveSurface(
                            surfaces,
                            _dialActiveSurfaceKey);
                if (
                    string.IsNullOrWhiteSpace(
                        _dialActiveSurfaceKey) ||
                    !_dialOwnedSurfaces.TryGetValue(
                        _dialActiveSurfaceKey,
                        out var activeSurface))
                {
                    return new(
                        false,
                        CurrentDialOptionName(before) ??
                        before.FocusedName ??
                        _dialControlName,
                        IsMenuOpen: true,
                        Error:
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                        ErrorDetail: "dial-navigation",
                        MenuWasPresent: true);
                }

                if (string.IsNullOrWhiteSpace(
                        activeSurface.ParentKey))
                {
                    return new(
                        true,
                        CurrentDialOptionName(before) ??
                        before.FocusedName ??
                        _dialControlName,
                        IsMenuOpen: true,
                        MenuWasPresent: true);
                }

                var parentSurfaceKey = activeSurface.ParentKey;
                var parentOptionKey =
                    _dialSurfaceOptionKeys.TryGetValue(
                        parentSurfaceKey,
                        out var rememberedParentOption)
                        ? rememberedParentOption
                        : null;
                var parentOption = FindDialPopupOption(
                    before,
                    parentSurfaceKey,
                    parentOptionKey);
                if (
                    parentOption is null ||
                    !TryCollapseDialPopupOption(
                        parentOption.Element))
                {
                    return new(
                        false,
                        CurrentDialOptionName(before) ??
                        _dialControlName,
                        IsMenuOpen: true,
                        Error:
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                        ErrorDetail: "dial-back",
                        MenuWasPresent: true);
                }

                var after = WaitForDialPopupTransition(
                    context.Value.Window,
                    context.Value.ProcessId,
                    before,
                    DialPopupMountTimeoutMs);
                if (!after.IsOpen)
                {
                    ClearOwnedDialPopup();
                    return new(
                        false,
                        parentOption.Name,
                        IsMenuOpen: false,
                        Error:
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                        ErrorDetail: "dial-back-closed-root",
                        MenuWasPresent: true);
                }

                UpdateOwnedDialPopup(after);
                _dialActiveSurfaceKey = parentSurfaceKey;
                var refreshedParent = FindDialPopupOption(
                    after,
                    parentSurfaceKey,
                    parentOption.Key);
                if (refreshedParent is not null)
                {
                    _ = TryFocusDialPopupOption(refreshedParent);
                    RememberDialOption(
                        parentSurfaceKey,
                        refreshedParent.Key,
                        refreshedParent.Name);
                }
                else
                {
                    RememberDialOption(
                        parentSurfaceKey,
                        parentOption.Key,
                        parentOption.Name);
                }

                return new(
                    true,
                    parentOption.Name,
                    IsMenuOpen: true,
                    MenuWasPresent: true);
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

    public ComposerDialResult DialCancel(AppSettings settings)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return gate;
        }

        lock (_dialSync)
        {
            if (_dialMenuOpen)
            {
                var selected = _dialControlName;
                if (_dialSelectionRequiresExplicitDismiss)
                {
                    // `composer.openModelPicker` is idempotent while open:
                    // Codex focuses the root picker target instead of
                    // toggling it. If a leaf already closed the picker, the
                    // same command reopens it. In either case the following
                    // Escape is delivered to an owned root surface, not the
                    // base composer, and closes any nested flyout with it.
                    var shortcut = settings.ModelPickerShortcut.Trim();
                    if (
                        shortcut.Length == 0 ||
                        !_sendShortcut(shortcut))
                    {
                        return new(
                            false,
                            selected,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .InputInjectionFailed,
                            ErrorDetail:
                                "composer-model-picker-refocus",
                            MenuWasPresent: true);
                    }

                    Thread.Sleep(NativePickerRefocusSettleMs);
                }

                var sent =
                    _microInput.TryDismissOpenMenu() ||
                    _sendKey(ComposerDialNativeInputPolicy.EscapeKey);
                if (!sent)
                {
                    return new(
                        false,
                        selected,
                        IsMenuOpen: true,
                        Error:
                            AgentAutomationErrorCodes.InputInjectionFailed,
                        ErrorDetail: "Escape",
                        MenuWasPresent: true);
                }

                ClearOwnedDialPopup();
                return new(
                    true,
                    selected,
                    IsMenuOpen: false,
                    MenuWasPresent: true);
            }
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

                var confirmationWasPresent = false;
                var confirmation =
                    FindDialConfirmation(
                        context.Value.Window);
                if (confirmation is not null)
                {
                    confirmationWasPresent = true;
                    ClearOwnedDialPopup();
                    if (
                        !TryInvokeDialConfirmationButton(
                            confirmation.CancelButton) ||
                        !WaitForDialConfirmationClosed(
                            context.Value.Window,
                            DialConfirmationTimeoutMs))
                    {
                        return new(
                            false,
                            confirmation.Title,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail:
                                "dial-confirmation-cancel",
                            MenuWasPresent: true,
                            RequiresConfirmation: true);
                    }
                }

                var before = ProbeOwnedDialPopup(
                    context.Value.Window,
                    context.Value.ProcessId);
                var expectedMenu =
                    _dialMenuOpen || confirmationWasPresent;
                var observedMenu = before.IsOpen;
                if (
                    !_dialMenuOpen &&
                    before.IsOpen)
                {
                    _ = TryAdoptOpenDialPopup(
                        context.Value.Window,
                        context.Value.ProcessId,
                        before);
                }

                var selected =
                    CurrentDialOptionName(before) ??
                    before.FocusedName ??
                    _dialControlName;
                if (!_dialMenuOpen)
                {
                    if (confirmationWasPresent && before.IsOpen)
                    {
                        return new(
                            false,
                            selected,
                            IsMenuOpen: true,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail:
                                "dial-confirmation-residual-popup",
                            MenuWasPresent: true);
                    }

                    return new(
                        true,
                        selected,
                        IsMenuOpen: false,
                        MenuWasPresent:
                            confirmationWasPresent);
                }

                if (!OwnsDialPopup(context.Value.Window, before))
                {
                    ClearOwnedDialPopup();
                    return new(
                        false,
                        selected,
                        IsMenuOpen: false,
                        AgentAutomationErrorCodes.ElementUnsupported,
                        "dial-popup-focus-lost",
                        MenuWasPresent:
                            expectedMenu || observedMenu);
                }

                var after = before;
                for (
                    var attempt = 0;
                    attempt < DialPopupCloseAttempts;
                    attempt++)
                {
                    if (!TryCloseOwnedDialControl(
                            context.Value.Window,
                            context.Value.ProcessId,
                            after))
                    {
                        return new(
                            false,
                            selected,
                            IsMenuOpen: true,
                            AgentAutomationErrorCodes
                                .ElementUnsupported,
                            "dial-popup-close",
                            MenuWasPresent: true);
                    }

                    after = WaitForDialPopupTransition(
                        context.Value.Window,
                        context.Value.ProcessId,
                        after,
                        DialPopupMountTimeoutMs);
                    if (
                        !after.IsOpen ||
                        !OwnsDialPopup(
                            context.Value.Window,
                            after))
                    {
                        ClearOwnedDialPopup();
                        return new(
                            true,
                            selected,
                            IsMenuOpen: false,
                            MenuWasPresent: true);
                    }

                    UpdateOwnedDialPopup(after);
                }

                return new(
                    false,
                    CurrentDialOptionName(after) ??
                    after.FocusedName ??
                    selected,
                    IsMenuOpen: true,
                    AgentAutomationErrorCodes.ElementUnsupported,
                    "dial-popup-close",
                    MenuWasPresent: true);
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

    public ComposerCatalog LoadCatalog() => _catalog.LoadCatalog();

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

    public Task<ComposerPickerResult> OpenPickerAsync(
        ComposerPickerView view,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => OpenPickerCore(
                view,
                settings,
                cancellationToken),
            cancellationToken);
    }

    public Task<ComposerPickerResult> StepSimplePowerAsync(
        int steps,
        bool allowShortcutFastPath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => StepSimplePowerCore(
                steps,
                pickerMenuLikelyOpen: !allowShortcutFastPath,
                settings,
                cancellationToken),
            cancellationToken);
    }

    public Task<ComposerPickerResult> SetSimpleSpeedAsync(
        bool fast,
        bool allowShortcutFastPath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => SetSimpleSpeedCore(
                fast,
                allowShortcutFastPath,
                settings,
                cancellationToken),
            cancellationToken);
    }

    public Task<ComposerPickerResult> ToggleSpeedAsync(
        bool allowShortcutFastPath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => ToggleSpeedCore(
                allowShortcutFastPath,
                settings,
                cancellationToken),
            cancellationToken);
    }

    public Task<ComposerPickerResult> StepAdvancedAsync(
        ComposerSettingKind kind,
        int direction,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => StepAdvancedCore(
                kind,
                direction,
                settings,
                cancellationToken),
            cancellationToken);
    }

    public Task<ComposerPlanToggleResult> TogglePlanModeAsync(
        string shortcut,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => TogglePlanModeCore(
                shortcut,
                settings,
                cancellationToken),
            cancellationToken);
    }

    public Task<ComposerAutomationResult> ScrollConversationAsync(
        ConversationBoundary boundary,
        AppSettings settings,
        CancellationToken cancellationToken) =>
        _commands.ScrollConversationAsync(
            boundary,
            settings,
            cancellationToken);

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

    public string? TryReadDispatchButtonName() =>
        _commands.TryReadDispatchButtonName();

    public bool IsComposerActionAvailable(params string[] actionNames) =>
        _commands.IsComposerActionAvailable(actionNames);

    public ComposerAutomationResult InvokeComposerAction(
        AppSettings settings,
        params string[] actionNames) =>
        _commands.InvokeComposerAction(settings, actionNames);

    internal static ComposerAutomationResult UiAutomationSucceeded(
        bool stateVerified = false) =>
        ComposerAutomationResults.UiAutomationSucceeded(stateVerified);

    public Task<ComposerAutomationResult> InvokeComposerActionAsync(
        AppSettings settings,
        int timeoutMs,
        CancellationToken cancellationToken,
        params string[] actionNames) =>
        _commands.InvokeComposerActionAsync(
            settings,
            timeoutMs,
            cancellationToken,
            actionNames);

    public ComposerAutomationResult SubmitComposer(
        AppSettings settings) =>
        _commands.SubmitComposer(settings);

    public ComposerAutomationResult ClearComposer(
        AppSettings settings) =>
        _commands.ClearComposer(settings);

    public ComposerAutomationResult StopCurrentTurn(
        AppSettings settings) =>
        _commands.StopCurrentTurn(settings);

    public ComposerAutomationResult CancelComposer(
        AppSettings settings) =>
        _commands.CancelComposer(settings);

    private static bool? TryReadPlanModeState(AutomationElement window)
    {
        var editor = FindComposerEditor(window);
        if (FindPlanIndicator(window, editor) is not null)
        {
            return true;
        }

        return
            editor is not null ||
            FindComposerButton(window) is not null
                ? false
                : null;
    }

    private static AutomationElement? FindPlanIndicator(
        AutomationElement window,
        AutomationElement? editor)
    {
        return FindVisibleNamedButtonNearComposer(
            window,
            PlanModeAutomationPolicy.IndicatorNames,
            editor);
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

        return
            _dialOwnedSurfaces.Count > 0 &&
            probe.Surfaces.Any(surface =>
                _dialOwnedSurfaces.ContainsKey(surface.Key));
    }

    private void UpdateOwnedDialPopup(DialPopupProbe probe)
    {
        _dialMenuOpen = probe.IsOpen;
        if (probe.ComposerRegion is { } composerRegion)
        {
            _dialComposerRegion = composerRegion;
        }
        if (!probe.IsOpen)
        {
            return;
        }

        var visibleSurfaceKeys = probe.Surfaces
            .Select(surface => surface.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var staleKey in _dialOwnedSurfaces.Keys
                     .Where(key => !visibleSurfaceKeys.Contains(key))
                     .ToArray())
        {
            _dialOwnedSurfaces.Remove(staleKey);
            _dialSurfaceOptionKeys.Remove(staleKey);
            _dialSurfaceOptionNames.Remove(staleKey);
        }

        var surfaceSnapshots =
            BuildOwnedSurfaceSnapshots(probe);
        _dialActiveSurfaceKey =
            ComposerDialMenuSelectionPolicy.ResolveActiveSurface(
                surfaceSnapshots,
                _dialActiveSurfaceKey);

        var focusedOption = probe.Options
            .Where(option =>
                (
                    option.HasKeyboardFocus ||
                    string.Equals(
                        option.Key,
                        probe.FocusedOptionKey,
                        StringComparison.Ordinal)
                ) &&
                _dialOwnedSurfaces.ContainsKey(option.SurfaceKey))
            .OrderByDescending(option =>
                _dialOwnedSurfaces[option.SurfaceKey].Depth)
            .FirstOrDefault();
        if (focusedOption is not null)
        {
            _dialActiveSurfaceKey = focusedOption.SurfaceKey;
            RememberDialOption(
                focusedOption.SurfaceKey,
                focusedOption.Key,
                focusedOption.Name);
        }
    }

    private void MarkNativePickerOpen(
        string? controlName,
        bool selectionRequiresExplicitDismiss = false)
    {
        lock (_dialSync)
        {
            _dialMenuOpen = true;
            _dialSelectionRequiresExplicitDismiss =
                selectionRequiresExplicitDismiss;
            if (!string.IsNullOrWhiteSpace(controlName))
            {
                _dialControlName = controlName;
            }
        }
    }

    private void ClearOwnedDialPopup()
    {
        _dialMenuOpen = false;
        _dialSelectionRequiresExplicitDismiss = false;
        _dialControlKey = null;
        _dialActiveSurfaceKey = null;
        _dialControlGeometry = null;
        _dialComposerRegion = null;
        _dialOwnedSurfaces.Clear();
        _dialSurfaceOptionKeys.Clear();
        _dialSurfaceOptionNames.Clear();
        _dialSurfaceMountSequence = 0;
    }

    private bool InitializeOwnedDialPopup(
        AutomationElement window,
        int processId,
        DialPopupProbe probe,
        AutomationElement trigger,
        bool preferFirstForComposite = true)
    {
        _dialMenuOpen = probe.IsOpen;
        _dialComposerRegion = probe.ComposerRegion;
        _dialActiveSurfaceKey = null;
        _dialOwnedSurfaces.Clear();
        _dialSurfaceOptionKeys.Clear();
        _dialSurfaceOptionNames.Clear();
        _dialSurfaceMountSequence = 0;

        var surface = FindInitialDialPopupSurface(
            probe,
            trigger);
        if (surface is null)
        {
            return false;
        }

        AddOwnedDialSurface(surface.Key, parentKey: null);
        _dialActiveSurfaceKey = surface.Key;
        return FocusInitialDialOption(
            window,
            processId,
            probe,
            surface.Key,
            parentSelectionName: null,
            preferFirst:
                preferFirstForComposite &&
                surface.Options.Count(option =>
                    option.CanExpand) >= 2);
    }

    private bool TryAdoptOpenDialPopup(
        AutomationElement window,
        int processId,
        DialPopupProbe probe)
    {
        if (!probe.IsOpen || probe.Surfaces.Count == 0)
        {
            return false;
        }

        var control = FindDialControls(window)
            .Select(candidate => new
            {
                Control = candidate,
                IsExpanded =
                    IsDialControlExpanded(candidate.Element),
                Distance = probe.Surfaces.Min(surface =>
                    DistanceBetween(
                        new System.Windows.Rect(
                            candidate.Left,
                            candidate.Top,
                            candidate.Width,
                            candidate.Height),
                        surface.Bounds)),
            })
            .OrderByDescending(candidate =>
                candidate.IsExpanded)
            .ThenBy(candidate => candidate.Distance)
            .FirstOrDefault();
        if (
            control is null ||
            !control.IsExpanded ||
            control.Distance > 180)
        {
            return false;
        }

        ClearOwnedDialPopup();
        _dialMenuOpen = true;
        _dialControlKey = control.Control.Key;
        _dialControlName = control.Control.Name;
        _dialControlGeometry = new(
            control.Control.Left,
            control.Control.Top,
            control.Control.Width,
            control.Control.Height);
        if (
            !InitializeOwnedDialPopup(
                window,
                processId,
                probe,
                control.Control.Element,
                preferFirstForComposite: false))
        {
            ClearOwnedDialPopup();
            return false;
        }

        return
            _dialMenuOpen &&
            _dialOwnedSurfaces.Count > 0;
    }

    private void AddOwnedDialSurface(
        string surfaceKey,
        string? parentKey)
    {
        if (_dialOwnedSurfaces.ContainsKey(surfaceKey))
        {
            return;
        }

        var depth =
            parentKey is not null &&
            _dialOwnedSurfaces.TryGetValue(
                parentKey,
                out var parent)
                ? parent.Depth + 1
                : 0;
        _dialOwnedSurfaces[surfaceKey] =
            new(
                parentKey,
                depth,
                ++_dialSurfaceMountSequence);
    }

    private IReadOnlyList<ComposerDialMenuSurfaceSnapshot>
        BuildOwnedSurfaceSnapshots(DialPopupProbe probe)
    {
        return probe.Surfaces
            .Select(surface =>
            {
                var owned =
                    _dialOwnedSurfaces.TryGetValue(
                        surface.Key,
                        out var state);
                var containsActive =
                    _dialSurfaceOptionKeys.TryGetValue(
                        surface.Key,
                        out var activeOptionKey) &&
                    surface.Options.Any(option =>
                        string.Equals(
                            option.Key,
                            activeOptionKey,
                            StringComparison.Ordinal));
                return new ComposerDialMenuSurfaceSnapshot(
                    surface.Key,
                    owned ? state!.ParentKey : null,
                    owned ? state!.Depth : 0,
                    owned ? state!.MountSequence : 0,
                    owned,
                    surface.Options.Any(option =>
                        option.HasKeyboardFocus),
                    containsActive);
            })
            .ToArray();
    }

    private static IReadOnlyList<ComposerDialMenuOptionSnapshot>
        BuildOptionSnapshots(DialPopupProbe probe)
    {
        return probe.Surfaces
            .SelectMany(surface =>
                surface.Options.Select((option, visualOrder) =>
                    new ComposerDialMenuOptionSnapshot(
                        option.Key,
                        surface.Key,
                        option.Name,
                        visualOrder,
                        option.HasKeyboardFocus,
                        IsActiveDescendant:
                            !option.HasKeyboardFocus &&
                            string.Equals(
                                option.Key,
                                probe.FocusedOptionKey,
                                StringComparison.Ordinal),
                        option.IsSelected)))
            .ToArray();
    }

    private string? CurrentDialOptionKey()
    {
        return
            !string.IsNullOrWhiteSpace(_dialActiveSurfaceKey) &&
            _dialSurfaceOptionKeys.TryGetValue(
                _dialActiveSurfaceKey,
                out var optionKey)
                ? optionKey
                : null;
    }

    private string? CurrentDialOptionName(
        DialPopupProbe? probe = null)
    {
        if (string.IsNullOrWhiteSpace(_dialActiveSurfaceKey))
        {
            return null;
        }

        if (
            _dialSurfaceOptionKeys.TryGetValue(
                _dialActiveSurfaceKey,
                out var optionKey) &&
            probe is not null)
        {
            var current = FindDialPopupOption(
                probe,
                _dialActiveSurfaceKey,
                optionKey);
            if (current is not null)
            {
                return current.Name;
            }
        }

        return _dialSurfaceOptionNames.TryGetValue(
            _dialActiveSurfaceKey,
            out var optionName)
            ? optionName
            : null;
    }

    private void RememberDialOption(
        string surfaceKey,
        string optionKey,
        string optionName)
    {
        _dialActiveSurfaceKey = surfaceKey;
        _dialSurfaceOptionKeys[surfaceKey] = optionKey;
        _dialSurfaceOptionNames[surfaceKey] = optionName;
    }

    private bool RequiresMicroSemanticNavigation(string? selectedName)
    {
        if (
            ComposerDialNativeInputPolicy
                .RequiresMicroSemanticNavigation(selectedName))
        {
            return true;
        }

        var surfaceKey = _dialActiveSurfaceKey;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (
            !string.IsNullOrWhiteSpace(surfaceKey) &&
            visited.Add(surfaceKey) &&
            _dialOwnedSurfaces.TryGetValue(
                surfaceKey,
                out var surface) &&
            !string.IsNullOrWhiteSpace(surface.ParentKey))
        {
            var parentKey = surface.ParentKey;
            if (
                _dialSurfaceOptionNames.TryGetValue(
                    parentKey,
                    out var parentSelectionName) &&
                ComposerDialNativeInputPolicy
                    .RequiresMicroSemanticNavigation(
                        parentSelectionName))
            {
                return true;
            }

            surfaceKey = parentKey;
        }

        return false;
    }

    private bool FocusInitialDialOption(
        AutomationElement window,
        int processId,
        DialPopupProbe probe,
        string surfaceKey,
        string? parentSelectionName,
        bool preferFirst)
    {
        var target =
            ComposerDialMenuSelectionPolicy.ResolveInitialTarget(
                BuildOptionSnapshots(probe),
                surfaceKey,
                parentSelectionName,
                preferFirst);
        var option =
            target is { } resolved
                ? FindDialPopupOption(probe, resolved)
                : null;
        if (target is null || option is null)
        {
            return false;
        }

        var surfaceOptionCount = probe.Surfaces
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Key,
                    surfaceKey,
                    StringComparison.Ordinal))
            ?.Options.Count ?? 0;
        var keys = ComposerDialNativeInputPolicy
            .BuildFocusNudgeSequence(
                target.Value.VisualIndex,
                surfaceOptionCount);
        var useNativeNudge =
            !ComposerDialNativeInputPolicy
                .RequiresMicroSemanticNavigation(
                    parentSelectionName);
        if (
            keys.Count == 0 ||
            !Win32Input.IsProcessForeground(processId) ||
            !TryFocusDialPopupOption(option))
        {
            return false;
        }

        var focusReady = WaitForOwnedDialObservation(
            window,
            processId,
            DialPopupMountTimeoutMs,
            current =>
                OwnsDialPopup(window, current) &&
                HasOwnedDialInputFocus(
                    processId,
                    current,
                    surfaceKey));
        if (
            !OwnsDialPopup(window, focusReady) ||
            !HasOwnedDialInputFocus(
                processId,
                focusReady,
                surfaceKey))
        {
            return false;
        }

        foreach (var key in useNativeNudge ? keys : [])
        {
            if (
                !TrySendOwnedDialKey(
                    processId,
                    focusReady,
                    surfaceKey,
                    key))
            {
                return false;
            }
        }

        var verified = WaitForOwnedDialObservation(
            window,
            processId,
            DialPopupMountTimeoutMs,
            current =>
                OwnsDialPopup(window, current) &&
                string.Equals(
                    current.FocusedOptionKey,
                    target.Value.OptionKey,
                    StringComparison.Ordinal) &&
                HasOwnedDialInputFocus(
                    processId,
                    current,
                    surfaceKey));
        if (
            !OwnsDialPopup(window, verified) ||
            !string.Equals(
                verified.FocusedOptionKey,
                target.Value.OptionKey,
                StringComparison.Ordinal) ||
            !HasOwnedDialInputFocus(
                processId,
                verified,
                surfaceKey))
        {
            return false;
        }

        UpdateOwnedDialPopup(verified);
        RememberDialOption(
            target.Value.SurfaceKey,
            target.Value.OptionKey,
            target.Value.Name);
        return true;
    }

    private static DialPopupOption? FindDialPopupOption(
        DialPopupProbe probe,
        ComposerDialMenuTarget target)
    {
        return FindDialPopupOption(
            probe,
            target.SurfaceKey,
            target.OptionKey);
    }

    private static DialPopupOption? FindDialPopupOption(
        DialPopupProbe probe,
        string? surfaceKey,
        string? optionKey)
    {
        if (
            string.IsNullOrWhiteSpace(surfaceKey) ||
            string.IsNullOrWhiteSpace(optionKey))
        {
            return null;
        }

        return probe.Options.FirstOrDefault(option =>
            string.Equals(
                option.SurfaceKey,
                surfaceKey,
                StringComparison.Ordinal) &&
            string.Equals(
                option.Key,
                optionKey,
                StringComparison.Ordinal));
    }

    private static ComposerDialMenuTarget? CreateDialMenuTarget(
        DialPopupProbe probe,
        DialPopupOption option)
    {
        var surface = probe.Surfaces.FirstOrDefault(candidate =>
            string.Equals(
                candidate.Key,
                option.SurfaceKey,
                StringComparison.Ordinal));
        if (surface is null)
        {
            return null;
        }

        var index = surface.Options
            .Select((candidate, visualIndex) =>
                new { candidate, visualIndex })
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.candidate.Key,
                    option.Key,
                    StringComparison.Ordinal))
            ?.visualIndex;
        return index is null
            ? null
            : new(
                option.SurfaceKey,
                option.Key,
                option.Name,
                index.Value);
    }

    private static bool TryFocusDialPopupOption(
        DialPopupOption option)
    {
        try
        {
            option.Element.SetFocus();
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
        AutomationElement window,
        int processId,
        DialPopupProbe probe)
    {
        if (string.IsNullOrWhiteSpace(_dialControlKey))
        {
            return false;
        }

        if (TrySendOwnedDialKey(
                processId,
                probe,
                _dialActiveSurfaceKey,
                ComposerDialNativeInputPolicy.EscapeKey))
        {
            return true;
        }

        var control = FindDialControls(window)
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Key,
                    _dialControlKey,
                    StringComparison.Ordinal)) ??
            FindDialControls(window)
                .FirstOrDefault(candidate =>
                    IsDialControlExpanded(candidate.Element));
        if (control is null)
        {
            return false;
        }

        try
        {
            if (
                control.Element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var patternObject) &&
                patternObject is ExpandCollapsePattern pattern &&
                pattern.Current.ExpandCollapseState is
                    ExpandCollapseState.Expanded or
                    ExpandCollapseState.PartiallyExpanded)
            {
                pattern.Collapse();
                return true;
            }

            return false;
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

    private static bool TrySendOwnedDialKey(
        int processId,
        DialPopupProbe probe,
        string? surfaceKey,
        ushort virtualKey)
    {
        if (
            string.IsNullOrWhiteSpace(surfaceKey) ||
            !HasOwnedDialInputFocus(
                processId,
                probe,
                surfaceKey) ||
            !Win32Input.SendKey(virtualKey))
        {
            return false;
        }

        return Win32Input.IsProcessForeground(processId);
    }

    private static bool HasOwnedDialInputFocus(
        int processId,
        DialPopupProbe probe,
        string? surfaceKey)
    {
        if (
            string.IsNullOrWhiteSpace(surfaceKey) ||
            !Win32Input.IsProcessForeground(processId))
        {
            return false;
        }

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return false;
            }

            var current = focused.Current;
            var kind = DialPopupKind(current.ControlType);
            var bounds = current.BoundingRectangle;
            if (
                current.ProcessId != processId ||
                current.IsOffscreen ||
                bounds.IsEmpty ||
                kind is null)
            {
                return false;
            }

            if (
                kind == ComposerDialPopupElementKind.Edit &&
                !ComposerDialPolicy.LooksLikeSearchEdit(
                    current.Name,
                    current.AutomationId,
                    current.ClassName,
                    current.HelpText))
            {
                return false;
            }

            var surface = probe.Surfaces.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Key,
                    surfaceKey,
                    StringComparison.Ordinal));
            if (surface is null)
            {
                return false;
            }

            return surface.Bounds.Contains(
                new System.Windows.Point(
                    bounds.Left + bounds.Width / 2,
                    bounds.Top + bounds.Height / 2));
        }
        catch
        {
            return false;
        }
    }

    private DialPopupProbe WaitForOwnedDialObservation(
        AutomationElement window,
        int processId,
        int timeoutMs,
        Func<DialPopupProbe, bool> isComplete)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var last = ProbeOwnedDialPopup(window, processId);
        while (true)
        {
            if (isComplete(last))
            {
                return last;
            }

            if (
                Environment.TickCount64 >= deadline ||
                !Win32Input.IsProcessForeground(processId))
            {
                return last;
            }

            Thread.Sleep(DialPopupPollIntervalMs);
            last = ProbeOwnedDialPopup(window, processId);
        }
    }

    private bool IsExpectedNativeNavigationState(
        AutomationElement window,
        int processId,
        DialPopupProbe before,
        DialPopupProbe current,
        ComposerDialNavigation navigation,
        string? previousActiveSurfaceKey,
        string? parentSurfaceKey,
        DialPopupOption? selectedOption,
        string? previousFocusedOptionKey)
    {
        if (!current.IsOpen || !OwnsDialPopup(window, current))
        {
            return false;
        }

        if (navigation == ComposerDialNavigation.Right)
        {
            var openedSurface = selectedOption is null
                ? null
                : FindNewDialPopupSurface(
                    before,
                    current,
                    selectedOption);
            return
                openedSurface is not null &&
                HasOwnedDialInputFocus(
                    processId,
                    current,
                    openedSurface.Key);
        }

        if (navigation == ComposerDialNavigation.Left)
        {
            return
                !string.IsNullOrWhiteSpace(parentSurfaceKey) &&
                !string.IsNullOrWhiteSpace(previousActiveSurfaceKey) &&
                current.Surfaces.All(surface =>
                    !string.Equals(
                        surface.Key,
                        previousActiveSurfaceKey,
                        StringComparison.Ordinal)) &&
                HasOwnedDialInputFocus(
                    processId,
                    current,
                    parentSurfaceKey);
        }

        if (
            navigation is not
                (ComposerDialNavigation.Up or
                 ComposerDialNavigation.Down) ||
            string.IsNullOrWhiteSpace(previousActiveSurfaceKey) ||
            string.IsNullOrWhiteSpace(current.FocusedOptionKey) ||
            !HasOwnedDialInputFocus(
                processId,
                current,
                previousActiveSurfaceKey))
        {
            return false;
        }

        var optionCount = current.Surfaces
            .FirstOrDefault(surface =>
                string.Equals(
                    surface.Key,
                    previousActiveSurfaceKey,
                    StringComparison.Ordinal))
            ?.Options.Count ?? 0;
        return
            optionCount == 1 ||
            string.IsNullOrWhiteSpace(previousFocusedOptionKey) ||
            !string.Equals(
                current.FocusedOptionKey,
                previousFocusedOptionKey,
                StringComparison.Ordinal);
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
                last.Options.Count > 0 &&
                (
                    stableOpenSamples >= 1
                    || !string.IsNullOrWhiteSpace(
                        last.FocusedOptionKey)
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

    private static DialPopupProbe WaitForDialTargetFocus(
        AutomationElement window,
        int processId,
        string targetOptionKey,
        int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var last = ProbeDialPopup(window, processId);
        while (Environment.TickCount64 < deadline)
        {
            if (
                !last.IsOpen ||
                string.Equals(
                    last.FocusedOptionKey,
                    targetOptionKey,
                    StringComparison.Ordinal))
            {
                return last;
            }

            Thread.Sleep(DialPopupPollIntervalMs);
            last = ProbeDialPopup(window, processId);
        }

        return last;
    }

    private static DialPopupSurface? FindInitialDialPopupSurface(
        DialPopupProbe probe,
        AutomationElement trigger)
    {
        if (probe.Surfaces.Count == 0)
        {
            return null;
        }

        System.Windows.Rect triggerBounds;
        try
        {
            triggerBounds = trigger.Current.BoundingRectangle;
        }
        catch (ElementNotAvailableException)
        {
            triggerBounds = System.Windows.Rect.Empty;
        }

        return probe.Surfaces
            .OrderByDescending(surface =>
                surface.Options.Any(option =>
                    option.HasKeyboardFocus))
            .ThenBy(surface =>
                DistanceBetween(
                    triggerBounds,
                    surface.Bounds))
            .ThenByDescending(surface =>
                surface.Options.Count)
            .FirstOrDefault();
    }

    private static DialPopupSurface? FindNewDialPopupSurface(
        DialPopupProbe before,
        DialPopupProbe after,
        DialPopupOption parentOption)
    {
        var previousKeys = before.Surfaces
            .Select(surface => surface.Key)
            .ToHashSet(StringComparer.Ordinal);
        var parentBounds = new System.Windows.Rect(
            parentOption.Left,
            parentOption.Top,
            Math.Max(1, parentOption.Width),
            Math.Max(1, parentOption.Height));
        return after.Surfaces
            .Where(surface =>
                !previousKeys.Contains(surface.Key))
            .OrderByDescending(surface =>
                surface.Options.Any(option =>
                    option.HasKeyboardFocus))
            .ThenBy(surface =>
                DistanceBetween(
                    parentBounds,
                    surface.Bounds))
            .ThenByDescending(surface =>
                surface.Options.Count)
            .FirstOrDefault();
    }

    private static double DistanceBetween(
        System.Windows.Rect left,
        System.Windows.Rect right)
    {
        if (left.IsEmpty || right.IsEmpty)
        {
            return double.MaxValue;
        }

        var horizontalGap = Math.Max(
            0,
            Math.Max(left.Left, right.Left) -
            Math.Min(left.Right, right.Right));
        var verticalGap = Math.Max(
            0,
            Math.Max(left.Top, right.Top) -
            Math.Min(left.Bottom, right.Bottom));
        return Math.Sqrt(
            horizontalGap * horizontalGap +
            verticalGap * verticalGap);
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

    private static bool TryCollapseDialPopupOption(
        AutomationElement element)
    {
        try
        {
            if (
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var expandObject) &&
                expandObject is ExpandCollapsePattern expand &&
                expand.Current.ExpandCollapseState is
                    ExpandCollapseState.Expanded or
                    ExpandCollapseState.PartiallyExpanded)
            {
                expand.Collapse();
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

    private DialPopupProbe ProbeOwnedDialPopup(
        AutomationElement window,
        int processId)
    {
        var hasCachedGeometry =
            _dialComposerRegion is not null &&
            _dialControlGeometry is not null;
        var probe = ProbeDialPopup(
            window,
            processId,
            _dialComposerRegion,
            _dialControlGeometry);
        if (probe.IsOpen || !hasCachedGeometry)
        {
            return probe;
        }

        return ProbeDialPopup(window, processId);
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

    private enum DialActivationIntent
    {
        OpenOnly,
        EnterSubmenu,
        SelectLeaf,
    }

    private sealed record OwnedDialSurface(
        string? ParentKey,
        int Depth,
        long MountSequence);

    private static ComposerPlanToggleResult TogglePlanModeCore(
        string shortcut,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        // Kept in the public contract for settings compatibility. Plan mode
        // now uses Codex's built-in slash command instead of a managed key.
        _ = shortcut;

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

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (
                !Win32Input.IsCodexForeground() &&
                !Win32Input.FocusCodexAndWait())
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.FocusRejected,
                    ErrorDetail: "plan-mode");
            }

            var context = FindCodexWindow();
            if (context is null)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var editor = FindComposerEditor(context.Value.Window);
            if (editor is null)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.ElementNotFound,
                    ErrorDetail:
                        PlanModeAutomationPolicy.StateUnavailableDetail);
            }

            var before = TryReadPlanModeState(context.Value.Window);
            if (!before.HasValue)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.ElementNotFound,
                    ErrorDetail:
                        PlanModeAutomationPolicy.StateUnavailableDetail);
            }

            if (FindVisibleNamedButtonNearComposer(
                    context.Value.Window,
                    PlanModeAutomationPolicy.RunningActionNames,
                    editor) is not null)
            {
                return new(
                    false,
                    IsPlanMode: before.Value,
                    Error: AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail:
                        PlanModeAutomationPolicy.RunningUnavailableDetail);
            }

            if (before.Value)
            {
                var indicator = FindPlanIndicator(
                    context.Value.Window,
                    editor);
                if (
                    indicator is null ||
                    !TryClickAutomationElement(
                        indicator,
                        context.Value.ProcessId))
                {
                    return new(
                        false,
                        IsPlanMode: true,
                        Error:
                            AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail:
                            PlanModeAutomationPolicy.CommandInvokeDetail);
                }
            }
            else
            {
                var draft = TryReadComposerDraft(editor);
                if (draft is null)
                {
                    return new(
                        false,
                        IsPlanMode: false,
                        Error:
                            AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail:
                            PlanModeAutomationPolicy.DraftUnavailableDetail);
                }

                var editorBounds = editor.Current.BoundingRectangle;
                if (!TryInsertPlanSlashQuery(
                        context.Value.Window,
                        context.Value.ProcessId,
                        draft,
                        cancellationToken))
                {
                    if (!TryRestoreAfterPlanProbe(
                            context.Value.Window,
                            context.Value.ProcessId,
                            draft,
                            cancellationToken))
                    {
                        return new(
                            false,
                            IsPlanMode: false,
                            Error:
                                AgentAutomationErrorCodes
                                    .ElementUnsupported,
                            ErrorDetail:
                                PlanModeAutomationPolicy
                                    .DraftRestoreDetail);
                    }

                    return new(
                        false,
                        IsPlanMode: false,
                        Error:
                            AgentAutomationErrorCodes
                                .InputInjectionFailed,
                        ErrorDetail:
                            PlanModeAutomationPolicy.SlashCommandQuery);
                }

                var command = WaitForPlanSlashCommand(
                    context.Value.Window,
                    editorBounds,
                    cancellationToken);
                if (
                    command is null ||
                    !TryClickAutomationElement(
                        command,
                        context.Value.ProcessId))
                {
                    var detail = command is null
                        ? PlanModeAutomationPolicy.CommandUnavailableDetail
                        : PlanModeAutomationPolicy.CommandInvokeDetail;
                    if (!TryRestoreAfterPlanProbe(
                            context.Value.Window,
                            context.Value.ProcessId,
                            draft,
                            cancellationToken))
                    {
                        detail =
                            PlanModeAutomationPolicy.DraftRestoreDetail;
                    }

                    return new(
                        false,
                        IsPlanMode: false,
                        Error: command is null
                            ? AgentAutomationErrorCodes.ElementNotFound
                            : AgentAutomationErrorCodes
                                .ElementUnsupported,
                        ErrorDetail: detail);
                }

                if (!TryRestoreAfterPlanProbe(
                        context.Value.Window,
                        context.Value.ProcessId,
                        draft,
                        cancellationToken))
                {
                    return new(
                        false,
                        IsPlanMode: TryReadPlanModeState(
                            context.Value.Window),
                        Error:
                            AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail:
                            PlanModeAutomationPolicy.DraftRestoreDetail);
                }
            }

            var latestWindow = context.Value.Window;
            var deadline =
                Environment.TickCount64 + PlanStateChangeTimeoutMs;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(PlanStatePollIntervalMs);
                var refreshed = FindCodexWindow();
                if (refreshed is null)
                {
                    continue;
                }

                latestWindow = refreshed.Value.Window;
                var after = TryReadPlanModeState(latestWindow);
                if (
                    after.HasValue &&
                    PlanModeAutomationPolicy.DidStateChange(
                        before.Value,
                        after.Value))
                {
                    return new(true, after.Value);
                }
            }
            while (Environment.TickCount64 < deadline);

            return new(
                false,
                IsPlanMode: before.Value,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail:
                    PlanModeAutomationPolicy.StateUnchangedDetail);
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.OperationCanceled);
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.AutomationStale,
                ErrorDetail: "plan-mode");
        }
        catch (Exception exception)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.Unexpected,
                ErrorDetail: exception.Message);
        }
    }

    private static bool TryInsertPlanSlashQuery(
        AutomationElement window,
        int processId,
        string originalDraft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var editor = FindComposerEditor(window);
        if (editor is null)
        {
            return false;
        }

        try
        {
            editor.SetFocus();
            Thread.Sleep(30);
            if (
                !Win32Input.IsProcessForeground(processId) ||
                !Win32Input.SendShortcut("Ctrl+Home") ||
                !Win32Input.SendText(
                    PlanModeAutomationPolicy.SlashCommandQuery))
            {
                return false;
            }

            var deadline = Environment.TickCount64 + 420;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(24);
                var refreshedEditor = FindComposerEditor(window);
                if (
                    refreshedEditor is not null &&
                    ComposerDraftEquals(
                        TryReadComposerDraft(refreshedEditor),
                        PlanModeAutomationPolicy.SlashCommandQuery +
                            originalDraft))
                {
                    return true;
                }
            }
            while (Environment.TickCount64 < deadline);

            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryRestoreAfterPlanProbe(
        AutomationElement window,
        int processId,
        string originalDraft,
        CancellationToken cancellationToken)
    {
        if (WaitForComposerDraft(
                window,
                originalDraft,
                timeoutMs: 260,
                cancellationToken))
        {
            return true;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var editor = FindComposerEditor(window);
            if (
                editor is null ||
                !HasInjectedPlanQuery(
                    TryReadComposerDraft(editor),
                    originalDraft))
            {
                return false;
            }

            try
            {
                editor.SetFocus();
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }

            Thread.Sleep(20);
            if (
                !Win32Input.IsProcessForeground(processId) ||
                !Win32Input.SendShortcut("Ctrl+Z"))
            {
                return false;
            }

            if (WaitForComposerDraft(
                    window,
                    originalDraft,
                    timeoutMs: 220,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private static AutomationElement? WaitForPlanSlashCommand(
        AutomationElement window,
        System.Windows.Rect editorBounds,
        CancellationToken cancellationToken)
    {
        var buttonCondition = new PropertyCondition(
            AutomationElement.ControlTypeProperty,
            ControlType.Button);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var buttons = window.FindAll(
                TreeScope.Descendants,
                buttonCondition);
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    var bounds = button.Current.BoundingRectangle;
                    if (
                        button.Current.IsEnabled &&
                        !button.Current.IsOffscreen &&
                        !bounds.IsEmpty &&
                        bounds.Bottom <= editorBounds.Bottom + 8 &&
                        IsNearComposerColumn(bounds, editorBounds) &&
                        PlanModeAutomationPolicy.IsSlashCommand(
                            button.Current.Name))
                    {
                        return button;
                    }
                }
                catch (ElementNotAvailableException)
                {
                    // Continue if the filtered command list rerendered.
                }
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static bool IsNearComposerColumn(
        System.Windows.Rect candidate,
        System.Windows.Rect composer)
    {
        var horizontalOverlap =
            Math.Min(candidate.Right, composer.Right) -
            Math.Max(candidate.Left, composer.Left);
        return horizontalOverlap >= composer.Width * 0.5;
    }

    private static bool TryClickAutomationElement(
        AutomationElement element,
        int processId)
    {
        try
        {
            var bounds = element.Current.BoundingRectangle;
            if (
                element.Current.ProcessId != processId ||
                !element.Current.IsEnabled ||
                element.Current.IsOffscreen ||
                bounds.IsEmpty ||
                !Win32Input.IsProcessForeground(processId))
            {
                return false;
            }

            var x = bounds.Left + (bounds.Width / 2);
            var y = bounds.Top + (bounds.Height / 2);
            return
                double.IsFinite(x) &&
                double.IsFinite(y) &&
                Win32Input.ClickAt(
                    (int)Math.Round(x),
                    (int)Math.Round(y));
        }
        catch (ElementNotAvailableException)
        {
            // Chromium replaced the element before the click.
        }

        return false;
    }

    private ComposerPickerResult OpenPickerCore(
        ComposerPickerView view,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (view == ComposerPickerView.Model)
        {
            var gate = ValidateInteractiveBridge(settings);
            if (gate is not null)
            {
                return new(
                    false,
                    Error: gate.Error,
                    ErrorDetail: gate.ErrorDetail);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var shortcut = settings.ModelPickerShortcut.Trim();
            if (
                shortcut.Length == 0 ||
                !_sendShortcut(shortcut))
            {
                return new(
                    false,
                    Error:
                        AgentAutomationErrorCodes.InputInjectionFailed,
                    ErrorDetail: "composer-model-picker-shortcut");
            }

            // The shortcut is provisioned to the official
            // `composer.openModelPicker` command. Once Codex accepts it,
            // arrows, Enter, and Escape own the complete interaction. The
            // Chromium accessibility tree is deliberately not inspected.
            MarkNativePickerOpen(
                "Model",
                selectionRequiresExplicitDismiss: true);
            return new(
                true,
                "Model",
                IsMenuOpen: true);
        }

        if (view == ComposerPickerView.Simple)
        {
            var gate = ValidateInteractiveBridge(settings);
            if (gate is not null)
            {
                return new(
                    false,
                    Error: gate.Error,
                    ErrorDetail: gate.ErrorDetail);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (_microInput.TryOpenReasoningControl())
            {
                MarkNativePickerOpen("Power");
                return new(
                    true,
                    "Power",
                    IsMenuOpen: true);
            }
        }

        var failure = PreparePicker(
            view,
            settings,
            cancellationToken,
            out var context);
        if (failure is not null)
        {
            return failure;
        }

        if (view == ComposerPickerView.Advanced)
        {
            lock (_dialSync)
            {
                var probe = ProbeDialPopup(
                    context!.Window,
                    context.ProcessId);
                if (!TryAdoptOpenDialPopup(
                        context.Window,
                        context.ProcessId,
                        probe))
                {
                    return new(
                        false,
                        SafeName(context.ComposerButton),
                        IsMenuOpen: probe.IsOpen,
                        Error:
                            AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail: "dial-popup-not-owned");
                }
            }
        }

        if (view == ComposerPickerView.Simple)
        {
            MarkNativePickerOpen(SafeName(context!.ComposerButton));
        }

        return new(
            true,
            SafeName(context!.ComposerButton),
            IsMenuOpen: true);
    }

    private ComposerPickerResult StepSimplePowerCore(
        int steps,
        bool pickerMenuLikelyOpen,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (steps == 0)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "composer-power-direction");
        }

        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return new(
                false,
                Error: gate.Error,
                ErrorDetail: gate.ErrorDetail);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_microInput.TryStepReasoning(
                steps,
                openFirst: !pickerMenuLikelyOpen))
        {
            MarkNativePickerOpen("Power");
            return new(
                true,
                steps > 0 ? "Power +" : "Power -",
                IsMenuOpen: true);
        }

        // Preferred transport: the provisioned Codex keybinding
        // (composer.increase/decreaseReasoningEffort). A keystroke costs
        // milliseconds where a menu pass costs hundreds, which is what
        // makes held-stick acceleration real end to end. Readback against
        // the composer button verifies every attempt; the menu path below
        // stays as the fallback and as the boundary disambiguator.
        var shortcutSawNoChange = false;
        if (
            !string.IsNullOrWhiteSpace(PowerShortcut(steps, settings)) &&
            _powerShortcutHealth.ShouldAttempt())
        {
            var viaShortcut = StepPowerViaShortcut(
                steps,
                settings,
                cancellationToken);
            if (viaShortcut is not null)
            {
                if (viaShortcut.Succeeded)
                {
                    return viaShortcut;
                }

                if (!IsPowerNoChangeResult(viaShortcut))
                {
                    return viaShortcut;
                }

                if (_powerShortcutHealth.IsProven)
                {
                    // The binding is known to work, so an unchanged
                    // readback means the value sits at a boundary. Running
                    // menu automation here could apply the step twice when
                    // the UI is merely slow.
                    return viaShortcut;
                }

                shortcutSawNoChange = true;
            }
        }

        var viaMenu = StepSimplePowerViaMenu(
            steps,
            settings,
            cancellationToken);
        if (viaMenu.Succeeded && shortcutSawNoChange)
        {
            _powerShortcutHealth.MarkSuspect();
        }

        return viaMenu;
    }

    private static string PowerShortcut(int steps, AppSettings settings) =>
        steps > 0
            ? settings.ReasoningUpShortcut
            : settings.ReasoningDownShortcut;

    private static bool IsPowerNoChangeResult(ComposerPickerResult result) =>
        result.ErrorDetail
            is "composer-power-no-change-right"
            or "composer-power-no-change-left";

    private ComposerPickerResult? StepPowerViaShortcut(
        int steps,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return new(
                false,
                Error: gate.Error,
                ErrorDetail: gate.ErrorDetail);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var shortcut = PowerShortcut(steps, settings).Trim();
            for (var index = 0; index < Math.Abs(steps); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Win32Input.SendShortcut(shortcut))
                {
                    // Injection failed; menu automation can still drive the
                    // slider through RangeValue without keystrokes.
                    return null;
                }
            }

            // SendInput success is the acknowledgement for the managed
            // shortcut fallback. Semantic UI readback is deliberately not
            // on the input path: it previously added 550+ ms per batch and
            // then opened the UIA picker when Chromium painted late.
            return new(
                true,
                steps > 0 ? "Power +" : "Power -",
                IsMenuOpen: false,
                Error: null);
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.OperationCanceled);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (Exception exception)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.Unexpected,
                ErrorDetail: exception.Message);
        }
    }

    private ComposerPickerResult StepSimplePowerViaMenu(
        int steps,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var failure = PreparePicker(
            ComposerPickerView.Simple,
            settings,
            cancellationToken,
            out var context);
        if (failure is not null)
        {
            return failure;
        }

        var powerItem = context!.Items.FirstOrDefault(item =>
                ComposerPickerViewPolicy.IsPowerItem(
                    SafePickerDescriptor(item))) ??
            context.Items.FirstOrDefault(HasWritableRangeValue);
        if (powerItem is null)
        {
            return new(
                false,
                SafeName(context.ComposerButton),
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "composer-power-focus");
        }

        var previousValue = SafeName(context.ComposerButton);
        cancellationToken.ThrowIfCancellationRequested();
        var changedThroughRange =
            TryStepPowerRangeValue(powerItem, steps);
        if (!changedThroughRange)
        {
            if (!TryFocusPickerItem(powerItem, context.ProcessId))
            {
                return new(
                    false,
                    SafeName(context.ComposerButton),
                    IsMenuOpen: true,
                    Error: AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "composer-power-focus");
            }

            var key = steps > 0
                ? ComposerDialNativeInputPolicy.RightKey
                : ComposerDialNativeInputPolicy.LeftKey;
            for (var index = 0; index < Math.Abs(steps); index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (
                    !Win32Input.IsProcessForeground(context.ProcessId) ||
                    !Win32Input.SendKey(key))
                {
                    return new(
                        false,
                        SafeName(context.ComposerButton),
                        IsMenuOpen: true,
                        Error: AgentAutomationErrorCodes.ElementUnsupported,
                        ErrorDetail: "composer-power-input");
                }

                Thread.Sleep(45);
            }
        }

        var deadline = Environment.TickCount64 + 700;
        do
        {
            Thread.Sleep(40);
            cancellationToken.ThrowIfCancellationRequested();
            var currentButton = FindComposerButton(context.Window);
            var currentValue = currentButton is null
                ? string.Empty
                : SafeName(currentButton);
            if (
                currentValue.Length > 0 &&
                !string.Equals(
                    currentValue,
                    previousValue,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    true,
                    currentValue,
                    IsMenuOpen: true);
            }
        }
        while (Environment.TickCount64 < deadline);

        if (changedThroughRange)
        {
            return new(
                true,
                previousValue,
                IsMenuOpen: true);
        }

        return new(
            false,
            previousValue,
            IsMenuOpen: true,
            Error: AgentAutomationErrorCodes.ElementUnsupported,
            ErrorDetail: steps > 0
                ? "composer-power-no-change-right"
                : "composer-power-no-change-left");
    }

    private ComposerPickerResult ToggleSpeedCore(
        bool allowShortcutFastPath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        // The composer button name carries the live speed as a trailing
        // token ("… Standard" / "… Fast"). When it is readable, decide the
        // toggle target from it instead of walking picker menus, so the
        // toggle keeps working even when the menu action wording changes.
        var currentSpeed = TryReadComposerSpeed();
        if (currentSpeed is not null)
        {
            return SetSimpleSpeedCore(
                fast: !currentSpeed.Value,
                allowShortcutFastPath,
                settings,
                cancellationToken);
        }

        var simpleFailure = PreparePicker(
            ComposerPickerView.Simple,
            settings,
            cancellationToken,
            out var simpleContext);
        if (simpleFailure is null)
        {
            var hasEnableFast = simpleContext!.Items.Any(item =>
                ComposerPickerViewPolicy.IsEnableFastAction(
                    SafePickerDescriptor(item)));
            var hasEnableStandard = simpleContext.Items.Any(item =>
                ComposerPickerViewPolicy.IsEnableStandardAction(
                    SafePickerDescriptor(item)));
            if (hasEnableFast != hasEnableStandard)
            {
                return SetSimpleSpeedCore(
                    fast: hasEnableFast,
                    allowShortcutFastPath: false,
                    settings,
                    cancellationToken);
            }

            var fastToggle = simpleContext.Items.FirstOrDefault(item =>
                ComposerPickerViewPolicy.IsFastToggle(
                    SafePickerDescriptor(item)));
            var toggleState = TryReadToggleState(fastToggle);
            if (toggleState is not null)
            {
                return SetSimpleSpeedCore(
                    fast: !toggleState.Value,
                    allowShortcutFastPath: false,
                    settings,
                    cancellationToken);
            }

            if (
                fastToggle is not null &&
                TryInvokePickerItem(
                    fastToggle,
                    simpleContext.ProcessId))
            {
                Thread.Sleep(80);
                cancellationToken.ThrowIfCancellationRequested();
                var after = TryReadComposerSpeed();
                if (after is not null)
                {
                    return new(
                        true,
                        ComposerSpeedSelectionPolicy.TargetLabel(
                            after.Value),
                        IsMenuOpen: true);
                }

                var readbackFailure = PreparePicker(
                    ComposerPickerView.Simple,
                    settings,
                    cancellationToken,
                    out var refreshed);
                var refreshedToggle = refreshed?.Items.FirstOrDefault(item =>
                    ComposerPickerViewPolicy.IsFastToggle(
                        SafePickerDescriptor(item)));
                var refreshedState = TryReadToggleState(refreshedToggle);
                if (
                    readbackFailure is null &&
                    refreshedState is not null)
                {
                    return new(
                        true,
                        ComposerSpeedSelectionPolicy.TargetLabel(
                            refreshedState.Value),
                        IsMenuOpen: true);
                }

                return new(
                    false,
                    IsMenuOpen: true,
                    Error:
                        AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "composer-speed-readback");
            }
        }
        else if (
            !string.Equals(
                simpleFailure.ErrorDetail,
                "composer-picker-view:simple",
                StringComparison.Ordinal))
        {
            return simpleFailure;
        }

        var advancedFailure = PreparePicker(
            ComposerPickerView.Advanced,
            settings,
            cancellationToken,
            out var advancedContext);
        if (advancedFailure is not null)
        {
            return advancedFailure;
        }

        const string category = "Speed";
        var categoryItem = advancedContext!.Items.FirstOrDefault(item =>
            IsAdvancedCategoryItem(SafeName(item), category));
        var categoryName = categoryItem is null
            ? string.Empty
            : SafeName(categoryItem);
        if (
            categoryItem is null ||
            (
                !ComposerSpeedSelectionPolicy.MatchesCategory(
                    categoryName,
                    fast: true) &&
                !ComposerSpeedSelectionPolicy.MatchesCategory(
                    categoryName,
                    fast: false)
            ))
        {
            return new(
                false,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementNotFound,
                ErrorDetail: "composer-speed-current");
        }

        var targetFast =
            !ComposerSpeedSelectionPolicy.MatchesCategory(
                categoryName,
                fast: true);
        return SetSimpleSpeedCore(
            targetFast,
            allowShortcutFastPath: false,
            settings,
            cancellationToken);
    }

    private bool? TryReadComposerSpeed()
    {
        try
        {
            var codex = FindCodexWindow();
            var button = codex is null
                ? null
                : FindComposerButton(codex.Value.Window);
            return button is null
                ? null
                : ComposerSpeedSelectionPolicy.TryParseSpeedSuffix(
                    SafeName(button));
        }
        catch
        {
            return null;
        }
    }

    private ComposerPickerResult SetSimpleSpeedCore(
        bool fast,
        bool allowShortcutFastPath,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return new(
                false,
                Error: gate.Error,
                ErrorDetail: gate.ErrorDetail);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (_microInput.TryToggleFast())
        {
            return new(
                true,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: false);
        }

        // Preferred transport: the provisioned composer.toggleFastMode
        // keybinding, verified against the composer button's trailing
        // speed token. Setting a target (not toggling blindly) keeps the
        // menu fallback idempotent even if the keystroke lands late.
        var shortcutSawMiss = false;
        if (
            allowShortcutFastPath &&
            !string.IsNullOrWhiteSpace(settings.FastToggleShortcut) &&
            _speedShortcutHealth.ShouldAttempt())
        {
            var viaShortcut = SetSpeedViaShortcut(
                fast,
                settings,
                cancellationToken);
            if (viaShortcut is not null)
            {
                if (viaShortcut.Succeeded)
                {
                    return viaShortcut;
                }

                if (!string.Equals(
                        viaShortcut.ErrorDetail,
                        "composer-speed-readback",
                        StringComparison.Ordinal))
                {
                    return viaShortcut;
                }

                shortcutSawMiss = true;
            }
        }

        var direct = SetSimpleSpeedDirectCore(
            fast,
            settings,
            cancellationToken);
        if (
            direct.Succeeded ||
            direct.ErrorDetail is not
                (
                    "composer-picker-view:simple" or
                    "composer-speed-option" or
                    "composer-speed-readback"
                ))
        {
            if (direct.Succeeded && shortcutSawMiss)
            {
                _speedShortcutHealth.MarkSuspect();
            }

            return direct;
        }

        var advanced = SetAdvancedSpeedCore(
            fast,
            settings,
            cancellationToken);
        if (advanced.Succeeded && shortcutSawMiss)
        {
            _speedShortcutHealth.MarkSuspect();
        }

        return advanced;
    }

    private ComposerPickerResult? SetSpeedViaShortcut(
        bool fast,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return new(
                false,
                Error: gate.Error,
                ErrorDetail: gate.ErrorDetail);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Win32Input.SendShortcut(
                    settings.FastToggleShortcut.Trim()))
            {
                return null;
            }

            // The caller owns the target-state cache. Waiting for Chromium's
            // accessible name previously turned an instant semantic command
            // into a 700 ms operation and could trigger a second UI action.
            return new(
                true,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: false);
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.OperationCanceled);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (Exception exception)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.Unexpected,
                ErrorDetail: exception.Message);
        }
    }

    private ComposerPickerResult SetSimpleSpeedDirectCore(
        bool fast,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var failure = PreparePicker(
            ComposerPickerView.Simple,
            settings,
            cancellationToken,
            out var context);
        if (failure is not null)
        {
            return failure;
        }

        var enableFast = context!.Items.FirstOrDefault(item =>
            ComposerPickerViewPolicy.IsEnableFastAction(
                SafePickerDescriptor(item)));
        var enableStandard = context.Items.FirstOrDefault(item =>
            ComposerPickerViewPolicy.IsEnableStandardAction(
                SafePickerDescriptor(item)));
        var fastToggle = context.Items.FirstOrDefault(item =>
            ComposerPickerViewPolicy.IsFastToggle(
                SafePickerDescriptor(item)));
        var toggleState = TryReadToggleState(fastToggle);
        var composerState = TryReadComposerSpeed();
        if (
            toggleState == fast ||
            (
                toggleState is null &&
                composerState == fast
            ))
        {
            return new(
                true,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: true);
        }

        var action = fast ? enableFast : enableStandard;
        var alreadySelected = fast
            ? enableStandard is not null
            : enableFast is not null;
        if (action is null && alreadySelected)
        {
            return new(
                true,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: true);
        }

        action ??= fastToggle;
        if (
            action is null ||
            !TryInvokePickerItem(action, context.ProcessId))
        {
            return new(
                false,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementNotFound,
                ErrorDetail: "composer-speed-option");
        }

        Thread.Sleep(80);
        cancellationToken.ThrowIfCancellationRequested();
        if (TryReadComposerSpeed() == fast)
        {
            return new(
                true,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: true);
        }

        var readbackFailure = PreparePicker(
            ComposerPickerView.Simple,
            settings,
            cancellationToken,
            out var refreshed);
        if (readbackFailure is not null)
        {
            return readbackFailure with
            {
                Value = ComposerSpeedSelectionPolicy.TargetLabel(fast),
            };
        }

        var hasEnableFast = refreshed!.Items.Any(item =>
            ComposerPickerViewPolicy.IsEnableFastAction(
                SafePickerDescriptor(item)));
        var hasEnableStandard = refreshed.Items.Any(item =>
            ComposerPickerViewPolicy.IsEnableStandardAction(
                SafePickerDescriptor(item)));
        var refreshedToggle = refreshed.Items.FirstOrDefault(item =>
            ComposerPickerViewPolicy.IsFastToggle(
                SafePickerDescriptor(item)));
        var refreshedToggleState = TryReadToggleState(refreshedToggle);
        var confirmed =
            refreshedToggleState == fast ||
            (fast ? hasEnableStandard : hasEnableFast) ||
            TryReadComposerSpeed() == fast;
        return confirmed
            ? new(
                true,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: true)
            : new(
                false,
                ComposerSpeedSelectionPolicy.TargetLabel(fast),
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "composer-speed-readback");
    }

    private ComposerPickerResult SetAdvancedSpeedCore(
        bool fast,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var failure = PreparePicker(
            ComposerPickerView.Advanced,
            settings,
            cancellationToken,
            out var context);
        if (failure is not null)
        {
            return failure;
        }

        const string category = "Speed";
        var targetName =
            ComposerSpeedSelectionPolicy.TargetLabel(fast);
        var categoryItem = context!.Items.FirstOrDefault(item =>
            IsAdvancedCategoryItem(SafeName(item), category));
        AutomationElement? refreshedCategoryItem = null;
        try
        {
            var categoryName = categoryItem is null
                ? string.Empty
                : SafeName(categoryItem);
            if (
                categoryItem is not null &&
                ComposerSpeedSelectionPolicy.MatchesCategory(
                    categoryName,
                    fast))
            {
                return new(
                    true,
                    targetName,
                    IsMenuOpen: true);
            }

            if (
                categoryItem is null ||
                !TryGetDialPopupContainer(
                    categoryItem,
                    context.Window,
                    out var rootContainerKey,
                    out _) ||
                !TryExpand(categoryItem))
            {
                return new(
                    false,
                    targetName,
                    IsMenuOpen: true,
                    Error: AgentAutomationErrorCodes.ElementNotFound,
                    ErrorDetail: "composer-speed-option");
            }

            var currentValue = AdvancedCategoryValue(
                categoryName,
                category);
            var options = WaitForAdvancedOptions(
                context.Window,
                context.ProcessId,
                rootContainerKey,
                currentValue,
                cancellationToken);
            var target = options?.FirstOrDefault(option =>
                ComposerSpeedSelectionPolicy.MatchesOption(
                    option.Name,
                    fast));
            if (
                target is null ||
                !TryInvokePickerItem(target.Element, context.ProcessId))
            {
                return new(
                    false,
                    targetName,
                    IsMenuOpen: true,
                    Error: AgentAutomationErrorCodes.ElementNotFound,
                    ErrorDetail: "composer-speed-option");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var readbackDeadline = Environment.TickCount64 + 900;
            ComposerPickerResult? lastReadbackFailure = null;
            do
            {
                Thread.Sleep(80);
                cancellationToken.ThrowIfCancellationRequested();
                var readbackFailure = PreparePicker(
                    ComposerPickerView.Advanced,
                    settings,
                    cancellationToken,
                    out var refreshed);
                if (readbackFailure is not null)
                {
                    lastReadbackFailure = readbackFailure;
                    if (
                        readbackFailure.Error ==
                            AgentAutomationErrorCodes.OperationCanceled ||
                        readbackFailure.Error ==
                            AgentAutomationErrorCodes.AgentNotForeground ||
                        readbackFailure.Error ==
                            AgentAutomationErrorCodes.BridgeSafePreview)
                    {
                        return readbackFailure with
                        {
                            Value = targetName,
                        };
                    }

                    continue;
                }

                var refreshedCategory =
                    refreshed!.Items.FirstOrDefault(item =>
                        IsAdvancedCategoryItem(
                            SafeName(item),
                            category));
                refreshedCategoryItem = refreshedCategory;
                if (
                    refreshedCategory is not null &&
                    ComposerSpeedSelectionPolicy.MatchesCategory(
                        SafeName(refreshedCategory),
                        fast))
                {
                    return new(
                        true,
                        targetName,
                        IsMenuOpen: true);
                }
            }
            while (Environment.TickCount64 < readbackDeadline);

            if (lastReadbackFailure is not null)
            {
                return lastReadbackFailure with
                {
                    Value = targetName,
                };
            }

            return new(
                false,
                targetName,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "composer-speed-readback");
        }
        finally
        {
            TryCollapse(refreshedCategoryItem);
            TryCollapse(categoryItem);
        }
    }

    private ComposerPickerResult StepAdvancedCore(
        ComposerSettingKind kind,
        int direction,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (direction == 0)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: "composer-advanced-direction");
        }

        var failure = PreparePicker(
            ComposerPickerView.Advanced,
            settings,
            cancellationToken,
            out var context);
        if (failure is not null)
        {
            return failure;
        }

        var category = CategoryLabel(kind);
        var categoryItem = context!.Items.FirstOrDefault(item =>
            IsAdvancedCategoryItem(SafeName(item), category));
        if (categoryItem is null)
        {
            return new(
                false,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementNotFound,
                ErrorDetail: $"composer-advanced-category:{category}");
        }

        var categoryName = SafeName(categoryItem);
        var currentValue = AdvancedCategoryValue(
            categoryName,
            category);
        if (
            !TryGetDialPopupContainer(
                categoryItem,
                context.Window,
                out var rootContainerKey,
                out _) ||
            !TryExpand(categoryItem))
        {
            return new(
                false,
                currentValue,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: $"composer-advanced-expand:{category}");
        }

        var options = WaitForAdvancedOptions(
            context.Window,
            context.ProcessId,
            rootContainerKey,
            currentValue,
            cancellationToken);
        if (options is null || options.Count == 0)
        {
            return new(
                false,
                currentValue,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementNotFound,
                ErrorDetail: $"composer-advanced-options:{category}");
        }

        var currentIndex = options
            .Select((option, index) => new { option, index })
            .FirstOrDefault(item => item.option.IsSelected)?.index ??
            options
                .Select((option, index) => new { option, index })
                .FirstOrDefault(item =>
                    ComposerSpeedSelectionPolicy
                        .OptionMatchesCurrentValue(
                            item.option.Name,
                            currentValue))?.index ??
            -1;
        if (currentIndex < 0)
        {
            return new(
                false,
                currentValue,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementNotFound,
                ErrorDetail: $"composer-advanced-current:{category}");
        }

        var nextIndex =
            ComposerPickerVisualOrderPolicy.ResolveNextIndex(
                currentIndex,
                options.Count,
                direction);
        if (nextIndex == currentIndex)
        {
            return new(
                false,
                options[currentIndex].Name,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: direction > 0
                    ? "composer-advanced-upper-boundary"
                    : "composer-advanced-lower-boundary");
        }

        var target = options[nextIndex];
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryInvokePickerItem(target.Element, context.ProcessId))
        {
            return new(
                false,
                target.Name,
                IsMenuOpen: true,
                Error: AgentAutomationErrorCodes.ElementUnsupported,
                ErrorDetail: $"composer-advanced-select:{category}");
        }

        var readbackDeadline = Environment.TickCount64 + 900;
        var actualValue = string.Empty;
        ComposerPickerResult? lastReadbackFailure = null;
        do
        {
            Thread.Sleep(80);
            cancellationToken.ThrowIfCancellationRequested();
            var readbackFailure = PreparePicker(
                ComposerPickerView.Advanced,
                settings,
                cancellationToken,
                out var refreshed);
            if (readbackFailure is not null)
            {
                lastReadbackFailure = readbackFailure;
                if (
                    readbackFailure.Error ==
                        AgentAutomationErrorCodes.OperationCanceled ||
                    readbackFailure.Error ==
                        AgentAutomationErrorCodes.AgentNotForeground ||
                    readbackFailure.Error ==
                        AgentAutomationErrorCodes.BridgeSafePreview)
                {
                    return readbackFailure with { Value = target.Name };
                }

                continue;
            }

            var refreshedCategory = refreshed!.Items.FirstOrDefault(item =>
                IsAdvancedCategoryItem(SafeName(item), category));
            actualValue = refreshedCategory is null
                ? string.Empty
                : AdvancedCategoryValue(
                    SafeName(refreshedCategory),
                    category);
            if (
                ComposerSpeedSelectionPolicy
                    .OptionMatchesCurrentValue(
                        target.Name,
                        actualValue))
            {
                return new(
                    true,
                    actualValue,
                    IsMenuOpen: true);
            }
        }
        while (Environment.TickCount64 < readbackDeadline);

        if (actualValue.Length == 0 && lastReadbackFailure is not null)
        {
            return lastReadbackFailure with { Value = target.Name };
        }

        return new(
            false,
            actualValue.Length > 0 ? actualValue : target.Name,
            IsMenuOpen: true,
            Error: AgentAutomationErrorCodes.ElementUnsupported,
            ErrorDetail: $"composer-advanced-readback:{category}");
    }

    private static IReadOnlyList<AdvancedPickerOption>?
        WaitForAdvancedOptions(
            AutomationElement window,
            int processId,
            string rootContainerKey,
            string currentValue,
            CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = new List<AdvancedPickerOption>();
            var sequence = 0;
            foreach (var item in FindMenuItems(window, processId))
            {
                var itemSequence = sequence++;
                if (!IsEnabledMenuElement(item))
                {
                    continue;
                }

                var name = SafeName(item);
                if (
                    name.Length == 0 ||
                    !TryGetDialPopupContainer(
                        item,
                        window,
                        out var containerKey,
                        out var containerBounds) ||
                    string.Equals(
                        containerKey,
                        rootContainerKey,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    candidates.Add(new(
                        containerKey,
                        containerBounds,
                        item.Current.BoundingRectangle,
                        name,
                        itemSequence,
                        IsDialPopupOptionSelected(item),
                        item));
                }
                catch (ElementNotAvailableException)
                {
                    // The next poll sees the replacement submenu item.
                }
            }

            var group = candidates
                .GroupBy(option => option.ContainerKey)
                .Select(options => new
                {
                    Options = options.ToArray(),
                    HasSelected = options.Any(option => option.IsSelected),
                    HasCurrent = options.Any(option =>
                        ComposerSpeedSelectionPolicy
                            .OptionMatchesCurrentValue(
                                option.Name,
                                currentValue)),
                    Bounds = options.First().ContainerBounds,
                })
                .Where(item => item.HasSelected || item.HasCurrent)
                .OrderByDescending(item => item.HasSelected)
                .ThenByDescending(item => item.HasCurrent)
                .ThenBy(item => item.Bounds.Left)
                .ThenBy(item => item.Bounds.Top)
                .FirstOrDefault();
            if (group is not null)
            {
                return group.Options
                    .OrderBy(option => option.ItemBounds.Top)
                    .ThenBy(option => option.ItemBounds.Left)
                    .ThenBy(option => option.Sequence)
                    .ToArray();
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static bool IsAdvancedCategoryItem(
        string name,
        string category) =>
        string.Equals(
            name,
            category,
            StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(
            $"{category} ",
            StringComparison.OrdinalIgnoreCase);

    private static string AdvancedCategoryValue(
        string name,
        string category)
    {
        return name.StartsWith(
            category,
            StringComparison.OrdinalIgnoreCase)
            ? name[category.Length..]
                .Trim(' ', ':', '-', '·')
            : string.Empty;
    }

    private ComposerPickerResult? PreparePicker(
        ComposerPickerView view,
        AppSettings settings,
        CancellationToken cancellationToken,
        out ComposerPickerContext? context)
    {
        context = null;
        var gate = ValidateInteractiveBridge(settings);
        if (gate is not null)
        {
            return new(
                false,
                Error: gate.Error,
                ErrorDetail: gate.ErrorDetail);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codex = FindCodexWindow();
            if (codex is null)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.AgentWindowNotFound);
            }

            var composerButton = FindComposerButton(codex.Value.Window);
            if (composerButton is null)
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.ElementNotFound,
                    ErrorDetail: "composer-model-button");
            }

            if (!TryExpand(composerButton))
            {
                return new(
                    false,
                    Error: AgentAutomationErrorCodes.ElementUnsupported,
                    ErrorDetail: "composer-model-button:expand");
            }

            var items = EnsurePickerView(
                codex.Value.Window,
                codex.Value.ProcessId,
                view,
                cancellationToken);
            if (items is null)
            {
                return new(
                    false,
                    SafeName(composerButton),
                    IsMenuOpen: true,
                    Error: AgentAutomationErrorCodes.ElementNotFound,
                    ErrorDetail:
                        $"composer-picker-view:{view.ToString().ToLowerInvariant()}");
            }

            var refreshedComposerButton =
                FindComposerButton(codex.Value.Window) ??
                composerButton;
            context = new(
                codex.Value.Window,
                codex.Value.ProcessId,
                refreshedComposerButton,
                items);
            return null;
        }
        catch (OperationCanceledException)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.OperationCanceled);
        }
        catch (ElementNotAvailableException)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.AutomationStale,
                ErrorDetail: "composer-picker");
        }
        catch (Exception exception)
        {
            return new(
                false,
                Error: AgentAutomationErrorCodes.Unexpected,
                ErrorDetail: exception.Message);
        }
    }

    private static AutomationElement[]? EnsurePickerView(
        AutomationElement window,
        int processId,
        ComposerPickerView desired,
        CancellationToken cancellationToken)
    {
        var toggled = false;
        for (var attempt = 0; attempt < 24; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enabledItems = FindPickerElements(window, processId)
                .Where(IsEnabledMenuElement)
                .ToArray();
            var visibleItems = enabledItems
                .Where(IsUsableMenuElement)
                .ToArray();
            var current = ComposerPickerViewPolicy.Detect(
                visibleItems.Select(SafePickerDescriptor),
                hasPowerRange:
                    visibleItems.Any(HasWritableRangeValue));
            if (current == desired)
            {
                return desired == ComposerPickerView.Simple
                    ? enabledItems
                        .Where(item =>
                            IsUsableMenuElement(item) ||
                            HasWritableRangeValue(item) ||
                            ComposerPickerViewPolicy.IsPowerItem(
                                SafePickerDescriptor(item)) ||
                            ComposerPickerViewPolicy.IsFastToggle(
                                SafePickerDescriptor(item)) ||
                            ComposerPickerViewPolicy.IsEnableFastAction(
                                SafePickerDescriptor(item)) ||
                            ComposerPickerViewPolicy.IsEnableStandardAction(
                                SafePickerDescriptor(item)))
                        .ToArray()
                    : visibleItems;
            }

            if (!toggled && current != ComposerPickerView.Unknown)
            {
                var toggle = visibleItems.FirstOrDefault(item =>
                    ComposerPickerViewPolicy.IsViewToggleToward(
                        SafePickerDescriptor(item),
                        desired));
                if (
                    toggle is null ||
                    !TryInvokePickerItem(toggle, processId))
                {
                    return null;
                }

                toggled = true;
            }

            Thread.Sleep(60);
        }

        return null;
    }

    private static bool TryFocusPickerItem(
        AutomationElement element,
        int processId)
    {
        try
        {
            if (!Win32Input.IsProcessForeground(processId))
            {
                return false;
            }

            var focused = AutomationElement.FocusedElement;
            if (IsPickerItemFocusWithin(element, focused))
            {
                return true;
            }

            var focusRequested = false;
            try
            {
                element.SetFocus();
                focusRequested = true;
            }
            catch (InvalidOperationException)
            {
                // Chromium may delegate focus to an unnamed descendant.
            }

            if (WaitForPickerItemFocus(element, processId))
            {
                return true;
            }

            if (
                TryClickAutomationElement(element, processId) &&
                WaitForPickerItemFocus(element, processId))
            {
                return true;
            }

            // SetFocus succeeding is useful evidence even when Chromium does
            // not expose the focused descendant in its accessibility tree.
            return
                focusRequested &&
                Win32Input.IsProcessForeground(processId);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStepPowerRangeValue(
        AutomationElement powerItem,
        int steps)
    {
        try
        {
            var candidates = new List<AutomationElement>
            {
                powerItem,
            };
            candidates.AddRange(
                powerItem
                    .FindAll(
                        TreeScope.Descendants,
                        Condition.TrueCondition)
                    .Cast<AutomationElement>());
            foreach (var candidate in candidates)
            {
                if (
                    !candidate.TryGetCurrentPattern(
                        RangeValuePattern.Pattern,
                        out var patternObject) ||
                    patternObject is not RangeValuePattern range ||
                    range.Current.IsReadOnly)
                {
                    continue;
                }

                var current = range.Current.Value;
                var step = range.Current.SmallChange;
                if (!double.IsFinite(step) || step <= 0)
                {
                    step = 1;
                }

                var target = Math.Clamp(
                    current + steps * step,
                    range.Current.Minimum,
                    range.Current.Maximum);
                if (Math.Abs(target - current) < double.Epsilon)
                {
                    return false;
                }

                range.SetValue(target);
                Thread.Sleep(35);
                return
                    Math.Abs(range.Current.Value - current) >
                    double.Epsilon;
            }
        }
        catch (ElementNotAvailableException)
        {
            // The keyboard fallback reacquires focus on the live menu item.
        }
        catch (InvalidOperationException)
        {
            // Chromium may expose a read-only-looking range that rejects set.
        }

        return false;
    }

    private static bool WaitForPickerItemFocus(
        AutomationElement item,
        int processId)
    {
        var deadline = Environment.TickCount64 + 240;
        do
        {
            Thread.Sleep(15);
            if (
                Win32Input.IsProcessForeground(processId) &&
                IsPickerItemFocusWithin(
                    item,
                    AutomationElement.FocusedElement))
            {
                return true;
            }
        }
        while (Environment.TickCount64 < deadline);

        return false;
    }

    private static bool IsPickerItemFocusWithin(
        AutomationElement item,
        AutomationElement? focused)
    {
        if (focused is null)
        {
            return false;
        }

        try
        {
            if (
                focused.Equals(item) ||
                ComposerPickerViewPolicy.IsPowerItem(
                    SafeName(focused)))
            {
                return true;
            }

            var parent = focused;
            for (var depth = 0; depth < 8; depth++)
            {
                parent = TreeWalker.ControlViewWalker.GetParent(parent);
                if (parent is null)
                {
                    break;
                }

                if (parent.Equals(item))
                {
                    return true;
                }
            }

            var itemBounds = item.Current.BoundingRectangle;
            var focusedBounds = focused.Current.BoundingRectangle;
            if (itemBounds.IsEmpty || focusedBounds.IsEmpty)
            {
                return false;
            }

            var center = new System.Windows.Point(
                focusedBounds.Left + focusedBounds.Width / 2,
                focusedBounds.Top + focusedBounds.Height / 2);
            return itemBounds.Contains(center);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static bool TryInvokePickerItem(
        AutomationElement element,
        int processId)
    {
        try
        {
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
                    TogglePattern.Pattern,
                    out var toggleObject) &&
                toggleObject is TogglePattern toggle)
            {
                toggle.Toggle();
                return true;
            }

            if (
                element.TryGetCurrentPattern(
                    ExpandCollapsePattern.Pattern,
                    out var expandObject) &&
                expandObject is ExpandCollapsePattern expand)
            {
                if (
                    expand.Current.ExpandCollapseState ==
                    ExpandCollapseState.Expanded)
                {
                    expand.Collapse();
                }
                else
                {
                    expand.Expand();
                }

                return true;
            }

            element.SetFocus();
            return
                Win32Input.IsProcessForeground(processId) &&
                Win32Input.SendKey(ComposerDialNativeInputPolicy.EnterKey);
        }
        catch
        {
            return false;
        }
    }

    private sealed record ComposerPickerContext(
        AutomationElement Window,
        int ProcessId,
        AutomationElement ComposerButton,
        AutomationElement[] Items);

    private sealed record AdvancedPickerOption(
        string ContainerKey,
        System.Windows.Rect ContainerBounds,
        System.Windows.Rect ItemBounds,
        string Name,
        int Sequence,
        bool IsSelected,
        AutomationElement Element);

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

            if (
                EnsurePickerView(
                    context.Value.Window,
                    context.Value.ProcessId,
                    ComposerPickerView.Advanced,
                    cancellationToken) is null)
            {
                return new(
                    false,
                    AgentAutomationErrorCodes.ElementNotFound,
                    "composer-picker-view:advanced");
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

    private static IEnumerable<AutomationElement> FindPickerElements(
        AutomationElement mainWindow,
        int processId)
    {
        var elementCondition = new OrCondition(
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
                ControlType.Button),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.CheckBox),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Slider),
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Custom));
        var composerRegion =
            TryGetComposerPopupRegion(mainWindow) ??
            TryGetComposerButtonPopupRegion(mainWindow);
        foreach (var root in FindDialPopupRoots(mainWindow, processId))
        {
            AutomationElementCollection collection;
            try
            {
                collection = root.Element.FindAll(
                    TreeScope.Descendants,
                    elementCondition);
            }
            catch
            {
                continue;
            }

            foreach (AutomationElement element in collection)
            {
                if (IsInsideComposerPopupRegion(element, composerRegion))
                {
                    yield return element;
                }
            }
        }
    }

    private static bool IsInsideComposerPopupRegion(
        AutomationElement element,
        System.Windows.Rect? composerRegion)
    {
        if (composerRegion is not { } region)
        {
            return false;
        }

        try
        {
            var bounds = element.Current.BoundingRectangle;
            return
                !bounds.IsEmpty &&
                region.Contains(
                    new System.Windows.Point(
                        bounds.Left + bounds.Width / 2,
                        bounds.Top + bounds.Height / 2));
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static System.Windows.Rect? TryGetComposerButtonPopupRegion(
        AutomationElement mainWindow)
    {
        var composerButton = FindComposerButton(mainWindow);
        if (composerButton is null)
        {
            return null;
        }

        try
        {
            var bounds = composerButton.Current.BoundingRectangle;
            return new System.Windows.Rect(
                bounds.Left - 260,
                bounds.Top - 720,
                bounds.Width + 520,
                820);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
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

    private static string SafePickerDescriptor(AutomationElement element)
    {
        try
        {
            var current = element.Current;
            return string.Join(
                " | ",
                new[]
                {
                    current.Name,
                    current.AutomationId,
                    current.HelpText,
                    current.ItemStatus,
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool HasWritableRangeValue(AutomationElement element)
    {
        try
        {
            if (
                element.TryGetCurrentPattern(
                    RangeValuePattern.Pattern,
                    out var directObject) &&
                directObject is RangeValuePattern directRange &&
                !directRange.Current.IsReadOnly)
            {
                return true;
            }

            var descendants = element.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(
                    AutomationElement
                        .IsRangeValuePatternAvailableProperty,
                    true));
            foreach (AutomationElement descendant in descendants)
            {
                if (
                    descendant.TryGetCurrentPattern(
                        RangeValuePattern.Pattern,
                        out var patternObject) &&
                    patternObject is RangeValuePattern range &&
                    !range.Current.IsReadOnly)
                {
                    return true;
                }
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

    private static bool? TryReadToggleState(AutomationElement? element)
    {
        if (element is null)
        {
            return null;
        }

        try
        {
            if (
                !element.TryGetCurrentPattern(
                    TogglePattern.Pattern,
                    out var patternObject) ||
                patternObject is not TogglePattern toggle)
            {
                return null;
            }

            return toggle.Current.ToggleState switch
            {
                ToggleState.On => true,
                ToggleState.Off => false,
                _ => null,
            };
        }
        catch
        {
            return null;
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

    private static bool IsEnabledMenuElement(AutomationElement element)
    {
        try
        {
            return element.Current.IsEnabled;
        }
        catch
        {
            return false;
        }
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

    private static string NormalizeChoice(string value) =>
        ComposerChoiceNormalizer.Normalize(value);
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
