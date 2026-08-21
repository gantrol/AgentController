# AgentController DeepSeek Harness 桥接插件

[English](README.md) | 中文

这是 AgentController Micro 与 DeepSeek Harness 之间的外部桥接 bundle。它不修改
Harness 源码，也不模拟键盘或鼠标。

## 职责

- 打开或聚焦唯一受控的 Harness 应用窗口。
- 列出最近会话、准确激活会话，并通过 Harness 服务执行原生动作。
- 通过 Harness API 导航输入区控件、切换“对话 / 轨迹”、修改推理或模型并打开
  Goal。
- 在 DeepSeek 输入框旁只增加一个麦克风按钮。
- 把这个按钮的切换请求转发给 Micro 小键盘，再把小键盘识别出的文字写回指定
  输入框。

插件**不**采集音频、不申请麦克风权限、不执行语音识别、不连接 ASR 服务、不保存
语音设置，也不保存语音密钥。这些职责全部属于 Codex Micro 小键盘进程；DeepSeek
的**语音通路**只接收最终文字。Harness 图片附件是另一条原生通路，只有当前后端
模型声明并实际支持图片输入时才有效。

## 安装方式

### 程序托管模式

DeepSeek 特调版 Micro 可以创建名为 `CodexMicro-DeepSeek` 的专用 WSL 发行版并调用
`scripts/install-dsh-wsl-runtime.sh`。安装器使用固定兼容版本的 Node、pnpm 和
`@deepseek-ai/dsh`，并安装此桥接。运行文件位于专用 Linux 用户的
`~/.local/share/codex-micro/deepseek`。

托管 rc.8 安装器会把 `deepseek-v4-flash-vision-exp` 加入 Harness 的 DeepSeek
模型目录，并声明 `text`、`image` 两种输入；其他模型和语言设置保持不变，也不会
强制把实验模型改成默认模型。实际可用性仍取决于官方 API 账号以及服务端返回的
模型。

检测到旧版托管 DSH 后，Micro 会明确询问是否执行可回滚升级。旧运行时会完整保留
为备份，只把凭据、设置和匿名安装标识复制到全新的 rc.8 home；不会导入 rc.8
不兼容的旧会话和存储。Web 与本 Bridge 都通过健康检查后才提交切换。用户自己管理
的 Harness 永远不会被 Micro 自动升级。

`scripts/start-dsh-wsl.sh` 接受 `--port`；官方默认端口为 3080。

### 用户管理的 Harness

在运行 Harness 的同一环境中安装已构建 bundle：

```text
dsh plugin --profile web add <本 bundle 的绝对路径>
```

按官方方式运行 `dsh web`，并在 Micro 中保存实际的回环控制地址。默认地址为
`http://127.0.0.1:3080/__agentcontroller/micro/request`。

## 本地协议

WSL 模式使用带大小限制的回环 HTTP UTF-8 JSON；原生 Windows 模式可在
`\\.\pipe\deepseek-harness-micro-v1` 上使用单行 JSON。协议版本为 `1`，来源为
`codex-micro`。

普通 DSH 标签页可以上报状态，但不会接收小键盘的聚焦、会话、动作或听写帧。没有
已确认的专用窗口时，Bridge 返回 `opening`，由 Windows 小键盘主程序优先使用 Edge
打开 app-mode 窗口，不依赖 WSL 的 Windows 可执行文件 interop。

通用动作：

- `activate`
- `state/read`
- `session/activate`
- `action/execute`

语音按钮转发动作：

- `voice/request`：小键盘长轮询 DeepSeek 按钮的切换请求。
- `voice/result`：小键盘完成该切换请求。
- `voice/status`：小键盘把聆听状态投影到按钮。
- `composer/dictate`：小键盘把识别文字发给指定输入框。

音频和 API Key 都不会经过此协议。Bridge 只连接 DeepSeek 与小键盘；小键盘直接
连接自己配置的语音提供商。

## 构建与测试

```powershell
pnpm run verify
pnpm run test:e2e
```

该命令执行严格 TypeScript 检查、浏览器/命名管道/回环 HTTP 测试，并构建 Host 与
浏览器两个 bundle。

## 限制

- 浏览器前台切换仍受 Windows/浏览器策略影响；最后的原生窗口置前由 Micro 完成。
- 语音提供商配置与排错属于 [Micro 小键盘](../../virtual-micro/README.zh-CN.md)，
  不属于此插件。
- 模型切换只使用 Harness 的共享模型目录，不代理 LLM provider 请求。
- Harness 能接收图片不等于纯文本后端自动获得视觉能力；当前 provider 模型必须
  真正接受原生图片请求。
