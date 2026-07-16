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

            [StringKeys.DeviceWaiting] = "Waiting for controller",
            [StringKeys.DeviceConnected] = "Controller connected: {0}",
            [StringKeys.DeviceDisconnected] =
                "Controller disconnected",
            [StringKeys.DeviceEnableBridge] = "Enable bridge",
            [StringKeys.DeviceGamepadBridge] = "Gamepad bridge",
            [StringKeys.DeviceLiveInput] = "Live input",
            [StringKeys.DeviceIdle] = "Idle",

            [StringKeys.ControlLeftStick] = "Left stick",
            [StringKeys.ControlLeftStickHint] =
                "↑↓ Move focus · → Enter project · ← Exit project · {1} Open task · {0} changes root",
            [StringKeys.ControlRightStick] = "Right stick",
            [StringKeys.ControlRightStickHint] =
                "← / → Control · {0} Open · hold {0} Settings",
            [StringKeys.ControlRightStickHintOpen] =
                "← / → Option · {0} Select · {1} Close",
            [StringKeys.ControlPrimary] = "{0} · Open task",
            [StringKeys.ControlPrimaryDescription] =
                "Open the focused task; use → to enter a project",
            [StringKeys.ControlHoldToTalk] = "{0} · Hold to talk",
            [StringKeys.ControlHoldToTalkDescription] =
                "Release to finish dictation",
            [StringKeys.ControlSend] = "{0} · Send",
            [StringKeys.ControlSendDescription] =
                "Submit the current prompt",
            [StringKeys.ControlCancelUndo] = "{0} · Cancel / undo",
            [StringKeys.ControlCancelUndoDescription] =
                "Cancel the action; briefly undo after opening",
            [StringKeys.ControlProjectContext] =
                "{0} · Project context",
            [StringKeys.ControlProjectContextDescription] =
                "Enter its project; filter pinned tasks within a project",
            [StringKeys.ControlWakeAgent] = "{0} · Wake {1}",
            [StringKeys.ControlWakeAgentDescription] =
                "Bring {0} to front and unlock controller input",

            [StringKeys.ComposerRightStickAdjustment] =
                "Right-stick virtual dial",
            [StringKeys.ComposerAgentNotForeground] =
                "{0} is not in the foreground",
            [StringKeys.ComposerDialReady] =
                "Move left or right to choose a control",
            [StringKeys.ComposerConnectController] =
                "Connect a controller to begin",
            [StringKeys.ComposerDialSettingsOpened] =
                "Controller settings opened",
            [StringKeys.ComposerDialCanceled] =
                "Current picker closed",
            [StringKeys.TermVirtualDial] = "Virtual dial",
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
                "Tasks without a project",
            [StringKeys.SidebarRecentEvents] = "Recent events",
            [StringKeys.SidebarPinnedBadge] = "Pinned",
            [StringKeys.SidebarEnterAction] = "→ Enter",
            [StringKeys.SidebarOpenAction] = "A Open",
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

            [StringKeys.ConfigTitle] = "Configuration",
            [StringKeys.ConfigDescription] =
                "Stick directions stay fixed; {0} commands can adapt to versions and personal preferences.",
            [StringKeys.ConfigLeftStickSidebar] =
                "Left stick · {0} sidebar",
            [StringKeys.ConfigMoveFocus] = "↑↓",
            [StringKeys.ConfigMoveFocusDescription] =
                "Select an item at the current level and sync focus with the native {0} sidebar",
            [StringKeys.ConfigEnterBack] = "→ / ← / A",
            [StringKeys.ConfigEnterBackDescription] =
                "→ enters the focused project; ← exits to its parent; A opens a focused task",
            [StringKeys.ConfigRootProjectGlyphs] = "{0} / {1}",
            [StringKeys.ConfigRootProjectDescription] =
                "{0} cycles four root scopes; {1} enters the owning project and toggles all / pinned-only within it",
            [StringKeys.ConfigSidebarBehavior] =
                "↑↓ follows this app's stable wheel and syncs {0} sidebar focus without opening a conversation. Activity timestamps never reorder the wheel; → enters a project, ← exits it, and A opens tasks only. Pinning and project expansion remain independent.",
            [StringKeys.ConfigRightStickComposer] =
                "Right stick · Virtual dial",
            [StringKeys.ConfigIncreaseDecrease] = "←→",
            [StringKeys.ConfigIncreaseDecreaseDescription] =
                "Previous / next composer control; traverse options after opening a menu",
            [StringKeys.ConfigModeSwitchGlyphs] = "{0} / {1}",
            [StringKeys.ConfigModeSwitchDescription] =
                "Click to open or select; hold for 500 ms to open controller settings",
            [StringKeys.ConfigSelectionBehavior] =
                "Model, reasoning, speed, and the new-task Project picker share one dial path. B closes an open picker first; outside a picker it cancels the current action or recording and can undo a recent task navigation.",
            [StringKeys.ConfigAgentShortcuts] = "{0} shortcuts",
            [StringKeys.ConfigAgentShortcutsDescription] =
                "The app safely appends fallback bindings; new bindings take effect after {0} restarts.",
            [StringKeys.ConfigOpenAgentShortcuts] =
                "Open {0} shortcuts",
            [StringKeys.ConfigLowerReasoning] =
                "Lower reasoning effort",
            [StringKeys.ConfigRaiseReasoning] =
                "Raise reasoning effort",
            [StringKeys.ConfigToggleFast] = "Toggle Fast",
            [StringKeys.ConfigSubmitPrompt] = "Submit prompt",
            [StringKeys.ConfigDictation] = "Dictation",
            [StringKeys.ConfigModelPicker] = "Model picker",
            [StringKeys.ConfigRestoreRecommended] =
                "Restore recommended values",
            [StringKeys.ConfigSave] = "Save configuration",

            [StringKeys.SettingsTitle] = "Settings",
            [StringKeys.SettingsDescription] =
                "Control bridge scope, stick feel, and background behavior.",
            [StringKeys.SettingsBehavior] = "Behavior",
            [StringKeys.SettingsOnlyForeground] =
                "Control {0} only when it is in the foreground",
            [StringKeys.SettingsOnlyForegroundDescription] =
                "Recommended. Press {0} once to bring {1} forward and unlock it. Leaving the foreground pauses input; returning to {1} resumes after the controller is neutral. Reconnecting after sleep works the same way.",
            [StringKeys.SettingsHaptic] =
                "Use light haptic feedback after successful actions",
            [StringKeys.SettingsOverlay] =
                "Show brief status overlays at the lower center of the screen",
            [StringKeys.SettingsRadialMenu] =
                "Shortcut wheel hints",
            [StringKeys.SettingsRadialMenuDescription] =
                "Always appears immediately; Learning appears after a short hold; Off hides the wheel.",
            [StringKeys.SettingsRadialMenuAlways] =
                "Always show",
            [StringKeys.SettingsRadialMenuLearning] =
                "Learning mode",
            [StringKeys.SettingsRadialMenuOff] =
                "Off",
            [StringKeys.SettingsStick] = "Sticks",
            [StringKeys.SettingsStickDescription] =
                "Primary-direction locking prevents diagonal movement from triggering horizontal and vertical actions together.",
            [StringKeys.SettingsDeadZone] = "Dead zone",
            [StringKeys.SettingsInitialRepeat] =
                "Initial repeat delay",
            [StringKeys.SettingsRepeatInterval] = "Repeat interval",
            [StringKeys.SettingsSystem] = "System",
            [StringKeys.SettingsStartWithWindows] =
                "Start after signing in to Windows",
            [StringKeys.SettingsMinimizeToTray] =
                "Keep running in the tray when the window closes",
            [StringKeys.SettingsOpenControllerSoftware] =
                "Open {0}",
            [StringKeys.SettingsOpenControllerSoftwareGeneric] =
                "Open controller software",
            [StringKeys.SettingsOpenAgent] = "Open {0} settings",
            [StringKeys.SettingsSave] = "Save settings",
            [StringKeys.SettingsLanguage] = "Display language",
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
                "Waiting for the controller to reconnect",
            [StringKeys.StatusAgentForegroundLocked] =
                "{0} is in the foreground · press {1} to unlock",
            [StringKeys.StatusAgentForegroundNeutral] =
                "{0} is in the foreground · release controls to resume",
            [StringKeys.StatusAgentForegroundArmed] =
                "{0} is in the foreground · unlocked",
            [StringKeys.StatusBackgroundArmed] =
                "Background control · unlocked",
            [StringKeys.StatusBackgroundLocked] =
                "Background control · press {0} to unlock",
            [StringKeys.StatusAgentAwayPaused] =
                "{0} left the foreground · control paused",
            [StringKeys.StatusAgentNotForeground] =
                "{0} is not in the foreground · press {1} to wake",
            [StringKeys.StatusControllerHelp] =
                "{0} wakes and unlocks · left stick ↑↓ focus, → enters project, ← exits project, {4} opens task, {1} changes root · {2} project context · right stick ←→ turns, {3} opens / selects · hold {5} to talk · {6} sends · {7} cancels / undoes",
            [StringKeys.TrayOpenApplication] =
                "Open Agent Controller",
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
                "The bridge is in safe preview",
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

            [StringKeys.MessageShortcutSettingsSaved] =
                "Shortcut settings saved",
            [StringKeys.MessageSettingsSaved] = "Settings saved",
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
                "The focused entry has no child directory · A opens tasks; Y finds the owning project",
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
            [StringKeys.MessageSafePreview] = "Safe preview",
            [StringKeys.MessageWaitingForAgentForeground] =
                "Waiting for {0} to enter the foreground",
            [StringKeys.MessageNotExecuted] = "Not executed",
            [StringKeys.MessageBridgeEnabled] = "Bridge enabled",
            [StringKeys.MessageBridgeSafePreview] =
                "Bridge switched to safe preview",
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
                "Tasks without a project",
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
