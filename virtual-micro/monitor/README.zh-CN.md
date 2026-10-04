# Codex Micro Monitor

**随时看见你的 Codex 会话状态。**

一个 Codex 插件：在桌面悬浮面板里，看见哪些会话正在运行、已经完成或需要你回应，点击会话即可继续处理。

外观与交互灵感来自 [Codex Micro 实体小键盘](https://learn.chatgpt.com/docs/features/codex-micro)。这是面向 Windows 的第三方项目。

[English](README.md) · [开始使用](#开始使用) · [当前限制](#当前限制)

## 可以做什么

- **关注多个会话：**在小键盘与最多显示 14 个会话的监控页之间切换。
- **找到需要你的会话：**点击会话键打开对应会话，右击已绑定的键可标记未读。
- **调整当前会话：**切换 Fast、Plan、两组快捷模型和推理强度。
- **留在手边：**拖动、置顶，或收起到 Windows 托盘。

## 开始使用

需要 Windows 10（19041+）或 Windows 11 x64、已登录的 Codex / ChatGPT 桌面应用，以及 [Microsoft .NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。

1. 安装插件，当前在仓库的 **Agent Controller** 插件市场中名为 **Codex Micro Keypad**。本地预览版安装方式见下方。
2. 对 Codex 说：**“打开 Codex Micro 小键盘。”**
3. 在面板中选择已有会话，等待状态同步后操作。

<details>
<summary>安装本地插件预览版</summary>

安装 .NET SDK 10.0.302 和 Codex CLI，在仓库根目录运行：

```powershell
.\scripts\package-codex-plugin.ps1 -Install
```

脚本会构建插件、登记本地插件市场并安装 `codex-micro-keypad@agent-controller`。安装后重启 Codex。市场配置详见[官方插件文档](https://developers.openai.com/plugins/build/plugins)。

</details>

<details>
<summary>单独运行桌面程序</summary>

安装同一版本的 .NET SDK，在仓库根目录运行：

```powershell
.\scripts\package-micro.ps1 -Version 0.3.13 -Preset monitor
```

解压 `dist/Codex-Micro-Monitor-v0.3.13-win-x64.zip`，保持 Codex 打开并运行 `CodexMicro.exe`。

[公开 Monitor 发行版](https://github.com/gantrol/AgentController/releases)截至 v0.3.10 仍使用旧的驱动方案；本文对应当前软件控制预览版。

</details>

## 常用操作

| 控件 | 用法 |
| --- | --- |
| 会话键 | 点击打开对应会话；右击可标记未读。 |
| Fast 键 | 切换当前会话的 Fast 模式。 |
| 摇杆向上 | 默认切换当前会话的 Plan 模式。 |
| 白色旋钮 | 设为“仅推理强度”后，旋转调整强度，短按切换快捷模型。 |
| 左下旋钮 | 短按切换快捷模型；右键配置模型 A / B。 |
| CODEX 键 | 唤起 Codex 主窗口。 |

模型与推理强度取决于账号可用选项；请先选择已有会话。

会话键：蓝色表示运行中，绿色表示完成未读，黄色表示等待输入，红色表示错误；当前会话另有选中光效。三颗小灯从上到下表示会话同步、Codex 连接、最近操作结果；最下面的绿灯短暂亮起表示操作已确认。

拖动机身空白处可移动窗口，右键可设置置顶。关闭窗口会收起到托盘；从托盘菜单选择“退出”才会结束程序。托盘菜单也可设置语言和开机自启动。

## 当前限制

- 草稿发送、语音、输入区导航、会话滚动、侧栏/历史导航和技能插入尚未接通，请在 Codex 中完成。CODEX 键当前只负责唤起应用。
- 停止、审批、分叉和打开 Review 已有实现，完整操作流程仍待验证。
- 空白草稿或无法唯一识别的会话可能无法调整设置；先打开一个标题可区分的已有会话。
- 兼容性依赖 Codex 桌面接口，更新后可能受影响。

无法启动时，检查是否安装了 **Desktop Runtime x64**；连接异常时，确认 Codex 已登录并运行，再重启小键盘。日志目录为 `%LOCALAPPDATA%\CodexMicro`：插件使用 `plugin-startup.log`，独立程序使用 `keypad-startup.log`。

## 许可证

[PolyForm Noncommercial 1.0.0](https://github.com/gantrol/AgentController/blob/main/LICENSE)。
