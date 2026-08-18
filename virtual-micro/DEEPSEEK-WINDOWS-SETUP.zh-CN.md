# Windows 首次使用 DeepSeek Harness

本文适用于 Deepseek Harness Keypad 的便携包与 Full oneclick 包。程序不假定自身位于某个盘符，也不
要求 DeepSeek Harness 源码位于固定目录。第一次点击 DeepSeek 键时，程序先探测
已有服务，再让用户明确选择：

- **在专用 WSL 中安装 DSH（推荐）**：程序准备隔离环境，安装兼容的 DSH 与独立 Bridge；
- **使用我已有的 DSH**：用户自行定位本机或 WSL 中的 Harness，原版本、目录和启动方式不变；
- **取消**：不修改系统，下次点击可继续。

DeepSeek Harness 仍处于开发者预览阶段，未来可能出现破坏兼容性的变化。安装前可
先阅读 [DeepSeek Harness 官方中文 README](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.zh.md)。

## 官方端口

DeepSeek 官方说明 `dsh web` 默认服务地址为：

```text
http://127.0.0.1:3080
```

官方 CLI 同时支持 `--port`，因此 3080 只是默认值，不是程序的硬编码限制。Micro
按以下顺序查找：

1. 用户已经保存的控制地址；
2. 官方默认地址 `127.0.0.1:3080`；
3. 程序托管环境发现 3080 被占用时，在回环地址上选择其他端口并保存。

依据见 [DeepSeek 官方 README](https://github.com/deepseek-ai/deepseek-harness/blob/master/README.zh.md)
和 [官方 CLI 行为参考](https://github.com/deepseek-ai/deepseek-harness/blob/master/apps/cli/reference/README.zh.md)。

Micro 使用的控制路径为：

```text
/__agentcontroller/micro/request
```

这个路径由随包提供的 Micro 桥接插件注册，不是 DeepSeek 官方 API。程序分别检查
官方 Web 服务和桥接端点，不能把“网页能打开”误报成“Micro 已经可用”。

## 方案一：在专用 WSL 中安装 DSH

点击 **在专用 WSL 中安装 DSH（推荐）** 后，窗口与 DeepSeek 键同步显示 **8 步**：

1. 检测已有 Harness；
2. 确认配置方式；
3. 检查或安装 WSL；
4. 验证安装载荷并选择端口；
5. 准备 Node 与官方 Harness 运行环境；
6. 安装并验证 Micro 桥接插件；
7. 保存启动方式并启动服务；
8. 检查 Web 服务和桥接端点。

程序托管环境使用注册名 `CodexMicro-DeepSeek`，默认存储在当前用户的：

```text
%LOCALAPPDATA%\CodexMicro\wsl\deepseek
```

这个位置通过 Windows API 动态取得，不依赖 `C:`、`D:` 或 AgentController 的源码
位置。Linux 内的运行文件位于专用用户 `codexmicro` 的 home 下，用户现有的 Ubuntu、
`.dsh`、Harness checkout 和会话数据不会被修改。

默认小包在线取得并锁定兼容版本的以下官方组件：

- Microsoft WSL 与 Canonical Ubuntu 24.04 发行版；
- [Node.js 官方 Linux 发行物](https://nodejs.org/en/download)；
- DeepSeek 官方发布的 `@deepseek-ai/dsh` npm 包；
- 随当前 Codex Micro 包发布的桥接插件。

程序不会执行 `git clone`，也不要求用户安装 Git、Node 或 pnpm。Node 下载使用固定
版本和 SHA-256 校验；Harness 及其依赖由 npm 的完整性校验负责。

### WSL 尚未开启

程序会请求 Windows 管理员授权，使用 Microsoft 支持的 WSL 安装入口准备专用发行
版。用户拒绝 UAC 时，不会留下“配置完成”状态。若 Windows 要求重启，界面显示：

```text
3/8 等待重启
```

重启后再次点击 DeepSeek 键，程序从 WSL 检测继续，不会把它显示成“打开失败”。
相关系统要求、支持版本和手工验证命令以
[Microsoft WSL 官方安装文档](https://learn.microsoft.com/en-us/windows/wsl/install)
为准。

### 自动配置完成后

日常点击不再显示安装步骤，而是执行固定的 **7 步打开流程**：检查适配器、启动
服务、等待适配器、请求打开窗口、等待网页桥接、将窗口置前、已就绪。

第一次进入官方 Web UI 后，还需在 **设置 → 模型** 中填写 DeepSeek API 密钥并
选择工作区。密钥由 Harness 自己管理，Codex Micro 不读取或保存。参见
[DeepSeek 官方 Web UI 指南](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/user/guide/index.zh.md)
和 [DeepSeek API 密钥页面](https://platform.deepseek.com/api_keys)。

## 方案二：使用已有 DSH

程序先尝试已保存地址和官方默认端口。若发现 Web 服务但没有桥接，会显示“已发现
Harness · 缺少桥接”，不会显示成软件损坏。

已有 Harness 可继续按照 DeepSeek 官方方式运行：

```sh
npx @deepseek-ai/dsh web
```

自定义端口示例：

```sh
npx @deepseek-ai/dsh web --port 3090
```

这些命令来自 DeepSeek 官方 README 和 CLI 参考。Node 的安装方式以
[Node.js 官方下载页](https://nodejs.org/en/download)为准。

桥接插件必须安装到实际使用的 `web` profile。正式发布资产中它独立分装为
`Deepseek-Harness-Keypad-Bridge-v<版本>.zip`；DeepSeek 主包也保留同一份精简运行
载荷，供专用 WSL 安装流程使用。请在 **Harness 所在的同一个运行环境**中解压独立
插件包，再按 DeepSeek 官方插件命令安装：

```text
dsh plugin --profile web add <DeepSeekHarness 插件的绝对路径>
```

官方插件命令会把参数交给 pnpm，因此用户管理的环境需要自行准备 pnpm；完整行为见
[DeepSeek 官方插件管理说明](https://github.com/deepseek-ai/deepseek-harness/blob/master/apps/cli/reference/README.zh.md#插件管理)。
不要把示例路径改写成另一个固定盘符；应使用实际解压位置在该环境中的绝对路径。

然后在 Codex Micro 的当前 Agent 设置中填写：

- **WSL 控制地址**：默认
  `http://127.0.0.1:3080/__agentcontroller/micro/request`；
- **启动程序、参数和工作目录**：只有希望 Micro 代为启动已有 Harness 时才填写；
- **离线时启动**：已有可靠启动命令后再开启。

保存后点击 **打开 Harness** 会执行真实连接测试。测试通过前，配置页不会宣称
“已经可用”。

## 可选语音：Windows 或本地 Qwen3-ASR

语音由 Codex Micro 小键盘负责，Bridge 只转发按钮状态以及小键盘产生的增量 / 最终
文字。DeepSeek 插件不采集音频、不连接 ASR，也不保存语音配置。

- 不安装模型：在小键盘“语音输入”中选择 Windows 系统语音识别；
- 本地模型：选择“本地流式 Qwen”，点“使用本机示例”，再“保存并验证”；
- 避免首次点击冷启动：把启动方式设为“随小键盘启动”，并在托盘启用“开机自启动”。

Qwen3-ASR 的流式识别需要 vLLM 后端。依赖安装、模型下载与硬件要求以
[Qwen3-ASR 官方安装指南](https://github.com/QwenLM/Qwen3-ASR#quickstart)为准；
Codex Micro 的协议适配参考上游
[官方流式推理示例](https://github.com/QwenLM/Qwen3-ASR/blob/main/examples/example_qwen3_asr_vllm_streaming.py)。
示例路径只用于说明，程序配置仍使用 `{AppDir}`、`{LocalAppData}`、环境变量或相对
路径，不写死本机盘符和用户目录。

## Full oneclick 包

`Deepseek-Harness-Keypad-Full-v0.2.7-oneclick.exe` 使用与在线配置相同的 8 步状态机，
但从安装目录的 `payload/deepseek-runtime.wsl` 导入干净载荷，不再下载 Linux 用户态、
Node、pnpm 或 Harness。载荷预装的是 DeepSeek 官方 npm 包 `@deepseek-ai/dsh`，版本
和 Node / pnpm 一起记录在 Bridge 的 `scripts/runtime-versions.env`；Bridge 没有修改
DSH 源码，后续替换官方版本只需更新该清单并重建。

另有 `Deepseek-Harness-Keypad-Full-v0.2.7-oneclick-no-dotnet.exe`，DSH 载荷完全相同，
但不包含 Windows .NET 运行时。它和便携 ZIP 需要安装
[Microsoft .NET 10 Desktop Runtime x64 官方安装程序](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.10-windows-x64-installer)；
推荐 oneclick 已自带 .NET。两种 oneclick 都按当前用户安装到 Windows API 返回的
LocalAppData 目录，注册后台自启动、开始菜单和卸载入口，不写死盘符。

Full 包仍需要 Windows 的 WSL 系统功能；WSL 内核和虚拟化功能不能安全地藏进普通
EXE。`.wsl` 导入格式需要 WSL 2.4.4 或更新版本，说明与更新方式以
[Microsoft 自定义 WSL 发行版指南](https://learn.microsoft.com/en-us/windows/wsl/build-custom-distro)
和 [WSL 官方安装指南](https://learn.microsoft.com/en-us/windows/wsl/install)为准。

## 状态与恢复

界面明确区分：尚未配置、配置中、等待重启、已配置但离线、启动中、可用和需要
处理。失败会停在实际步骤，保留用户已有数据，并允许重试：

- **第 2/8 步**：当前包缺少桥接载荷，改用 DeepSeek 特调包或连接已有 Harness；
- **第 3/8 步**：检查 Windows 版本、虚拟化和 WSL；若刚启用则先重启；
- **第 4/8 步**：检查解压包是否完整，以及 WSL 是否能访问解压位置；
- **第 5/8 步**：检查网络、代理、磁盘空间和官方包下载；
- **第 6/8 步**：检查桥接 bundle 是否包含构建后的 `lib`；
- **第 7/8 步**：检查端口占用和托管启动配置；
- **第 8/8 步**：网页已启动但桥接未连接时，重试或查看 Harness 输出。

DeepSeek 的冷启动就绪等待默认是 300 秒，可在适配器设置中配置为 1–600 秒。程序先用
无副作用的 `state/read` 探测桥接，确认就绪后只发送一次真正的打开请求。若仍然超时，
适配器设置卡会显示上次超时的时间、耗时、探测次数和最后结果；同一份信息保存在
`%LOCALAPPDATA%\CodexMicro\harness-diagnostics.json`，并包含启动器 PID、退出状态，便于
区分“WSL / DSH 没启动”与“服务已启动但桥接没有响应”。

DeepSeek API 返回 401、`MISSING_CREDENTIAL` 或输入框不可用时，属于 Harness 的模型
或工作区配置，不是 WSL 安装失败。按
[DeepSeek 官方模型配置指南](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/user/guide/providers.zh.md)
和 Web UI 指南处理。
