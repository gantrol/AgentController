# Codex Micro Monitor 0.3.0 / Codex Micro 监控器 0.3.0

## English

Download and extract:

`Codex-Micro-Monitor-v0.3.0-win-x64.zip`

Run `CodexMicro.exe`. Windows requires the .NET 10 Desktop Runtime x64.

### Emergency fix: model switching in new tasks

- A blank Codex new-task draft can now switch the configured quick model A/B
  before its first turn creates an App Server thread.
- The draft path is restricted to the visible model picker in the foreground
  Codex window and selects the exact target model and configured effort. It is
  enabled only after initialized IPC reports no visible real task for a stable
  window.
- Portal-based model and effort submenus are supported through same-process
  popup roots bound geometrically to the same editor and model trigger.
- Once a real task exists, switching continues to use the authoritative
  per-task owner/follower IPC path; a real-task IPC failure never falls back to
  UI automation.
- Any real-task visibility event, reconnection, foreground change, or editor
  replacement invalidates the draft operation immediately.
- A delayed draft result cannot overwrite a newly-created real task's state.
- The official blank-draft picker may also update Codex's default model for
  later new tasks. Switching inside a real task still leaves the global
  default unchanged.
- Production binaries contain no model-toggle diagnostic logging code.

### Virtual HID driver

- This release updates only Codex Micro Monitor. The virtual HID driver is
  unchanged and is not bundled in the application ZIP.
- If the existing driver installation completed with `Ready`, do not reinstall
  it. If it is missing or did not reach `Ready`, install it separately by
  following the [virtual HID driver guide](https://github.com/gantrol/AgentController/blob/codex-micro-monitor-v0.3.0/virtual-micro/UNSIGNED-DRIVER.md).

## 中文

下载并解压：

`Codex-Micro-Monitor-v0.3.0-win-x64.zip`

运行 `CodexMicro.exe`。Windows 需要安装 .NET 10 Desktop Runtime x64。

### 紧急修复：新会话模型切换

- 空白 Codex 新会话在第一轮创建 App Server thread 之前，现在也能切换设置中的快捷
  模型 A / B。
- 草稿通路严格限定为前台 Codex 窗口内可见的官方模型菜单，并精确选择目标模型与设置
  的思考强度；仅在 IPC 已初始化且稳定确认没有可见真实任务后才会启用。
- Model 与 Effort 的 portal 子菜单通过同进程 popup root 访问，并与同一编辑器和模型
  按钮进行几何绑定。
- 真实任务创建后继续使用权威的 per-task owner/follower IPC；真实任务 IPC 失败时绝不
  降级到 UI 自动化。
- 一旦出现真实任务、IPC 重连、前台窗口变化或编辑器被替换，草稿操作立即失效。
- 延迟返回的草稿结果不会覆盖刚刚创建的真实任务状态。
- 官方空白草稿模型菜单可能同时更新 Codex 以后新建任务的默认模型；真实任务内切换
  仍不会修改全局默认值。
- 生产二进制不包含模型切换诊断日志代码。

### 虚拟 HID 驱动

- 本版本只更新 Codex Micro Monitor；虚拟 HID 驱动没有变更，也不会捆绑进应用 ZIP。
- 已有驱动安装结果为 `Ready` 时不要重装。尚未安装或未达到 `Ready` 时，请按
  [虚拟 HID 驱动安装指南](https://github.com/gantrol/AgentController/blob/codex-micro-monitor-v0.3.0/virtual-micro/UNSIGNED-DRIVER.zh-CN.md)
  单独安装。
