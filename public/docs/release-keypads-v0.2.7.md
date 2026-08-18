# Codex / Deepseek Keypads v0.2.7（简体中文）

这是 Codex 键交互与小键盘退出生命周期的修复版。Codex 标准包与 DeepSeek 特调包共享同一套 broker 修复；DeepSeek 版继续保留 v0.2.6 的专用 Edge surface 路由。

## 修复内容

- Codex 不在前台时，按下 Codex 键只启动应用或置前主窗口，不提交输入框；Codex 已在前台后再次按下才发送 `ACT12`。
- 找不到 Codex 主窗口时，通过已注册的 Windows 应用入口启动 Codex，并等待主窗口出现后置前。
- 最后一个客户端正常断开后，`--micro-broker` 立即退出并释放程序文件，避免升级、覆盖或重新构建时出现文件锁；仍有 Agent Controller 等客户端连接时继续共享 broker。
- DeepSeek 特调包继承上述退出修复，并继续使用已确认的专用 app-mode surface，普通 Chrome 或 Edge 标签页不能截获小键盘动作。

## 下载选择

- `CodexMicro-Keypad-0.2.7-win-x64.zip`：Codex 标准便携包。
- `Deepseek-Harness-Keypad-Full-v0.2.7-oneclick.exe`：推荐的 DeepSeek 完整一键安装包，内置 .NET 运行时与官方 DSH WSL 载荷。
- `Deepseek-Harness-Keypad-Full-v0.2.7-oneclick-no-dotnet.exe`：相同 DSH 载荷，需要另装 .NET 10 Desktop Runtime x64。
- `Deepseek-Harness-Keypad-v0.2.7-win-x64.zip`：DeepSeek 便携轻量包。
- `Deepseek-Harness-Keypad-Bridge-v0.2.7.zip`：只给已有 DSH 使用的独立 Bridge。

每个正式发布资产均附带独立 SHA-256 文件。

---

# Codex / Deepseek Keypads v0.2.7 (English)

This patch fixes the Codex-key interaction and the keypad shutdown lifecycle. The standard Codex and tailored DeepSeek packages share the same broker fix, while the DeepSeek package retains the dedicated Edge surface routing introduced in v0.2.6.

When Codex is not in the foreground, the Codex key now only starts or focuses the app and never submits composer text. Pressing the key again after Codex is already in front sends `ACT12`. If no main window exists, the keypad launches the registered Windows app and waits for that window before activating it.

A graceful disconnect from the last client now stops `--micro-broker` immediately and releases the program files. A broker that is still shared with Agent Controller or another client remains alive. The DeepSeek package inherits this lifecycle fix while continuing to reject normal Chrome and Edge tabs as keypad targets.

Choose `CodexMicro-Keypad-0.2.7-win-x64.zip` for the standard Codex keypad. For DeepSeek, use the self-contained `Deepseek-Harness-Keypad-Full-v0.2.7-oneclick.exe`, the smaller `oneclick-no-dotnet` variant when .NET 10 Desktop Runtime x64 is installed, the portable keypad ZIP, or the standalone Bridge ZIP. Every asset has an adjacent SHA-256 file.
