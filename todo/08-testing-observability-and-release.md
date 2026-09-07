# 08 — 测试、诊断与发布工程

> Status: Planned
> Priority: P0 / Ongoing
> Depends on: All feature tracks

## 目标

建立能区分“协议正确”“平台正确”“真实 Codex 生效”和“用户体验可用”的测试体系，避免用单元测试或 transport ACK 代替端到端证据。

## 待办

### 测试分层

- [ ] Domain：手势、映射、路由、安全和状态聚合纯单元测试。
- [ ] Application：用 fake ports 验证完整 use case 和失败恢复。
- [ ] App Server：按 Codex 版本运行 schema/contract tests。
- [ ] Micro：golden packet、双向重组、指纹和状态机测试。
- [ ] Platform：Windows/macOS 输入、权限、IPC 和生命周期集成测试。
- [ ] Desktop：ViewModel、可达性和截图回归。
- [ ] E2E：真实控制器、真实 Codex build 和干净账户冒烟。

### 测试资产

- [ ] 建立控制器硬件矩阵、Codex 版本矩阵和操作系统矩阵。
- [ ] fixture 不包含用户 prompt、任务正文或凭据。
- [ ] 协议观察、截图和 UIA 快照绑定明确版本与来源。
- [ ] 失败案例进入可复现 regression fixture，不只写在 issue 描述中。

### 基础实机验收（README 基线）

以 [README 中文版](../README.zh-CN.md) 对用户承诺的基础操作为发布基线。每个 release candidate 至少在一个干净账户、当前支持的 Codex build 和真实手柄上逐项执行；高级模型控制可带已知限制发布，但失败现象和证据必须可追踪。

- [ ] 菜单键（Xbox ☰ / Start / `+`）能启动 Codex，或在 Codex 已运行时将它可靠地置于前台；重复按键不创建多余实例。
- [ ] 左摇杆上/下能在同级任务中移动，右进入项目、左退出项目，A 打开当前任务；L3 能依次访问置顶任务、置顶项目、项目和未归项目任务，并实际打开至少一个置顶任务和一个未置顶任务。
- [ ] 右摇杆上/左每次重复只产生一个 `ENC_CW act=2` 档位，右/下只产生一个 `ENC_CC act=2` 档位，并能遍历 Advanced、Fast、Power 等控件；短按 R3 只产生一对 `ENC` down/up，不再进入旧简易/高级状态机。
- [ ] 驱动 `NotSent` 时旋钮才允许走既有降级路径；`Accepted`、`OutcomeUnknown`、`Rejected` 都不得重复注入键盘/UIA，transport ACK 只证明投递，不能冒充界面 readback。
- [ ] 按住 LT 开始录音、松开停止；覆盖短按、正常口述、Codex 失焦和菜单打开场景，确认不会残留录音状态。
- [ ] X 能发送已有输入，并能区分发送成功、仍在输入框和正在运行三种结果。
- [ ] Y 打开动作面板后，连续两次 A 才能清空输入；第一次确认、超时、B 撤回和空输入框都不得误删。
- [ ] R3 建立 Micro 菜单会话后，短按 B 只发送一次 `AG00` tap 并退出当前菜单；运行中长按 B 三秒必须显示完整倒计时并取消任务，提前松开不得取消。
- [ ] 十字键上/下能跳到上一轮/下一轮问答；按住上四秒回到顶部，按住下三秒回到底部，短按和长按不得串扰。
- [ ] Y 后按十字键上能新建任务，且不会残留动作面板状态或误触其他 Y 组合动作。
- [ ] 首页动态教程的六个页签均可用鼠标和键盘切换；真实点按 Y、按住 LB/RB、扣住 RT 以及按下 L3/R3 会自动停留在对应教学页，短按 LB/RB 不得误报为按住层。分别在 1220×800、最小 1060×690、中英文和 Xbox/8BitDo glyph 下做截图回归，L3/R3 必须写明“垂直按下摇杆帽”，不能只显示缩写。
- [ ] 分别验证悬浮 Overlay 可见和 `TopMost=false`/Overlay 被遮挡两种反馈模式；后者必须在 Agent Controller 伴随窗口提供等价的焦点、路径、倒计时和结果反馈。
- [ ] 每项记录前置状态、实际按键序列、期望界面、最终 readback、手柄型号、Codex/应用版本和失败截图或日志；任一 P0/P1 路径失败即阻止 release candidate 晋级。

### 运行时诊断

- [ ] 每个 Action 生成 correlation id、executor、timing、result 和 evidence。
- [ ] 显示 App Server、Micro、Broker、权限和控制器 backend 健康状态。
- [ ] 使用有界 ring buffer，默认不记录敏感内容。
- [ ] 支持一键导出、用户预览和脱敏。
- [ ] 区分 NotSent、AcceptedUnverified、Unknown 和 Failed 的用户文案。

### 编程诊断与性能分析集成

- [ ] 定义与 IDE 无关的 `DeveloperDiagnostic` 契约，至少包含 provider、tool version、workspace、file、line/column、severity、code、message、project、configuration、correlation id 和原始证据位置；Domain/Application 不引用 Visual Studio、Roslyn 或 MSBuild DTO。
- [ ] 首个 provider 使用已固定 SDK 的 `dotnet build` / MSBuild 结构化输出，采集编译错误、警告、项目与目标框架；必须直接执行项目或解决方案，不通过 Visual Studio UI 自动化。
- [ ] 支持导入 SARIF，并把 Roslyn analyzers、编译器、NuGet audit 和后续第三方静态分析结果规范化到同一诊断模型；保留 provider 原始 error code，不用模型生成的分类覆盖工具结论。
- [ ] 错误列表按根因指纹去重和聚合，区分首个失败、级联错误、重复诊断和 stale 结果；用户修复后只重跑受影响项目，避免每次重建完整 solution。
- [ ] 合并 CLI、语言服务器与 IDE 诊断时按 document version、build id 和 provider authority 仲裁；Visual Studio Error List 与 `dotnet build` 命中同一诊断时只显示一项，并保留全部来源证据。
- [ ] 修复建议必须区分编译器/Analyzer 提供的 code fix、项目配置建议和模型推断；自动应用前展示 diff，高风险或跨项目修改仍需用户确认。
- [ ] 为诊断执行设置 workspace allowlist、命令 allowlist、超时、并发和输出大小上限；默认禁止执行仓库脚本、任意 MSBuild target 和诊断建议中的命令。
- [ ] 性能分析首选 `dotnet-counters`、`dotnet-trace` / EventPipe 和 MSBuild binary log 等稳定命令行接口；采集必须显式开启、限时、可取消，并记录目标进程、配置、采样窗口与工具版本。
- [ ] 将“构建性能”和“应用运行性能”分开：前者展示 restore/evaluation/compile target 耗时与增量构建失效原因，后者展示 CPU、GC、分配、线程与关键 Action latency，不把两类数据混成单一分数。
- [ ] 提供可选的 Visual Studio 原生适配器：优先使用进程外 `VisualStudio.Extensibility` 读取活动 solution/document、build event 与 Error List，并支持定位到对应源码；允许与 VS UI 联动，但不得阻塞 IDE UI thread。
- [ ] `vswhere`、MSBuild/toolchain 与 `.sln` 配置发现仍可脱离已启动的 VS 工作；旧 DTE/COM 仅作为版本门禁后的兼容 fallback，不成为诊断核心的必需依赖。
- [ ] 给各 provider 设置性能预算并记录自身开销；文件变化采用 debounce、取消旧运行和项目级缓存，后台诊断不得与用户主动构建争抢并行 MSBuild 节点。
- [ ] 诊断导出默认脱敏绝对路径、用户名、源码片段、命令参数和环境变量；性能 trace 与 binlog 视为可能含源码/路径的敏感产物，导出前单独提示并允许用户预览。
- [ ] 为每个 provider 暴露 Available、Unsupported、Misconfigured、Running、Succeeded、Failed、Cancelled 状态以及明确修复建议；缺少 VS 时仍可使用 dotnet SDK provider。

建议顺序：先落地 `dotnet build + SARIF` 的只读诊断垂直切片，再接 Visual Studio 进程外扩展与去重仲裁，然后做增量重跑、MSBuild binlog 和 EventPipe 性能采集。VS UI 可以作为原生入口，但编译器宿主、诊断聚合和性能采样器仍位于独立 adapter/use case 边界，不直接塞进 Desktop/ViewModel。

#### 2026-09-02：Developer Tools 后端切片

- [x] Application 增加 provider、request、run、diagnostic/evidence/location 和 performance measurement 契约，不引用 VS、Roslyn 或 MSBuild DTO。
- [x] 增加独立 `AgentController.Adapters.DeveloperTools` 项目；首个 provider 只允许 `dotnet build`，限制 workspace/target/configuration、默认 `--no-restore`，并要求调用方显式确认项目执行。
- [x] 编译器/Analyzer SARIF 与 MSBuild 控制台错误进入同一聚合器，按 code、规范化 message、file、line/column 指纹去重并保留多来源证据。
- [x] 输出、SARIF、超时和产物目录均有边界；原始 stdout/stderr 落到单次 build id 目录，可选 binlog 使用 `ProjectImports=None`。
- [x] coordinator 对同一 target 做 debounce，并在新请求到达时取消旧构建；只缓存最新的非取消结果，避免后台诊断堆积。
- [x] 独立 Release 编译通过，0 warnings、0 errors；按仓库约束未新增或运行测试。
- [ ] 待接入宿主 composition、Visual Studio 进程外扩展、Error List 导航、binlog target 耗时解析和 EventPipe 运行时采样。

### CI 与发布

- [ ] Windows 和 macOS 分平台构建、测试和打包。
- [ ] PR 必须通过格式、编译、单元、合同和依赖规则测试。
- [ ] Release candidate 必须通过真机冒烟清单，不能只看 CI 绿色。
- [ ] 建立 Stable、Preview、Experimental Native Components 三个发行通道。
- [ ] 发布说明自动包含支持矩阵、已知限制、校验值和回滚方式。

## 完成门槛

- 任一失败能定位到 Input、Binding、Router、Executor、Transport 或 Verification 层。
- 私有协议版本变化会在发布前被合同测试发现。
- 正式发布同时具备自动化证据和真机验收记录。
- 诊断信息足以支持用户排错，但不泄露用户内容。
- 编译诊断在未安装 Visual Studio 时仍可通过固定的 .NET SDK 工作，且同一问题不会因级联错误或重复 provider 被多次呈现。
- 性能结论可追溯到明确工具、采样窗口和原始证据；未获得用户显式触发时不附加或采样其他进程。
