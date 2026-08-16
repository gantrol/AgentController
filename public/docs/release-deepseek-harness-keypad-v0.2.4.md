# Deepseek Harness Keypad v0.2.4（简体中文）

正式版现已发布：Windows 登录后可自动启动小键盘，点击 DeepSeek 键即可启动或置前 Harness。语音完全由小键盘负责，可使用 Windows 系统语音识别，也可配置本地 Qwen3-ASR。

## 亮点

- 内置 DeepSeek Harness 目标，同时支持外部 Harness；首次启动可选择在专用 WSL 中安装 DSH（推荐），或使用本机 / WSL 中已有的 DSH。
- Bridge 独立分装，DeepSeek 插件只保留一个语音按钮；插件不连接 ASR，不保存语音配置或凭据。
- DeepSeek 输入框实时显示流式识别增量；本地 Qwen 默认使用 1 秒音频分块。
- 左下角第三颗灯在 DeepSeek 模式下专门显示小键盘语音服务状态，不再受 Harness 任务数量影响；Qwen 服务退出和恢复会自动刷新。
- 语音配置验证失败或中途关闭窗口时，保留上一次验证成功的配置。
- 路径支持 `{AppDir}`、`{LocalAppData}`、环境变量和相对路径，不写死安装目录。

## 下载资产

- `Deepseek-Harness-Keypad-v0.2.4-win-x64.zip`
- `Deepseek-Harness-Keypad-Bridge-v0.2.4.zip`
- 两个 ZIP 各自附带一个 `.sha256` 校验文件。

主程序为 framework-dependent 单文件，需要 Microsoft .NET 10 Desktop Runtime x64。模型权重、Python 环境和虚拟 HID 驱动不包含在 ZIP 中。

## 官方安装指南

- [Microsoft：安装 WSL](https://learn.microsoft.com/en-us/windows/wsl/install)
- [DeepSeek Harness 官方中文 README](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.zh.md)
- 可选：[Qwen3-ASR 官方安装指南](https://github.com/QwenLM/Qwen3-ASR#quickstart)
- 可选：[Qwen3-ASR 官方流式推理示例](https://github.com/QwenLM/Qwen3-ASR/blob/main/examples/example_qwen3_asr_vllm_streaming.py)

---

# Deepseek Harness Keypad v0.2.4 (English)

This stable release starts with Windows and launches or focuses Harness from the DeepSeek key. Voice stays keypad-owned: use Windows speech or optional local Qwen3-ASR, while the separately packaged Bridge adds one DeepSeek microphone button and receives recognized text only.

Streaming partial text now appears live in the DeepSeek composer. In DeepSeek mode, the third lower-left LED reports keypad voice-service readiness independently of running Harness tasks.

Assets: `Deepseek-Harness-Keypad-v0.2.4-win-x64.zip`, `Deepseek-Harness-Keypad-Bridge-v0.2.4.zip`, and their SHA-256 files. See the official [WSL install guide](https://learn.microsoft.com/en-us/windows/wsl/install), [DeepSeek Harness README](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.zh.md), and optional [Qwen3-ASR quickstart](https://github.com/QwenLM/Qwen3-ASR#quickstart).
