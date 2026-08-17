# Deepseek Harness Keypad v0.2.5（简体中文）

这是 Agent 状态灯与本地语音可靠性补丁版。DeepSeek Bridge 现在读取 Harness 的真实会话列表结构，并把空闲、运行、完成、等待和错误状态同步到六个 Agent 键；当前选中任务完成时也会稳定亮绿灯。本地 Qwen3-ASR 的启动流程同时修复了缺少侧车、无关配置变更触发重启以及重复模型实例争用 GPU 的问题。

## 修复内容

- Agent 键统一使用空闲熄灭、运行蓝、完成绿、等待橙、错误红的状态词汇。
- Bridge 读取 DSH 的 `{ items, current }` 会话快照，并保留对旧列表形状的兼容。
- DSH 原生完成提醒不覆盖当前选中任务；Bridge 现在锁存该任务的 `running → idle` 边沿，直到下一次运行。
- Harness 状态每秒同步，语音健康检查仍独立运行，避免两类状态相互覆盖。
- 发行包明确携带 Qwen 启动脚本和服务端侧车。
- 等价的语音配置更新不再取消并重启预热服务。
- Qwen 服务在加载模型前取得按用户和端口隔离的单实例锁，杜绝重复实例争用显存。

## 下载资产

- `Deepseek-Harness-Keypad-Full-v0.2.5-oneclick.exe`：推荐，内置 Windows .NET 运行时与官方 DSH WSL 载荷。
- `Deepseek-Harness-Keypad-Full-v0.2.5-oneclick-no-dotnet.exe`：内置相同 DSH 载荷，需要另装 .NET 10 Desktop Runtime x64。
- `Deepseek-Harness-Keypad-v0.2.5-win-x64.zip`：便携轻量包。
- `Deepseek-Harness-Keypad-Bridge-v0.2.5.zip`：只给已有 DSH 使用的独立 Bridge。
- 每个发布资产均附带 `.sha256` 校验文件。

Full 包不包含 DeepSeek API 密钥、Qwen 模型权重、Python 环境或虚拟 HID 驱动。已有 Ubuntu、外部 DSH、会话和 Qwen 服务不会被覆盖。

---

# Deepseek Harness Keypad v0.2.5 (English)

This patch release fixes Agent-key status synchronization and local voice startup reliability. The DeepSeek Bridge now reads the Harness's real session-list contract and maps idle, running, completed, waiting, and error states onto the six Agent keys. Completion of the selected task is latched as green until its next run. Qwen3-ASR startup now ships with its required sidecars, ignores equivalent voice-profile updates, and prevents duplicate model processes from competing for GPU memory.

## Release assets

- `Deepseek-Harness-Keypad-Full-v0.2.5-oneclick.exe`: recommended; includes the Windows .NET runtime and official DSH WSL payload.
- `Deepseek-Harness-Keypad-Full-v0.2.5-oneclick-no-dotnet.exe`: same DSH payload; requires the .NET 10 Desktop Runtime x64.
- `Deepseek-Harness-Keypad-v0.2.5-win-x64.zip`: portable lightweight package.
- `Deepseek-Harness-Keypad-Bridge-v0.2.5.zip`: standalone Bridge for an existing DSH installation.
- Every release asset has an adjacent `.sha256` file.

The Full installers do not include a DeepSeek API key, Qwen model weights, a Python environment, or a virtual HID driver. Existing Ubuntu distributions, external DSH installations, sessions, and Qwen services are not overwritten.
