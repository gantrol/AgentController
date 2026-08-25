# Agent Controller v1.2.1

Agent Controller v1.2.1 is a performance and reliability release for the Windows controller, with the DeepSeek Harness target and session synchronization included in the same package.

## Downloads

- `AgentController-1.2.1-win-x64.zip`: self-contained Windows x64 package. No separate .NET installation is required.
- `AgentController-1.2.1-win-x64-compact.zip`: compact, framework-dependent Windows x64 package. It requires the [official Microsoft .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
- Each archive has a matching `.sha256` checksum file.
- The standalone keypad remains published separately as [Codex Micro Keypad v1.2.0](https://github.com/gantrol/AgentController/releases/tag/codex-micro-v1.2.0).

Extract exactly one Controller archive completely, then run `AgentController.exe` as a normal user. Do not combine the self-contained and compact archives in the same folder.

## Highlights

- Added the DeepSeek Harness as a first-class controller target with session discovery, activation, composer navigation, model/reasoning selection, submit, stop, fork, approval, rejection, and layout actions.
- Added current-session synchronization and direct foreground activation for DeepSeek, while keeping Codex-only input paths isolated from the DeepSeek target.
- Reduced rollout status parsing allocations by scanning UTF-8 data in bounded chunks with pooled buffers.
- Reduced Codex snapshot and thread-availability costs by projecting only the required SQL fields and using indexed lookups.
- Simplified foreground detection to the current window handle instead of enumerating every top-level window.
- Preserved the self-contained package and the reproducible framework-dependent `-compact` package, each with an independent SHA-256 checksum.

## Validation

- All 1,099 Release .NET tests pass, including the DeepSeek, controller, data-service, Micro Broker, and standalone keypad suites.
- All 52 DeepSeek Harness bridge typecheck and unit tests pass.
- Self-contained archive: approximately 75.64 MiB.
- Compact archive: approximately 9.63 MiB and within the 30 MiB packaging limit.
- The matching `.sha256` files contain the final SHA-256 values for each archive.

## Known limits

- Codex and DeepSeek integrations follow observed private contracts that may change in future releases.
- The application and developer driver are unsigned. Review the source and test with non-critical tasks first.
- The compact package will not start until the required Desktop Runtime is installed; use the self-contained package if you do not want that dependency.

---

# Agent Controller v1.2.1（简体中文）

Agent Controller v1.2.1 是一次 Windows Controller 性能与可靠性更新，并将 DeepSeek Harness 目标和会话同步能力一并纳入发布包。

## 下载

- `AgentController-1.2.1-win-x64.zip`：Windows x64 自包含包，不需要另装 .NET。
- `AgentController-1.2.1-win-x64-compact.zip`：Windows x64 framework-dependent 精简包，需要安装[微软官方 .NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。
- 每个压缩包都有对应的 `.sha256` 校验文件。
- 独立小键盘仍单独发布为 [Codex Micro Keypad v1.2.0](https://github.com/gantrol/AgentController/releases/tag/codex-micro-v1.2.0)。

请任选一种 Controller 压缩包完整解压，再以普通用户运行 `AgentController.exe`。不要把自包含包与精简包解压到同一个目录混用。

## 主要更新

- 将 DeepSeek Harness 作为一等 Controller 目标，支持会话发现、激活、输入框导航、模型/推理选择、提交、停止、分支、批准、拒绝和布局操作。
- 新增 DeepSeek 当前会话同步和直接前台激活，同时隔离 Codex 专用输入路径，避免跨目标误操作。
- 使用分块 UTF-8 扫描和池化缓冲区解析 rollout 状态，降低分配和读取开销。
- 通过只投影必要的 SQL 字段和使用索引查询，降低 Codex 快照及线程可用性检查的成本。
- 前台检测直接检查当前窗口句柄，不再枚举所有顶层窗口。
- 保留自包含包和可复现的 framework-dependent `-compact` 精简包，分别提供独立 SHA-256 校验值。

## 验证

- Release .NET 测试共 1,099 项全部通过，覆盖 DeepSeek、Controller、数据服务、Micro Broker 和独立小键盘套件。
- DeepSeek Harness bridge 的类型检查和单元测试共 52 项全部通过。
- 自包含包约 75.64 MiB。
- 精简包约 9.63 MiB，符合 30 MiB 封包上限。
- 对应的 `.sha256` 文件包含每个压缩包的最终 SHA-256 值。

## 已知限制

- Codex 和 DeepSeek 集成依赖观察所得的私有协议，未来版本可能改变这些行为。
- 应用和开发者驱动均未签名。请先审查源码，并仅用非关键任务试用。
- 未安装所需 Desktop Runtime 时精简包无法启动；不希望增加该依赖时请使用自包含包。
