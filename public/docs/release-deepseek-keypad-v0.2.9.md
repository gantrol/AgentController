# DeepSeek 键盘 0.2.9

普通用户只需下载并解压：

`Deepseek-Harness-Keypad-v0.2.9-win-x64.zip`

运行其中的 `CodexMicro.exe`。本包包含 Micro Bridge 与自动配置入口；首次配置时会
在线安装并锁定 DeepSeek Harness `v0.1.0-rc.8`。Windows 需安装 .NET 10 Desktop
Runtime x64，托管模式仍需启用 WSL。

## 主要变化

- 修复快速切换 Codex 模型偶发显示旧任务模型、误报红灯或在 owner 切换窗口失败的问题。
- 模型切换严格绑定点击时的当前任务，并复用 Codex 的权威任务状态；任务切换时不会把
  迟到结果写回新任务。
- 对短暂 owner 不可用、连接切换和请求超时执行固定目标的安全重试；瞬态状态显示黄灯，
  只有协议拒绝等永久错误显示红灯。
- 快捷模型 A/B 可分别选择目标思考强度，并按当前 Codex 模型元数据校验可用档位。
- 修复 follower 订阅、旧快照和连接重建竞态；生产包不包含模型切换诊断日志代码。

WSL 系统功能仍由 Windows 提供。安装和升级细节见包内
`DEEPSEEK-WINDOWS-SETUP.zh-CN.md`。

## English

Users only need `Deepseek-Harness-Keypad-v0.2.9-win-x64.zip`. Extract it and
run `CodexMicro.exe`. The online package includes the Micro Bridge and installs
the pinned DeepSeek Harness `v0.1.0-rc.8` during managed first-run setup. It
requires the .NET 10 Desktop Runtime x64 and WSL.

This release makes quick Codex model switching task-scoped and authoritative,
adds safe fixed-target retries during owner/connection transitions, separates
transient amber status from permanent red failures, and lets each quick-model
slot select a validated reasoning effort. It also closes follower, stale
snapshot, and reconnect races. Production packages contain no model-toggle
diagnostic logging code.
