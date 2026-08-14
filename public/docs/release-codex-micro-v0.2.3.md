# Codex Micro Keypad v0.2.3

This release makes Agent hover titles follow the same recent-task order as Codex and adds a quick Sol/Luna model switch.

## What's new

- Agent titles are read from Codex App Server `thread/list` using `recency_at`, so slot titles no longer follow the stale `session_index.jsonl` update order.
- The observer follows the configured Agent source. Only the locally provable `recent` source receives named titles; `pinned`, `priority`, and `custom` keep safe generic slot labels instead of guessing.
- App Server failures retain the last proven roster and use the legacy index only as an initial fallback.
- A short click on the quota knob toggles the current task's next-turn model between Sol and Luna; a long press still opens Micro settings.
- Chinese and English accessibility names, tooltips, and status text describe the new behavior.

## Download and run

- `CodexMicro-Keypad-0.2.3-win-x64.zip`: standalone keypad for Windows x64.
- `CodexMicro-Keypad-0.2.3-win-x64.zip.sha256`: checksum for the keypad archive.
- Extract the complete archive, then run `CodexMicro.exe`.
- This compact package is framework-dependent and requires the [official Microsoft .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

When upgrading, exit the previous version from its tray menu before replacing it or extracting the new version into a separate directory.

## Verification

- Codex Micro desktop tests: 103 passed.
- Live UI Automation check: all six Agent titles matched the current App Server recency roster.
- Standalone Release build: 0 warnings, 0 errors.

---

# Codex Micro Keypad v0.2.3（简体中文）

本版本让 Agent 悬停标题使用与 Codex 相同的最近任务顺序，并加入 Sol/Luna 快速切换。

## 主要更新

- Agent 标题改为读取 Codex App Server 的 `thread/list`，按 `recency_at` 排序，避免 `session_index.jsonl` 的更新时间顺序造成标题串位。
- 观察器跟随 Agent 来源设置。只有本地可证明的 `recent` 来源显示具体任务名；`pinned`、`priority`、`custom` 保留通用槽位标题，不做猜测。
- App Server 暂不可用时保留最后一次已确认的 roster，旧索引只作为首次读取失败时的降级。
- 短按额度旋钮可在当前任务下一轮的 Sol 与 Luna 之间切换；长按仍打开 Micro 设置。
- 中英文无障碍名称、提示和状态文本同步更新。

## 下载与运行

- `CodexMicro-Keypad-0.2.3-win-x64.zip`：独立小键盘程序（Windows x64）。
- `CodexMicro-Keypad-0.2.3-win-x64.zip.sha256`：小键盘压缩包校验值。
- 解压完整目录后运行 `CodexMicro.exe`。
- 本包为精简的 framework-dependent 构建，需要安装[微软官方 .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。

升级时，请先从旧版托盘菜单退出，再覆盖或解压到新目录。

## 验证

- Codex Micro 桌面测试：103 项通过。
- 现场 UI Automation 核对：6 个 Agent 标题全部与当前 App Server recency roster 一致。
- 独立 Release 构建：0 警告、0 错误。
