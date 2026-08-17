# Deepseek Harness Keypad v0.2.6（简体中文）

这是 DeepSeek 专用窗口路由的可靠性修复版，并完整保留 v0.2.5 的 Agent 灯状态同步与本地语音修复。按 DeepSeek 键时，普通 Chrome 或 Edge 标签页不再抢占激活；Windows 主程序会优先使用 Edge 打开带专用标记的 app-mode 窗口。

## 修复内容

- 激活、Agent 会话、动作和听写帧只发送给已确认的专用 DeepSeek surface。
- 启动过程中排队的帧只有在专用窗口完成 SSE 握手后才会投递，重连的普通标签页无法截走。
- 专用窗口改由 Windows 小键盘主程序启动，Edge 优先、Chrome 仅作兼容回退，不再依赖可能失效的 WSL Windows-executable interop。
- HWND 激活拒绝标题带 `Google Chrome` 或 `Microsoft Edge` 浏览器外壳的普通窗口。
- 专用窗口报告任务已运行时，Agent 灯状态立即同步为 `running`，并同时修正旧版兼容字段，不再等待可能滞后的 host session list。
- 新增三条 loopback HTTP/SSE E2E，覆盖普通标签抢激活、聚焦标签与专用窗口竞争、Agent 运行灯同步，以及重连标签抢排队动作。
- 保留 Agent 键蓝/绿/橙/红状态同步、当前任务完成锁存，以及 Qwen3-ASR 单实例和侧车启动修复。

## 下载选择

- `Deepseek-Harness-Keypad-Full-v0.2.6-oneclick.exe`：推荐，内置 Windows .NET 运行时与官方 DSH WSL 载荷。
- `Deepseek-Harness-Keypad-Full-v0.2.6-oneclick-no-dotnet.exe`：内置相同 DSH 载荷，需要另装 .NET 10 Desktop Runtime x64。
- `Deepseek-Harness-Keypad-v0.2.6-win-x64.zip`：便携轻量包。
- `Deepseek-Harness-Keypad-Bridge-v0.2.6.zip`：只给已有 DSH 使用的独立 Bridge。

每个正式发布资产均附带独立 SHA-256 文件。

---

# Deepseek Harness Keypad v0.2.6 (English)

This patch fixes dedicated-window routing while retaining the Agent-light and local-voice fixes from v0.2.5. A focused normal Chrome or Edge tab can no longer intercept the DeepSeek key. The Windows keypad host opens an Edge-first app-mode surface and delivers physical-key frames only after that dedicated surface is confirmed.

The dedicated surface's `running` report now drives both the detailed Agent-light state and the legacy compatibility bit immediately, even while the host session list is stale. Three loopback HTTP/SSE E2E cases cover tab interception, focused-tab versus dedicated-surface routing, Agent running-light synchronization, and reconnect races for queued actions. Native HWND activation also rejects normal browser-chrome titles.

Use `Deepseek-Harness-Keypad-Full-v0.2.6-oneclick.exe` for the recommended self-contained installer, or the `oneclick-no-dotnet` build when .NET 10 Desktop Runtime x64 is already installed. Portable keypad and standalone Bridge ZIPs are also included. Every asset has an adjacent SHA-256 file.
