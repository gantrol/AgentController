# AgentController DeepSeek Harness 桥接插件

[English](README.md) | 中文

这是供 AgentController Micro 使用的外部 DeepSeek Harness 双端 bundle。它安装到 Harness profile，不修改 DeepSeek Harness 源码树。

## 功能

- Harness/终端进程运行在 WSL2；Windows Micro 通过仅限回环的 HTTP 控制端点直连，原生 Windows 模式仍保留命名管道回退，不模拟键盘或鼠标。
- 只打开/聚焦一个受控的 Harness 应用窗口，可列出最近会话、准确激活会话并执行 Harness 原生动作。
- 通过原生会话视图 store 在“对话 / 轨迹”间切换，不点击或查询 DOM。
- 左上旋钮只遍历当前输入框中实时可见、可用的控件；弹层打开后导航范围锁定在
  最上层菜单，其他插件后来注入的按钮会自动加入，并保持唯一的蓝色高亮。
- 菜单根层与子层状态会同步给 Micro；进入菜单后 AG00 临时成为红色“返回上一级”
  键，其余 Agent 键锁定，避免切换会话破坏当前选择。
- 通过 Harness 共享模型目录直接实现“仅推理强度”和两个可配置快捷模型，不模拟输入。
- 提供 Goal 原生动作；在不覆盖无关草稿的前提下打开 `/goal`。
- 在 Harness 输入框旁加入麦克风按钮，并加入完整的“语音输入”设置页。
- 接收 Micro 的按住说话边沿（`voice/start` / `voice/stop`），把最终转写写回开始录音时所属的会话。
- 明确支持三种识别方式：
  - **本地流式 Qwen**：通过统一的 `dsh-stream-v1` WebSocket 协议持续发送 PCM，可配置启动脚本与健康检查。
  - **系统/浏览器**：当前浏览器提供 `SpeechRecognition` 时使用。
  - **远程 WebSocket**：通过 `wss://`（或 loopback `ws://`）使用固定的 `dsh-stream-v1` PCM16 协议。
- 非密钥设置保存在 DSH storage 目录。API Key 由 Harness credential service 以只写引用管理，浏览器无法读回密钥。

## 安装到 WSL2

先在 Windows 安装 WSL2/Ubuntu。然后从 PowerShell 运行一次安装器；它会校验并安装独立的 Linux Node 24、pnpm，复制 Harness 到 WSL 的 ext4 文件系统、迁移一份用户 profile，并安装此外部 bundle。Windows 下原来的 checkout 和 `%USERPROFILE%\.dsh` 不会被修改：

```powershell
wsl.exe --distribution Ubuntu --exec bash /mnt/d/AgentController/micro-bridge/DeepSeekHarness/scripts/install-dsh-wsl-runtime.sh
```

安装后的稳定入口是：

```powershell
wsl.exe --distribution Ubuntu --exec bash /mnt/d/AgentController/micro-bridge/DeepSeekHarness/scripts/start-dsh-wsl.sh
```

Micro 的内置 DeepSeek 配置会调用这个入口，并优先连接
`http://127.0.0.1:3080/__agentcontroller/micro/request`。Harness 的 Node、Bash、PTY 和终端检查都因此运行在 Linux；网页和 Micro 仍是 Windows 原生窗口。

若 checkout、AgentController 或 Windows 用户目录不在默认位置，可向安装器传入
`DSH_SOURCE_CHECKOUT`、`AGENTCONTROLLER_ROOT`、`WINDOWS_USER_NAME`。迁移只转换 profile 与 storage JSON 中的盘符路径；历史会话会保留，但建议新建一个 WSL 会话继续实际终端工作。

## 本地协议

WSL 模式在 loopback HTTP POST 上承载同一份有大小限制的 UTF-8 JSON；原生 Windows 模式仍可使用 `\\.\pipe\deepseek-harness-micro-v1` 的单行协议。版本为 `1`、来源为 `codex-micro`，动作包括：

- `activate`
- `state/read`
- `session/activate`
- `action/execute`
- `voice/start` / `voice/stop`

浏览器端通过 Harness 的 `sessions`、`workspaces`、`conversation`、`layout` 与已注册会话视图 store 执行动作。没有浏览器连接时 Host 才暂存帧；连续按 Micro 键不会重复打开许多网页。默认四键是新建、对话/轨迹、停止和分叉。

输入区导航动作包括 `composer/select-previous`、`composer/select-next`、
`composer/activate-selection` 与 `composer/back`。`state/read` 的
`navigationDepth` 为 `0` 时没有菜单，`1` 为菜单根层，`2` 为模型或推理等级子层。

音频不会经过控制 HTTP 或命名管道。本地和远程录音在浏览器与插件同源 WebSocket 网关之间使用单声道 16 kHz PCM16。本地流式和健康检查地址必须位于 loopback；公网远程流式地址必须使用 `wss://`。

## 首次语音配置

未验证的 profile 不能直接开启麦克风。首次按麦克风会打开插件自己的配置弹窗，选择识别方式并完成预检。本地模式可以在首次录音时、随 Harness，或完全手动管理 PowerShell 脚本/可执行程序。插件会检查脚本路径、工作目录、健康地址、模型启动超时以及服务返回的 `ready` 握手，通过后才标记配置完成。重复启动只会等待同一个进程任务。

设置页的“内置 Qwen 启动器”会填写随插件发布的
`scripts/start-qwen3-asr-stream.ps1`。它在 WSL 中运行
`qwen3-asr-stream-server.py`，把 Qwen3-ASR 官方 vLLM 流式状态转换成
`dsh-stream-v1`，不是把整段 HTTP 转写伪装成流式。Qwen 官方目前只在 vLLM
后端支持这种增量推理，参见
[Qwen3-ASR 官方流式说明](https://github.com/QwenLM/Qwen3-ASR#streaming-inference)。

### 从零准备本地环境

1. Windows 安装 WSL2、Ubuntu，并确认 WSL 内可使用 NVIDIA GPU；插件不会替用户修改系统功能或驱动。
2. 在 WSL 的独立 Python 3.12 环境安装 `qwen-asr[vllm]`、`aiohttp` 与 `numpy`。推荐路径是
   `~/.local/share/dsh-qwen-asr/venv`；启动器也兼容参考工程已有的
   `~/.local/share/catai-qwen-asr/venv`。
3. 模型可预先放在 `~/.local/share/dsh-qwen-asr/models/Qwen3-ASR-0.6B`；若不存在，启动器使用
   `Qwen/Qwen3-ASR-0.6B`，由官方库首次下载。首次下载和加载可能较久，应相应增大设置页的启动超时。
4. 首次按麦克风，选择“本地流式 Qwen”并点“采用”内置启动器，再点“保存并验证”。只有健康检查和
   WebSocket `ready` 都成功后，配置才会完成。
5. 之后可选“首次按麦克风时启动”或“随 Harness 启动”。停止/退出时插件会终止它自己创建的整个
   PowerShell、WSL 和模型工作进程树；外部手动启动的服务不会被终止。

常见失败会保留明确错误码与受限日志尾部：WSL/发行版不存在、Python 或依赖缺失、脚本/工作目录错误、
端口占用、模型下载或加载超时、显存不足、健康检查失败、流式 `ready` 超时。读取失败不会被显示成
“已就绪”，也不会偷偷改用系统或远程识别。

## 流式协议

客户端依次发送 `start` JSON、PCM16 二进制帧和 `stop` JSON；远程服务返回 `ready`、`partial`、`final`、`done` 或 `error` JSON。配置的凭据由 Host 作为 Bearer header 发送，不暴露给浏览器 JavaScript。

## 构建与测试

```powershell
pnpm run verify
```

该命令依次执行严格 TypeScript 检查、独立的浏览器/命名管道/WSL 回环 HTTP/设置测试，并构建 Host 与浏览器两个 bundle。

## 限制

- 浏览器前台切换仍受 Windows/浏览器策略影响；最后的原生窗口置前由 Micro 完成，不模拟输入。
- 麦克风不再使用旧的 OpenAI 兼容 `/audio/transcriptions` 分块路径；可以使用内置 Qwen 适配器，或让自定义本地服务实现流式协议并返回 partial/final。
- 系统识别的质量以及音频是否离开设备由浏览器/操作系统决定；插件不会静默切换识别方式。
- 左上旋钮配置由 Micro 按 Harness 独立保存；默认输入区控件导航，也可切换为
  “仅推理强度”或“最近会话”。

## 模型体验

插件只通过 Harness 的共享模型目录修改指定会话下一轮的模型与推理强度，不自行
组装或代理 LLM provider 请求。
