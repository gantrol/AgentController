# Codex Micro Monitor 0.2.9 / Codex Micro 监控器 0.2.9

## English

Download and extract:

`Codex-Micro-Monitor-v0.2.9-win-x64.zip`

Run `CodexMicro.exe`. This is the general Codex-focused package; it does not
include the DeepSeek Harness Bridge or managed WSL setup. Windows requires the
.NET 10 Desktop Runtime x64.

### Highlights

- Quick model switching is now bound to the Codex task that was visible when
  the control was pressed.
- The monitor follows authoritative per-task model state and safely retries a
  fixed target during temporary owner, connection, or timeout transitions.
- During model switching, temporary conditions use an amber status; permanent
  protocol rejection is reserved for red status.
- Quick model A and B can each select a validated target reasoning effort.
- Follower, stale snapshot, reconnect, and delayed-result races are fixed.
- Production binaries contain no model-toggle diagnostic logging code.

## 中文

下载并解压：

`Codex-Micro-Monitor-v0.2.9-win-x64.zip`

运行 `CodexMicro.exe`。这是面向 Codex 的通用监控器包，不包含 DeepSeek Harness
Bridge 或托管 WSL 安装流程。Windows 需要安装 .NET 10 Desktop Runtime x64。

### 主要变化

- 快速模型切换严格绑定按下控件时可见的 Codex 任务。
- Monitor 跟随每个任务的权威模型状态，并在 owner、连接或超时的短暂切换期间安全地
  重试同一个固定目标。
- 模型切换期间，瞬态问题显示黄灯；红灯仅用于永久协议拒绝。
- 快捷模型 A/B 可分别选择并校验目标思考强度。
- 修复 follower、旧快照、重连和延迟结果竞态。
- 生产二进制不包含模型切换诊断日志代码。
