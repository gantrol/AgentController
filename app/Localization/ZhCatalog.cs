namespace CodexController.Localization;

public sealed class ZhCatalog : DictionaryStringCatalog
{
    public ZhCatalog()
        : base(
            AppLanguage.ZhCn,
            AppLanguageParser.ZhCnValue,
            Values)
    {
    }

    private static IReadOnlyDictionary<string, string> Values { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [StringKeys.AppTitle] = "Agent Controller",
            [StringKeys.AppSubtitle] = "{0} → {1}",
            [StringKeys.NavDevice] = "设备",
            [StringKeys.NavConfiguration] = "配置",
            [StringKeys.NavSettings] = "设置",
            [StringKeys.OverlayNotificationName] =
                "Agent Controller 通知",

            [StringKeys.DeviceWaiting] = "等待手柄",
            [StringKeys.DeviceConnected] = "手柄已连接：{0}",
            [StringKeys.DeviceDisconnected] = "手柄连接已断开",
            [StringKeys.DeviceEnableBridge] = "启用桥接",
            [StringKeys.DeviceGamepadBridge] = "手柄桥接",
            [StringKeys.DeviceLiveInput] = "实时输入",
            [StringKeys.DeviceIdle] = "空闲",

            [StringKeys.ControlLeftStick] = "左摇杆",
            [StringKeys.ControlLeftStickHint] =
                "↑↓ 移动焦点 · → 进入项目 · ← 退出项目 · {1} 打开任务 · {0} 切根区域",
            [StringKeys.ControlRightStick] = "右摇杆",
            [StringKeys.ControlRightStickHint] =
                "简易：← / → 切模型＋思考档位 · 高级：← / → 切控件、↑ / ↓ 调值 · 长按 {0} 设置",
            [StringKeys.ControlRightStickHintOpen] =
                "模型选择器已打开 · ↑ / ↓ 选择 · {2} 确认 · {1} 关闭",
            [StringKeys.ControlRightStickHintConfirmation] =
                "模型选择确认 · {2} 确认 · {1} 取消",
            [StringKeys.ControlPrimary] = "{0} · 打开任务",
            [StringKeys.ControlPrimaryDescription] =
                "打开当前焦点任务；进入项目请按 →",
            [StringKeys.ControlHoldToTalk] = "{0} · 按住说话",
            [StringKeys.ControlHoldToTalkDescription] =
                "松开结束语音识别",
            [StringKeys.ControlSend] = "{0} · 发送",
            [StringKeys.ControlSendDescription] = "提交当前提示词",
            [StringKeys.ControlCancelUndo] = "{0} · 取消 / 撤回",
            [StringKeys.ControlCancelUndoDescription] =
                "取消操作；打开后短时返回",
            [StringKeys.ControlProjectContext] = "{0} · 动作面板",
            [StringKeys.ControlProjectContextDescription] =
                "Plan、侧边栏、前后导航、清空输入与项目上下文",
            [StringKeys.ControlWakeAgent] = "{0} · 唤醒 {1}",
            [StringKeys.ControlWakeAgentDescription] =
                "将 {0} 置于前台并解锁手柄控制",

            [StringKeys.ComposerRightStickAdjustment] =
                "右摇杆模型旋钮",
            [StringKeys.ComposerAgentNotForeground] = "{0} 未在前台",
            [StringKeys.ComposerDialReady] =
                "简易模式 · 模型＋思考强度组合档位",
            [StringKeys.ComposerConnectController] =
                "连接手柄后开始",
            [StringKeys.ComposerDialSettingsOpened] =
                "已打开手柄设置",
            [StringKeys.ComposerDialCanceled] =
                "已关闭当前选择器",
            [StringKeys.TermVirtualDial] = "虚拟旋钮",
            [StringKeys.TermReasoningEffort] = "思考强度",
            [StringKeys.TermModel] = "模型",
            [StringKeys.TermSpeed] = "速度",

            [StringKeys.SidebarAgent] = "{0} 侧边栏",
            [StringKeys.SidebarCurrentProject] = "当前项目",
            [StringKeys.SidebarRefresh] = "刷新",
            [StringKeys.SidebarPinnedTasks] = "置顶任务",
            [StringKeys.SidebarPinnedProjects] = "置顶项目",
            [StringKeys.SidebarProjects] = "项目",
            [StringKeys.SidebarProjectlessTasks] = "未归项目",
            [StringKeys.SidebarRecentEvents] = "最近事件",
            [StringKeys.SidebarPinnedBadge] = "置顶",
            [StringKeys.SidebarEnterAction] = "→ 进入",
            [StringKeys.SidebarOpenAction] = "A 打开",
            [StringKeys.SidebarProjectTaskCountOne] =
                "{0} 个任务",
            [StringKeys.SidebarProjectTaskCountMany] =
                "{0} 个任务",
            [StringKeys.SidebarPinnedRelativeTime] =
                "置顶 · {0}",
            [StringKeys.SidebarUntitledTask] = "未命名任务",
            [StringKeys.SidebarJustNow] = "刚刚",
            [StringKeys.SidebarOneMinuteAgo] = "1 分钟前",
            [StringKeys.SidebarMinutesAgo] = "{0} 分钟前",
            [StringKeys.SidebarOneHourAgo] = "1 小时前",
            [StringKeys.SidebarHoursAgo] = "{0} 小时前",
            [StringKeys.SidebarOneDayAgo] = "1 天前",
            [StringKeys.SidebarDaysAgo] = "{0} 天前",

            [StringKeys.ConfigTitle] = "配置",
            [StringKeys.ConfigDescription] =
                "手柄方向固定，{0} 命令可按版本和个人习惯调整。",
            [StringKeys.ConfigLeftStickSidebar] =
                "左摇杆 · {0} 侧边栏",
            [StringKeys.ConfigMoveFocus] = "↑↓",
            [StringKeys.ConfigMoveFocusDescription] =
                "选择当前层级条目，并同步 {0} 原生侧边栏焦点",
            [StringKeys.ConfigEnterBack] = "→ / ← / A",
            [StringKeys.ConfigEnterBackDescription] =
                "→ 进入当前焦点项目；← 退出到父级；A 打开当前焦点任务",
            [StringKeys.ConfigRootProjectGlyphs] = "{0} / {1}",
            [StringKeys.ConfigRootProjectDescription] =
                "{0} 循环四个根区域；{1} 打开动作面板",
            [StringKeys.ConfigSidebarBehavior] =
                "↑↓ 遵循本程序的稳定滚轮目录，并同步 {0} 原生侧边栏焦点，但不会自动打开对话。任务活动时间不会重排滚轮；→ 进入项目，← 退出项目，A 只打开任务。置顶与项目折叠互相独立。",
            [StringKeys.ConfigRightStickComposer] =
                "右摇杆 · 模型旋钮",
            [StringKeys.ConfigIncreaseDecrease] = "←→",
            [StringKeys.ConfigIncreaseDecreaseDescription] =
                "简易模式切换模型＋思考强度组合档位；高级模式用 ← / → 切换模型、思考强度或速度，用 ↑ / ↓ 调整当前值",
            [StringKeys.ConfigModeSwitchGlyphs] = "{0} / {1}",
            [StringKeys.ConfigModeSwitchDescription] =
                "单击显示当前简易档位，或循环高级模式控件；长按 500 ms 打开手柄设置",
            [StringKeys.ConfigSelectionBehavior] =
                "旋钮只操作模型相关设置。简易模式合并模型与思考强度；高级模式拆分模型、思考强度和速度，绝不会进入 Full access 或 Project 选择器。",
            [StringKeys.ConfigAgentShortcuts] = "{0} 快捷键",
            [StringKeys.ConfigAgentShortcutsDescription] =
                "程序会安全追加降级绑定；新绑定在 {0} 重启后生效。",
            [StringKeys.ConfigOpenAgentShortcuts] =
                "打开 {0} 快捷键",
            [StringKeys.ConfigLowerReasoning] = "降低思考强度",
            [StringKeys.ConfigRaiseReasoning] = "提高思考强度",
            [StringKeys.ConfigToggleFast] = "切换 Fast",
            [StringKeys.ConfigSubmitPrompt] = "发送提示词",
            [StringKeys.ConfigDictation] = "语音识别",
            [StringKeys.ConfigModelPicker] = "模型选择器",
            [StringKeys.ConfigRestoreRecommended] = "恢复推荐值",
            [StringKeys.ConfigSave] = "保存配置",

            [StringKeys.SettingsTitle] = "设置",
            [StringKeys.SettingsDescription] =
                "控制桥接生效范围、摇杆手感和后台运行方式。",
            [StringKeys.SettingsBehavior] = "行为",
            [StringKeys.SettingsOnlyForeground] =
                "仅前台时控制 {0}",
            [StringKeys.SettingsOnlyForegroundDescription] =
                "推荐保持开启。首次按 {0} 将 {1} 置于前台并解锁；之后离开前台只暂停，回到 {1} 且手柄回中后自动恢复。手柄休眠重连同样会恢复。",
            [StringKeys.SettingsHaptic] =
                "操作成功时提供轻微震动",
            [StringKeys.SettingsOverlay] =
                "在屏幕中下方显示短暂状态浮层",
            [StringKeys.SettingsRadialMenu] =
                "组合键轮盘提示",
            [StringKeys.SettingsRadialMenuDescription] =
                "始终显示会立即出现；学习期显示会在短暂按住后出现；关闭则隐藏轮盘。",
            [StringKeys.SettingsRadialMenuAlways] =
                "始终显示",
            [StringKeys.SettingsRadialMenuLearning] =
                "学习期显示",
            [StringKeys.SettingsRadialMenuOff] =
                "关闭",
            [StringKeys.SettingsComposerDialMode] =
                "模型旋钮模式",
            [StringKeys.SettingsComposerDialModeDescription] =
                "简易模式按组合档位切换模型与思考强度；高级模式分别提供模型、思考强度和速度三个控件。",
            [StringKeys.SettingsComposerDialModeSimple] =
                "简易模式",
            [StringKeys.SettingsComposerDialModeAdvanced] =
                "高级模式",
            [StringKeys.SettingsStick] = "摇杆",
            [StringKeys.SettingsStickDescription] =
                "当前采用主方向锁定，斜推不会同时触发横纵动作。",
            [StringKeys.SettingsDeadZone] = "死区",
            [StringKeys.SettingsInitialRepeat] = "首次连发",
            [StringKeys.SettingsRepeatInterval] = "连发间隔",
            [StringKeys.SettingsSystem] = "系统",
            [StringKeys.SettingsStartWithWindows] =
                "登录 Windows 后自动启动",
            [StringKeys.SettingsMinimizeToTray] =
                "关闭窗口时继续在托盘运行",
            [StringKeys.SettingsOpenControllerSoftware] =
                "打开 {0}",
            [StringKeys.SettingsOpenControllerSoftwareGeneric] =
                "打开手柄配置软件",
            [StringKeys.SettingsOpenAgent] = "打开 {0} 设置",
            [StringKeys.SettingsSave] = "保存设置",
            [StringKeys.SettingsLanguage] = "界面语言",
            [StringKeys.SettingsLanguageAuto] = "跟随系统",
            [StringKeys.SettingsLanguageZhCn] = "简体中文",
            [StringKeys.SettingsLanguageEnUs] = "English (US)",

            [StringKeys.DispatchSend] = "发送",
            [StringKeys.DispatchSendDescription] =
                "使用当前撰写内容开始新一轮。",
            [StringKeys.DispatchSteer] = "加入当前运行",
            [StringKeys.DispatchSteerDescription] =
                "将当前撰写内容加入仍在执行的这一轮。",
            [StringKeys.DispatchQueue] = "排到下一轮",
            [StringKeys.DispatchQueueDescription] =
                "保存当前撰写内容，在这一轮完成后用于下一轮。",
            [StringKeys.DispatchDefault] = "默认提交",
            [StringKeys.DispatchDefaultDescription] =
                "遵循 Codex 当前的提交行为；Agent Controller 尚未获得足够的活动轮次与 Follow-up 状态，无法确定具体结果。",

            [StringKeys.StatusReady] = "{0} 已就绪",
            [StringKeys.StatusLoadingAgentData] =
                "正在加载 {0} 数据…",
            [StringKeys.StatusAgentDataLoadFailed] =
                "读取 {0} 本机任务失败",
            [StringKeys.StatusLocalBridge] = "本机桥接",
            [StringKeys.StatusControllerArmed] = "手柄控制已启用",
            [StringKeys.StatusControllerLocked] = "手柄控制已锁定",
            [StringKeys.StatusControllerPaused] = "手柄控制已暂停",
            [StringKeys.StatusControllerResumed] = "手柄控制已恢复",
            [StringKeys.StatusWaitingForReconnect] =
                "等待手柄重新连接",
            [StringKeys.StatusAgentForegroundLocked] =
                "{0} 位于前台 · 按 {1} 解锁",
            [StringKeys.StatusAgentForegroundNeutral] =
                "{0} 位于前台 · 松开按键后恢复",
            [StringKeys.StatusAgentForegroundArmed] =
                "{0} 位于前台 · 已解锁",
            [StringKeys.StatusBackgroundArmed] =
                "后台控制 · 已解锁",
            [StringKeys.StatusBackgroundLocked] =
                "后台控制 · 按 {0} 解锁",
            [StringKeys.StatusAgentAwayPaused] =
                "{0} 暂离前台 · 控制已暂停",
            [StringKeys.StatusAgentNotForeground] =
                "{0} 未在前台 · 按 {1} 唤醒",
            [StringKeys.StatusControllerHelp] =
                "{0} 首次唤醒并解锁 · 左摇杆 ↑↓ 移动焦点、→ 进入项目、← 退出项目、{4} 打开任务、{1} 切根区域 · {2} 项目上下文 · 右摇杆 ←→ 转动、{3} 打开 / 确认 · {5} 按住说话 · {6} 发送 · {7} 取消 / 撤回",
            [StringKeys.TrayOpenApplication] =
                "打开 Agent Controller",
            [StringKeys.TrayOpenAgent] = "打开 {0}",
            [StringKeys.TrayExit] = "退出",

            [StringKeys.FeedbackStatusUpdated] = "状态已更新",
            [StringKeys.FeedbackOperationFailed] = "操作失败",
            [StringKeys.FeedbackWakeStarted] =
                "正在将 {0} 置于前台",
            [StringKeys.FeedbackWakeSucceeded] =
                "{0} 已置于前台",
            [StringKeys.FeedbackWakeFailed] =
                "无法将 {0} 置于前台",
            [StringKeys.FeedbackScopeChanged] =
                "侧边栏区域：{0}",
            [StringKeys.FeedbackFocusChanged] =
                "侧边栏焦点：{0}",
            [StringKeys.FeedbackEntryOpened] = "已打开：{0}",
            [StringKeys.FeedbackNavigationUndone] =
                "已撤回上一次侧边栏跳转",
            [StringKeys.FeedbackSelectionPreviewed] =
                "{0}预选：{1}",
            [StringKeys.FeedbackSelectionApplied] = "{0}：{1}",
            [StringKeys.FeedbackSelectionCanceled] =
                "已撤销待执行选择",
            [StringKeys.FeedbackPromptSent] = "提示词已发送",
            [StringKeys.FeedbackListening] = "正在聆听",
            [StringKeys.FeedbackDictationEnded] =
                "语音输入已结束",

            [StringKeys.ErrorBridgeSafePreview] =
                "桥接已关闭",
            [StringKeys.ErrorAgentNotForeground] =
                "目标 Agent 未在前台",
            [StringKeys.ErrorAgentWindowNotFound] =
                "找不到 Agent 窗口",
            [StringKeys.ErrorAutomationElementNotFound] =
                "找不到所需界面元素",
            [StringKeys.ErrorAutomationFocusRejected] =
                "Agent 未接受界面焦点",
            [StringKeys.ErrorComposerEmpty] =
                "撰写区为空",
            [StringKeys.ErrorInputInjectionFailed] =
                "按键输入失败",
            [StringKeys.ErrorAutomationElementUnsupported] =
                "界面元素不支持该操作",
            [StringKeys.ErrorOperationCanceled] =
                "已取消",
            [StringKeys.ErrorAutomationStale] =
                "操作期间 Agent 界面发生了变化",
            [StringKeys.ErrorNavigationUnavailable] =
                "当前没有可撤回的导航",
            [StringKeys.ErrorKeybindingsInvalid] =
                "快捷键配置无效，已停止写入以保护原配置",
            [StringKeys.ErrorKeybindingsPathUnavailable] =
                "无法确定快捷键配置目录",
            [StringKeys.ErrorAutomationUnexpected] =
                "自动化操作发生意外错误",
            [StringKeys.ErrorCapabilityUnavailable] =
                "当前 Agent 不支持此功能",
            [StringKeys.ErrorWithDetail] = "{0}：{1}",

            [StringKeys.MessageShortcutSettingsSaved] =
                "快捷键配置已保存",
            [StringKeys.MessageSettingsSaved] = "设置已保存",
            [StringKeys.MessageAgentKeybindingsWriteFailed] =
                "{0} 快捷键未写入 · {1}",
            [StringKeys.MessageAgentKeybindingsConflict] =
                "{0} 快捷键冲突 · {1}",
            [StringKeys.MessageFallbackKeybindingsWritten] =
                "降级快捷键已写入 · 重启 {0} 后生效",
            [StringKeys.MessageAgentSidebar] = "{0} 侧边栏",
            [StringKeys.MessageAlreadyAtRootScope] =
                "当前已在根区域",
            [StringKeys.MessageFocusedEntryHasNoChildDirectory] =
                "当前条目没有下级目录 · A 打开任务；Y 定位所属项目",
            [StringKeys.MessageUseRightToEnterProject] =
                "项目是目录 · 按 → 进入；A 只打开任务",
            [StringKeys.MessageNoAvailableEntries] =
                "当前区域没有可用条目",
            [StringKeys.MessageProjectTasks] = "项目任务",
            [StringKeys.MessageTaskHasNoProject] =
                "该任务未归属项目",
            [StringKeys.MessageProjectUnavailable] =
                "项目当前不可用",
            [StringKeys.MessageNoAvailableTasks] =
                "暂无可用任务",
            [StringKeys.MessageProjectTasksPosition] =
                "项目任务 · {0} · {1}",
            [StringKeys.MessageProjectTitle] = "项目 › {0}",
            [StringKeys.MessageButtonProjectTasks] =
                "{0} · 项目任务",
            [StringKeys.MessageNoLocatableEntry] =
                "当前没有可定位条目",
            [StringKeys.MessageProjectHasNoPinnedTasks] =
                "该项目没有置顶任务",
            [StringKeys.MessageProjectPinnedOnly] =
                "仅该项目置顶",
            [StringKeys.MessageAllTasks] = "全部任务",
            [StringKeys.MessageProjectTaskFilter] =
                "项目任务筛选 · {0}",
            [StringKeys.MessageScopeHasNoEntries] =
                "{0}中暂无可用条目",
            [StringKeys.MessageSidebarFocusFailed] =
                "侧边栏焦点未同步 · {0}",
            [StringKeys.MessageDisclosureRestoreFailed] =
                "项目折叠状态未恢复 · {0}",
            [StringKeys.MessageTaskUnavailableSkipped] =
                "任务已归档或不可用 · 已跳过",
            [StringKeys.MessageOpeningThread] =
                "正在打开 · {0}",
            [StringKeys.MessageOpeningTask] = "正在打开任务",
            [StringKeys.MessageOpenThreadFailed] =
                "打开任务失败 · {0}",
            [StringKeys.MessageUndoUnavailableUnique] =
                "撤回未启用 · 无法唯一确认 {0}",
            [StringKeys.MessageOpenedUndoAvailable] =
                "已打开 · {0} · {1} 可撤回",
            [StringKeys.MessageOpenedTask] = "已打开任务",
            [StringKeys.MessageUndoWithinSeconds] =
                "{0} · {1} 秒内按 {2} 撤回",
            [StringKeys.MessageUndoUnavailableUnconfirmed] =
                "撤回未启用 · 未确认已到达 {0}",
            [StringKeys.MessageUndoUnconfirmed] =
                "未确认任务已打开，未执行返回",
            [StringKeys.MessageRightStickGesture] =
                "右摇杆 {0}",
            [StringKeys.MessageRightStickMode] =
                "右摇杆模式",
            [StringKeys.MessagePreviewValue] = "{0} · 预选",
            [StringKeys.MessageSettleToConfirm] =
                "{0} · 停稳后确认",
            [StringKeys.MessageApplyingValue] =
                "{0} · 正在应用…",
            [StringKeys.MessageShortcutSentValue] =
                "{0} · 快捷键已发送",
            [StringKeys.MessageExactSelection] = "精确选择",
            [StringKeys.MessageShortcutSentRestart] =
                "快捷键已发送（新绑定需重启 {0}）",
            [StringKeys.MessageShortcutSent] = "快捷键已发送",
            [StringKeys.MessageNotExecutedValue] =
                "{0} · 未执行",
            [StringKeys.MessageComposerSelectionApplied] =
                "{0} → {1} · {2}",
            [StringKeys.MessageComposerSelectionFailed] =
                "{0} · 未执行 · {1}",
            [StringKeys.MessageButtonUndo] = "{0} · 撤回",
            [StringKeys.MessageButtonCancel] = "{0} · 取消",
            [StringKeys.MessageStartDictation] =
                "{0} · 开始语音识别",
            [StringKeys.MessageRecordingReleaseToStop] =
                "正在录音 · 松开 {0} 停止",
            [StringKeys.MessageReleaseNoRecording] =
                "{0} 松开 · 未发现活动录音",
            [StringKeys.MessageNoActiveRecording] =
                "未发现活动录音",
            [StringKeys.MessageReleaseEndingDictation] =
                "{0} 松开 · 正在结束语音识别",
            [StringKeys.MessageReleaseEndingRecording] =
                "松开 · 正在结束录音",
            [StringKeys.MessageReleaseEndDictation] =
                "{0} 松开 · 结束语音识别",
            [StringKeys.MessageRecordingEnded] = "录音已结束",
            [StringKeys.MessageSendPrompt] =
                "{0} · 发送提示词",
            [StringKeys.MessageSent] = "已发送",
            [StringKeys.MessageAbortDictation] =
                "{0} · 中止语音",
            [StringKeys.MessageDictationStopped] =
                "已中止语音识别",
            [StringKeys.MessagePendingSelectionUndone] =
                "{0} · 已撤销待执行选择",
            [StringKeys.MessageCurrentOperationStopped] =
                "{0} · 已中止当前操作",
            [StringKeys.MessageCurrentOperationStoppedDetail] =
                "已中止当前操作",
            [StringKeys.MessageUndoQueued] =
                "{0} · 已排队撤回",
            [StringKeys.MessageUndoAfterOpen] =
                "任务打开后将自动返回",
            [StringKeys.MessageCancel] = "{0} · 取消",
            [StringKeys.MessageCanceled] = "已取消",
            [StringKeys.MessageUndoPageChanged] =
                "{0} · 页面已变化，未执行导航撤回",
            [StringKeys.MessageUndoPageChangedDetail] =
                "页面已变化，未执行返回",
            [StringKeys.MessageUndoSucceeded] =
                "{0} · 已撤回 · {1}",
            [StringKeys.MessageReturnedToPreviousTask] =
                "已返回上一任务 · {0}",
            [StringKeys.MessageUndoFailed] =
                "{0} · 撤回失败 · {1}",
            [StringKeys.MessageDataLoaded] =
                "已读取 {0} 个任务、{1} 个项目 · 已过滤 {2} 个归档、{3} 个不可用",
            [StringKeys.MessageDataLoadFailed] =
                "数据读取失败 · {0}",
            [StringKeys.MessageExecuted] = "已执行",
            [StringKeys.MessageSafePreview] = "桥接已关闭",
            [StringKeys.MessageWaitingForAgentForeground] =
                "等待 {0} 前台",
            [StringKeys.MessageNotExecuted] = "未执行",
            [StringKeys.MessageBridgeEnabled] = "桥接已启用",
            [StringKeys.MessageBridgeSafePreview] =
                "手柄不会控制 Codex。请在 Agent Controller 顶部重新启用。",
            [StringKeys.MessageAgentDataRefreshed] =
                "已刷新 {0} 本机任务",
            [StringKeys.MessageAgentShortcutsOpened] =
                "已打开 {0} 快捷键设置",
            [StringKeys.MessageControllerSoftwareOpenFailed] =
                "无法打开手柄配置软件",
            [StringKeys.MessageWindowHiddenBackground] =
                "窗口已隐藏，桥接继续在后台运行",

            [StringKeys.ValueScopePinnedTasks] = "置顶任务",
            [StringKeys.ValueScopePinnedProjects] = "置顶项目",
            [StringKeys.ValueScopeProjects] = "普通项目",
            [StringKeys.ValueScopeProjectTasks] = "项目任务",
            [StringKeys.ValueScopeProjectlessTasks] =
                "未归项目任务",
            [StringKeys.ValueReasoningMinimal] = "最低",
            [StringKeys.ValueReasoningLight] = "轻量",
            [StringKeys.ValueReasoningLow] = "低",
            [StringKeys.ValueReasoningMedium] = "中",
            [StringKeys.ValueReasoningHigh] = "高",
            [StringKeys.ValueReasoningExtraHigh] = "极高",
            [StringKeys.ValueReasoningMax] = "最高",
            [StringKeys.ValueReasoningUltra] = "超高",
            [StringKeys.ValueSpeedStandard] = "标准",
            [StringKeys.ValueSpeedFast] = "Fast",
        };
}
