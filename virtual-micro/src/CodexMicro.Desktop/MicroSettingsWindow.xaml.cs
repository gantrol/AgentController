using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CodexMicro.Desktop.Services;

namespace CodexMicro.Desktop;

public partial class MicroSettingsWindow : Window
{
    private sealed record ModelChoice(
        CodexQuickModel Model,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record SettingChoice(
        string Id,
        string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly MicroLocalization _localization;
    private readonly MicroProfileSettings _profileSettings;
    private readonly CodexMicroLayoutObserver _layoutObserver;
    private readonly CodexMicroConfigWriter _configWriter;
    private readonly MicroHarnessRegistry _harnessRegistry;
    private readonly MicroVoiceInputService _voiceInput;
    private readonly bool _ownsVoiceInput;
    private readonly Func<Task>? _openOfficialSettings;
    private readonly Func<Task>? _reconnect;
    private readonly Func<bool> _isConnected;
    private readonly Func<Task>? _codexConfigChanged;
    private bool _lastConfigSaveSucceeded = true;
    private bool _syncing;
    private bool _showHarnessAdapterDetail;
    private MicroVoiceSettingsWindow? _voiceSettingsWindow;

    internal MicroSettingsWindow(
        MicroLocalization localization,
        MicroProfileSettings profileSettings,
        Visual previewVisual,
        CodexMicroLayoutObserver? layoutObserver = null,
        CodexMicroConfigWriter? configWriter = null,
        MicroHarnessRegistry? harnessRegistry = null,
        MicroVoiceInputService? voiceInput = null,
        Func<Task>? openOfficialSettings = null,
        Func<Task>? reconnect = null,
        Func<bool>? isConnected = null,
        Func<Task>? codexConfigChanged = null)
    {
        _localization = localization ??
            throw new ArgumentNullException(nameof(localization));
        _profileSettings = profileSettings ??
            throw new ArgumentNullException(nameof(profileSettings));
        ArgumentNullException.ThrowIfNull(previewVisual);
        _layoutObserver = layoutObserver ?? new CodexMicroLayoutObserver();
        _configWriter = configWriter ??
            new CodexMicroConfigWriter(_layoutObserver.ConfigPath);
        _harnessRegistry = harnessRegistry ?? new MicroHarnessRegistry();
        _ownsVoiceInput = voiceInput is null;
        _voiceInput = voiceInput ?? new MicroVoiceInputService(profileSettings);
        _openOfficialSettings = openOfficialSettings;
        _reconnect = reconnect;
        _isConnected = isConnected ?? (() => false);
        _codexConfigChanged = codexConfigChanged;

        InitializeComponent();
        LiveMicroPreviewBrush.Visual = previewVisual;
        _localization.LanguageChanged += Localization_LanguageChanged;
        _profileSettings.Changed += ProfileSettings_Changed;
        _layoutObserver.LayoutChanged += LayoutObserver_LayoutChanged;
        _harnessRegistry.Changed += HarnessRegistry_Changed;
        Closed += Window_Closed;
        RefreshPresentation();
    }

    private void RefreshPresentation()
    {
        var english = _localization.IsEnglish;
        var profile = _profileSettings.Current;
        var layout = _layoutObserver.Current;
        var harness = _harnessRegistry.Resolve(profile.ActiveHarnessId);
        var isCodex = harness.Id == "codex";

        _syncing = true;
        try
        {
            var models = new[]
            {
                new ModelChoice(CodexQuickModel.Sol, "Sol"),
                new ModelChoice(CodexQuickModel.Luna, "Luna"),
                new ModelChoice(CodexQuickModel.Terra, "Terra"),
            };
            QuickModelACombo.ItemsSource = models;
            QuickModelBCombo.ItemsSource = models;
            QuickModelACombo.SelectedItem = models.First(choice =>
                choice.Model == profile.QuickModelA);
            QuickModelBCombo.SelectedItem = models.First(choice =>
                choice.Model == profile.QuickModelB);

            SetChoices(
                QuickModelAEffortCombo,
                CreateReasoningEffortChoices(
                    profile.QuickModelA,
                    english),
                profile.QuickModelAEffort ?? "remember");
            SetChoices(
                QuickModelBEffortCombo,
                CreateReasoningEffortChoices(
                    profile.QuickModelB,
                    english),
                profile.QuickModelBEffort ?? "remember");

            SetChoices(
                AgentSourceCombo,
                isCodex
                    ? [
                        new("recent", english ? "Most recent chats" : "最近任务"),
                        new("pinned", english ? "Pinned chats" : "已固定任务"),
                        new("priority", english ? "Priority order" : "优先级顺序"),
                        new("custom", english ? "Custom mapping" : "自定义映射"),
                    ]
                    : [new(
                        "harness-sessions",
                        english ? "Harness sessions" : "Harness 会话")],
                isCodex ? profile.AgentSource : "harness-sessions");
            SetChoices(
                KnobModeCombo,
                isCodex
                    ? [
                        new("composer-navigation", english ? "Composer navigation" : "输入区导航"),
                        new("reasoning", english ? "Reasoning only" : "仅推理强度"),
                        new("conversation-scroll", english ? "Conversation scroll" : "对话滚动"),
                        new("custom", english ? "Custom" : "自定义"),
                    ]
                    : [
                        new(
                            MicroHarnessKnobModes.ComposerNavigation,
                            english ? "Composer controls" : "输入区控件"),
                        new(
                            MicroHarnessKnobModes.ReasoningOnly,
                            english ? "Reasoning only" : "仅推理强度"),
                        new(
                            MicroHarnessKnobModes.RecentSessions,
                            english ? "Recent sessions" : "最近会话"),
                    ],
                isCodex
                    ? layout.EncoderMode
                    : _harnessRegistry.ResolveKnobMode(harness.Id));
            SetChoices(
                MicrophoneModeCombo,
                isCodex
                    ? [
                        new("push-to-talk", english ? "Push to talk" : "按住说话"),
                        new(
                            "tap-to-toggle",
                            english ? "Tap to start / stop" : "点按开始 / 再点停止"),
                        new(
                            "realtime",
                            english ? "Codex realtime voice" : "Codex 实时语音"),
                    ]
                    : [
                        new("push-to-talk", english ? "Push to talk" : "按住说话"),
                        new(
                            "tap-to-toggle",
                            english ? "Tap to start / stop" : "点按开始 / 再点停止"),
                    ],
                profile.TapToToggleVoice
                    ? "tap-to-toggle"
                    : isCodex ? layout.VoiceButtonMode : "push-to-talk");

            HarnessCombo.ItemsSource = _harnessRegistry.Definitions;
            HarnessCombo.SelectedItem = _harnessRegistry.Resolve(
                profile.ActiveHarnessId);
            InvertDialDirectionToggle.IsChecked =
                isCodex && profile.InvertDialDirection;
            SingleTapToggle.IsChecked = profile.SingleTapAgentKeys;
            if (!isCodex)
            {
                HarnessPipeNameTextBox.Text = harness.Connection.PipeName ?? string.Empty;
                HarnessControlUriTextBox.Text = harness.Connection.ControlUri ?? string.Empty;
                HarnessExecutableTextBox.Text = harness.Connection.Executable ?? string.Empty;
                HarnessArgumentsTextBox.Text = harness.Connection.Arguments ?? string.Empty;
                HarnessWorkingDirectoryTextBox.Text =
                    harness.Connection.WorkingDirectory ?? string.Empty;
                HarnessReadyTimeoutTextBox.Text =
                    harness.Connection.ReadyTimeoutMilliseconds.ToString();
                HarnessAutoStartToggle.IsChecked = harness.Connection.AutoStart;
                RefreshHarnessKeyMapChoices(harness, english);
            }
        }
        finally
        {
            _syncing = false;
        }

        Title = isCodex
            ? english
                ? "Codex Micro · Software settings"
                : "Codex Micro · 软件设置"
            : $"{harness.DisplayName} · Micro settings";
        WindowTitleText.Text = isCodex
            ? english ? "Micro software settings" : "Micro 软件设置"
            : $"{harness.DisplayName} · Micro";
        LocalBadgeText.Text = english ? "LIVE" : "实时";
        WindowSubtitleText.Text = isCodex
            ? english
                ? "The running keypad is the editor. Changes are saved to the real Micro configuration."
                : "直接编辑正在运行的小键盘；改动会保存到真实的 Micro 配置。"
            : english
                ? "Edit this Harness's live Micro layout here. Voice capture and recognition are configured on this keypad."
                : "在此直接编辑当前 Harness 的实时 Micro 布局；语音采集与识别均在此小键盘配置。";
        LayoutHeadingText.Text = "Layout";
        ResetButton.Content = english ? "Reset layout" : "重置布局";
        PreviewHintText.Text = english
            ? "Hover and click a keycap to edit"
            : "悬停并点击键帽进行编辑";
        OptionsHeadingText.Text = "Options";
        AgentKeysTitleText.Text = "Agent keys";
        AgentKeysDetailText.Text = english
            ? isCodex
                ? "Choose what the six agent keys follow or trigger"
                : "The six keys show sessions returned by this Harness adapter"
            : isCodex
                ? "选择六个 Agent 键跟随或触发的内容"
                : "六个 Agent 键显示此 Harness 适配器返回的会话";
        KnobTitleText.Text = english ? "Knob" : "旋钮";
        KnobDetailText.Text = english
            ? isCodex
                ? "Choose what turning the knob controls"
                : "Choose composer controls, reasoning only, or recent Harness sessions"
            : isCodex
                ? "选择转动旋钮时控制的内容"
                : "选择输入区控件、仅推理强度或最近 Harness 会话";
        InvertDialDirectionTitleText.Text = english
            ? "Reverse dial direction"
            : "反转旋钮方向";
        InvertDialDirectionDetailText.Text = english
            ? "Swap clockwise and counterclockwise input for this Codex keypad"
            : "仅对当前 Codex 小键盘交换顺时针与逆时针输入";
        JoystickTitleText.Text = english ? "Joystick" : "摇杆";
        JoystickDetailText.Text = english
            ? "Configure four native Harness directions"
            : "配置四个 Harness 原生方向动作";
        JoystickMappingButton.Content = english ? "Directions ›" : "方向 ›";
        MicrophoneTitleText.Text = english ? "Microphone key" : "麦克风键";
        MicrophoneDetailText.Text = english
            ? "Choose hold, tap-to-toggle, or Codex realtime behavior"
            : "选择按住说话、点按开始 / 停止，或 Codex 实时语音";
        SingleTapTitleText.Text = english
            ? isCodex
                ? "Focus Codex with a single tap"
                : "Open a Harness session with one tap"
            : isCodex
                ? "单击聚焦 Codex"
                : "单击打开 Harness 会话";
        SingleTapDetailText.Text = english
            ? isCodex
                ? "Open the assigned task and focus Codex with one tap instead of two"
                : "When off, the first tap selects and the second tap opens the session"
            : isCodex
                ? "单击即可打开对应任务并聚焦 Codex，无需双击"
                : "关闭时，第一次单击只选择，第二次单击才打开会话";
        ExtensionsHeadingText.Text = english ? "Extensions" : "扩展";
        QuickModelATitleText.Text = english ? "Quick model A" : "快捷模型 A";
        QuickModelADetailText.Text = english
            ? "First model and its target reasoning effort"
            : "第一个模型及切换后的目标思考强度";
        QuickModelBTitleText.Text = english ? "Quick model B" : "快捷模型 B";
        QuickModelBDetailText.Text = english
            ? "Second model and its target reasoning effort"
            : "第二个模型及切换后的目标思考强度";
        HarnessTitleText.Text = english ? "Codex key target" : "Codex 键目标";
        HarnessDetailText.Text = english
            ? "Select Codex, DeepSeek Harness, or any registered direct adapter"
            : "选择 Codex、DeepSeek Harness，或其他已注册的直连适配器";
        HarnessAdapterHeadingText.Text = english
            ? $"{harness.DisplayName} adapter"
            : $"{harness.DisplayName} 适配器";
        HarnessManagementHeadingText.Text = english
            ? $"{harness.DisplayName} options"
            : $"{harness.DisplayName} 选项";
        HarnessServiceTitleText.Text = english
            ? "Harness service"
            : "Harness 服务";
        HarnessServiceDetailText.Text = english
            ? "Start when offline; open and focus the existing surface when running"
            : "未运行时启动；运行时打开并置前现有页面";
        ManageHarnessButton.Content = _showHarnessAdapterDetail
            ? english ? "Done" : "完成"
            : english ? "Configure" : "配置";
        HarnessVoiceTitleText.Text = english ? "Voice input" : "语音输入";
        HarnessVoiceDetailText.Text = english
            ? "Keypad-owned microphone, system speech, local Qwen, or remote streaming API"
            : "由小键盘持有麦克风、系统识别、本地 Qwen 或远程流式 API";
        ConfigureHarnessVoiceButton.Content = english
            ? "Keypad settings"
            : "小键盘设置";
        HarnessPipeTitleText.Text = english ? "Adapter pipe" : "适配器管道";
        HarnessPipeDetailText.Text = english
            ? "Versioned local direct-plugin endpoint"
            : "带版本的本机直连插件端点";
        HarnessControlUriTitleText.Text = english
            ? "WSL control URL"
            : "WSL 控制地址";
        HarnessControlUriDetailText.Text = english
            ? "Loopback HTTP endpoint; preferred over the pipe when set"
            : "仅限回环的 HTTP 端点；填写后优先于命名管道";
        HarnessExecutableTitleText.Text = english ? "Executable" : "启动程序";
        HarnessExecutableDetailText.Text = english
            ? "Started directly without a command shell"
            : "直接启动，不经过命令行 Shell";
        HarnessArgumentsTitleText.Text = english ? "Arguments" : "启动参数";
        HarnessArgumentsDetailText.Text = english
            ? "Arguments passed directly to the executable"
            : "直接传给启动程序的参数";
        HarnessWorkingDirectoryTitleText.Text = english
            ? "Working directory"
            : "工作目录";
        HarnessWorkingDirectoryDetailText.Text = english
            ? "Project directory used by the Harness process"
            : "Harness 进程使用的项目目录";
        HarnessAutoStartTitleText.Text = english
            ? "Start when offline"
            : "离线时启动";
        HarnessAutoStartDetailText.Text = english
            ? "The Harness button starts this process when its adapter is unavailable"
            : "按钮发现适配器离线时，自动启动此进程";
        var lastTimeout = !isCodex
            ? _harnessRegistry.GetLastTimeoutDiagnostic(harness.Id)
            : null;
        HarnessLaunchStatusText.Text = lastTimeout is null
            ? english
                ? "Settings are isolated per Harness and saved on this device."
                : "设置按 Harness 隔离并保存在本机。"
            : english
                ? $"Last cold-start timeout: " +
                    $"{lastTimeout.TimedOutAt.ToLocalTime():g}; " +
                    $"{lastTimeout.ElapsedMilliseconds / 1000d:F1}s, " +
                    $"{lastTimeout.ProbeAttempts} checks. " +
                    $"Last check: {lastTimeout.LastProbeMessage}"
                : $"上次冷启动超时：" +
                    $"{lastTimeout.TimedOutAt.ToLocalTime():g}；" +
                    $"{lastTimeout.ElapsedMilliseconds / 1000d:F1} 秒，" +
                    $"探测 {lastTimeout.ProbeAttempts} 次。" +
                    $"最后结果：{lastTimeout.LastProbeMessage}";
        SaveHarnessButton.Content = english ? "Save" : "保存";
        OpenHarnessButton.Content = english ? "Open Harness" : "打开 Harness";
        HarnessKeyMapHeadingText.Text = english
            ? $"{harness.DisplayName} key map"
            : $"{harness.DisplayName} 键位";
        HarnessKeyMapDetailText.Text = english
            ? "Frequency-based defaults: New, Conversation / trajectory, Stop, and Fork. Every Harness keeps its own map."
            : "按使用频率默认设置为：新会话、对话 / 轨迹、停止、分叉。每个 Harness 独立保存。";
        HarnessAgentMapTitleText.Text = english ? "Agent keys ×6" : "Agent 键 ×6";
        HarnessAgentMapValueText.Text = english
            ? "Recent Harness sessions"
            : "最近 Harness 会话";
        HarnessAction06MapTitleText.Text = "ACT06 · New";
        HarnessAction07MapTitleText.Text = english
            ? "ACT07 · Conversation / trajectory"
            : "ACT07 · 对话 / 轨迹";
        HarnessAction08MapTitleText.Text = "ACT08 · Stop";
        HarnessAction09MapTitleText.Text = "ACT09 · Fork";
        HarnessVoiceWideMapTitleText.Text = english
            ? "ACT10 + ACT11 · Wide key"
            : "ACT10 + ACT11 · 宽键";
        HarnessVoiceLeftMapTitleText.Text = english
            ? "ACT10 · Split key"
            : "ACT10 · 分体键";
        HarnessVoiceRightMapTitleText.Text = english
            ? "ACT11 · Split key"
            : "ACT11 · 分体键";
        HarnessJoystickUpMapTitleText.Text = english ? "Joystick · Up" : "摇杆 · 上";
        HarnessJoystickDownMapTitleText.Text = english ? "Joystick · Down" : "摇杆 · 下";
        HarnessJoystickLeftMapTitleText.Text = english ? "Joystick · Left" : "摇杆 · 左";
        HarnessJoystickRightMapTitleText.Text = english ? "Joystick · Right" : "摇杆 · 右";
        HarnessAction12MapTitleText.Text = "ACT12 · Harness Logo";
        HarnessAction12MapValueText.Text = english
            ? "Open or focus Harness"
            : "打开或聚焦 Harness";
        DiagnosticsHeadingText.Text = english ? "Connection" : "连接";
        OpenOfficialSettingsButton.Content = english
            ? "Official Micro settings"
            : "官方 Micro 设置";
        ReconnectButton.Content = english ? "Reconnect" : "重新连接";

        AutomationProperties.SetName(
            CloseButton,
            english ? "Close settings" : "关闭设置");
        AutomationProperties.SetName(
            HarnessCombo,
            english ? "Codex key target" : "Codex 键目标");
        ApplyHarnessScope(harness);
        RefreshLayoutPresentation(layout);
        RefreshConnectionState();
        RefreshSaveState();
    }

    private void ApplyHarnessScope(MicroHarnessDefinition harness)
    {
        var isCodex = harness.Id == "codex";
        LayoutCard.IsEnabled = true;
        LayoutCard.Opacity = 1;
        PreviewHintText.Text = _localization.IsEnglish
            ? "Hover and click a keycap to edit"
            : "悬停并点击键帽进行编辑";

        KnobModeCombo.IsEnabled = true;
        AgentSourceCombo.IsEnabled = isCodex;
        MicrophoneModeCombo.IsEnabled = true;
        InvertDialDirectionOptionRow.Visibility = isCodex
            ? Visibility.Visible
            : Visibility.Collapsed;
        InvertDialDirectionSeparator.Visibility =
            InvertDialDirectionOptionRow.Visibility;
        InvertDialDirectionToggle.IsEnabled = isCodex;
        JoystickOptionRow.Visibility = isCodex
            ? Visibility.Collapsed
            : Visibility.Visible;
        JoystickOptionSeparator.Visibility = JoystickOptionRow.Visibility;
        SingleTapToggle.IsEnabled = true;
        QuickModelARow.Visibility = isCodex
            ? Visibility.Visible
            : Visibility.Collapsed;
        QuickModelBRow.Visibility = QuickModelARow.Visibility;
        HarnessManagementHeadingText.Visibility = isCodex
            ? Visibility.Collapsed
            : Visibility.Visible;
        HarnessManagementCard.Visibility = HarnessManagementHeadingText.Visibility;
        HarnessAdapterHeadingText.Visibility = isCodex
            ? Visibility.Collapsed
            : _showHarnessAdapterDetail
                ? Visibility.Visible
                : Visibility.Collapsed;
        HarnessAdapterCard.Visibility = HarnessAdapterHeadingText.Visibility;
        // Harness key mappings are edited from the live Layout preview. Keep
        // the legacy flat list out of the primary settings flow.
        HarnessKeyMapHeadingText.Visibility = Visibility.Collapsed;
        HarnessKeyMapDetailText.Visibility = Visibility.Collapsed;
        HarnessKeyMapCard.Visibility = Visibility.Collapsed;
        OpenOfficialSettingsButton.Visibility = isCodex
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReconnectButton.Visibility = OpenOfficialSettingsButton.Visibility;
    }

    private static void SetChoices(
        ComboBox comboBox,
        IReadOnlyList<SettingChoice> choices,
        string selectedId)
    {
        comboBox.ItemsSource = choices;
        comboBox.SelectedItem = choices.FirstOrDefault(choice =>
            choice.Id == selectedId) ?? choices[0];
    }

    private IReadOnlyList<SettingChoice> CreateReasoningEffortChoices(
        CodexQuickModel model,
        bool english)
    {
        var choices = new List<SettingChoice>
        {
            new("remember", english ? "Remember" : "记忆上次"),
        };
        choices.AddRange(
            _profileSettings.GetSupportedReasoningEfforts(model)
                .Select(effort => new SettingChoice(
                    effort,
                    effort switch
                    {
                        "low" => "Low",
                        "medium" => "Medium",
                        "high" => "High",
                        "xhigh" => "XHigh",
                        "max" => "Max",
                        "ultra" => "Ultra",
                        _ => effort,
                    })));
        return choices;
    }

    private void RefreshHarnessKeyMapChoices(
        MicroHarnessDefinition harness,
        bool english)
    {
        var choices = new SettingChoice[]
        {
            new(MicroHarnessActionIds.None, english ? "Unassigned" : "未分配"),
            new(MicroHarnessActionIds.NewSession, english ? "New session" : "新建会话"),
            new(MicroHarnessActionIds.ToggleConversationView, english ? "Conversation / trajectory" : "对话 / 轨迹"),
            new(MicroHarnessActionIds.ApproveInteraction, english ? "Allow once / approve plan" : "允许一次 / 批准计划"),
            new(MicroHarnessActionIds.CancelTurn, english ? "Stop current generation" : "停止当前生成"),
            new(MicroHarnessActionIds.ForkSession, english ? "Fork current session" : "Fork 当前会话"),
            new(MicroHarnessActionIds.RejectInteraction, english ? "Reject / decline plan" : "拒绝 / 否决计划"),
            new(MicroHarnessActionIds.ArchiveSession, english ? "Archive current session" : "归档当前会话"),
            new(MicroHarnessActionIds.LoadOlderHistory, english ? "Load older history" : "加载更早历史"),
            new(MicroHarnessActionIds.ToggleSidebar, english ? "Toggle sidebar" : "切换侧边栏"),
            new(MicroHarnessActionIds.OpenDetails, english ? "Open details" : "打开详情栏"),
            new(MicroHarnessActionIds.CloseDetails, english ? "Close details" : "关闭详情栏"),
            new(MicroHarnessActionIds.PreviousSession, english ? "Previous session" : "上一个会话"),
            new(MicroHarnessActionIds.NextSession, english ? "Next session" : "下一个会话"),
            new(MicroHarnessActionIds.OpenSelectedSession, english ? "Open selected session" : "打开已选会话"),
            new(MicroHarnessActionIds.ActivateSurface, english ? "Open / focus Harness" : "打开 / 聚焦 Harness"),
        };
        var voiceChoices = choices
            .Append(new SettingChoice(
                MicroHarnessActionIds.VoiceDictation,
                english ? "Push to talk" : "按住说话"))
            .ToArray();
        var map = _harnessRegistry.ResolveKeyMap(harness.Id);
        var controls = new (ComboBox Combo, string ControlId)[]
        {
            (HarnessAction06Combo, MicroHarnessControlIds.Action06),
            (HarnessAction07Combo, MicroHarnessControlIds.Action07),
            (HarnessAction08Combo, MicroHarnessControlIds.Action08),
            (HarnessAction09Combo, MicroHarnessControlIds.Action09),
            (HarnessVoiceWideCombo, MicroHarnessControlIds.VoiceWide),
            (HarnessVoiceLeftCombo, MicroHarnessControlIds.VoiceLeft),
            (HarnessVoiceRightCombo, MicroHarnessControlIds.VoiceRight),
            (HarnessJoystickUpCombo, MicroHarnessControlIds.JoystickUp),
            (HarnessJoystickDownCombo, MicroHarnessControlIds.JoystickDown),
            (HarnessJoystickLeftCombo, MicroHarnessControlIds.JoystickLeft),
            (HarnessJoystickRightCombo, MicroHarnessControlIds.JoystickRight),
        };
        foreach (var (combo, controlId) in controls)
        {
            SetChoices(
                combo,
                MicroHarnessControlIds.IsVoice(controlId)
                    ? voiceChoices
                    : choices,
                map.Resolve(controlId));
            AutomationProperties.SetName(
                combo,
                english
                    ? $"{harness.DisplayName} mapping for {controlId}"
                    : $"{harness.DisplayName} 的 {controlId} 映射");
        }
    }

    private void RefreshLayoutPresentation(CodexMicroLayoutSnapshot layout)
    {
        EditCombinedMicrophoneButton.Visibility = layout.SeparateMicrophoneKeys
            ? Visibility.Collapsed
            : Visibility.Visible;
        EditMicrophone1Button.Visibility = layout.SeparateMicrophoneKeys
            ? Visibility.Visible
            : Visibility.Collapsed;
        EditMicrophone2Button.Visibility = layout.SeparateMicrophoneKeys
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    internal void FocusHarnessOptions()
    {
        Show();
        Activate();
        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        FrameworkElement target = harness.Id == "codex"
            ? HarnessOptionRow
            : HarnessManagementCard;
        target.BringIntoView();
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            target.BringIntoView();
            (harness.Id == "codex"
                ? (IInputElement)HarnessCombo
                : ManageHarnessButton).Focus();
        }));
    }

    internal void FocusActiveAgentSettings()
    {
        Show();
        Activate();
        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        FrameworkElement target = harness.Id == "codex"
            ? QuickModelARow
            : HarnessManagementCard;
        target.BringIntoView();
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            target.BringIntoView();
            (harness.Id == "codex"
                ? (IInputElement)QuickModelACombo
                : ManageHarnessButton).Focus();
        }));
    }

    internal void FocusHarnessSetup()
    {
        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id == "codex")
        {
            FocusActiveAgentSettings();
            return;
        }

        _showHarnessAdapterDetail = true;
        RefreshPresentation();
        Show();
        Activate();
        HarnessAdapterCard.BringIntoView();
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            HarnessAdapterCard.BringIntoView();
            HarnessExecutableTextBox.Focus();
        }));
    }

    private void ManageHarnessButton_Click(object sender, RoutedEventArgs e)
    {
        _showHarnessAdapterDetail = !_showHarnessAdapterDetail;
        RefreshPresentation();
        var target = _showHarnessAdapterDetail
            ? (FrameworkElement)HarnessAdapterCard
            : HarnessManagementCard;
        target.BringIntoView();
    }

    internal void FocusHarnessVoiceSettings()
    {
        Show();
        Activate();
        HarnessManagementCard.BringIntoView();
        OpenVoiceSettingsWindow();
    }

    private void ConfigureHarnessVoiceButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        OpenVoiceSettingsWindow();
    }

    private void OpenVoiceSettingsWindow()
    {
        if (_voiceSettingsWindow is not null)
        {
            if (_voiceSettingsWindow.WindowState == WindowState.Minimized)
            {
                _voiceSettingsWindow.WindowState = WindowState.Normal;
            }

            _voiceSettingsWindow.Topmost = Topmost;
            _voiceSettingsWindow.Show();
            _voiceSettingsWindow.Activate();
            return;
        }

        var window = new MicroVoiceSettingsWindow(
            _localization,
            _profileSettings,
            _voiceInput)
        {
            Owner = this,
            Topmost = Topmost,
        };
        _voiceSettingsWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_voiceSettingsWindow, window))
            {
                _voiceSettingsWindow = null;
            }
        };
        window.Show();
        window.Activate();
    }

    internal void RefreshConnectionState()
    {
        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id != "codex")
        {
            ConnectionStatusDot.Fill = new SolidColorBrush(
                Color.FromRgb(0xD2, 0xB8, 0x72));
            ConnectionStatusText.Text = _localization.IsEnglish
                ? $"{harness.DisplayName} adapter is checked on open"
                : $"打开时检查 {harness.DisplayName} 适配器";
            return;
        }

        var connected = _isConnected();
        ConnectionStatusDot.Fill = new SolidColorBrush(
            connected
                ? Color.FromRgb(0x79, 0xA5, 0xFF)
                : Color.FromRgb(0xD2, 0xB8, 0x72));
        ConnectionStatusText.Text = connected
            ? _localization.IsEnglish
                ? "Virtual HID connected"
                : "虚拟 HID 已连接"
            : _localization.IsEnglish
                ? "Virtual HID is not connected"
                : "虚拟 HID 尚未连接";
    }

    private void RefreshSaveState()
    {
        var saved = _profileSettings.LastSaveSucceeded &&
            _lastConfigSaveSucceeded &&
            _harnessRegistry.LastSaveSucceeded;
        SaveStatusText.Text = saved
            ? _localization.IsEnglish
                ? "Changes are saved automatically on this device."
                : "改动会自动保存在本机。"
            : _localization.IsEnglish
                ? "The change is active, but it could not be saved to disk."
                : "改动已在本次运行中生效，但无法写入磁盘。";
        SaveStatusText.Foreground = new SolidColorBrush(
            saved
                ? Color.FromRgb(0x77, 0x7A, 0x7D)
                : Color.FromRgb(0xC0, 0x78, 0x58));
    }

    private void QuickModelACombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_syncing && QuickModelACombo.SelectedItem is ModelChoice choice)
        {
            _profileSettings.SetQuickModelA(choice.Model);
        }
    }

    private void QuickModelBCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_syncing && QuickModelBCombo.SelectedItem is ModelChoice choice)
        {
            _profileSettings.SetQuickModelB(choice.Model);
        }
    }

    private void QuickModelAEffortCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_syncing &&
            QuickModelAEffortCombo.SelectedItem is SettingChoice choice)
        {
            _profileSettings.SetQuickModelAEffort(
                choice.Id == "remember" ? null : choice.Id);
        }
    }

    private void QuickModelBEffortCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_syncing &&
            QuickModelBEffortCombo.SelectedItem is SettingChoice choice)
        {
            _profileSettings.SetQuickModelBEffort(
                choice.Id == "remember" ? null : choice.Id);
        }
    }

    private void AgentSourceCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_syncing && AgentSourceCombo.SelectedItem is SettingChoice choice)
        {
            _profileSettings.SetAgentSource(choice.Id);
        }
    }

    private void HarnessCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_syncing &&
            HarnessCombo.SelectedItem is MicroHarnessDefinition harness)
        {
            _profileSettings.SetActiveHarness(harness.Id);
        }
    }

    private void KnobModeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncing ||
            KnobModeCombo.SelectedItem is not SettingChoice choice)
        {
            return;
        }

        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id == "codex")
        {
            SaveLayoutChange(() => _configWriter.SetEncoderMode(choice.Id));
        }
        else
        {
            _harnessRegistry.UpdateKnobMode(harness.Id, choice.Id);
            RefreshSaveState();
        }
    }

    private void MicrophoneModeCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncing ||
            MicrophoneModeCombo.SelectedItem is not SettingChoice choice)
        {
            return;
        }

        var isCodex = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId).Id == "codex";
        var tapToToggle = choice.Id == "tap-to-toggle";
        _profileSettings.SetTapToToggleVoice(tapToToggle);
        if (isCodex)
        {
            SaveLayoutChange(() => _configWriter.SetVoiceButtonMode(
                tapToToggle ? "push-to-talk" : choice.Id));
        }
    }

    private void SingleTapToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
        {
            _profileSettings.SetSingleTapAgentKeys(
                SingleTapToggle.IsChecked == true);
        }
    }

    private void InvertDialDirectionToggle_Changed(
        object sender,
        RoutedEventArgs e)
    {
        if (!_syncing &&
            _harnessRegistry.Resolve(_profileSettings.Current.ActiveHarnessId).Id ==
                "codex")
        {
            _profileSettings.SetInvertDialDirection(
                InvertDialDirectionToggle.IsChecked == true);
        }
    }

    private void HarnessKeyMapCombo_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_syncing ||
            sender is not ComboBox
            {
                Tag: string controlId,
                SelectedItem: SettingChoice choice,
            })
        {
            return;
        }

        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id == "codex")
        {
            return;
        }

        _harnessRegistry.UpdateKeyMapping(
            harness.Id,
            controlId,
            choice.Id);
        RefreshSaveState();
    }

    private void JoystickMappingButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id == "codex")
        {
            return;
        }

        var choices = HarnessAction06Combo.Items
            .Cast<SettingChoice>()
            .Where(choice => !MicroHarnessActionIds.IsVoice(choice.Id))
            .ToArray();
        var map = _harnessRegistry.ResolveKeyMap(harness.Id);
        var directions = new[]
        {
            (Arrow: "↑", Name: _localization.IsEnglish ? "Up" : "上", ControlId: MicroHarnessControlIds.JoystickUp),
            (Arrow: "→", Name: _localization.IsEnglish ? "Right" : "右", ControlId: MicroHarnessControlIds.JoystickRight),
            (Arrow: "↓", Name: _localization.IsEnglish ? "Down" : "下", ControlId: MicroHarnessControlIds.JoystickDown),
            (Arrow: "←", Name: _localization.IsEnglish ? "Left" : "左", ControlId: MicroHarnessControlIds.JoystickLeft),
        };
        var menu = new ContextMenu
        {
            PlacementTarget = JoystickMappingButton,
        };
        foreach (var direction in directions)
        {
            var currentId = map.Resolve(direction.ControlId);
            var current = choices.FirstOrDefault(choice =>
                choice.Id == currentId)?.DisplayName ?? currentId;
            var directionItem = new MenuItem
            {
                Header = $"{direction.Arrow} {direction.Name} · {current}",
            };
            foreach (var choice in choices)
            {
                var actionItem = new MenuItem
                {
                    Header = choice.DisplayName,
                    IsCheckable = true,
                    IsChecked = choice.Id == currentId,
                };
                var controlId = direction.ControlId;
                var actionId = choice.Id;
                actionItem.Click += (_, _) =>
                {
                    _harnessRegistry.UpdateKeyMapping(
                        harness.Id,
                        controlId,
                        actionId);
                    RefreshSaveState();
                };
                directionItem.Items.Add(actionItem);
            }

            menu.Items.Add(directionItem);
        }

        menu.IsOpen = true;
    }

    private void SaveLayoutChange(Func<bool> save)
    {
        _lastConfigSaveSucceeded = save();
        if (_lastConfigSaveSucceeded)
        {
            _layoutObserver.ReloadNow();
            _ = NotifyCodexConfigChangedAsync();
        }

        RefreshSaveState();
    }

    private async void PreviewSlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string slotId })
        {
            return;
        }

        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id != "codex" && slotId == "ACT12")
        {
            HarnessManagementCard.BringIntoView();
            ManageHarnessButton.Focus();
            return;
        }

        try
        {
            var editor = harness.Id == "codex"
                ? new KeycapEditorWindow(
                    slotId,
                    _layoutObserver.Current.GetSlot(slotId),
                    _localization,
                    _configWriter,
                    _layoutObserver)
                : new KeycapEditorWindow(
                    slotId,
                    harness.Id,
                    _localization,
                    _harnessRegistry);
            editor.Owner = this;
            editor.Topmost = Topmost;
            if (editor.ShowDialog() == true && harness.Id == "codex")
            {
                await NotifyCodexConfigChangedAsync();
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException)
        {
            SaveStatusText.Text = _localization.IsEnglish
                ? $"Could not open the {slotId} editor: {exception.Message}"
                : $"无法打开 {slotId} 编辑器：{exception.Message}";
            SaveStatusText.Foreground = new SolidColorBrush(
                Color.FromRgb(0xB0, 0x6B, 0x4F));
        }
    }

    private async Task NotifyCodexConfigChangedAsync()
    {
        if (_codexConfigChanged is null)
        {
            return;
        }

        try
        {
            await _codexConfigChanged();
        }
        catch (Exception exception) when (
            exception is IOException or
                TimeoutException or
                InvalidDataException or
                InvalidOperationException or
                ObjectDisposedException)
        {
            SaveStatusText.Text = _localization.IsEnglish
                ? "Saved, but Codex has not reloaded the setting yet. " +
                    "Reconnect or restart Codex."
                : "设置已保存，但 Codex 尚未重新加载；请重新连接或重启 Codex。";
            SaveStatusText.Foreground = new SolidColorBrush(
                Color.FromRgb(0xB0, 0x6B, 0x4F));
        }
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id == "codex")
        {
            SaveLayoutChange(_configWriter.ResetLayout);
        }
        else
        {
            _harnessRegistry.ResetKeyMap(harness.Id);
            RefreshPresentation();
        }
    }

    private void SaveHarnessButton_Click(object sender, RoutedEventArgs e) =>
        SaveHarnessSettings();

    private bool SaveHarnessSettings()
    {
        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        if (harness.Id == "codex")
        {
            return false;
        }

        if (!int.TryParse(
                HarnessReadyTimeoutTextBox.Text,
                out var readyTimeoutMilliseconds) ||
            readyTimeoutMilliseconds is < 1_000 or
                > MicroHarnessRegistry.MaximumReadyTimeoutMilliseconds)
        {
            HarnessLaunchStatusText.Text = _localization.IsEnglish
                ? "Ready timeout must be between 1000 and 600000 ms."
                : "就绪超时必须在 1000 到 600000 毫秒之间。";
            return false;
        }

        var controlUriText = HarnessControlUriTextBox.Text.Trim();
        if (controlUriText.Length != 0 &&
            (!Uri.TryCreate(controlUriText, UriKind.Absolute, out var controlUri) ||
                controlUri.Scheme != Uri.UriSchemeHttp ||
                !controlUri.IsLoopback ||
                !string.IsNullOrEmpty(controlUri.UserInfo)))
        {
            HarnessLaunchStatusText.Text = _localization.IsEnglish
                ? "WSL control URL must be an unauthenticated loopback http:// address."
                : "WSL 控制地址必须是无凭据的本机回环 http:// 地址。";
            return false;
        }

        var saved = _harnessRegistry.UpdateConnectionSettings(
            harness.Id,
            new(
                HarnessPipeNameTextBox.Text,
                HarnessExecutableTextBox.Text,
                HarnessArgumentsTextBox.Text,
                HarnessWorkingDirectoryTextBox.Text,
                HarnessAutoStartToggle.IsChecked == true,
                readyTimeoutMilliseconds,
                controlUriText));
        if (saved)
        {
            saved = _harnessRegistry.MarkSetupCompleted(harness.Id);
        }
        HarnessLaunchStatusText.Text = saved
            ? _localization.IsEnglish
                ? "Harness adapter settings saved."
                : "Harness 适配器设置已保存。"
            : _localization.IsEnglish
                ? "The settings are active, but could not be saved to disk."
                : "设置已生效，但无法保存到磁盘。";
        RefreshSaveState();
        return saved;
    }

    private async void OpenHarnessButton_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveHarnessSettings())
        {
            return;
        }

        var harness = _harnessRegistry.Resolve(
            _profileSettings.Current.ActiveHarnessId);
        OpenHarnessButton.IsEnabled = false;
        try
        {
            HarnessLaunchStatusText.Text = _localization.IsEnglish
                ? $"Opening {harness.DisplayName}…"
                : $"正在打开 {harness.DisplayName}…";
            var result = await _harnessRegistry
                .ActivateUntilSurfaceReadyAsync(harness.Id);
            HarnessLaunchStatusText.Text = result.Message;
            ConnectionStatusDot.Fill = new SolidColorBrush(result.Success
                ? Color.FromRgb(0x79, 0xA5, 0xFF)
                : Color.FromRgb(0xD2, 0xB8, 0x72));
            ConnectionStatusText.Text = result.Success
                ? _localization.IsEnglish
                    ? $"{harness.DisplayName} adapter connected"
                    : $"{harness.DisplayName} 适配器已连接"
                : _localization.IsEnglish
                    ? $"{harness.DisplayName} adapter is offline"
                    : $"{harness.DisplayName} 适配器离线";
        }
        finally
        {
            OpenHarnessButton.IsEnabled = true;
        }
    }

    private async void OpenOfficialSettingsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_openOfficialSettings is null)
        {
            return;
        }

        OpenOfficialSettingsButton.IsEnabled = false;
        try
        {
            await _openOfficialSettings();
        }
        finally
        {
            OpenOfficialSettingsButton.IsEnabled = true;
        }
    }

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_reconnect is null)
        {
            return;
        }

        ReconnectButton.IsEnabled = false;
        try
        {
            await _reconnect();
        }
        finally
        {
            ReconnectButton.IsEnabled = true;
            RefreshConnectionState();
        }
    }

    private void TitleBar_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Localization_LanguageChanged(object? sender, EventArgs e) =>
        RunOnDispatcher(RefreshPresentation);

    private void ProfileSettings_Changed(object? sender, EventArgs e) =>
        RunOnDispatcher(RefreshPresentation);

    private void HarnessRegistry_Changed(object? sender, EventArgs e) =>
        RunOnDispatcher(RefreshPresentation);

    private void LayoutObserver_LayoutChanged(
        object? sender,
        CodexMicroLayoutSnapshot snapshot) =>
        RunOnDispatcher(RefreshPresentation);

    private void RunOnDispatcher(Action action)
    {
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = Dispatcher.BeginInvoke(action);
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        _voiceSettingsWindow?.Close();
        _voiceSettingsWindow = null;
        LiveMicroPreviewBrush.Visual = null;
        _localization.LanguageChanged -= Localization_LanguageChanged;
        _profileSettings.Changed -= ProfileSettings_Changed;
        _layoutObserver.LayoutChanged -= LayoutObserver_LayoutChanged;
        _harnessRegistry.Changed -= HarnessRegistry_Changed;
        Closed -= Window_Closed;
        if (_ownsVoiceInput)
        {
            await _voiceInput.DisposeAsync();
        }
    }
}
