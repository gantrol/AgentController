# 02 — Codex App Server 集成

> Status: Planned (read-only probes landed; semantic integration pending)
> Priority: P0
> Depends on: 01-core-architecture
> Updated: 2026-08-31

## 目标

使用 Codex App Server 作为 Thread、Turn、审批、历史和流式事件的权威业务通道，减少对桌面 UI 结构的依赖。

## 当前实现盘点（2026-08-31）

官方 App Server 当前覆盖 Thread 生命周期、Turn 驱动、审批、流式事件、模型目录和执行能力，详见 [Codex App Server 官方文档](https://developers.openai.com/codex/app-server)。本项目必须把“协议支持”与“当前代码已接通”分开记录：

| 能力 | 当前代码状态 | 当前实际行为 |
| --- | --- | --- |
| initialize / initialized | ✅ 已使用 | 两个只读探针均通过 codex.exe app-server --stdio 完成握手。 |
| thread/list | ✅ 局部接通 | 读取按 recency_at 排序的最近任务；请求最多 18 条，观察器只取前 6 条用于 Agent 槽位标题。 |
| account/rateLimits/read | ✅ 局部接通 | 只读额度窗口；失败保持未知，不写入账户状态。 |
| thread/start / thread/resume / thread/read / thread/fork | ❌ 未接入 | 当前新建、打开、恢复和 Fork 仍由本地 UI、deeplink、快捷键或适配器处理。 |
| turn/start / turn/steer / turn/interrupt | ❌ 未接入 | 当前 Submit、Steer、Queue、Stop 没有直接 App Server request 和权威 Turn readback。 |
| 审批请求/回应与 turn/*、item/* 通知 | ❌ 未接入 | 没有持续 App Server 事件流；不能把 HID transport ACK 当作业务完成。 |
| 模型/effort 切换 | ⚠️ 间接接入 | 当前使用 codex-ipc 的桌面跨窗口协议更新下一轮设置，不是 App Server stdio 的直接调用。 |

上述两个只读服务目前都是短生命周期进程，拿到响应后退出；它们不是持久的 Thread/Turn companion connection。完整语义垂直切片仍以本文件的未勾选任务和完成门槛为准。

## 技术决策：语义平面与设备平面不互相替代

1. App Server 是 Thread、Turn、Steer、Interrupt、审批状态、历史和流式事件的权威语义平面。
2. Micro HID 是 Agent/Command key、Dial、Analog、PTT、设备身份、灯光和设备状态的设备平面。
3. 对真实 Micro 已有等价控件的操作，继续走 HID，让 Codex 自己解释当前 layout 和 Composer 上下文；不把 ACT* 或 ENC* 重新实现成一套 UIA 语义协议。
4. 对 Micro 无法表达的任意任务树、任意 Thread 打开、Turn 控制和权威 readback，目标改为 App Server adapter；在 adapter 完成前，现有 UI/快捷键路径只能标记为 Limited 或 AcceptedUnverified。
5. App Server-only 可以作为无驱动的 Limited mode，但不能替代 Full Micro mode 所需的 HID 驱动、Broker 和双向设备协议。

## 待办

### 协议与版本

- [ ] 检测实际 `codex` 可执行文件和 app-server 版本。
- [ ] 在构建或兼容测试中生成该版本的 TypeScript/JSON Schema 快照。
- [ ] 将生成文件隔离在 `GeneratedSchemas/`，禁止手工编辑。
- [ ] 记录稳定 API 与 `experimentalApi` 字段，默认不启用实验能力。

### 客户端基础设施

- [ ] 首先实现 stdio JSONL transport；WebSocket 不作为桌面本机默认方案。
- [ ] 实现 initialize/initialized 握手、request id、通知流和断线清理。
- [ ] 实现有界出站队列、超时、取消和指数退避。
- [ ] 将 server request 与 notification 分开处理，避免 UI 线程阻塞协议读取。
- [ ] 日志默认只记录 method、id、时序和错误，不记录 prompt 或代码内容。

### 垂直功能切片

- [ ] `thread/list`、start、resume、fork 的领域映射。
- [ ] `turn/start`、steer、interrupt 及 completed 状态映射。
- [ ] item/turn 增量事件转换为统一 `StateObservation`。
- [ ] 审批请求与回应映射到安全 Action。
- [ ] 模型、effort、speed 和能力差异的实时 catalog。
- [ ] 账户未登录、版本不兼容和 server 缺失的明确诊断。

### 产品模式

- [ ] 明确“App Server 自有会话模式”和“控制现有 ChatGPT 桌面 Companion 模式”的边界。
- [ ] 验证两种模式的 thread 可见性、身份和状态是否一致，不能凭共享目录推断。
- [ ] UI 明确显示当前动作由哪个会话/客户端执行。

## 不在本任务中

- 不用 App Server 冒充 ChatGPT 窗口导航或系统前台控制。
- 不默认开放远程 WebSocket listener。
- 不把实验 API 当作稳定产品合同。

## 完成门槛

- 一个真实 Thread 可通过 App Server 创建/恢复并完成一个 Turn。
- Steer、Interrupt、Fork 至少各有一条端到端验证路径。
- 状态事件不依赖 UIA 或 rollout 猜测。
- 断线、重连、版本变化和 server 缺失均安全降级。
- 协议合同测试绑定到明确 Codex 版本。
