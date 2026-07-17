# Agent Controller

[![README in English](https://img.shields.io/badge/README-English-blue.svg)](README.md)
[![简体中文说明](https://img.shields.io/badge/README-简体中文-red.svg)](README.zh-CN.md)

![version](https://img.shields.io/badge/version-0.4b-blue) ![platform](https://img.shields.io/badge/platform-Windows-lightgrey)

---

用游戏手柄驱动 AI 编程 Agent。Agent Controller 是一款 Windows 桌面应用，把 XInput 手柄（当前以 8BitDo Ultimate 2 实测）映射到 Codex 桌面版：摇杆导航任务、按住 LT 说话、X 一键发送，外加震动反馈与屏幕浮层。

灵感来自 Codex Micro——那块为 Codex 打造的迷你专用键盘。但我觉得，用手柄控制（controller control）要好得多：双摇杆、十字键、震动，而且它本来就长在你手里。

> ⚠️ **安全提示——使用前请读**
>
> 本仓库代码由 **Codex（AI）生成，未经人工审计**。请自担风险使用，建议运行前先阅读源码。应用在你机器上会做的事：
>
> - 向 **Codex 窗口**发送键盘快捷键与 UI Automation 指令（默认仅在 Codex 位于前台、且你按 Menu 解锁后才生效）；
> - **只读**读取 Codex 本机任务数据（`~/.codex`）；
> - 在 `%LOCALAPPDATA%` 写入自身设置，并可向 Codex 快捷键配置追加降级绑定（F17/F18/F20/F22）；
> - 可选注册开机自启（默认关闭）；
> - **不发起任何网络请求**（唯一涉网行为是在浏览器中打开厂商/Codex 链接）。

### 使用要求

- Windows 10（build 19041+）或 Windows 11
- [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)（x64）
- 已安装 Codex 桌面版
- XInput 兼容手柄（实测设备为 8BitDo Ultimate 2；其他 XInput 手柄大多可用，Xbox 与飞智在测试计划中）

### 从 Release 下载安装

1. 到 [Releases](../../releases) 下载最新 zip。
2. 解压到任意目录，运行 `AgentController.exe`。
3. 程序未签名，Windows SmartScreen 可能拦截：点 **更多信息 → 仍要运行**（见上方安全提示；介意可自行构建）。
4. 若缺少 .NET 9 Desktop Runtime，Windows 会提示安装。
5. 手柄以 XInput 模式连接，启动 Codex。设备页显示 `LIVE` 即就绪。

### 默认操作

| 输入 | 动作 |
| --- | --- |
| Menu | 首次使用时唤醒 Codex、置于前台并解锁控制 |
| 左摇杆 ↑↓ | 在本软件的稳定侧边栏滚轮中移动，不自动打开 |
| 左摇杆 ← | 从项目任务返回；Base 层向右不执行动作 |
| L3 | 循环根区域：置顶任务 → 置顶项目 → 项目 → 未归项目 |
| Y | 打开动作面板 |
| 十字键 ↑ / ↓ | 上一条 / 下一条用户消息；按住 ↑ 4 秒置顶，按住 ↓ 3 秒置底 |
| 右摇杆 ←→ | 转动撰写栏虚拟旋钮；选择器打开后遍历选项 |
| R3 单击 / 长按 | 打开或确认旋钮目标 / 打开手柄设置 |
| A | 进入焦点项目或打开焦点任务 |
| LT（按住） | 按住说话（语音输入），松开结束 |
| X | 发送提示词 |
| B | 短按关闭菜单或撤回最近导航；长按 3 秒并经过屏幕倒计时后取消当前会话 |

### 从源码构建

```powershell
dotnet build app/AgentController.csproj -c Release
dotnet test app.Tests/AgentController.Tests.csproj -c Release
```

产物位于 `app/bin/Release/net9.0-windows10.0.19041.0/`，主程序为 `AgentController.exe`。

### 仓库结构

本仓库后续仅提交：

- `app/` —— Windows（WPF）应用，行为以它为准；
- `app.Tests/` —— 手柄、本地化、导航与适配器行为的回归测试；
- `todo.md` —— 路线图与备忘。

### 致谢

手柄插图源自 CREATRBOI 的 "White XBOX Controller" 模型；许可证与署名文件随应用分发（Release 内 `THIRD-PARTY/` 目录）。
