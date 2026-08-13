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
        ("已向当前 Plan 提问卡片发送一次 Escape；未追加 AG00。", "Sent Escape once to the current Plan question card; AG00 was not appended."),
        ("检测到疑似或不唯一的 Plan 提问卡片；本次操作已消费，未发送 AG00。", "A possible or ambiguous Plan question card was detected; the action was consumed without sending AG00."),
        ("Plan 提问卡片取消失败；本次操作已消费，未发送 AG00。", "Could not cancel the Plan question card; the action was consumed without sending AG00."),
        ("驱动已就绪，但尚未确认 Codex 已连接；Fast、语音和旋钮可能不会生效", "The driver is ready, but Codex has not confirmed its connection; Fast, voice, and the encoder may not work"),
        ("黄灯表示 Codex 尚未回传连接信号；此时驱动接受按键，不代表 Codex 已处理。", "The yellow light means Codex has not returned a connection signal. Driver acceptance does not mean Codex handled the input."),
        ("输入只走 Micro HID，不会降级到 UIA。", "Input uses Micro HID only and never falls back to UIA."),
        ("虚拟 HID 链路已停止；右键左下黑色旋钮重新连接。", "The virtual HID link stopped. Right-click the lower-left black knob to reconnect."),
        ("点击黑色设置旋钮打开设置；Codex 键会将 Codex 切到前台。", "Click the black settings knob to open settings; the Codex key brings Codex to the foreground."),
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
        ("单击切换。", "Click to switch."),
        ("单击执行。", "Click to run."),
        ("等待", "Waiting"),
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
