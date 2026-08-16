using System.Globalization;

namespace CodexMicro.Desktop.Services;

public enum MicroLanguage
{
    Auto,
    ZhCn,
    EnUs,
}

public sealed class MicroLocalization
{
    private readonly Func<MicroLanguage?> _agentControllerLanguage;
    private readonly Func<CultureInfo> _systemCulture;
    private MicroLanguage _selectedLanguage;
    private MicroLanguage _effectiveLanguage;

    public MicroLocalization(
        MicroLanguage selectedLanguage = MicroLanguage.Auto,
        Func<MicroLanguage?>? agentControllerLanguage = null,
        Func<CultureInfo>? systemCulture = null)
    {
        _agentControllerLanguage = agentControllerLanguage ?? (() => null);
        _systemCulture = systemCulture ?? (() => CultureInfo.CurrentUICulture);
        _selectedLanguage = selectedLanguage;
        _effectiveLanguage = ResolveEffectiveLanguage();
    }

    public event EventHandler? LanguageChanged;

    public MicroLanguage SelectedLanguage => _selectedLanguage;

    public MicroLanguage EffectiveLanguage => _effectiveLanguage;

    public bool IsEnglish => _effectiveLanguage == MicroLanguage.EnUs;

    public void SetLanguage(MicroLanguage language)
    {
        Validate(language);
        var selectionChanged = language != _selectedLanguage;
        _selectedLanguage = language;
        var effective = ResolveEffectiveLanguage();
        if (!selectionChanged && effective == _effectiveLanguage)
        {
            return;
        }

        _effectiveLanguage = effective;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshAutoLanguage()
    {
        if (_selectedLanguage != MicroLanguage.Auto)
        {
            return;
        }

        var effective = ResolveEffectiveLanguage();
        if (effective == _effectiveLanguage)
        {
            return;
        }

        _effectiveLanguage = effective;
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Text(string value) => IsEnglish
        ? MicroEnglishTranslations.Translate(value)
        : value;

    public static MicroLanguage Parse(string? value) =>
        value?.Trim().Replace('_', '-').ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "zh-hans" or "zh-hans-cn" or "zhcn" =>
                MicroLanguage.ZhCn,
            "en" or "en-us" or "enus" => MicroLanguage.EnUs,
            _ => MicroLanguage.Auto,
        };

    public static string ToSettingValue(MicroLanguage language) => language switch
    {
        MicroLanguage.Auto => "auto",
        MicroLanguage.ZhCn => "zh-CN",
        MicroLanguage.EnUs => "en-US",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };

    private MicroLanguage ResolveEffectiveLanguage()
    {
        if (_selectedLanguage != MicroLanguage.Auto)
        {
            return _selectedLanguage;
        }

        var shared = _agentControllerLanguage();
        if (shared is MicroLanguage.ZhCn or MicroLanguage.EnUs)
        {
            return shared.Value;
        }

        return _systemCulture().TwoLetterISOLanguageName.Equals(
            "zh",
            StringComparison.OrdinalIgnoreCase)
                ? MicroLanguage.ZhCn
                : MicroLanguage.EnUs;
    }

    private static void Validate(MicroLanguage language)
    {
        if (!Enum.IsDefined(language))
        {
            throw new ArgumentOutOfRangeException(nameof(language));
        }
    }
}

internal static class MicroEnglishTranslations
{
    private static readonly (string Chinese, string English)[] Replacements =
    [
        // Keep complete status sentences ahead of generic fragments such as
        // "运行中" and "已连接" so localization cannot leave mixed text.
        ("正在等待网页桥接回报当前模型。", "Waiting for the browser bridge to report the current model."),
        ("当前模型：{currentModel}。", "Current model: {currentModel}."),
        ("左键在 DeepSeek Harness 中配置的两个快捷模型间切换；右键直达此 Agent 的适配器与独立键位设置。", "Left-click switches between the two quick models configured in DeepSeek Harness. Right-click opens this Agent's adapter and key-map settings."),
        ("当前尚无可切换会话；左键先打开并连接 DeepSeek Harness，右键直达此 Agent 的适配器与独立键位设置。", "There is no switchable session yet. Left-click opens and connects DeepSeek Harness first; right-click opens this Agent's adapter and key-map settings."),
        ("正在切换当前 DeepSeek 会话的快捷模型…", "Switching the current DeepSeek session's quick model…"),
        ("正在切换 DeepSeek 模型", "Switching DeepSeek model"),
        ("{session.DisplayTitle} 已是当前会话；已将 {harness.DisplayName} 置前。", "{session.DisplayTitle} is already the current session; brought {harness.DisplayName} to the foreground."),
        ("Harness 已连接 · 当前没有运行中的会话", "Harness connected · no sessions are running"),
        ("{runningCount} 个 Harness 会话正在运行", "{runningCount} Harness sessions running"),
        ("{harness.DisplayName} 适配器已连接", "{harness.DisplayName} adapter connected"),
        ("Harness 适配器离线", "Harness adapter offline"),
        ("{harness.DisplayName} 尚未配置。点击 DeepSeek 键可选择“自动配置”或“连接已有 Harness”。", "{harness.DisplayName} is not configured. Click the DeepSeek key to choose automatic setup or connect an existing Harness."),
        ("请填写已有 {harness.DisplayName} 的控制地址；官方默认 Web 地址为 http://127.0.0.1:3080。若使用自定义端口，请填写实际端口。", "Enter the control address for the existing {harness.DisplayName}. The official default Web address is http://127.0.0.1:3080; enter the actual port when it was customized."),
        ("{harness.DisplayName} 尚未配置；再次点击 DeepSeek 键即可继续。", "{harness.DisplayName} is not configured; click the DeepSeek key again to continue."),
        ("{harness.DisplayName} 尚未配置：{exception.Message}", "{harness.DisplayName} is not configured: {exception.Message}"),
        ("{harness.DisplayName} 已完成程序托管配置。", "Program-managed setup for {harness.DisplayName} is complete."),
        ("请重启 Windows，之后再次点击 DeepSeek 键继续配置。", "Restart Windows, then click the DeepSeek key again to continue setup."),
        ("网页桥接未连接", "Browser bridge disconnected"),
        ("没有运行中的会话", "No sessions are running"),
        ("语音采集、识别、模型地址和密钥均在此小键盘配置；DeepSeek 只接收文字。", "Voice capture, recognition, model endpoints, and credentials are configured on this keypad; DeepSeek receives text only."),
        ("正在启动小键盘持有的 Qwen 语音服务。", "Starting the keypad-owned Qwen voice service."),
        ("正在检查小键盘持有的 Qwen 语音服务。", "Checking the keypad-owned Qwen voice service."),
        ("小键盘持有的 Qwen 语音服务已就绪。", "The keypad-owned Qwen voice service is ready."),
        ("小键盘持有的 Qwen 语音服务尚未启动。", "The keypad-owned Qwen voice service is not running."),
        ("请先在小键盘中完成语音配置。", "Configure voice in the keypad first."),
        ("Windows 系统语音识别已配置。", "Windows speech recognition is configured."),
        ("远程语音服务已在小键盘中配置。", "The remote voice service is configured in the keypad."),
        ("小键盘语音服务已就绪。", "The keypad voice service is ready."),
        ("正在检查小键盘语音服务。", "Checking the keypad voice service."),
        ("小键盘语音服务错误。", "The keypad voice service has an error."),
        ("小键盘语音服务尚未启动。", "The keypad voice service is not running."),
        ("小键盘语音状态不可用。", "Keypad voice status is unavailable."),
        ("语音服务", "Voice service"),
        ("小键盘正在聆听", "Keypad listening"),
        ("小键盘语音设置", "Keypad voice settings"),
        ("语音已完成", "Voice complete"),
        ("请查看 Harness 窗口", "Check the Harness window"),
        ("语音设置需要处理", "Voice setup needs attention"),
        ("输入区上一控件", "Previous composer control"),
        ("输入区下一控件", "Next composer control"),
        ("执行高亮输入区控件", "Activate highlighted composer control"),
        ("降低推理强度", "Decrease reasoning effort"),
        ("提高推理强度", "Increase reasoning effort"),
        ("切换快捷模型", "Toggle quick model"),
        ("设置或查看 Goal", "Set or view Goal"),
        ("{harness.DisplayName} 输入区旋钮", "{harness.DisplayName} composer encoder"),
        ("滚轮或拖动：只选择当前输入框内可见、可用的控件；其他插件新增的输入区按钮会自动加入。\n短按：执行网页中蓝色高亮的控件。", "Wheel or drag: select only visible, enabled controls in the current composer; controls added there by other plugins join automatically.\nClick: activate the blue-highlighted web control."),
        ("滚轮或拖动：只调节当前模型的推理强度。\n短按：在设置的两个快捷模型间切换。", "Wheel or drag: adjust only the current model's reasoning effort.\nClick: switch between the two configured quick models."),
        ("滚轮或拖动：只选择当前输入框内可见、可用的控件；其他插件新增的输入区按钮会自动加入。\n", "Wheel or drag: select only visible, enabled controls in the current composer; controls added there by other plugins join automatically.\n"),
        ("短按：执行网页中蓝色高亮的控件。", "Click: activate the blue-highlighted web control."),
        ("滚轮或拖动：只调节当前模型的推理强度。\n", "Wheel or drag: adjust only the current model's reasoning effort.\n"),
        ("短按：在设置的两个快捷模型间切换。", "Click: switch between the two configured quick models."),
        ("滚轮/按住左键上下或左右拖动：只调节推理强度。", "Wheel or hold the left button and drag vertically or horizontally: adjust reasoning effort only."),
        ("短按：在快捷模型 A/B 间切换。", "Click: switch between quick models A/B."),
        ("此 Harness 尚未提供额度读取能力。左键切换配置的快捷模型；右键直达此 Agent 的适配器与独立键位设置。", "This Harness does not expose quota data. Left-click switches the configured quick models; right-click opens this Agent's adapter and independent key-map settings."),
        ("此 Harness 尚未提供额度读取能力。左键打开并置前；右键直达此 Agent 的适配器与独立键位设置。", "This Harness does not expose quota data. Left-click opens and focuses it; right-click opens this Agent's adapter and independent key-map settings."),
        ("当前模型：{modelLabel}。左键打开并置前 DeepSeek Harness；右键直达此 Agent 的适配器与独立键位设置。", "Current model: {modelLabel}. Left-click opens and focuses DeepSeek Harness; right-click opens this Agent's adapter and independent key-map settings."),
        ("语音输入需要由麦克风键的按下与松开边沿触发", "Voice input requires the microphone key's press and release edges"),
        ("适配器没有声明语音输入能力", "The adapter does not advertise voice input"),
        ("适配器离线 · 点击 Harness 按钮启动", "Adapter offline · click the Harness button to start"),
        ("适配器已连接 · 点击打开并置前", "Adapter connected · click to open and focus"),
        ("适配器离线 · 点击启动", "Adapter offline · click to start"),
        ("尚未配置 · 点击开始", "Not configured · click to begin"),
        ("对话 / 轨迹", "Conversation / trajectory"),
        ("{harness.DisplayName} CMD 旋钮", "{harness.DisplayName} CMD encoder"),
        ("滚轮或拖动：选择 Harness 原生快捷操作。\n短按：直接执行当前操作；不模拟键盘或鼠标。", "Wheel or drag: select a native Harness quick action.\nClick: run the selected action directly without simulating keyboard or pointer input."),
        ("滚轮或拖动：选择 Harness 原生快捷操作。\n", "Wheel or drag: select a native Harness quick action.\n"),
        ("短按：直接执行当前操作；不模拟键盘或鼠标。", "Click: run the selected action directly without simulating keyboard or pointer input."),
        ("按住说话，松开结束。", "Hold to talk; release to stop."),
        ("语音启动中", "Starting voice input"),
        ("配置语音", "Configure voice"),
        ("正在打开 Harness", "Opening Harness"),
        ("检查服务", "Checking service"),
        ("检查适配器", "Check adapter"),
        ("检测已有 Harness", "Detect existing Harness"),
        ("正在检查已保存地址和官方默认端口 3080。", "Checking the saved address and official default port 3080."),
        ("确认配置方式", "Choose setup method"),
        ("检查 WSL", "Check WSL"),
        ("验证安装载荷", "Verify setup payload"),
        ("准备运行环境", "Prepare runtime"),
        ("安装桥接插件", "Install bridge plugin"),
        ("保存并启动", "Save and start"),
        ("健康检查", "Health check"),
        ("启动服务", "Starting service"),
        ("等待适配器", "Wait for adapter"),
        ("等待插件", "Waiting for plugin"),
        ("请求打开窗口", "Request window open"),
        ("将窗口置前", "Bring window to foreground"),
        ("准备开始", "Preparing"),
        ("检查语音设置", "Check voice settings"),
        ("启动并等待服务", "Start and wait for service"),
        ("打开并连接网页", "Open and connect browser"),
        ("准备识别引擎", "Prepare recognition engine"),
        ("连接语音通道", "Connect voice channel"),
        ("获取麦克风", "Acquire microphone"),
        ("停止采集", "Stop capture"),
        ("等待最终转写", "Wait for final transcript"),
        ("写入输入框", "Write to composer"),
        ("处理中", "Working"),
        ("停在 {step}/{totalSteps}，需要处理：{stage}", "Needs attention at {step}/{totalSteps}: {stage}"),
        ("配置需要处理", "Setup needs attention"),
        ("{result.Step}/8 配置需要处理", "Setup needs attention at {result.Step}/8"),
        ("配置已暂停", "Setup paused"),
        ("8/8 配置完成", "8/8 configured"),
        ("连接已有 Harness", "Connect existing Harness"),
        ("3/8 等待重启", "3/8 waiting for restart"),
        ("短按：切换设置中的两个快捷模型。\n长按：打开官方 Micro 设置。\n右键：直达右下角当前 Agent 的软件设置。", "Click: switch between the two configured quick models.\nHold: open official Micro settings.\nRight-click: open the current lower-right Agent's software settings."),
        ("已通过直连插件协议打开 {harness.DisplayName}，并将专用网页窗口置前。\n{dispatch.Message}", "Opened {harness.DisplayName} through the direct plugin protocol and brought its dedicated web window to the foreground.\n{dispatch.Message}"),
        ("{harness.DisplayName} 停在第 {stoppedAt}/7 步；请查看任务栏中的窗口。\n{dispatch.Message}", "{harness.DisplayName} stopped at step {stoppedAt}/7; check its taskbar window.\n{dispatch.Message}"),
        ("{harness.DisplayName} 停在第 {stoppedAt}/7 步，需要处理。\n{dispatch.Message}", "{harness.DisplayName} stopped at step {stoppedAt}/7 and needs attention.\n{dispatch.Message}"),
        ("{harness.DisplayName} 已收到打开请求，但浏览器拒绝或隐藏了前台切换；请从任务栏打开。\n{dispatch.Message}", "{harness.DisplayName} received the open request, but the browser blocked or hid foreground activation; open it from the taskbar.\n{dispatch.Message}"),
        ("圆环显示当前更紧张的额度窗口。短按为当前任务的下一轮切换 {quickModelPair}；长按打开官方 Micro 设置；右键直达右下角当前 Agent 的软件设置。", "The ring shows the tighter quota window. Click switches {quickModelPair} for this task's next turn; hold opens official Micro settings; right-click opens the current lower-right Agent's software settings."),
        ("短按：为当前任务的下一轮切换 {quickModelPair} · 长按：打开官方 Micro 设置 · 右键：直达右下角当前 Agent 的软件设置。", "Click: switch {quickModelPair} for this task's next turn · Hold: open official Micro settings · Right-click: open the current lower-right Agent's software settings."),
        ("此 Harness 尚未提供额度读取能力。单击或右键可直达此 Agent 的适配器与独立键位设置。", "This Harness does not expose quota data. Click or right-click to open this Agent's adapter and independent key-map settings."),
        ("首次使用 {harness.DisplayName}：请确认启动方式，并配置此 Agent 独立的常用按键。", "First use of {harness.DisplayName}: confirm how it starts and configure this Agent's independent common keys."),
        ("使用已有 DSH", "USE EXISTING DSH"),
        ("{harness.DisplayName} 尚未配置。点击 DeepSeek 键可选择“在专用 WSL 中安装 DSH”或“使用我已有的 DSH”。", "{harness.DisplayName} is not configured. Click the DeepSeek key to install DSH in dedicated WSL or use your existing DSH."),
        ("{harness.DisplayName} 原生窗口置前失败：{exception.Message}", "Native foreground activation failed for {harness.DisplayName}: {exception.Message}"),
        ("允许一次 / 批准计划", "Allow once / approve plan"),
        ("拒绝 / 否决计划", "Reject / decline plan"),
        ("归档当前会话", "Archive current session"),
        ("加载更早历史", "Load older history"),
        ("切换侧边栏", "Toggle sidebar"),
        ("打开详情栏", "Open details"),
        ("关闭详情栏", "Close details"),
        ("正在打开 Codex", "Opening Codex"),
        ("正在打开网页", "Opening web page"),
        ("正在打开会话", "Opening session"),
        ("请从任务栏打开", "Open from taskbar"),
        ("未找到窗口", "Window not found"),
        ("首次配置", "First-time setup"),
        ("等待就绪", "Waiting until ready"),
        ("启动中", "Starting"),
        ("连接中", "Connecting"),
        ("正在置前", "Bringing to foreground"),
        ("已置前", "In foreground"),
        ("需要处理", "Needs attention"),
        ("已选择 {harness.DisplayName} 槽位 {selectedSlot + 1}；", "Selected {harness.DisplayName} slot {selectedSlot + 1}; "),
        ("再次单击可打开。此行为跟随 Agent 键双击设置。", "click again to open it. This follows the Agent-key double-tap setting."),
        ("已通过直连插件协议打开或聚焦 {harness.DisplayName}。\n{dispatch.Message}", "Opened or focused {harness.DisplayName} through the direct plugin protocol.\n{dispatch.Message}"),
        ("{harness.DisplayName} · {label} 已通过直连插件执行。\n{result.Message}", "{harness.DisplayName} · {label} completed through the direct plugin.\n{result.Message}"),
        ("{harness.DisplayName} · {label} 未执行。\n{result.Message}", "{harness.DisplayName} · {label} was not executed.\n{result.Message}"),
        ("{label} 在当前 Harness 中不可用", "{label} is unavailable for the current Harness"),
        ("为避免串台，本次不会降级发送 Codex HID。", "To prevent cross-Harness input, this action will not fall back to Codex HID."),
        ("此键在当前 Harness 的独立键位设置中未分配", "this control is unassigned in the current Harness key map"),
        ("适配器当前没有可切换的会话", "the adapter currently has no session to switch to"),
        ("当前没有已选择的 Harness 会话", "there is no selected Harness session"),
        ("未知 Harness 动作 {actionId}", "unknown Harness action {actionId}"),
        ("打开已选会话", "Open selected session"),
        ("打开 / 聚焦 Harness", "Open / focus Harness"),
        ("{controlId} · {harness.DisplayName} 独立键位：{actionLabel}。", "{controlId} · {harness.DisplayName} key map: {actionLabel}. "),
        ("通过直连适配器执行；不会发送 Codex HID。", "Runs through the direct adapter; no Codex HID is sent."),
        ("当前未分配、缺少会话，或适配器未声明此能力。", "Currently unassigned, missing a session, or not declared by the adapter."),
        ("通过直连适配器执行。", "Runs through the direct adapter."),
        ("。在软件设置中按 Harness 独立配置；不会发送 Codex HID。", ". Configure each Harness independently in Software settings; no Codex HID is sent."),
        ("ACT06 · 通过 {harness.DisplayName} 插件调用 session/new；不会发送 Codex HID。", "ACT06 · Call session/new through the {harness.DisplayName} plugin; no Codex HID is sent."),
        ("批准 · 暂不可用", "Approve · unavailable"),
        ("ACT07 · 当前适配器没有稳定的待确认交互接口；此键已禁用。不会发送 Codex HID。", "ACT07 · The adapter has no stable pending-interaction API; this key is disabled and sends no Codex HID."),
        ("拒绝 · 暂不可用", "Reject · unavailable"),
        ("ACT08 · 当前适配器没有稳定的待确认交互接口；此键已禁用。不会发送 Codex HID。", "ACT08 · The adapter has no stable pending-interaction API; this key is disabled and sends no Codex HID."),
        ("ACT09 · 通过 {harness.DisplayName} 插件 Fork 当前会话并打开子会话。", "ACT09 · Fork the current session through the {harness.DisplayName} plugin and open the child."),
        ("语音输入 · 暂不可用", "Voice input · unavailable"),
        ("ACT10 / ACT11 · 当前 Harness 没有声明语音能力；此键已禁用。不会发送 Codex HID。", "ACT10 / ACT11 · The current Harness exposes no voice capability; this key is disabled and sends no Codex HID."),
        ("语音输入 1 · 暂不可用", "Voice input 1 · unavailable"),
        ("语音输入 2 · 暂不可用", "Voice input 2 · unavailable"),
        ("当前 Harness 没有声明语音能力。", "The current Harness exposes no voice capability."),
        ("摇杆上 · {harness.DisplayName} session/new", "Joystick up · {harness.DisplayName} session/new"),
        ("摇杆下 · {harness.DisplayName} turn/cancel", "Joystick down · {harness.DisplayName} turn/cancel"),
        ("摇杆左 · 打开上一个 {harness.DisplayName} 会话", "Joystick left · open the previous {harness.DisplayName} session"),
        ("摇杆右 · 打开下一个 {harness.DisplayName} 会话", "Joystick right · open the next {harness.DisplayName} session"),
        ("{harness.DisplayName} 快捷操作", "{harness.DisplayName} shortcuts"),
        ("上：新建会话；下：停止生成；左/右：切换最近会话。不会发送 Codex HID。", "Up: new session; down: stop generation; left/right: switch recent sessions. No Codex HID is sent."),
        ("适配器尚未提供稳定的待确认交互接口", "the adapter does not yet expose a stable pending-interaction API"),
        ("适配器尚未提供语音能力", "the adapter does not yet expose voice capability"),
        ("适配器没有声明此能力", "the adapter did not declare this capability"),
        ("当前没有可操作的会话", "there is no current session to operate on"),
        ("此实体键尚未映射到 Harness 能力", "this physical key has no Harness capability mapping"),
        ("此方向没有 Harness 映射", "this direction has no Harness mapping"),
        ("正在执行 {label}", "Executing {label}"),
        ("停止当前生成", "Stop current generation"),
        ("新建会话", "New session"),
        ("上一个会话", "Previous session"),
        ("下一个会话", "Next session"),
        ("语音输入", "Voice input"),
        ("批准", "Approve"),
        ("拒绝", "Reject"),
        ("正在激活 {harness.DisplayName}", "Activating {harness.DisplayName}"),
        ("已通过直连插件协议激活 {harness.DisplayName}。\n{dispatch.Message}", "Activated {harness.DisplayName} through the direct plugin protocol.\n{dispatch.Message}"),
        ("{harness.DisplayName} 插件尚未响应。\n{dispatch.Message}", "The {harness.DisplayName} plugin has not responded.\n{dispatch.Message}"),
        ("管理 Agent / Harness…", "Manage Agent / Harness…"),
        ("Codex 键现已指向 {harness.DisplayName}。右键 Codex 键可随时切换。", "The Codex key now targets {harness.DisplayName}. Right-click the Codex key to switch at any time."),
        ("ACT12 · 当前目标：{harness.DisplayName}。左键激活；右键切换 Agent / Harness。", "ACT12 · current target: {harness.DisplayName}. Left-click activates it; right-click switches Agent / Harness."),
        ("当前目标：{harness.DisplayName}", "Current target: {harness.DisplayName}"),
        ("已通过直连插件协议激活 ", "Activated "),
        ("正在激活 ", "Activating "),
        (" 插件尚未响应。", " plugin has not responded."),
        ("Codex 键现已指向 ", "The Codex key now targets "),
        ("。右键 Codex 键可随时切换。", ". Right-click the Codex key to switch at any time."),
        ("当前目标：", "Current target: "),
        ("。左键激活；右键切换 Agent / Harness。", ". Left-click activates it; right-click switches Agent / Harness."),
        ("打开完整设置页", "Open the full settings page"),
        ("原生 Micro HID 与任务路由", "Native Micro HID and task routing"),
        ("直连插件 · 不模拟键鼠", "Direct plugin · no simulated input"),
        ("已注册的直连 Harness 适配器", "Registered direct Harness adapter"),
        ("圆环显示当前更紧张的额度窗口。短按为当前任务的下一轮切换 {quickModelPair}；长按打开官方 Micro 设置；右键打开设置菜单。", "The ring shows the tighter window. Click switches {quickModelPair} for this task's next turn; hold opens official Micro settings; right-click opens the settings menu."),
        ("短按：为当前任务的下一轮切换 {quickModelPair} · 长按：打开官方 Micro 设置 · 右键：打开设置菜单。", "Click: switch {quickModelPair} for this task's next turn · Hold: open official Micro settings · Right-click: open the settings menu."),
        ("快捷模型切换超时；模型保持不变，请再试一次。", "The quick-model switch timed out; the model was not changed. Try again."),
        ("快捷模型切换失败；模型保持不变，请再试一次。", "The quick-model switch failed; the model was not changed. Try again."),
        ("当前账户的模型菜单中没有找到 Terra；模型保持不变。", "Terra was not found in this account's model menu; the model was not changed."),
        ("短按：切换设置中的两个快捷模型。", "Click: switch between the two configured quick models."),
        ("长按：打开官方 Micro 设置。", "Hold: open official Micro settings."),
        ("右键：打开设置菜单。", "Right-click: open the settings menu."),
        ("{quickModelPair} 快速切换已就绪", "{quickModelPair} quick switch ready"),
        ("正在切换 {quickModelPair}", "Switching {quickModelPair}"),
        ("Codex Micro 官方设置", "Official Codex Micro settings"),
        ("快捷模型切换失败", "Quick-model switch failed"),
        ("快捷模型切换超时", "Quick-model switch timed out"),
        ("快捷模型切换", "Quick-model switch"),
        ("快捷模型", "quick model"),
        ("软件设置", "Software settings"),
        ("圆环显示当前更紧张的额度窗口。短按为当前任务的下一轮切换 Sol / Luna；长按打开 Micro 设置；右键重新连接虚拟 HID。", "The ring shows the tighter window. Click toggles Sol/Luna for this task's next turn; hold opens Micro settings; right-click reconnects the virtual HID."),
        ("短按：为当前任务的下一轮切换 Sol / Luna · 长按：打开 Micro 设置 · 右键：重新连接虚拟 HID。", "Click: toggle Sol/Luna for this task's next turn · Hold: open Micro settings · Right-click: reconnect the virtual HID."),
        ("短按：在 Sol 与 Luna 间切换当前任务的下一轮。", "Click: toggle Sol/Luna for this task's next turn."),
        ("没有找到当前任务的模型按钮；请先打开一个可输入的 Codex 任务。", "The current task's model button was not found; open a Codex task with an available composer first."),
        ("无法打开 Codex 的 Advanced 模型菜单；模型保持不变。", "Could not open Codex's Advanced model menu; the model was not changed."),
        ("当前账户的模型菜单中没有找到 Sol；模型保持不变。", "Sol was not found in this account's model menu; the model was not changed."),
        ("当前账户的模型菜单中没有找到 Luna；模型保持不变。", "Luna was not found in this account's model menu; the model was not changed."),
        ("模型选择已发送，但无法确认结果；请查看 Codex 输入框下方的模型按钮。", "The model selection was sent but could not be verified; check the model button below the Codex composer."),
        ("当前任务的下一轮已切换到 ", "This task's next turn now uses "),
        ("；全局默认模型未更改。", "; the global default model was not changed."),
        ("Sol / Luna 切换超时；模型保持不变，请再试一次。", "The Sol/Luna switch timed out; the model was not changed. Try again."),
        ("Sol / Luna 切换失败；模型保持不变，请再试一次。", "The Sol/Luna switch failed; the model was not changed. Try again."),
        ("没有找到 Codex 主窗口；模型保持不变。", "The Codex main window was not found; the model was not changed."),
        ("Sol/Luna 快速切换已就绪", "Sol/Luna quick switch ready"),
        ("Sol / Luna 快速切换已就绪", "Sol/Luna quick switch ready"),
        ("Sol / Luna 快速切换", "Sol/Luna quick switch"),
        ("Sol / Luna 切换失败", "Sol/Luna switch failed"),
        ("Sol / Luna 切换超时", "Sol/Luna switch timed out"),
        ("正在切换 Sol / Luna", "Switching between Sol and Luna"),
        ("已切换到 ", "Switched to "),
        ("当前模型 ", "Current model "),
        ("长按：打开 Micro 设置。", "Hold: open Micro settings."),
        ("暂时无法读取额度。当前保持未知状态，不会误显示为 0%；面板显示时会自动重试。", "Quota is temporarily unavailable. It remains unknown rather than being shown as 0%, and will retry while the panel is visible."),
        ("最近一次刷新失败，当前显示上次成功读取的结果。", "The latest refresh failed; showing the last successful reading."),
        ("圆环显示当前更紧张的额度窗口。左键打开 Micro 设置；右键重新连接虚拟 HID。", "The ring shows the tighter window. Left-click opens Micro settings; right-click reconnects the virtual HID."),
        ("左键：打开 Micro 设置 · 右键：重新连接虚拟 HID。", "Left-click: open Micro settings · Right-click: reconnect the virtual HID."),
        ("正在读取 Codex 剩余额度。", "Reading the remaining Codex quota."),
        ("Codex 剩余额度", "Codex quota"),
        ("额度暂不可用", "Quota unavailable"),
        ("剩余额度", "quota remaining"),
        ("周额度", "weekly limit"),
        ("天额度", "day limit"),
        ("小时额度", "hour limit"),
        ("分钟额度", "minute limit"),
        ("更新于", "Updated"),
        ("重置", "resets"),
        ("剩余", "left"),
        ("正在转写", "Transcribing"),
        ("转写完成", "Transcription complete"),
        ("语音失败", "Voice failed"),
        ("适配器没有声明语音输入能力", "The adapter does not advertise voice input"),
        ("语音启动中", "Starting voice input"),
        ("正在聆听", "Listening"),
        ("语音输入需要由麦克风键的按下与松开边沿触发", "Voice input requires the microphone key's press and release edges"),
        ("按住说话", "Push to talk"),
        ("已向当前 Plan 提问卡片发送一次 Escape；未追加 AG00。", "Sent Escape once to the current Plan question card; AG00 was not appended."),
        ("检测到疑似或不唯一的 Plan 提问卡片；本次操作已消费，未发送 AG00。", "A possible or ambiguous Plan question card was detected; the action was consumed without sending AG00."),
        ("Plan 提问卡片取消失败；本次操作已消费，未发送 AG00。", "Could not cancel the Plan question card; the action was consumed without sending AG00."),
        ("驱动已就绪，但尚未确认 Codex 已连接；Fast、语音和旋钮可能不会生效", "The driver is ready, but Codex has not confirmed its connection; Fast, voice, and the encoder may not work"),
        ("黄灯表示 Codex 尚未回传连接信号；此时驱动接受按键，不代表 Codex 已处理。", "The yellow light means Codex has not returned a connection signal. Driver acceptance does not mean Codex handled the input."),
        ("输入只走 Micro HID，不会降级到 UIA。", "Input uses Micro HID only and never falls back to UIA."),
        ("虚拟 HID 链路已停止；右键左下黑色旋钮重新连接。", "The virtual HID link stopped. Right-click the lower-left black knob to reconnect."),
        ("点击黑色设置旋钮打开设置；Codex 键会将 Codex 切到前台。", "Click the black settings knob to open settings; the Codex key brings Codex to the foreground."),
        ("ACT12 · Codex 正在前台。纸飞机角标表示此键会发送当前输入；右键可切换 Agent / Harness。", "ACT12 · Codex is in the foreground. The paper-plane badge means this key sends the current input. Right-click switches Agent / Harness."),
        ("ACT12 · 将 Codex 置于前台。Codex 位于前台后会出现纸飞机角标，此键随即用于发送当前输入；右键可切换 Agent / Harness。", "ACT12 · Brings Codex to the foreground. Once Codex is in front, a paper-plane badge appears and this key sends the current input. Right-click switches Agent / Harness."),
        ("ACT12 · {displayName} 正在前台。纸飞机角标表示此键会发送当前输入；右键可切换 Agent / Harness。", "ACT12 · {displayName} is in the foreground. The paper-plane badge means this key sends the current input. Right-click switches Agent / Harness."),
        ("ACT12 · 将 {displayName} 置于前台。置前后会出现纸飞机角标，此键随即用于发送当前输入；右键可切换 Agent / Harness。", "ACT12 · Brings {displayName} to the foreground. Once it is in front, a paper-plane badge appears and this key sends the current input. Right-click switches Agent / Harness."),
        ("ACT12 · 打开或置前 {displayName}。当前适配器尚未声明直接发送能力。", "ACT12 · Opens or focuses {displayName}. Its adapter has not advertised direct composer submit."),
        ("{displayName} 正在前台；发送键", "{displayName} is in the foreground; send key"),
        ("{displayName} 不在前台；置前键", "{displayName} is not in the foreground; activate key"),
        ("{displayName} 直接发送不可用；置前键", "{displayName} direct send unavailable; activate key"),
        ("发送当前输入", "Send current input"),
        ("Codex 正在前台；发送键", "Codex is in the foreground; send key"),
        ("Codex 不在前台；置前键", "Codex is not in the foreground; activate key"),
        ("Codex Micro 设置已打开，并已将 Codex 主窗口切到前台。", "Codex Micro settings opened and the main Codex window was brought to the foreground."),
        ("事件已交付，但当前没有可激活的 Codex 主窗口。", "The event was delivered, but no Codex main window is available to activate."),
        ("虚拟 HID 链路尚未就绪。", "The virtual HID link is not ready."),
        ("右键左下黑色旋钮重新连接。", "Right-click the lower-left black knob to reconnect."),
        ("右键左下旋钮可重新连接。", "Right-click the lower-left knob to reconnect."),
        ("窗口已置顶。右击机身空白处可取消置顶。", "The window is always on top. Right-click empty body space to turn it off."),
        ("窗口已取消置顶。右击机身空白处可再次置顶。", "Always on top is off. Right-click empty body space to enable it again."),
        ("滚轮/按住左键上下或左右拖动：调节推理强度或移动菜单选项。", "Use the wheel or drag vertically/horizontally with the left button to adjust reasoning effort or move through menu items."),
        ("滚轮/按住左键上下或左右拖动：移动输入区控件或菜单选项。", "Use the wheel or drag vertically/horizontally with the left button to move through composer controls or menu items."),
        ("全局旋钮捕获未启动；仍可按住左键上下或左右拖动选择。", "Global encoder capture did not start; hold the left button and drag vertically or horizontally to select."),
        ("拖动黑色圆帽进行连续输入，或单击四周阴刻方向键。", "Drag the black cap for continuous input, or click one of the engraved direction controls."),
        ("左键：长按 ENC 并打开 Micro 设置。", "Left-click: hold ENC and open Micro settings."),
        ("右键：重新连接虚拟 HID。", "Right-click: reconnect the virtual HID."),
        ("项目与标题来自 Codex 本地最近任务索引。", "Project and title come from Codex's local recent-task index."),
        ("键帽图标随 Codex Micro 设置同步。", "Keycap icons follow the Codex Micro settings."),
        ("拖动机身移动 · 右击机身打开窗口菜单 · 关闭时收起到托盘", "Drag the body to move · Right-click for the window menu · Close to hide in the tray"),
        ("按住并向任意方向拖动，松开后自动回中。", "Hold and drag in any direction; release to return to center."),
        ("单击触发并自动回中。", "Click to trigger and automatically return to center."),
        ("按住说话，松开结束。", "Hold to talk; release to stop."),
        ("短按：打开或确认。", "Click: open or confirm."),
        ("正在连接共享 Micro Broker 与 Codex Micro HID。", "Connecting to the shared Micro Broker and Codex Micro HID."),
        ("正在检查 Codex 与虚拟 HID。", "Checking Codex and the virtual HID."),
        ("正在探测 Codex 运行时能力", "Detecting Codex runtime capabilities"),
        ("正在等待 Codex 运行时能力信号。", "Waiting for the Codex runtime capability signal."),
        ("正在等待驱动连接。", "Waiting for the driver connection."),
        ("正在查找 Codex Micro HID", "Looking for Codex Micro HID"),
        ("Codex 运行时握手已确认 · 无版本白名单", "Codex runtime handshake confirmed · no version allowlist"),
        (" 与 Codex 运行时握手已完成。", " and the Codex runtime handshake is complete."),
        ("Codex 运行时握手已完成。", "Codex runtime handshake completed."),
        ("Codex Micro 虚拟 HID 尚未出现。", "The Codex Micro virtual HID is not present yet."),
        ("虚拟 HID 连接失败：", "Virtual HID connection failed: "),
        ("虚拟 HID 链路故障", "Virtual HID link failure"),
        ("虚拟 HID 未连接", "Virtual HID disconnected"),
        ("虚拟 HID 尚未连接", "Virtual HID is not connected"),
        ("重新连接虚拟 HID", "Reconnect virtual HID"),
        ("等待 Codex 识别 Micro HID", "Waiting for Codex to recognize Micro HID"),
        ("HID / RPC 已就绪", "HID / RPC ready"),
        ("无事件链路", "No event link"),
        ("Broker 已停止", "Broker stopped"),
        ("VHF 事件已继续交付；", "VHF event delivery continued; "),
        ("Agent 状态已同步", "Agent state synchronized"),
        ("个亮灯槽位", " lit slots"),
        ("当前会话 · 无对应状态", "Current session · no matching state"),
        ("当前会话", "Current session"),
        ("白光选择提示", "white selection highlight"),
        ("显示亮度", "display brightness"),
        ("Agent 槽位", "Agent slot"),
        ("未分配", "Unassigned"),
        ("思考中", "Thinking"),
        ("已完成", "Completed"),
        ("等待输入", "Waiting for input"),
        ("已点亮", "Lit"),
        ("空闲", "Idle"),
        ("错误", "Error"),
        ("Codex 运行时握手", "Codex runtime handshake"),
        ("虚拟 HID 驱动", "Virtual HID driver"),
        ("虚拟 HID", "Virtual HID"),
        ("最近事件", "Latest event"),
        ("事件状态", "Event status"),
        ("尚未发送事件。", "No event has been sent."),
        ("未激活窗口滚轮捕获已就绪", "Background-window wheel capture ready"),
        ("旋钮输入捕获失败", "Encoder input capture failed"),
        ("选择旋钮", "Selection encoder"),
        ("推理强度旋钮", "Reasoning-effort encoder"),
        ("模拟摇杆", "Virtual joystick"),
        ("摇杆方向", "Joystick direction"),
        ("摇杆事件未发送", "Joystick event not sent"),
        ("摇杆事件结果未知", "Joystick event result unknown"),
        ("摇杆事件发送失败", "Joystick event send failed"),
        ("摇杆", "Joystick"),
        ("Plan 提问已取消", "Plan question canceled"),
        ("Plan 提问卡片无法安全确认", "Plan question card could not be identified safely"),
        ("Plan 取消未发送", "Plan cancellation not sent"),
        ("滚轮路由已接收", "Wheel route received"),
        ("旋钮按压", "Encoder press"),
        ("旋钮拖动", "Encoder drag"),
        ("旋钮确认", "Encoder confirm"),
        ("旋钮滚轮", "Encoder wheel"),
        ("旋钮合并输入", "Merged encoder input"),
        ("确认框向上选择", "Dialog selection up"),
        ("确认框向下选择", "Dialog selection down"),
        ("向上选择", "Selection up"),
        ("向下选择", "Selection down"),
        ("确认当前对话框选项", "Confirm current dialog option"),
        ("打开或确认当前选项", "Open or confirm current option"),
        ("旋钮动画已跳过", "Encoder animation skipped"),
        ("菜单位置读取已跳过", "Menu-position read skipped"),
        ("打开 Codex Micro 设置", "Open Codex Micro settings"),
        ("Codex Micro 设置已打开", "Codex Micro settings opened"),
        ("Codex Micro 设置", "Codex Micro settings"),
        ("Codex 主窗口激活失败", "Could not activate the Codex main window"),
        ("未找到 Codex 主窗口", "Codex main window not found"),
        ("正在发送", "Sending"),
        ("事件发送失败", "Event send failed"),
        ("效果未知；为避免双执行不会自动重试。", "effect unknown; automatic retry is disabled to avoid duplicate execution."),
        (" 已通过 ", " was delivered through "),
        (" 交付。", "."),
        ("已连接", "connected"),
        ("已交付", "delivered"),
        ("结果未知", "result unknown"),
        ("未发送", "not sent"),
        ("失败，但模拟器仍在运行：", " failed, but the simulator is still running: "),
        ("失败", "failed"),
        ("窗口置顶", "Always on top"),
        ("Codex Micro 小键盘", "Codex Micro keypad"),
        ("隐藏面板", "Hide panel"),
        ("确认权限", "Confirm access"),
        ("权限模式", "Access mode"),
        ("转动旋钮选择", "Turn the encoder to select"),
        ("转动选择", "Turn to select"),
        ("单击切换到该槽位；颜色由 Codex 状态同步。", "Click to switch to this slot; color follows Codex state."),
        ("正在打开 {session.DisplayTitle}", "Opening {session.DisplayTitle}"),
        ("已通过 {harness.DisplayName} 直连插件打开会话：{session.DisplayTitle}。", "Opened session {session.DisplayTitle} through the {harness.DisplayName} direct adapter."),
        ("{ActiveHarness().DisplayName} 尚未返回可选择的会话；按下旋钮可打开 Harness。", "{ActiveHarness().DisplayName} has not returned selectable sessions; press the encoder to open the Harness."),
        ("{harness.DisplayName} · 槽位 {slotId + 1}", "{harness.DisplayName} · slot {slotId + 1}"),
        ("适配器离线或尚未返回状态；单击可打开 Harness。", "The adapter is offline or has not returned state; click to open the Harness."),
        ("当前 Harness 没有为此槽位返回会话。", "The current Harness returned no session for this slot."),
        ("当前位于 {harness.DisplayName} 选择菜单第 {depth} 层 · 单击返回上一级。", "Currently at level {depth} of the {harness.DisplayName} selection menu · click to go back one level."),
        ("选择菜单打开期间暂时锁定；请先使用红色返回键。", "Temporarily locked while a selection menu is open; use the red Back key first."),
        ("{harness.DisplayName} 当前位于选择菜单中；", "{harness.DisplayName} is currently in a selection menu; "),
        ("AG00 已临时变为返回键，其他 Agent 键已锁定。", "AG00 is temporarily the Back key; the other Agent keys are locked."),
        ("请先返回上一级", "Return one level first"),
        ("返回上一级", "Back one level"),
        ("运行中", "Running"),
        ("可打开", "Ready to open"),
        ("{harness.DisplayName} 会话 · AG{slotId:00} · ", "{harness.DisplayName} session · AG{slotId:00} · "),
        (")} · 单击通过插件直连打开。\n", ")} · Click to open through the direct adapter.\n"),
        ("{harness.DisplayName} 会话旋钮", "{harness.DisplayName} session encoder"),
        ("滚轮或拖动：在此 Harness 返回的六个最近会话间选择。\n", "Wheel or drag: select among the six recent sessions returned by this Harness.\n"),
        ("短按：通过直连插件打开选中的会话；不会发送 Codex HID。", "Press: open the selected session through the direct adapter; no Codex HID input is sent."),
        ("不会发送 Codex HID。", "No Codex HID input is sent."),
        ("{harness.DisplayName} · {sessionCount} 个会话", "{harness.DisplayName} · {sessionCount} sessions"),
        ("此 Harness 尚未提供额度读取能力。单击打开其适配器设置。", "This Harness does not expose quota data. Click to open its adapter settings."),
        ("单击切换。", "Click to switch."),
        ("单击执行。", "Click to run."),
        ("已停止 / 按需启动", "stopped / on demand"),
        ("正在加载模型", "loading model"),
        ("Harness 适配器离线", "Harness adapter offline"),
        ("网页桥接未连接", "Browser bridge disconnected"),
        ("没有运行中的会话", "No sessions are running"),
        ("{harness.DisplayName} 适配器已连接", "{harness.DisplayName} adapter connected"),
        ("{runningCount} 个 Harness 会话正在运行", "{runningCount} Harness sessions running"),
        ("Harness 已连接 · 当前没有运行中的会话", "Harness connected · no sessions are running"),
        ("Harness 适配器", "Harness adapter"),
        ("网页桥接", "Browser bridge"),
        ("语音配置", "Voice setup"),
        ("本地 ASR", "Local ASR"),
        ("— 未连接", "— disconnected"),
        ("— 需要配置", "— required"),
        ("已就绪", "ready"),
        ("未配置", "not configured"),
        ("异常", "error"),
        ("等待", "Waiting"),
        ("小键盘 1", "Keypad 1"),
        ("已新增 {title} 小键盘；当前小键盘未改变。", "Added a new {title} keypad; this keypad is unchanged."),
        ("新增 {title} 小键盘；当前小键盘保持不变", "Add a new {title} keypad; keep this keypad unchanged"),
        ("新增 {title} 小键盘（当前小键盘不变）", "Add a new {title} keypad without changing this keypad"),
        ("关闭此小键盘", "Close this keypad"),
        ("移除此窗口及其独立小键盘配置", "Remove this window and its independent keypad profile"),
        ("设置", "Settings"),
    ];

    internal static string Translate(string value)
    {
        var translated = value;
        foreach (var (chinese, english) in Replacements)
        {
            translated = translated.Replace(
                chinese,
                english,
                StringComparison.Ordinal);
        }

        return translated;
    }
}
