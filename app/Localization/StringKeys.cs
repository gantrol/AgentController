namespace CodexController.Localization;

/// <summary>
/// Stable identifiers shared by every catalog. They intentionally describe
/// meaning rather than a particular WPF control so ViewModels can reuse them.
/// </summary>
public static class StringKeys
{
    public const string AppTitle = "app.title";
    public const string AppSubtitle = "app.subtitle";
    public const string NavDevice = "nav.device";
    public const string NavConfiguration = "nav.configuration";
    public const string NavSettings = "nav.settings";
    public const string OverlayNotificationName =
        "overlay.notification-name";

    public const string DeviceWaiting = "device.waiting";
    public const string DeviceConnected = "device.connected";
    public const string DeviceDisconnected = "device.disconnected";
    public const string DeviceEnableBridge = "device.enable-bridge";
    public const string DeviceGamepadBridge = "device.gamepad-bridge";
    public const string DeviceLiveInput = "device.live-input";
    public const string DeviceIdle = "device.idle";

    public const string ControlLeftStick = "control.left-stick";
    public const string ControlLeftStickHint = "control.left-stick-hint";
    public const string ControlRightStick = "control.right-stick";
    public const string ControlRightStickHint = "control.right-stick-hint";
    public const string ControlRightStickHintOpen =
        "control.right-stick-hint-open";
    public const string ControlPrimary = "control.primary";
    public const string ControlPrimaryDescription =
        "control.primary-description";
    public const string ControlHoldToTalk = "control.hold-to-talk";
    public const string ControlHoldToTalkDescription =
        "control.hold-to-talk-description";
    public const string ControlSend = "control.send";
    public const string ControlSendDescription =
        "control.send-description";
    public const string ControlCancelUndo = "control.cancel-undo";
    public const string ControlCancelUndoDescription =
        "control.cancel-undo-description";
    public const string ControlProjectContext =
        "control.project-context";
    public const string ControlProjectContextDescription =
        "control.project-context-description";
    public const string ControlWakeAgent = "control.wake-agent";
    public const string ControlWakeAgentDescription =
        "control.wake-agent-description";

    public const string ComposerRightStickAdjustment =
        "composer.right-stick-adjustment";
    public const string ComposerAgentNotForeground =
        "composer.agent-not-foreground";
    public const string ComposerDialReady =
        "composer.dial-ready";
    public const string ComposerConnectController =
        "composer.connect-controller";
    public const string ComposerDialSettingsOpened =
        "composer.dial-settings-opened";
    public const string ComposerDialCanceled =
        "composer.dial-canceled";
    public const string TermVirtualDial = "term.virtual-dial";
    public const string TermReasoningEffort = "term.reasoning-effort";
    public const string TermModel = "term.model";
    public const string TermSpeed = "term.speed";

    public const string SidebarAgent = "sidebar.agent";
    public const string SidebarCurrentProject =
        "sidebar.current-project";
    public const string SidebarRefresh = "sidebar.refresh";
    public const string SidebarPinnedTasks =
        "sidebar.pinned-tasks";
    public const string SidebarPinnedProjects =
        "sidebar.pinned-projects";
    public const string SidebarProjects = "sidebar.projects";
    public const string SidebarProjectlessTasks =
        "sidebar.projectless-tasks";
    public const string SidebarRecentEvents =
        "sidebar.recent-events";
    public const string SidebarPinnedBadge =
        "sidebar.entry.pinned-badge";
    public const string SidebarEnterAction =
        "sidebar.entry.action-enter";
    public const string SidebarOpenAction =
        "sidebar.entry.action-open";
    public const string SidebarProjectTaskCountOne =
        "sidebar.entry.project-task-count-one";
    public const string SidebarProjectTaskCountMany =
        "sidebar.entry.project-task-count-many";
    public const string SidebarPinnedRelativeTime =
        "sidebar.entry.pinned-relative-time";
    public const string SidebarUntitledTask =
        "sidebar.entry.untitled-task";
    public const string SidebarJustNow =
        "sidebar.entry.time-just-now";
    public const string SidebarOneMinuteAgo =
        "sidebar.entry.time-one-minute-ago";
    public const string SidebarMinutesAgo =
        "sidebar.entry.time-minutes-ago";
    public const string SidebarOneHourAgo =
        "sidebar.entry.time-one-hour-ago";
    public const string SidebarHoursAgo =
        "sidebar.entry.time-hours-ago";
    public const string SidebarOneDayAgo =
        "sidebar.entry.time-one-day-ago";
    public const string SidebarDaysAgo =
        "sidebar.entry.time-days-ago";

    public const string ConfigTitle = "config.title";
    public const string ConfigDescription = "config.description";
    public const string ConfigLeftStickSidebar =
        "config.left-stick-sidebar";
    public const string ConfigMoveFocus = "config.move-focus";
    public const string ConfigMoveFocusDescription =
        "config.move-focus-description";
    public const string ConfigEnterBack = "config.enter-back";
    public const string ConfigEnterBackDescription =
        "config.enter-back-description";
    public const string ConfigRootProjectGlyphs =
        "config.root-project-glyphs";
    public const string ConfigRootProjectDescription =
        "config.root-project-description";
    public const string ConfigSidebarBehavior =
        "config.sidebar-behavior";
    public const string ConfigRightStickComposer =
        "config.right-stick-composer";
    public const string ConfigIncreaseDecrease =
        "config.increase-decrease";
    public const string ConfigIncreaseDecreaseDescription =
        "config.increase-decrease-description";
    public const string ConfigModeSwitchGlyphs =
        "config.mode-switch-glyphs";
    public const string ConfigModeSwitchDescription =
        "config.mode-switch-description";
    public const string ConfigSelectionBehavior =
        "config.selection-behavior";
    public const string ConfigAgentShortcuts =
        "config.agent-shortcuts";
    public const string ConfigAgentShortcutsDescription =
        "config.agent-shortcuts-description";
    public const string ConfigOpenAgentShortcuts =
        "config.open-agent-shortcuts";
    public const string ConfigLowerReasoning =
        "config.lower-reasoning";
    public const string ConfigRaiseReasoning =
        "config.raise-reasoning";
    public const string ConfigToggleFast = "config.toggle-fast";
    public const string ConfigSubmitPrompt =
        "config.submit-prompt";
    public const string ConfigDictation = "config.dictation";
    public const string ConfigModelPicker = "config.model-picker";
    public const string ConfigRestoreRecommended =
        "config.restore-recommended";
    public const string ConfigSave = "config.save";

    public const string SettingsTitle = "settings.title";
    public const string SettingsDescription =
        "settings.description";
    public const string SettingsBehavior = "settings.behavior";
    public const string SettingsOnlyForeground =
        "settings.only-foreground";
    public const string SettingsOnlyForegroundDescription =
        "settings.only-foreground-description";
    public const string SettingsHaptic = "settings.haptic";
    public const string SettingsOverlay = "settings.overlay";
    public const string SettingsRadialMenu =
        "settings.radial-menu";
    public const string SettingsRadialMenuDescription =
        "settings.radial-menu-description";
    public const string SettingsRadialMenuAlways =
        "settings.radial-menu.always";
    public const string SettingsRadialMenuLearning =
        "settings.radial-menu.learning";
    public const string SettingsRadialMenuOff =
        "settings.radial-menu.off";
    public const string SettingsStick = "settings.stick";
    public const string SettingsStickDescription =
        "settings.stick-description";
    public const string SettingsDeadZone = "settings.dead-zone";
    public const string SettingsInitialRepeat =
        "settings.initial-repeat";
    public const string SettingsRepeatInterval =
        "settings.repeat-interval";
    public const string SettingsSystem = "settings.system";
    public const string SettingsStartWithWindows =
        "settings.start-with-windows";
    public const string SettingsMinimizeToTray =
        "settings.minimize-to-tray";
    public const string SettingsOpenControllerSoftware =
        "settings.open-controller-software";
    public const string SettingsOpenControllerSoftwareGeneric =
        "settings.open-controller-software-generic";
    public const string SettingsOpenAgent =
        "settings.open-agent";
    public const string SettingsSave = "settings.save";
    public const string SettingsLanguage = "settings.language";
    public const string SettingsLanguageAuto =
        "settings.language.auto";
    public const string SettingsLanguageZhCn =
        "settings.language.zh-cn";
    public const string SettingsLanguageEnUs =
        "settings.language.en-us";

    public const string DispatchSend =
        "dispatch.send";
    public const string DispatchSendDescription =
        "dispatch.send-description";
    public const string DispatchSteer =
        "dispatch.steer";
    public const string DispatchSteerDescription =
        "dispatch.steer-description";
    public const string DispatchQueue =
        "dispatch.queue";
    public const string DispatchQueueDescription =
        "dispatch.queue-description";
    public const string DispatchDefault =
        "dispatch.default";
    public const string DispatchDefaultDescription =
        "dispatch.default-description";

    public const string StatusReady = "status.ready";
    public const string StatusLoadingAgentData =
        "status.loading-agent-data";
    public const string StatusAgentDataLoadFailed =
        "status.agent-data-load-failed";
    public const string StatusLocalBridge = "status.local-bridge";
    public const string StatusControllerArmed =
        "status.controller-armed";
    public const string StatusControllerLocked =
        "status.controller-locked";
    public const string StatusControllerPaused =
        "status.controller-paused";
    public const string StatusControllerResumed =
        "status.controller-resumed";
    public const string StatusWaitingForReconnect =
        "status.waiting-for-reconnect";
    public const string StatusAgentForegroundLocked =
        "status.agent-foreground-locked";
    public const string StatusAgentForegroundNeutral =
        "status.agent-foreground-neutral";
    public const string StatusAgentForegroundArmed =
        "status.agent-foreground-armed";
    public const string StatusBackgroundArmed =
        "status.background-armed";
    public const string StatusBackgroundLocked =
        "status.background-locked";
    public const string StatusAgentAwayPaused =
        "status.agent-away-paused";
    public const string StatusAgentNotForeground =
        "status.agent-not-foreground";
    public const string StatusControllerHelp =
        "status.controller-help";

    public const string TrayOpenApplication =
        "tray.open-application";
    public const string TrayOpenAgent = "tray.open-agent";
    public const string TrayExit = "tray.exit";

    public const string FeedbackStatusUpdated =
        "feedback.status-updated";
    public const string FeedbackOperationFailed =
        "feedback.operation-failed";
    public const string FeedbackWakeStarted =
        "feedback.wake-started";
    public const string FeedbackWakeSucceeded =
        "feedback.wake-succeeded";
    public const string FeedbackWakeFailed =
        "feedback.wake-failed";
    public const string FeedbackScopeChanged =
        "feedback.scope-changed";
    public const string FeedbackFocusChanged =
        "feedback.focus-changed";
    public const string FeedbackEntryOpened =
        "feedback.entry-opened";
    public const string FeedbackNavigationUndone =
        "feedback.navigation-undone";
    public const string FeedbackSelectionPreviewed =
        "feedback.selection-previewed";
    public const string FeedbackSelectionApplied =
        "feedback.selection-applied";
    public const string FeedbackSelectionCanceled =
        "feedback.selection-canceled";
    public const string FeedbackPromptSent =
        "feedback.prompt-sent";
    public const string FeedbackListening =
        "feedback.listening";
    public const string FeedbackDictationEnded =
        "feedback.dictation-ended";

    public const string ErrorBridgeSafePreview =
        "error.bridge-safe-preview";
    public const string ErrorAgentNotForeground =
        "error.agent-not-foreground";
    public const string ErrorAgentWindowNotFound =
        "error.agent-window-not-found";
    public const string ErrorAutomationElementNotFound =
        "error.automation-element-not-found";
    public const string ErrorAutomationFocusRejected =
        "error.automation-focus-rejected";
    public const string ErrorComposerEmpty =
        "error.composer-empty";
    public const string ErrorInputInjectionFailed =
        "error.input-injection-failed";
    public const string ErrorAutomationElementUnsupported =
        "error.automation-element-unsupported";
    public const string ErrorOperationCanceled =
        "error.operation-canceled";
    public const string ErrorAutomationStale =
        "error.automation-stale";
    public const string ErrorNavigationUnavailable =
        "error.navigation-unavailable";
    public const string ErrorKeybindingsInvalid =
        "error.keybindings-invalid";
    public const string ErrorKeybindingsPathUnavailable =
        "error.keybindings-path-unavailable";
    public const string ErrorAutomationUnexpected =
        "error.automation-unexpected";
    public const string ErrorCapabilityUnavailable =
        "error.capability-unavailable";
    public const string ErrorWithDetail =
        "error.with-detail";

    public const string MessageShortcutSettingsSaved =
        "message.shortcut-settings-saved";
    public const string MessageSettingsSaved =
        "message.settings-saved";
    public const string MessageAgentKeybindingsWriteFailed =
        "message.agent-keybindings-write-failed";
    public const string MessageAgentKeybindingsConflict =
        "message.agent-keybindings-conflict";
    public const string MessageFallbackKeybindingsWritten =
        "message.fallback-keybindings-written";
    public const string MessageAgentSidebar =
        "message.agent-sidebar";
    public const string MessageAlreadyAtRootScope =
        "message.already-at-root-scope";
    public const string MessageFocusedEntryHasNoChildDirectory =
        "message.focused-entry-has-no-child-directory";
    public const string MessageUseRightToEnterProject =
        "message.use-right-to-enter-project";
    public const string MessageNoAvailableEntries =
        "message.no-available-entries";
    public const string MessageProjectTasks =
        "message.project-tasks";
    public const string MessageTaskHasNoProject =
        "message.task-has-no-project";
    public const string MessageProjectUnavailable =
        "message.project-unavailable";
    public const string MessageNoAvailableTasks =
        "message.no-available-tasks";
    public const string MessageProjectTasksPosition =
        "message.project-tasks-position";
    public const string MessageProjectTitle =
        "message.project-title";
    public const string MessageButtonProjectTasks =
        "message.button-project-tasks";
    public const string MessageNoLocatableEntry =
        "message.no-locatable-entry";
    public const string MessageProjectHasNoPinnedTasks =
        "message.project-has-no-pinned-tasks";
    public const string MessageProjectPinnedOnly =
        "message.project-pinned-only";
    public const string MessageAllTasks =
        "message.all-tasks";
    public const string MessageProjectTaskFilter =
        "message.project-task-filter";
    public const string MessageScopeHasNoEntries =
        "message.scope-has-no-entries";
    public const string MessageSidebarFocusFailed =
        "message.sidebar-focus-failed";
    public const string MessageDisclosureRestoreFailed =
        "message.disclosure-restore-failed";
    public const string MessageTaskUnavailableSkipped =
        "message.task-unavailable-skipped";
    public const string MessageOpeningThread =
        "message.opening-thread";
    public const string MessageOpeningTask =
        "message.opening-task";
    public const string MessageOpenThreadFailed =
        "message.open-thread-failed";
    public const string MessageUndoUnavailableUnique =
        "message.undo-unavailable-unique";
    public const string MessageOpenedUndoAvailable =
        "message.opened-undo-available";
    public const string MessageOpenedTask =
        "message.opened-task";
    public const string MessageUndoWithinSeconds =
        "message.undo-within-seconds";
    public const string MessageUndoUnavailableUnconfirmed =
        "message.undo-unavailable-unconfirmed";
    public const string MessageUndoUnconfirmed =
        "message.undo-unconfirmed";
    public const string MessageRightStickGesture =
        "message.right-stick-gesture";
    public const string MessageRightStickMode =
        "message.right-stick-mode";
    public const string MessagePreviewValue =
        "message.preview-value";
    public const string MessageSettleToConfirm =
        "message.settle-to-confirm";
    public const string MessageApplyingValue =
        "message.applying-value";
    public const string MessageShortcutSentValue =
        "message.shortcut-sent-value";
    public const string MessageExactSelection =
        "message.exact-selection";
    public const string MessageShortcutSentRestart =
        "message.shortcut-sent-restart";
    public const string MessageShortcutSent =
        "message.shortcut-sent";
    public const string MessageNotExecutedValue =
        "message.not-executed-value";
    public const string MessageComposerSelectionApplied =
        "message.composer-selection-applied";
    public const string MessageComposerSelectionFailed =
        "message.composer-selection-failed";
    public const string MessageButtonUndo =
        "message.button-undo";
    public const string MessageButtonCancel =
        "message.button-cancel";
    public const string MessageStartDictation =
        "message.start-dictation";
    public const string MessageRecordingReleaseToStop =
        "message.recording-release-to-stop";
    public const string MessageReleaseNoRecording =
        "message.release-no-recording";
    public const string MessageNoActiveRecording =
        "message.no-active-recording";
    public const string MessageReleaseEndingDictation =
        "message.release-ending-dictation";
    public const string MessageReleaseEndingRecording =
        "message.release-ending-recording";
    public const string MessageReleaseEndDictation =
        "message.release-end-dictation";
    public const string MessageRecordingEnded =
        "message.recording-ended";
    public const string MessageSendPrompt =
        "message.send-prompt";
    public const string MessageSent =
        "message.sent";
    public const string MessageAbortDictation =
        "message.abort-dictation";
    public const string MessageDictationStopped =
        "message.dictation-stopped";
    public const string MessagePendingSelectionUndone =
        "message.pending-selection-undone";
    public const string MessageCurrentOperationStopped =
        "message.current-operation-stopped";
    public const string MessageCurrentOperationStoppedDetail =
        "message.current-operation-stopped-detail";
    public const string MessageUndoQueued =
        "message.undo-queued";
    public const string MessageUndoAfterOpen =
        "message.undo-after-open";
    public const string MessageCancel =
        "message.cancel";
    public const string MessageCanceled =
        "message.canceled";
    public const string MessageUndoPageChanged =
        "message.undo-page-changed";
    public const string MessageUndoPageChangedDetail =
        "message.undo-page-changed-detail";
    public const string MessageUndoSucceeded =
        "message.undo-succeeded";
    public const string MessageReturnedToPreviousTask =
        "message.returned-to-previous-task";
    public const string MessageUndoFailed =
        "message.undo-failed";
    public const string MessageDataLoaded =
        "message.data-loaded";
    public const string MessageDataLoadFailed =
        "message.data-load-failed";
    public const string MessageExecuted =
        "message.executed";
    public const string MessageSafePreview =
        "message.safe-preview";
    public const string MessageWaitingForAgentForeground =
        "message.waiting-for-agent-foreground";
    public const string MessageNotExecuted =
        "message.not-executed";
    public const string MessageBridgeEnabled =
        "message.bridge-enabled";
    public const string MessageBridgeSafePreview =
        "message.bridge-safe-preview";
    public const string MessageAgentDataRefreshed =
        "message.agent-data-refreshed";
    public const string MessageAgentShortcutsOpened =
        "message.agent-shortcuts-opened";
    public const string MessageControllerSoftwareOpenFailed =
        "message.controller-software-open-failed";
    public const string MessageWindowHiddenBackground =
        "message.window-hidden-background";

    public const string ValueScopePinnedTasks =
        "value.scope.pinned-tasks";
    public const string ValueScopePinnedProjects =
        "value.scope.pinned-projects";
    public const string ValueScopeProjects =
        "value.scope.projects";
    public const string ValueScopeProjectTasks =
        "value.scope.project-tasks";
    public const string ValueScopeProjectlessTasks =
        "value.scope.projectless-tasks";
    public const string ValueReasoningMinimal =
        "value.reasoning.minimal";
    public const string ValueReasoningLight =
        "value.reasoning.light";
    public const string ValueReasoningLow = "value.reasoning.low";
    public const string ValueReasoningMedium =
        "value.reasoning.medium";
    public const string ValueReasoningHigh = "value.reasoning.high";
    public const string ValueReasoningExtraHigh =
        "value.reasoning.extra-high";
    public const string ValueReasoningMax =
        "value.reasoning.max";
    public const string ValueReasoningUltra =
        "value.reasoning.ultra";
    public const string ValueSpeedStandard = "value.speed.standard";
    public const string ValueSpeedFast = "value.speed.fast";

    public static IReadOnlyList<string> All { get; } =
    [
        AppTitle,
        AppSubtitle,
        NavDevice,
        NavConfiguration,
        NavSettings,
        OverlayNotificationName,
        DeviceWaiting,
        DeviceConnected,
        DeviceDisconnected,
        DeviceEnableBridge,
        DeviceGamepadBridge,
        DeviceLiveInput,
        DeviceIdle,
        ControlLeftStick,
        ControlLeftStickHint,
        ControlRightStick,
        ControlRightStickHint,
        ControlRightStickHintOpen,
        ControlPrimary,
        ControlPrimaryDescription,
        ControlHoldToTalk,
        ControlHoldToTalkDescription,
        ControlSend,
        ControlSendDescription,
        ControlCancelUndo,
        ControlCancelUndoDescription,
        ControlProjectContext,
        ControlProjectContextDescription,
        ControlWakeAgent,
        ControlWakeAgentDescription,
        ComposerRightStickAdjustment,
        ComposerAgentNotForeground,
        ComposerDialReady,
        ComposerConnectController,
        ComposerDialSettingsOpened,
        ComposerDialCanceled,
        TermVirtualDial,
        TermReasoningEffort,
        TermModel,
        TermSpeed,
        SidebarAgent,
        SidebarCurrentProject,
        SidebarRefresh,
        SidebarPinnedTasks,
        SidebarPinnedProjects,
        SidebarProjects,
        SidebarProjectlessTasks,
        SidebarRecentEvents,
        SidebarPinnedBadge,
        SidebarEnterAction,
        SidebarOpenAction,
        SidebarProjectTaskCountOne,
        SidebarProjectTaskCountMany,
        SidebarPinnedRelativeTime,
        SidebarUntitledTask,
        SidebarJustNow,
        SidebarOneMinuteAgo,
        SidebarMinutesAgo,
        SidebarOneHourAgo,
        SidebarHoursAgo,
        SidebarOneDayAgo,
        SidebarDaysAgo,
        ConfigTitle,
        ConfigDescription,
        ConfigLeftStickSidebar,
        ConfigMoveFocus,
        ConfigMoveFocusDescription,
        ConfigEnterBack,
        ConfigEnterBackDescription,
        ConfigRootProjectGlyphs,
        ConfigRootProjectDescription,
        ConfigSidebarBehavior,
        ConfigRightStickComposer,
        ConfigIncreaseDecrease,
        ConfigIncreaseDecreaseDescription,
        ConfigModeSwitchGlyphs,
        ConfigModeSwitchDescription,
        ConfigSelectionBehavior,
        ConfigAgentShortcuts,
        ConfigAgentShortcutsDescription,
        ConfigOpenAgentShortcuts,
        ConfigLowerReasoning,
        ConfigRaiseReasoning,
        ConfigToggleFast,
        ConfigSubmitPrompt,
        ConfigDictation,
        ConfigModelPicker,
        ConfigRestoreRecommended,
        ConfigSave,
        SettingsTitle,
        SettingsDescription,
        SettingsBehavior,
        SettingsOnlyForeground,
        SettingsOnlyForegroundDescription,
        SettingsHaptic,
        SettingsOverlay,
        SettingsRadialMenu,
        SettingsRadialMenuDescription,
        SettingsRadialMenuAlways,
        SettingsRadialMenuLearning,
        SettingsRadialMenuOff,
        SettingsStick,
        SettingsStickDescription,
        SettingsDeadZone,
        SettingsInitialRepeat,
        SettingsRepeatInterval,
        SettingsSystem,
        SettingsStartWithWindows,
        SettingsMinimizeToTray,
        SettingsOpenControllerSoftware,
        SettingsOpenControllerSoftwareGeneric,
        SettingsOpenAgent,
        SettingsSave,
        SettingsLanguage,
        SettingsLanguageAuto,
        SettingsLanguageZhCn,
        SettingsLanguageEnUs,
        DispatchSend,
        DispatchSendDescription,
        DispatchSteer,
        DispatchSteerDescription,
        DispatchQueue,
        DispatchQueueDescription,
        DispatchDefault,
        DispatchDefaultDescription,
        StatusReady,
        StatusLoadingAgentData,
        StatusAgentDataLoadFailed,
        StatusLocalBridge,
        StatusControllerArmed,
        StatusControllerLocked,
        StatusControllerPaused,
        StatusControllerResumed,
        StatusWaitingForReconnect,
        StatusAgentForegroundLocked,
        StatusAgentForegroundNeutral,
        StatusAgentForegroundArmed,
        StatusBackgroundArmed,
        StatusBackgroundLocked,
        StatusAgentAwayPaused,
        StatusAgentNotForeground,
        StatusControllerHelp,
        TrayOpenApplication,
        TrayOpenAgent,
        TrayExit,
        FeedbackStatusUpdated,
        FeedbackOperationFailed,
        FeedbackWakeStarted,
        FeedbackWakeSucceeded,
        FeedbackWakeFailed,
        FeedbackScopeChanged,
        FeedbackFocusChanged,
        FeedbackEntryOpened,
        FeedbackNavigationUndone,
        FeedbackSelectionPreviewed,
        FeedbackSelectionApplied,
        FeedbackSelectionCanceled,
        FeedbackPromptSent,
        FeedbackListening,
        FeedbackDictationEnded,
        ErrorBridgeSafePreview,
        ErrorAgentNotForeground,
        ErrorAgentWindowNotFound,
        ErrorAutomationElementNotFound,
        ErrorAutomationFocusRejected,
        ErrorComposerEmpty,
        ErrorInputInjectionFailed,
        ErrorAutomationElementUnsupported,
        ErrorOperationCanceled,
        ErrorAutomationStale,
        ErrorNavigationUnavailable,
        ErrorKeybindingsInvalid,
        ErrorKeybindingsPathUnavailable,
        ErrorAutomationUnexpected,
        ErrorCapabilityUnavailable,
        ErrorWithDetail,
        MessageShortcutSettingsSaved,
        MessageSettingsSaved,
        MessageAgentKeybindingsWriteFailed,
        MessageAgentKeybindingsConflict,
        MessageFallbackKeybindingsWritten,
        MessageAgentSidebar,
        MessageAlreadyAtRootScope,
        MessageFocusedEntryHasNoChildDirectory,
        MessageUseRightToEnterProject,
        MessageNoAvailableEntries,
        MessageProjectTasks,
        MessageTaskHasNoProject,
        MessageProjectUnavailable,
        MessageNoAvailableTasks,
        MessageProjectTasksPosition,
        MessageProjectTitle,
        MessageButtonProjectTasks,
        MessageNoLocatableEntry,
        MessageProjectHasNoPinnedTasks,
        MessageProjectPinnedOnly,
        MessageAllTasks,
        MessageProjectTaskFilter,
        MessageScopeHasNoEntries,
        MessageSidebarFocusFailed,
        MessageDisclosureRestoreFailed,
        MessageTaskUnavailableSkipped,
        MessageOpeningThread,
        MessageOpeningTask,
        MessageOpenThreadFailed,
        MessageUndoUnavailableUnique,
        MessageOpenedUndoAvailable,
        MessageOpenedTask,
        MessageUndoWithinSeconds,
        MessageUndoUnavailableUnconfirmed,
        MessageUndoUnconfirmed,
        MessageRightStickGesture,
        MessageRightStickMode,
        MessagePreviewValue,
        MessageSettleToConfirm,
        MessageApplyingValue,
        MessageShortcutSentValue,
        MessageExactSelection,
        MessageShortcutSentRestart,
        MessageShortcutSent,
        MessageNotExecutedValue,
        MessageComposerSelectionApplied,
        MessageComposerSelectionFailed,
        MessageButtonUndo,
        MessageButtonCancel,
        MessageStartDictation,
        MessageRecordingReleaseToStop,
        MessageReleaseNoRecording,
        MessageNoActiveRecording,
        MessageReleaseEndingDictation,
        MessageReleaseEndingRecording,
        MessageReleaseEndDictation,
        MessageRecordingEnded,
        MessageSendPrompt,
        MessageSent,
        MessageAbortDictation,
        MessageDictationStopped,
        MessagePendingSelectionUndone,
        MessageCurrentOperationStopped,
        MessageCurrentOperationStoppedDetail,
        MessageUndoQueued,
        MessageUndoAfterOpen,
        MessageCancel,
        MessageCanceled,
        MessageUndoPageChanged,
        MessageUndoPageChangedDetail,
        MessageUndoSucceeded,
        MessageReturnedToPreviousTask,
        MessageUndoFailed,
        MessageDataLoaded,
        MessageDataLoadFailed,
        MessageExecuted,
        MessageSafePreview,
        MessageWaitingForAgentForeground,
        MessageNotExecuted,
        MessageBridgeEnabled,
        MessageBridgeSafePreview,
        MessageAgentDataRefreshed,
        MessageAgentShortcutsOpened,
        MessageControllerSoftwareOpenFailed,
        MessageWindowHiddenBackground,
        ValueScopePinnedTasks,
        ValueScopePinnedProjects,
        ValueScopeProjects,
        ValueScopeProjectTasks,
        ValueScopeProjectlessTasks,
        ValueReasoningMinimal,
        ValueReasoningLight,
        ValueReasoningLow,
        ValueReasoningMedium,
        ValueReasoningHigh,
        ValueReasoningExtraHigh,
        ValueReasoningMax,
        ValueReasoningUltra,
        ValueSpeedStandard,
        ValueSpeedFast,
    ];
}
