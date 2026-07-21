namespace CodexController.Localization;

public sealed class EnCatalog : DictionaryStringCatalog
{
    public EnCatalog()
        : base(
            AppLanguage.EnUs,
            AppLanguageParser.EnUsValue,
            Values)
    {
    }

    private static IReadOnlyDictionary<string, string> Values { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StringKeys.AppTitle] = "Agent Controller",
            [StringKeys.AppSubtitle] = "{0} → {1}",
            [StringKeys.NavDevice] = "Device",
            [StringKeys.NavConfiguration] = "Configuration",
            [StringKeys.NavSettings] = "Settings",
            [StringKeys.OverlayNotificationName] =
                "Agent Controller notification",

            [StringKeys.DeviceWaiting] = "Waiting…",
            [StringKeys.DeviceConnected] = "Connected · {0}",
            [StringKeys.DeviceDisconnected] =
                "Controller disconnected",
            [StringKeys.DeviceEnableBridge] =
                "Controller input (this session)",
            [StringKeys.DeviceGamepadBridge] = "Gamepad bridge",
            [StringKeys.DeviceLiveInput] = "Live input",
            [StringKeys.DeviceIdle] = "Idle",

            [StringKeys.ControlLeftStick] = "Left stick",
            [StringKeys.ControlLeftStickHint] =
                "↑↓ Move focus · → Enter project · ← Exit project · {1} Open task · press {0} (L3) to change root",
            [StringKeys.ControlRightStick] = "Right stick",
            [StringKeys.ControlRightStickHint] =
                "Up/left: previous · down/right: next · tap {0} (R3) to enter or confirm · hold {0} for Agent Controller settings",
            [StringKeys.ControlRightStickHintOpen] =
                "Micro menu active · ↑/← previous · ↓/→ next · {2} Enter or confirm · {1} Back",
            [StringKeys.ControlRightStickHintConfirmation] =
                "Model selection confirmation · {2} Confirm · {1} Cancel",
            [StringKeys.ControlPrimary] = "{0} · Open task",
            [StringKeys.ControlPrimaryDescription] =
                "Open the focused task; use → to enter a project",
            [StringKeys.ControlHoldToTalk] = "{0} · Hold to talk",
            [StringKeys.ControlHoldToTalkDescription] =
                "Release to finish dictation",
            [StringKeys.ControlSend] = "{0} · Send",
            [StringKeys.ControlSendDescription] =
                "Submit the current prompt",
            [StringKeys.ControlCancelUndo] =
                "{0} · Close / undo · hold 3s to cancel turn",
            [StringKeys.ControlCancelUndoDescription] =
                "Short press closes menus or undoes local navigation; hold for 3 seconds to cancel the active turn",
            [StringKeys.ControlProjectContext] =
                "{0} · Action panel",
            [StringKeys.ControlProjectContextDescription] =
                "New task, sidebar, history, and clear input",
            [StringKeys.ControlWakeAgent] = "{0} · Wake {1}",
            [StringKeys.ControlWakeAgentDescription] =
                "Bring {0} to front; foreground control enables automatically",

            [StringKeys.ComposerRightStickAdjustment] =
                "Micro dial",
            [StringKeys.ComposerAgentNotForeground] =
                "{0} in background",
            [StringKeys.ComposerDialReady] =
                "Micro control · ↑/← previous · ↓/→ next · R3 enter",
            [StringKeys.ComposerConnectController] =
                "Connect controller",
            [StringKeys.ComposerDialSettingsOpened] =
                "Controller settings opened",
            [StringKeys.ComposerDialCanceled] =
                "Current picker closed",
            [StringKeys.TermVirtualDial] = "Micro dial",
            [StringKeys.TermReasoningEffort] = "Reasoning effort",
            [StringKeys.TermModel] = "Model",
            [StringKeys.TermSpeed] = "Speed",

            [StringKeys.SidebarAgent] = "{0} sidebar",
            [StringKeys.SidebarCurrentProject] = "Current project",
            [StringKeys.SidebarRefresh] = "Refresh",
            [StringKeys.SidebarPinnedTasks] = "Pinned tasks",
            [StringKeys.SidebarPinnedProjects] = "Pinned projects",
            [StringKeys.SidebarProjects] = "Projects",
            [StringKeys.SidebarProjectlessTasks] =
                "Loose tasks",
            [StringKeys.SidebarRecentEvents] = "Recent events",
            [StringKeys.SidebarPinnedBadge] = "Pinned",
            [StringKeys.SidebarEnterAction] = "→",
            [StringKeys.SidebarOpenAction] = "A",
            [StringKeys.SidebarProjectTaskCountOne] =
                "{0} task",
            [StringKeys.SidebarProjectTaskCountMany] =
                "{0} tasks",
            [StringKeys.SidebarPinnedRelativeTime] =
                "Pinned · {0}",
            [StringKeys.SidebarUntitledTask] = "Untitled task",
            [StringKeys.SidebarJustNow] = "Just now",
            [StringKeys.SidebarOneMinuteAgo] = "1 minute ago",
            [StringKeys.SidebarMinutesAgo] =
                "{0} minutes ago",
            [StringKeys.SidebarOneHourAgo] = "1 hour ago",
            [StringKeys.SidebarHoursAgo] = "{0} hours ago",
            [StringKeys.SidebarOneDayAgo] = "1 day ago",
            [StringKeys.SidebarDaysAgo] = "{0} days ago",

            [StringKeys.ConfigTitle] = "Controls",
            [StringKeys.ConfigDescription] =
                "Stick directions stay fixed; {0} commands can adapt to versions and personal preferences. Changes save automatically.",
            [StringKeys.ConfigLeftStickSidebar] =
                "Left stick · {0}",
            [StringKeys.ConfigMoveFocus] = "↑↓",
            [StringKeys.ConfigMoveFocusDescription] =
                "Select an item at the current level and sync focus with the native {0} sidebar",
            [StringKeys.ConfigEnterBack] = "→ / ← / A",
            [StringKeys.ConfigEnterBackDescription] =
                "→ enters the focused project; ← exits to its parent; A opens a focused task",
            [StringKeys.ConfigRootProjectGlyphs] = "{0} / {1}",
            [StringKeys.ConfigRootProjectDescription] =
                "{0} cycles four root scopes; {1} opens the action panel",
            [StringKeys.ConfigSidebarBehavior] =
                "↑↓ follows this app's stable wheel and syncs {0} sidebar focus without opening a conversation. Activity timestamps never reorder the wheel; → enters a project, ← exits it, and A opens tasks only. Pinning and project expansion remain independent.",
            [StringKeys.ConfigRightStickComposer] =
                "Right stick · Micro dial",
            [StringKeys.ConfigIncreaseDecrease] = "↔ / ↕",
            [StringKeys.ConfigIncreaseDecreaseDescription] =
                "↑↓ uses Micro encoder detents to traverse Advanced, Fast, Power, and other controls; ←→ keeps its screen direction and adjusts the current control.",
            [StringKeys.ConfigModeSwitchGlyphs] = "{0} / {1}",
            [StringKeys.ConfigModeSwitchDescription] =
                "Tap presses the Micro encoder; hold for 500 ms to open Agent Controller settings",
            [StringKeys.ConfigSelectionBehavior] =
                "Codex composer-navigation owns control traversal; the gamepad keeps two-dimensional intent and never merges left/right with up/down.",
            [StringKeys.ConfigAgentShortcuts] = "{0} keys",
            [StringKeys.ConfigAgentShortcutsDescription] =
                "The app safely appends fallback bindings; new bindings take effect after {0} restarts.",
            [StringKeys.ConfigOpenAgentShortcuts] =
                "Open {0} shortcuts",
            [StringKeys.ConfigLowerReasoning] =
                "Reasoning −",
            [StringKeys.ConfigRaiseReasoning] =
                "Reasoning +",
            [StringKeys.ConfigToggleFast] = "Fast mode",
            [StringKeys.ConfigSubmitPrompt] = "Submit",
            [StringKeys.ConfigDictation] = "Dictation",
            [StringKeys.ConfigModelPicker] = "Model",
            [StringKeys.ConfigRestoreDefaults] =
                "Restore defaults",

            [StringKeys.SettingsTitle] = "Settings",
            [StringKeys.SettingsDescription] =
                "Control bridge scope, stick feel, and background behavior. Changes save automatically.",
            [StringKeys.SettingsBehavior] = "Behavior",
            [StringKeys.SettingsOnlyForeground] =
                "Foreground only",
            [StringKeys.SettingsOnlyForegroundDescription] =
                "Recommended. Controller input enables automatically while {1} is foreground and the controls are neutral. Press {0} only to bring {1} forward. Leaving the foreground pauses input; returning or reconnecting resumes after neutral.",
            [StringKeys.SettingsHaptic] =
                "Haptics",
            [StringKeys.SettingsOverlay] =
                "Status overlay",
            [StringKeys.SettingsRadialMenu] =
                "Wheel hints",
            [StringKeys.SettingsRadialMenuDescription] =
                "Always appears immediately; Learning appears after a short hold; Off hides the wheel.",
            [StringKeys.SettingsRadialMenuAlways] =
                "Always show",
            [StringKeys.SettingsRadialMenuLearning] =
                "Learning mode",
            [StringKeys.SettingsRadialMenuOff] =
                "Off",
            [StringKeys.SettingsComposerDialMode] =
                "Dial mode",
            [StringKeys.SettingsComposerDialModeDescription] =
                "Simple drives Codex's live Power and Speed controls. Advanced exposes the Model, Reasoning effort, and Speed options currently available to this account.",
            [StringKeys.SettingsComposerDialModeSimple] =
                "Simple",
            [StringKeys.SettingsComposerDialModeAdvanced] =
                "Advanced",
            [StringKeys.SettingsStick] = "Sticks",
            [StringKeys.SettingsStickDescription] =
                "Primary-direction locking prevents diagonal movement from triggering horizontal and vertical actions together.",
            [StringKeys.SettingsDeadZone] = "Dead zone",
            [StringKeys.SettingsInitialRepeat] =
                "Repeat delay",
            [StringKeys.SettingsRepeatInterval] = "Repeat interval",
            [StringKeys.SettingsSystem] = "System",
            [StringKeys.SettingsTextSize] = "Text size",
            [StringKeys.SettingsTextSizeSmall] = "Small",
            [StringKeys.SettingsTextSizeMedium] = "Medium",
            [StringKeys.SettingsTextSizeLarge] = "Large",
            [StringKeys.SettingsTextSizeExtraLarge] = "Extra large",
            [StringKeys.SettingsStartWithWindows] =
                "Start with Windows",
            [StringKeys.SettingsMinimizeToTray] =
                "Keep running in tray",
            [StringKeys.SettingsOpenControllerSoftware] =
                "Open {0}",
            [StringKeys.SettingsOpenControllerSoftwareGeneric] =
                "Open controller software",
            [StringKeys.SettingsOpenAgent] = "Open {0} settings",
            [StringKeys.SettingsRestoreDefaults] =
                "Restore defaults",
            [StringKeys.SettingsLanguage] = "Language",
            [StringKeys.SettingsLanguageAuto] = "Follow system",
            [StringKeys.SettingsLanguageZhCn] = "简体中文",
            [StringKeys.SettingsLanguageEnUs] = "English (US)",

            [StringKeys.DispatchSend] = "Send",
            [StringKeys.DispatchSendDescription] =
                "Start a new turn with the current composer text.",
            [StringKeys.DispatchSteer] = "Steer current turn",
            [StringKeys.DispatchSteerDescription] =
                "Add the current composer text to the turn that is still running.",
            [StringKeys.DispatchQueue] = "Queue next turn",
            [StringKeys.DispatchQueueDescription] =
                "Save the current composer text for the next turn after the current one finishes.",
            [StringKeys.DispatchDefault] = "Default dispatch",
            [StringKeys.DispatchDefaultDescription] =
                "Follow Codex's current dispatch behavior. Agent Controller has not verified enough active-turn and Follow-up state to name the outcome.",

            [StringKeys.StatusReady] = "{0} is ready",
            [StringKeys.StatusLoadingAgentData] =
                "Loading {0} data…",
            [StringKeys.StatusAgentDataLoadFailed] =
                "Could not read local {0} tasks",
            [StringKeys.StatusLocalBridge] = "Local bridge",
            [StringKeys.StatusControllerArmed] =
                "Controller input enabled",
            [StringKeys.StatusControllerLocked] =
                "Controller input locked",
            [StringKeys.StatusControllerPaused] =
                "Controller input paused",
            [StringKeys.StatusControllerResumed] =
                "Controller input resumed",
            [StringKeys.StatusWaitingForReconnect] =
                "Reconnecting…",
            [StringKeys.StatusAgentForegroundLocked] =
                "{0} active · enabling input",
            [StringKeys.StatusAgentForegroundNeutral] =
                "{0} active · release controls",
            [StringKeys.StatusAgentForegroundArmed] =
                "{0} active · unlocked",
            [StringKeys.StatusBackgroundArmed] =
                "Background control · unlocked",
            [StringKeys.StatusBackgroundLocked] =
                "Background control · press {0} to unlock",
            [StringKeys.StatusAgentAwayPaused] =
                "{0} in background · paused",
            [StringKeys.StatusAgentNotForeground] =
                "{0} in background · {1} to wake",
            [StringKeys.StatusControllerHelp] =
                "{0} wakes the agent when needed · foreground control enables automatically · left stick ↑↓ focus, → enters project, ← exits project, {4} opens task, {1} changes root · right stick ←→ turns, {3} opens / selects · hold {5} to talk · {6} sends · {7} closes / undoes; hold 3s to cancel turn",
            [StringKeys.TrayOpenApplication] =
                "Open Agent Controller",
            [StringKeys.TrayOpenMicroSurface] =
                "Open Micro Surface",
            [StringKeys.TrayOpenAgent] = "Open {0}",
            [StringKeys.TrayExit] = "Exit",

            [StringKeys.FeedbackStatusUpdated] = "Status updated",
            [StringKeys.FeedbackOperationFailed] =
                "Operation failed",
            [StringKeys.FeedbackWakeStarted] =
                "Bringing {0} to the foreground",
            [StringKeys.FeedbackWakeSucceeded] =
                "{0} is now in the foreground",
            [StringKeys.FeedbackWakeFailed] =
                "Could not bring {0} to the foreground",
            [StringKeys.FeedbackScopeChanged] =
                "Sidebar scope: {0}",
            [StringKeys.FeedbackFocusChanged] =
                "Sidebar focus: {0}",
            [StringKeys.FeedbackEntryOpened] = "Opened: {0}",
            [StringKeys.FeedbackNavigationUndone] =
                "Undid the previous sidebar navigation",
            [StringKeys.FeedbackSelectionPreviewed] =
                "{0} preview: {1}",
            [StringKeys.FeedbackSelectionApplied] = "{0}: {1}",
            [StringKeys.FeedbackSelectionCanceled] =
                "Canceled the pending selection",
            [StringKeys.FeedbackPromptSent] = "Prompt sent",
            [StringKeys.FeedbackListening] = "Listening",
            [StringKeys.FeedbackDictationEnded] =
                "Dictation ended",

            [StringKeys.ErrorBridgeSafePreview] =
                "Bridge is off",
            [StringKeys.ErrorAgentNotForeground] =
                "The Agent is not in the foreground",
            [StringKeys.ErrorAgentWindowNotFound] =
                "The Agent window could not be found",
            [StringKeys.ErrorAutomationElementNotFound] =
                "A required interface element could not be found",
            [StringKeys.ErrorAutomationFocusRejected] =
                "The Agent did not accept interface focus",
            [StringKeys.ErrorComposerEmpty] =
                "The composer is empty",
            [StringKeys.ErrorInputInjectionFailed] =
                "Key input failed",
            [StringKeys.ErrorAutomationElementUnsupported] =
                "The interface element does not support this action",
            [StringKeys.ErrorOperationCanceled] =
                "Operation canceled",
            [StringKeys.ErrorAutomationStale] =
                "The Agent interface changed during the operation",
            [StringKeys.ErrorNavigationUnavailable] =
                "There is no navigation to undo",
            [StringKeys.ErrorKeybindingsInvalid] =
                "The shortcut configuration is invalid; writing was stopped to protect it",
            [StringKeys.ErrorKeybindingsPathUnavailable] =
                "The shortcut configuration directory could not be determined",
            [StringKeys.ErrorAutomationUnexpected] =
                "The automation operation failed unexpectedly",
            [StringKeys.ErrorCapabilityUnavailable] =
                "This Agent does not support the requested capability",
            [StringKeys.ErrorWithDetail] = "{0}: {1}",

            [StringKeys.MessageAgentKeybindingsWriteFailed] =
                "{0} shortcuts were not written · {1}",
            [StringKeys.MessageAgentKeybindingsConflict] =
                "{0} shortcut conflict · {1}",
            [StringKeys.MessageFallbackKeybindingsWritten] =
                "Fallback shortcuts written · restart {0} to apply",
            [StringKeys.MessageAgentSidebar] = "{0} sidebar",
            [StringKeys.MessageAlreadyAtRootScope] =
                "Already at a root scope",
            [StringKeys.MessageFocusedEntryHasNoChildDirectory] =
                "The focused entry has no child directory · A opens the task",
            [StringKeys.MessageUseRightToEnterProject] =
                "Projects are directories · press → to enter; A opens tasks only",
            [StringKeys.MessageNoAvailableEntries] =
                "No available entries in this scope",
            [StringKeys.MessageProjectTasks] = "Project tasks",
            [StringKeys.MessageTaskHasNoProject] =
                "This task is not assigned to a project",
            [StringKeys.MessageProjectUnavailable] =
                "This project is currently unavailable",
            [StringKeys.MessageNoAvailableTasks] =
                "No available tasks",
            [StringKeys.MessageProjectTasksPosition] =
                "Project tasks · {0} · {1}",
            [StringKeys.MessageProjectTitle] = "Project › {0}",
            [StringKeys.MessageButtonProjectTasks] =
                "{0} · Project tasks",
            [StringKeys.MessageNoLocatableEntry] =
                "No entry is available to locate",
            [StringKeys.MessageProjectHasNoPinnedTasks] =
                "This project has no pinned tasks",
            [StringKeys.MessageProjectPinnedOnly] =
                "Pinned in this project",
            [StringKeys.MessageAllTasks] = "All tasks",
            [StringKeys.MessageProjectTaskFilter] =
                "Project task filter · {0}",
            [StringKeys.MessageScopeHasNoEntries] =
                "No available entries in {0}",
            [StringKeys.MessageSidebarFocusFailed] =
                "Sidebar focus was not synchronized · {0}",
            [StringKeys.MessageDisclosureRestoreFailed] =
                "Project disclosure state was not restored · {0}",
            [StringKeys.MessageTaskUnavailableSkipped] =
                "Task is archived or unavailable · skipped",
            [StringKeys.MessageOpeningThread] =
                "Opening · {0}",
            [StringKeys.MessageOpeningTask] = "Opening task",
            [StringKeys.MessageOpenThreadFailed] =
                "Could not open task · {0}",
            [StringKeys.MessageUndoUnavailableUnique] =
                "Undo unavailable · could not uniquely identify {0}",
            [StringKeys.MessageOpenedUndoAvailable] =
                "Opened · {0} · {1} can undo",
            [StringKeys.MessageOpenedTask] = "Task opened",
            [StringKeys.MessageUndoWithinSeconds] =
                "{0} · press {2} within {1} seconds to undo",
            [StringKeys.MessageUndoUnavailableUnconfirmed] =
                "Undo unavailable · arrival at {0} was not confirmed",
            [StringKeys.MessageUndoUnconfirmed] =
                "Task opening was not confirmed; navigation was not reversed",
            [StringKeys.MessageRightStickGesture] =
                "Right stick {0}",
            [StringKeys.MessageRightStickMode] =
                "Right stick mode",
            [StringKeys.MessagePreviewValue] = "{0} · Preview",
            [StringKeys.MessageSettleToConfirm] =
                "{0} · Hold steady to confirm",
            [StringKeys.MessageApplyingValue] =
                "{0} · Applying…",
            [StringKeys.MessageShortcutSentValue] =
                "{0} · Shortcut sent",
            [StringKeys.MessageExactSelection] =
                "Exact selection",
            [StringKeys.MessageShortcutSentRestart] =
                "Shortcut sent (restart {0} for the new binding)",
            [StringKeys.MessageShortcutSent] = "Shortcut sent",
            [StringKeys.MessageNotExecutedValue] =
                "{0} · Not executed",
            [StringKeys.MessageComposerSelectionApplied] =
                "{0} → {1} · {2}",
            [StringKeys.MessageComposerSelectionFailed] =
                "{0} · Not executed · {1}",
            [StringKeys.MessageButtonUndo] = "{0} · Undo",
            [StringKeys.MessageButtonCancel] = "{0} · Cancel",
            [StringKeys.MessageStartDictation] =
                "{0} · Start dictation",
            [StringKeys.MessageRecordingReleaseToStop] =
                "Recording · release {0} to stop",
            [StringKeys.MessageReleaseNoRecording] =
                "{0} released · no active recording found",
            [StringKeys.MessageNoActiveRecording] =
                "No active recording found",
            [StringKeys.MessageReleaseEndingDictation] =
                "{0} released · ending dictation",
            [StringKeys.MessageReleaseEndingRecording] =
                "Released · ending recording",
            [StringKeys.MessageReleaseEndDictation] =
                "{0} released · end dictation",
            [StringKeys.MessageRecordingEnded] = "Recording ended",
            [StringKeys.MessageSendPrompt] =
                "{0} · Send prompt",
            [StringKeys.MessageSent] = "Sent",
            [StringKeys.MessageAbortDictation] =
                "{0} · Stop dictation",
            [StringKeys.MessageDictationStopped] =
                "Dictation stopped",
            [StringKeys.MessagePendingSelectionUndone] =
                "{0} · Pending selection undone",
            [StringKeys.MessageCurrentOperationStopped] =
                "{0} · Current operation stopped",
            [StringKeys.MessageCurrentOperationStoppedDetail] =
                "Current operation stopped",
            [StringKeys.MessageUndoQueued] =
                "{0} · Undo queued",
            [StringKeys.MessageUndoAfterOpen] =
                "Will return automatically after the task opens",
            [StringKeys.MessageCancel] = "{0} · Cancel",
            [StringKeys.MessageCanceled] = "Canceled",
            [StringKeys.MessageUndoPageChanged] =
                "{0} · Page changed; navigation was not undone",
            [StringKeys.MessageUndoPageChangedDetail] =
                "Page changed; did not navigate back",
            [StringKeys.MessageUndoSucceeded] =
                "{0} · Undone · {1}",
            [StringKeys.MessageReturnedToPreviousTask] =
                "Returned to the previous task · {0}",
            [StringKeys.MessageUndoFailed] =
                "{0} · Undo failed · {1}",
            [StringKeys.MessageDataLoaded] =
                "Loaded {0} tasks and {1} projects · filtered {2} archived and {3} unavailable",
            [StringKeys.MessageDataLoadFailed] =
                "Data load failed · {0}",
            [StringKeys.MessageExecuted] = "Executed",
            [StringKeys.MessageSafePreview] =
                "Controller input paused for this session",
            [StringKeys.MessageWaitingForAgentForeground] =
                "Waiting for {0} to enter the foreground",
            [StringKeys.MessageNotExecuted] = "Not executed",
            [StringKeys.MessageBridgeEnabled] =
                "Controller input enabled",
            [StringKeys.MessageBridgeSafePreview] =
                "The controller will not control Codex for now. Re-enable it at the top; it also resets on restart.",
            [StringKeys.MessageAgentDataRefreshed] =
                "Refreshed local {0} tasks",
            [StringKeys.MessageAgentShortcutsOpened] =
                "Opened {0} shortcut settings",
            [StringKeys.MessageControllerSoftwareOpenFailed] =
                "Could not open controller software",
            [StringKeys.MessageWindowHiddenBackground] =
                "Window hidden; the bridge continues running in the background",

            [StringKeys.ValueScopePinnedTasks] = "Pinned tasks",
            [StringKeys.ValueScopePinnedProjects] =
                "Pinned projects",
            [StringKeys.ValueScopeProjects] = "Projects",
            [StringKeys.ValueScopeProjectTasks] = "Project tasks",
            [StringKeys.ValueScopeProjectlessTasks] =
                "Loose tasks",
            [StringKeys.ValueReasoningMinimal] = "Minimal",
            [StringKeys.ValueReasoningLight] = "Light",
            [StringKeys.ValueReasoningLow] = "Low",
            [StringKeys.ValueReasoningMedium] = "Medium",
            [StringKeys.ValueReasoningHigh] = "High",
            [StringKeys.ValueReasoningExtraHigh] = "Extra high",
            [StringKeys.ValueReasoningMax] = "Max",
            [StringKeys.ValueReasoningUltra] = "Ultra",
            [StringKeys.ValueSpeedStandard] = "Standard",
            [StringKeys.ValueSpeedFast] = "Fast",
        };
}
