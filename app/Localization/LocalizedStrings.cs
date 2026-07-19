using System.ComponentModel;
using CodexController.Agents;

namespace CodexController.Localization;

/// <summary>
/// Bindable facade over the active string catalog. Static labels are exposed
/// as properties; labels that depend on the selected Agent, controller model,
/// or physical button glyph are methods so those values never become
/// translation-layer constants.
/// </summary>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    private readonly LocalizationService _localization;

    internal LocalizedStrings(LocalizationService localization)
    {
        _localization = localization
            ?? throw new ArgumentNullException(nameof(localization));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppLanguage Language => _localization.EffectiveLanguage;

    public string this[string key] => _localization.Catalog[key];

    public string AppTitle => Get(StringKeys.AppTitle);
    public string NavDevice => Get(StringKeys.NavDevice);
    public string NavConfiguration =>
        Get(StringKeys.NavConfiguration);
    public string NavSettings => Get(StringKeys.NavSettings);

    public string DeviceWaiting => Get(StringKeys.DeviceWaiting);
    public string DeviceDisconnected =>
        Get(StringKeys.DeviceDisconnected);
    public string DeviceEnableBridge =>
        Get(StringKeys.DeviceEnableBridge);
    public string DeviceGamepadBridge =>
        Get(StringKeys.DeviceGamepadBridge);
    public string DeviceLiveInput =>
        Get(StringKeys.DeviceLiveInput);
    public string DeviceIdle => Get(StringKeys.DeviceIdle);

    public string ControlLeftStick =>
        Get(StringKeys.ControlLeftStick);
    public string ControlRightStick =>
        Get(StringKeys.ControlRightStick);
    public string ControlPrimaryDescription =>
        Get(StringKeys.ControlPrimaryDescription);
    public string ControlHoldToTalkDescription =>
        Get(StringKeys.ControlHoldToTalkDescription);
    public string ControlSendDescription =>
        Get(StringKeys.ControlSendDescription);
    public string ControlCancelUndoDescription =>
        Get(StringKeys.ControlCancelUndoDescription);
    public string ControlProjectContextDescription =>
        Get(StringKeys.ControlProjectContextDescription);

    public string ComposerRightStickAdjustment =>
        Get(StringKeys.ComposerRightStickAdjustment);
    public string ComposerDialReady =>
        Get(StringKeys.ComposerDialReady);
    public string ComposerConnectController =>
        Get(StringKeys.ComposerConnectController);
    public string ComposerDialSettingsOpened =>
        Get(StringKeys.ComposerDialSettingsOpened);
    public string ComposerDialCanceled =>
        Get(StringKeys.ComposerDialCanceled);
    public string VirtualDial =>
        Get(StringKeys.TermVirtualDial);
    public string ReasoningEffort =>
        Get(StringKeys.TermReasoningEffort);
    public string Model => Get(StringKeys.TermModel);
    public string Speed => Get(StringKeys.TermSpeed);

    public string SidebarCurrentProject =>
        Get(StringKeys.SidebarCurrentProject);
    public string SidebarRefresh =>
        Get(StringKeys.SidebarRefresh);
    public string SidebarPinnedTasks =>
        Get(StringKeys.SidebarPinnedTasks);
    public string SidebarPinnedProjects =>
        Get(StringKeys.SidebarPinnedProjects);
    public string SidebarProjects =>
        Get(StringKeys.SidebarProjects);
    public string SidebarProjectlessTasks =>
        Get(StringKeys.SidebarProjectlessTasks);
    public string SidebarRecentEvents =>
        Get(StringKeys.SidebarRecentEvents);
    public string SidebarPinnedBadge =>
        Get(StringKeys.SidebarPinnedBadge);
    public string SidebarEnterAction =>
        Get(StringKeys.SidebarEnterAction);
    public string SidebarOpenAction =>
        Get(StringKeys.SidebarOpenAction);
    public string SidebarUntitledTask =>
        Get(StringKeys.SidebarUntitledTask);
    public string SidebarJustNow =>
        Get(StringKeys.SidebarJustNow);

    public string ConfigTitle => Get(StringKeys.ConfigTitle);
    public string ConfigMoveFocus =>
        Get(StringKeys.ConfigMoveFocus);
    public string ConfigEnterBack =>
        Get(StringKeys.ConfigEnterBack);
    public string ConfigEnterBackDescription =>
        Get(StringKeys.ConfigEnterBackDescription);
    public string ConfigIncreaseDecrease =>
        Get(StringKeys.ConfigIncreaseDecrease);
    public string ConfigIncreaseDecreaseDescription =>
        Get(StringKeys.ConfigIncreaseDecreaseDescription);
    public string ConfigModeSwitchDescription =>
        Get(StringKeys.ConfigModeSwitchDescription);
    public string ConfigLowerReasoning =>
        Get(StringKeys.ConfigLowerReasoning);
    public string ConfigRaiseReasoning =>
        Get(StringKeys.ConfigRaiseReasoning);
    public string ConfigToggleFast =>
        Get(StringKeys.ConfigToggleFast);
    public string ConfigSubmitPrompt =>
        Get(StringKeys.ConfigSubmitPrompt);
    public string ConfigDictation =>
        Get(StringKeys.ConfigDictation);
    public string ConfigModelPicker =>
        Get(StringKeys.ConfigModelPicker);
    public string ConfigRestoreRecommended =>
        Get(StringKeys.ConfigRestoreRecommended);
    public string ConfigSave => Get(StringKeys.ConfigSave);

    public string SettingsTitle =>
        Get(StringKeys.SettingsTitle);
    public string SettingsDescription =>
        Get(StringKeys.SettingsDescription);
    public string SettingsBehavior =>
        Get(StringKeys.SettingsBehavior);
    public string SettingsHaptic =>
        Get(StringKeys.SettingsHaptic);
    public string SettingsOverlay =>
        Get(StringKeys.SettingsOverlay);
    public string SettingsRadialMenu =>
        Get(StringKeys.SettingsRadialMenu);
    public string SettingsRadialMenuDescription =>
        Get(StringKeys.SettingsRadialMenuDescription);
    public string SettingsRadialMenuAlways =>
        Get(StringKeys.SettingsRadialMenuAlways);
    public string SettingsRadialMenuLearning =>
        Get(StringKeys.SettingsRadialMenuLearning);
    public string SettingsRadialMenuOff =>
        Get(StringKeys.SettingsRadialMenuOff);
    public string SettingsComposerDialMode =>
        Get(StringKeys.SettingsComposerDialMode);
    public string SettingsComposerDialModeDescription =>
        Get(StringKeys.SettingsComposerDialModeDescription);
    public string SettingsComposerDialModeSimple =>
        Get(StringKeys.SettingsComposerDialModeSimple);
    public string SettingsComposerDialModeAdvanced =>
        Get(StringKeys.SettingsComposerDialModeAdvanced);
    public string SettingsStick =>
        Get(StringKeys.SettingsStick);
    public string SettingsStickDescription =>
        Get(StringKeys.SettingsStickDescription);
    public string SettingsDeadZone =>
        Get(StringKeys.SettingsDeadZone);
    public string SettingsInitialRepeat =>
        Get(StringKeys.SettingsInitialRepeat);
    public string SettingsRepeatInterval =>
        Get(StringKeys.SettingsRepeatInterval);
    public string SettingsSystem =>
        Get(StringKeys.SettingsSystem);
    public string SettingsStartWithWindows =>
        Get(StringKeys.SettingsStartWithWindows);
    public string SettingsMinimizeToTray =>
        Get(StringKeys.SettingsMinimizeToTray);
    public string SettingsSave => Get(StringKeys.SettingsSave);
    public string SettingsLanguage =>
        Get(StringKeys.SettingsLanguage);
    public string SettingsLanguageAuto =>
        Get(StringKeys.SettingsLanguageAuto);
    public string SettingsLanguageZhCn =>
        Get(StringKeys.SettingsLanguageZhCn);
    public string SettingsLanguageEnUs =>
        Get(StringKeys.SettingsLanguageEnUs);

    public string DispatchSend =>
        Get(StringKeys.DispatchSend);
    public string DispatchSendDescription =>
        Get(StringKeys.DispatchSendDescription);
    public string DispatchSteer =>
        Get(StringKeys.DispatchSteer);
    public string DispatchSteerDescription =>
        Get(StringKeys.DispatchSteerDescription);
    public string DispatchQueue =>
        Get(StringKeys.DispatchQueue);
    public string DispatchQueueDescription =>
        Get(StringKeys.DispatchQueueDescription);
    public string DispatchDefault =>
        Get(StringKeys.DispatchDefault);
    public string DispatchDefaultDescription =>
        Get(StringKeys.DispatchDefaultDescription);

    public string StatusLocalBridge =>
        Get(StringKeys.StatusLocalBridge);
    public string StatusControllerArmed =>
        Get(StringKeys.StatusControllerArmed);
    public string StatusControllerLocked =>
        Get(StringKeys.StatusControllerLocked);
    public string StatusControllerPaused =>
        Get(StringKeys.StatusControllerPaused);
    public string StatusControllerResumed =>
        Get(StringKeys.StatusControllerResumed);

    public string WaitingForReconnect =>
        Get(StringKeys.StatusWaitingForReconnect);

    public string FeedbackStatusUpdated =>
        Get(StringKeys.FeedbackStatusUpdated);
    public string FeedbackOperationFailed =>
        Get(StringKeys.FeedbackOperationFailed);
    public string FeedbackNavigationUndone =>
        Get(StringKeys.FeedbackNavigationUndone);
    public string FeedbackSelectionCanceled =>
        Get(StringKeys.FeedbackSelectionCanceled);
    public string FeedbackPromptSent =>
        Get(StringKeys.FeedbackPromptSent);
    public string FeedbackListening =>
        Get(StringKeys.FeedbackListening);
    public string FeedbackDictationEnded =>
        Get(StringKeys.FeedbackDictationEnded);

    public string ErrorLabel(string? errorCode)
    {
        var key = errorCode switch
        {
            AgentAutomationErrorCodes.BridgeSafePreview =>
                StringKeys.ErrorBridgeSafePreview,
            AgentAutomationErrorCodes.AgentNotForeground =>
                StringKeys.ErrorAgentNotForeground,
            AgentAutomationErrorCodes.AgentWindowNotFound =>
                StringKeys.ErrorAgentWindowNotFound,
            AgentAutomationErrorCodes.ElementNotFound =>
                StringKeys.ErrorAutomationElementNotFound,
            AgentAutomationErrorCodes.FocusRejected =>
                StringKeys.ErrorAutomationFocusRejected,
            AgentAutomationErrorCodes.ComposerEmpty =>
                StringKeys.ErrorComposerEmpty,
            AgentAutomationErrorCodes.InputInjectionFailed =>
                StringKeys.ErrorInputInjectionFailed,
            AgentAutomationErrorCodes.ElementUnsupported =>
                StringKeys.ErrorAutomationElementUnsupported,
            AgentAutomationErrorCodes.OperationCanceled =>
                StringKeys.ErrorOperationCanceled,
            AgentAutomationErrorCodes.AutomationStale =>
                StringKeys.ErrorAutomationStale,
            AgentAutomationErrorCodes.NavigationUnavailable =>
                StringKeys.ErrorNavigationUnavailable,
            AgentAutomationErrorCodes.KeybindingsInvalid =>
                StringKeys.ErrorKeybindingsInvalid,
            AgentAutomationErrorCodes.KeybindingsPathUnavailable =>
                StringKeys.ErrorKeybindingsPathUnavailable,
            AgentAutomationErrorCodes.Unexpected =>
                StringKeys.ErrorAutomationUnexpected,
            AgentAutomationErrorCodes.CapabilityUnavailable =>
                StringKeys.ErrorCapabilityUnavailable,
            _ => null,
        };

        if (key is not null)
        {
            return Get(key);
        }

        return string.IsNullOrWhiteSpace(errorCode)
            ? FeedbackOperationFailed
            : Format(
                StringKeys.ErrorWithDetail,
                FeedbackOperationFailed,
                errorCode);
    }

    public string ErrorLabel(
        string? errorCode,
        string? errorDetail)
    {
        var label = ErrorLabel(errorCode);
        return string.IsNullOrWhiteSpace(errorDetail)
            ? label
            : Format(
                StringKeys.ErrorWithDetail,
                label,
                errorDetail);
    }

    public string TrayOpenApplication =>
        Get(StringKeys.TrayOpenApplication);

    public string TrayExit => Get(StringKeys.TrayExit);

    public string AppSubtitle(
        string controllerName,
        string agentName) =>
        Format(
            StringKeys.AppSubtitle,
            controllerName,
            agentName);

    public string DeviceConnected(string controllerName) =>
        Format(StringKeys.DeviceConnected, controllerName);

    public string ControlLeftStickHint(
        string pressGlyph,
        string primaryGlyph) =>
        Format(
            StringKeys.ControlLeftStickHint,
            pressGlyph,
            primaryGlyph);

    public string ControlRightStickHint(
        string pressGlyph,
        string cancelGlyph,
        string primaryGlyph,
        bool menuOpen = false,
        bool requiresConfirmation = false) =>
        Format(
            requiresConfirmation
                ? StringKeys.ControlRightStickHintConfirmation
                : menuOpen
                    ? StringKeys.ControlRightStickHintOpen
                    : StringKeys.ControlRightStickHint,
            pressGlyph,
            cancelGlyph,
            primaryGlyph);

    public string ControlPrimary(string glyph) =>
        Format(StringKeys.ControlPrimary, glyph);

    public string ControlHoldToTalk(string glyph) =>
        Format(StringKeys.ControlHoldToTalk, glyph);

    public string ControlSend(string glyph) =>
        Format(StringKeys.ControlSend, glyph);

    public string ControlCancelUndo(string glyph) =>
        Format(StringKeys.ControlCancelUndo, glyph);

    public string ControlProjectContext(string glyph) =>
        Format(StringKeys.ControlProjectContext, glyph);

    public string ControlWakeAgent(
        string glyph,
        string agentName) =>
        Format(StringKeys.ControlWakeAgent, glyph, agentName);

    public string ControlWakeAgentDescription(string agentName) =>
        Format(
            StringKeys.ControlWakeAgentDescription,
            agentName);

    public string ComposerAgentNotForeground(string agentName) =>
        Format(
            StringKeys.ComposerAgentNotForeground,
            agentName);

    public string SidebarAgent(string agentName) =>
        Format(StringKeys.SidebarAgent, agentName);

    public string SidebarProjectTaskCount(int count) =>
        Format(
            count == 1
                ? StringKeys.SidebarProjectTaskCountOne
                : StringKeys.SidebarProjectTaskCountMany,
            count);

    public string SidebarPinnedRelativeTime(string relativeTime) =>
        Format(
            StringKeys.SidebarPinnedRelativeTime,
            relativeTime);

    public string SidebarMinutesAgo(int minutes) =>
        minutes == 1
            ? Get(StringKeys.SidebarOneMinuteAgo)
            : Format(StringKeys.SidebarMinutesAgo, minutes);

    public string SidebarHoursAgo(int hours) =>
        hours == 1
            ? Get(StringKeys.SidebarOneHourAgo)
            : Format(StringKeys.SidebarHoursAgo, hours);

    public string SidebarDaysAgo(int days) =>
        days == 1
            ? Get(StringKeys.SidebarOneDayAgo)
            : Format(StringKeys.SidebarDaysAgo, days);

    public string ConfigDescription(string agentName) =>
        Format(StringKeys.ConfigDescription, agentName);

    public string ConfigLeftStickSidebar(string agentName) =>
        Format(StringKeys.ConfigLeftStickSidebar, agentName);

    public string ConfigMoveFocusDescription(string agentName) =>
        Format(
            StringKeys.ConfigMoveFocusDescription,
            agentName);

    public string ConfigRootProjectGlyphs(
        string rootGlyph,
        string projectGlyph) =>
        Format(
            StringKeys.ConfigRootProjectGlyphs,
            rootGlyph,
            projectGlyph);

    public string ConfigRootProjectDescription(
        string rootGlyph,
        string projectGlyph) =>
        Format(
            StringKeys.ConfigRootProjectDescription,
            rootGlyph,
            projectGlyph);

    public string ConfigSidebarBehavior(string agentName) =>
        Format(StringKeys.ConfigSidebarBehavior, agentName);

    public string ConfigRightStickComposer =>
        Get(StringKeys.ConfigRightStickComposer);

    public string ConfigModeSwitchGlyphs(
        string horizontalGlyph,
        string pressGlyph) =>
        Format(
            StringKeys.ConfigModeSwitchGlyphs,
            horizontalGlyph,
            pressGlyph);

    public string ConfigSelectionBehavior(string agentName) =>
        Format(StringKeys.ConfigSelectionBehavior, agentName);

    public string ConfigAgentShortcuts(string agentName) =>
        Format(StringKeys.ConfigAgentShortcuts, agentName);

    public string ConfigAgentShortcutsDescription(
        string agentName) =>
        Format(
            StringKeys.ConfigAgentShortcutsDescription,
            agentName);

    public string ConfigOpenAgentShortcuts(string agentName) =>
        Format(
            StringKeys.ConfigOpenAgentShortcuts,
            agentName);

    public string SettingsOnlyForeground(string agentName) =>
        Format(StringKeys.SettingsOnlyForeground, agentName);

    public string SettingsOnlyForegroundDescription(
        string wakeGlyph,
        string agentName) =>
        Format(
            StringKeys.SettingsOnlyForegroundDescription,
            wakeGlyph,
            agentName);

    public string SettingsOpenControllerSoftware(
        string softwareName) =>
        Format(
            StringKeys.SettingsOpenControllerSoftware,
            softwareName);

    public string SettingsOpenAgent(string agentName) =>
        Format(StringKeys.SettingsOpenAgent, agentName);

    public string StatusReady(string productName) =>
        Format(StringKeys.StatusReady, productName);

    public string StatusLoadingAgentData(string agentName) =>
        Format(StringKeys.StatusLoadingAgentData, agentName);

    public string StatusAgentDataLoadFailedFor(
        string agentName) =>
        Format(
            StringKeys.StatusAgentDataLoadFailed,
            agentName);

    public string AgentForegroundLocked(
        string agentName,
        string wakeGlyph) =>
        Format(
            StringKeys.StatusAgentForegroundLocked,
            agentName,
            wakeGlyph);

    public string AgentForegroundNeutral(string agentName) =>
        Format(
            StringKeys.StatusAgentForegroundNeutral,
            agentName);

    public string AgentForegroundArmed(string agentName) =>
        Format(
            StringKeys.StatusAgentForegroundArmed,
            agentName);

    public string BackgroundLocked(string wakeGlyph) =>
        Format(StringKeys.StatusBackgroundLocked, wakeGlyph);

    public string BackgroundArmed =>
        Get(StringKeys.StatusBackgroundArmed);

    public string AgentAwayPaused(string agentName) =>
        Format(StringKeys.StatusAgentAwayPaused, agentName);

    public string AgentNotForeground(
        string agentName,
        string wakeGlyph) =>
        Format(
            StringKeys.StatusAgentNotForeground,
            agentName,
            wakeGlyph);

    public string ControllerHelp(
        string wakeGlyph,
        string leftPressGlyph,
        string projectGlyph,
        string rightPressGlyph,
        string primaryGlyph,
        string voiceGlyph,
        string sendGlyph,
        string cancelGlyph) =>
        Format(
            StringKeys.StatusControllerHelp,
            wakeGlyph,
            leftPressGlyph,
            projectGlyph,
            rightPressGlyph,
            primaryGlyph,
            voiceGlyph,
            sendGlyph,
            cancelGlyph);

    public string TrayOpenAgent(string agentName) =>
        Format(StringKeys.TrayOpenAgent, agentName);

    public string FeedbackWakeStarted(string agentName) =>
        Format(StringKeys.FeedbackWakeStarted, agentName);

    public string FeedbackWakeSucceeded(string agentName) =>
        Format(StringKeys.FeedbackWakeSucceeded, agentName);

    public string FeedbackWakeFailed(string agentName) =>
        Format(StringKeys.FeedbackWakeFailed, agentName);

    public string FeedbackScopeChanged(string scope) =>
        Format(StringKeys.FeedbackScopeChanged, scope);

    public string FeedbackFocusChanged(string label) =>
        Format(StringKeys.FeedbackFocusChanged, label);

    public string FeedbackEntryOpened(string label) =>
        Format(StringKeys.FeedbackEntryOpened, label);

    public string FeedbackSelectionPreviewed(
        string category,
        string value) =>
        Format(
            StringKeys.FeedbackSelectionPreviewed,
            category,
            value);

    public string FeedbackSelectionApplied(
        string category,
        string value) =>
        Format(
            StringKeys.FeedbackSelectionApplied,
            category,
            value);

    public string ScopeValue(string value)
    {
        return NormalizeValue(value) switch
        {
            "pinnedtasks" or "pinned-tasks" =>
                Get(StringKeys.ValueScopePinnedTasks),
            "pinnedprojects" or "pinned-projects" =>
                Get(StringKeys.ValueScopePinnedProjects),
            "projects" =>
                Get(StringKeys.ValueScopeProjects),
            "projecttasks" or "project-tasks" =>
                Get(StringKeys.ValueScopeProjectTasks),
            "projectlesstasks" or "projectless-tasks" =>
                Get(StringKeys.ValueScopeProjectlessTasks),
            _ => value,
        };
    }

    public string ReasoningValue(string value)
    {
        return NormalizeValue(value) switch
        {
            "minimal" =>
                Get(StringKeys.ValueReasoningMinimal),
            "light" => Get(StringKeys.ValueReasoningLight),
            "low" => Get(StringKeys.ValueReasoningLow),
            "medium" =>
                Get(StringKeys.ValueReasoningMedium),
            "high" => Get(StringKeys.ValueReasoningHigh),
            "xhigh" or "extra-high" =>
                Get(StringKeys.ValueReasoningExtraHigh),
            "max" => Get(StringKeys.ValueReasoningMax),
            "ultra" => Get(StringKeys.ValueReasoningUltra),
            _ => value,
        };
    }

    public string SpeedValue(string value)
    {
        return NormalizeValue(value) switch
        {
            "standard" or "normal" =>
                Get(StringKeys.ValueSpeedStandard),
            "fast" => Get(StringKeys.ValueSpeedFast),
            _ => value,
        };
    }

    public string Get(string key) => _localization.Catalog.Get(key);

    public string Format(string key, params object?[] arguments) =>
        _localization.Catalog.Format(key, arguments);

    internal void Refresh()
    {
        // Empty means every property changed. This also refreshes indexer
        // bindings while keeping one stable LocalizedStrings instance.
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(string.Empty));
    }

    private static string NormalizeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Trim()
            .Replace("_", "-", StringComparison.Ordinal)
            .ToLowerInvariant();
    }
}
