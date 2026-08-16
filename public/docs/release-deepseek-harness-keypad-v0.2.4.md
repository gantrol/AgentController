# Deepseek Harness Keypad v0.2.4（简体中文）

正式版现已发布：Windows 登录后可自动启动小键盘，点击 DeepSeek 键即可启动或置前 Harness。新增 Full oneclick 单文件安装器，内置官方 npm `@deepseek-ai/dsh@0.1.0-rc.6`；语音仍完全由小键盘负责，可使用 Windows 系统语音识别或本地 Qwen3-ASR。

## 亮点

- 内置 DeepSeek Harness 目标，同时支持外部 Harness；首次启动可选择在专用 WSL 中安装 DSH（推荐），或使用本机 / WSL 中已有的 DSH。
- Bridge 独立分装，DeepSeek 插件只保留一个语音按钮；插件不连接 ASR，不保存语音配置或凭据。
- DeepSeek 输入框实时显示流式识别增量；本地 Qwen 默认使用 1 秒音频分块。
- 左下角第三颗灯在 DeepSeek 模式下专门显示小键盘语音服务状态，不再受 Harness 任务数量影响；Qwen 服务退出和恢复会自动刷新。
- 语音配置验证失败或中途关闭窗口时，保留上一次验证成功的配置。
- 路径支持 `{AppDir}`、`{LocalAppData}`、环境变量和相对路径，不写死安装目录。
- DSH、Node 和 pnpm 版本集中在独立运行时清单中；Bridge 不修改 DSH，后续可直接替换官方 npm 版本重新构建。

## 下载资产

- `Deepseek-Harness-Keypad-Full-v0.2.4-oneclick.exe`：推荐，内置 Windows .NET 运行时与官方 DSH WSL 载荷；单文件安装、当前用户后台自启动、支持修复 / 更新回滚和卸载。
- `Deepseek-Harness-Keypad-Full-v0.2.4-oneclick-no-dotnet.exe`：同样内置官方 DSH，但不包含 Windows .NET 运行时，体积更小。
- `Deepseek-Harness-Keypad-v0.2.4-win-x64.zip`：便携轻量包，首次托管安装时在线取得官方 DSH。
- `Deepseek-Harness-Keypad-Bridge-v0.2.4.zip`：只给已有 DSH 使用的独立 Bridge。
- 每个发布资产都附带 `.sha256` 校验文件。

不带 .NET 的 oneclick 和便携 ZIP 需要先安装 [Microsoft .NET 10 Desktop Runtime x64 官方安装程序](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.10-windows-x64-installer)。推荐 oneclick 已自带对应运行时，不需要另装。.NET 链接由 Microsoft 官方提供。

Full 包不包含 DeepSeek API 密钥，也不包含 Qwen 模型权重、Python 环境或虚拟 HID 驱动。首次点击 DeepSeek 键时，若没有可用的外部 DSH，可把内置载荷导入程序专用的 `CodexMicro-DeepSeek` WSL；已有 Ubuntu、外部 DSH、会话和 Qwen 服务不会被覆盖。

## 官方安装指南

- [Microsoft：安装 WSL](https://learn.microsoft.com/en-us/windows/wsl/install)
- [Microsoft：自定义 WSL 发行版与 `.wsl` 文件](https://learn.microsoft.com/en-us/windows/wsl/build-custom-distro)
- [DeepSeek Harness 官方中文 README](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.zh.md)
- 可选：[Qwen3-ASR 官方安装指南](https://github.com/QwenLM/Qwen3-ASR#quickstart)
- 可选：[Qwen3-ASR 官方流式推理示例](https://github.com/QwenLM/Qwen3-ASR/blob/main/examples/example_qwen3_asr_vllm_streaming.py)

---

# Deepseek Harness Keypad v0.2.4 (English)

This stable release starts with Windows and launches or focuses Harness from the DeepSeek key. The new Full oneclick installers bundle the official npm package `@deepseek-ai/dsh@0.1.0-rc.6`; voice stays keypad-owned and the separate Bridge receives recognized text only.

Streaming partial text now appears live in the DeepSeek composer. In DeepSeek mode, the third lower-left LED reports keypad voice-service readiness independently of running Harness tasks.

Use `Deepseek-Harness-Keypad-Full-v0.2.4-oneclick.exe` for the self-contained Windows build, or `Deepseek-Harness-Keypad-Full-v0.2.4-oneclick-no-dotnet.exe` for the smaller build that requires the [official Microsoft .NET 10 Desktop Runtime x64 installer](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.10-windows-x64-installer). The portable keypad ZIP, standalone Bridge ZIP, and SHA-256 files remain available. See the official [WSL install guide](https://learn.microsoft.com/en-us/windows/wsl/install), [DeepSeek Harness README](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.zh.md), and optional [Qwen3-ASR quickstart](https://github.com/QwenLM/Qwen3-ASR#quickstart).
