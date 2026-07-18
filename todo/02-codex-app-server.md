# 02 — Codex App Server 集成

> Status: Planned
> Priority: P0
> Depends on: 01-core-architecture

## 目标

使用 Codex App Server 作为 Thread、Turn、审批、历史和流式事件的权威业务通道，减少对桌面 UI 结构的依赖。

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
