# 01 — 核心架构拆分

> Status: In Progress
> Priority: P0
> Depends on: 00-product-direction-and-business

## 目标

建立可跨 Windows/macOS、可接入多个 Agent、可替换协议和 UI 的模块化单体，并逐步拆除 `MainWindow.xaml.cs` 与 `CodexComposerService.cs` 的职责聚集。

## 目标依赖方向

```text
Desktop -> Application -> Domain
Platform / Agent adapters -> Domain ports
Native helper <-> versioned local IPC <-> Platform adapter
```

## 待办

### 解决方案骨架

- [x] 新建 `AgentController.sln`、`Directory.Build.props` 和集中包版本管理；net9 Release 基线 587/587 测试通过，见 [ADR-0001 实施记录](../docs/adr/0001-dotnet-10-migration-sequence.zh-CN.md#实施记录)。
- [x] 冻结 [目标项目结构与渐进迁移规则](../docs/architecture/target-project-structure.zh-CN.md)。
- [x] 新建 `AgentController.Domain`，目标框架不带 `-windows`。
- [x] 新建 `AgentController.Application` 和 `AgentController.Platform.Abstractions`。
- [x] 为项目引用建立依赖规则测试，禁止 Domain 反向引用平台或 UI。
- [x] 通过 [ADR-0001](../docs/adr/0001-dotnet-10-migration-sequence.zh-CN.md) 规划迁移到 .NET 10 LTS；升级不得和行为重写混在同一个提交。
- [x] 执行独立的 .NET 10 TFM/SDK/包升级，并保持现有自动化测试无回归；CLI 端 609/609 测试、NuGet audit 与自包含打包均通过。
- [ ] 将开发环境升级到 Visual Studio 2026/MSBuild 18，并在 IDE 内复验 Debug/Release；VS 2022/MSBuild 17.14 无法加载 SDK 10.0.302。

### 核心契约

- [x] 定义动态 `ControlId`，不再以封闭枚举表示所有物理按钮。
- [x] 定义 `Gesture`、`InputContext`、`BindingRule` 和 `ActionId`。
- [x] 定义 `ActionRequest`，包含来源、上下文、幂等键和安全等级。
- [x] 定义 `ActionResult`：Succeeded、NotSent、AcceptedUnverified、Unsupported、Incompatible、Blocked、Failed。
- [x] 定义 `ActionEvidence`，区分传输证据、状态证据和 UI 观察证据。
- [x] 定义 `StateObservation`，包含 source、epoch、sequence、observedAt、confidence。
- [x] 定义执行器能力探测和路由优先级，不把优先级硬编码在 View 中。

### 从现有代码抽离

- [ ] 把 `MainWindow` 中手柄状态机提取为 `ControllerInteractionCoordinator`。
  - [x] 首个无行为变化切片：协调器接管有序状态缓冲、基础/物理按钮历史、LT 滞回、左右摇杆回中门禁与重复节奏，并由 `AppServices` 注入旧 WPF 客户端。
  - [x] 第二个无行为变化切片：基础层按钮边沿解析为有序 `ControllerInteractionIntent`；`MainWindow` 只按顺序分发到现有动作实现。
  - [ ] 继续把 radial/virtual-dial 上下文、组合层、长按生命周期与 Action 发射移出 `MainWindow`，之后再迁入跨平台 Application/Domain 边界。
- [ ] 把输入采样、边沿保留、组合层、长按和动作分发分成独立阶段。
  - [x] 已将 XInput 事件后的状态缓冲、按钮边沿、模拟扳机滞回、摇杆手势和 repeat timing 收敛到可独立测试的协调器阶段。
  - [x] 基础层的 L3/R3、D-pad、ABXY 与 B release 已从 WPF 回调解析中分离为可测试的有序意图；中性轮询帧不分配意图集合。
  - [ ] 分离 radial/virtual-dial 组合层、长按识别与 Domain Action 发射；这些阶段仍在 `MainWindow` 中。
- [ ] 把 `CodexComposerService` 拆成 WindowLocator、PopupProbe、CommandExecutor、ResultVerifier。
- [ ] 将 Sidebar、Composer、Thread 类型从 Codex 专用类型收敛为 Domain 契约。
- [ ] 将 `AppServices` 改为纯 composition root，业务对象不自行构造基础设施。
- [ ] UI 只订阅 Application state 和 command，不直接调用 Win32/UIA/Micro 服务。
  - [x] `thread.open` 首个垂直切片已由 WPF 构造 `ActionRequest`，经 Application `ActionRouter` 选择 Codex Deep Link executor，并消费 `ActionResult`；旧 `IDeepLinks.OpenThread` 直接入口已删除。
  - [x] `thread.create` 第二个垂直切片已删除 WPF 内的 UIA/快捷键回退策略；Codex executor 统一执行“查找 New task 控件，找不到时回退 Ctrl+N”，WPF 只消费 `ActionResult`。
  - [x] `composer.submit` 与 `composer.clear` 共用一个 Codex Composer executor；旧 `IComposerAutomation.Submit/Clear` 直达入口已删除，清空动作在 executor 边界强制要求 `ConfirmationRequired`。
  - [x] 三秒 B 长按后的 `turn.stop` 已接入同一 Composer executor，并在执行边界强制要求 `HighRisk`；短按 B 的菜单关闭、导航撤回和本地取消仍保持独立分流。
  - [x] `thread.fork` 已将 Micro、配置快捷键和 UIA 的有序回退移出 WPF；窗口内仅供 Fork 使用的 `TryExecuteMicroInput` helper 已删除。
  - [x] 动作面板的前进、后退和切换侧边栏已改为 `navigation.forward`、`navigation.back`、`sidebar.toggle`；快捷键映射与执行门禁由同一个 Codex shell executor 持有。
  - [x] 十字键短按的上一条/下一条用户消息已改为 `conversation.previous-user-message` / `conversation.next-user-message`；4 秒回顶与 3 秒到底的长按状态机保持独立。
  - [x] 十字键长按阈值后的回顶/到底已改为 `conversation.scroll-top` / `conversation.scroll-bottom`；异步 executor 只有在 UIA 滚动位置 readback 后才返回 `Succeeded`。
  - [ ] 将 thread availability、foreground gate、undo snapshot 和 UI feedback 收敛到 Application command/state；当前为保持行为不变仍留在 WPF。

### 状态聚合

- [ ] 实现权威源优先级和 stale/epoch 清理。
- [ ] 将 App Server、rollout/unread 和 Micro SlotOnly 保持为可辨识来源。
- [ ] 禁止 UI 颜色、文件 mtime 或“发送成功”推导业务成功。

## 不在本任务中

- 不重写完整 WPF UI。
- 不引入网络微服务、数据库服务器或远程消息队列。
- 不在 Domain 中出现 F17、`v.oai.*`、AutomationElement 或 USB report。

## 完成门槛

- Domain/Application 可在 Windows 和 macOS 目标上编译。
- 现有 WPF 主程序通过 Application 接口完成至少一条完整动作路径。
- `MainWindow` 不再拥有手势解析或执行器选择逻辑。
- 所有动作结果都包含真实执行通道和证据类型。
- 现有 v0.7 回归测试无行为性退化。

## 实施记录

### 2026-07-18：ControllerInteractionCoordinator 首个切片

- 新增旧客户端迁移缝 `ControllerInteractionCoordinator`，由 composition root 持有；`MainWindow` 不再直接拥有 `ControllerStateBuffer`、两个 `StickGestureRouter`、`AxisRepeater`、`AnalogTriggerLatch` 或两套按钮历史。
- 保留现有行为边界：`MainWindow` 仍负责 foreground/session gate、radial/virtual-dial 上下文、长按和具体 Agent 动作；本切片不调整 LT 阈值、右摇杆映射或模型选择器逻辑。
- 新增 5 项协调器合同测试；聚焦输入回归 33/33，通过完整 Release solution 测试 614/614（旧客户端 592、Domain 15、Architecture 7）。
- 语音键与右摇杆偶发失效均作为既有运行时问题留在 `90-v0.7-maintenance.md`，不阻塞本次架构迁移。

### 2026-07-18：基础按钮意图切片

- 新增值类型 `ControllerInteractionIntent`，将基础层按钮 down/up、R3 物理 release、会话上下轮导航和侧边栏方向解析从 `MainWindow` 移入协调器；继承层级已压缩为 enum + payload，避免为每个意图分配对象。
- `MainWindow` 改为顺序执行意图，保留旧路径的执行顺序、dial-context A 键分流、R3 suppression、D-pad hold release 和 B release 语义。
- 新增 8 项意图合同测试，覆盖并发边沿顺序、held 去重、上下文分流、被 suppression 的 R3 press/release、会话 hold release 与中性帧零集合分配；完整 Release solution 测试 622/622（旧客户端 600、Domain 15、Architecture 7）。

### 2026-07-18：`thread.open` 首个 Application 垂直切片

- Application 新增按 capability priority 选择执行器的 `ActionRouter`；无可用执行器时保留 Blocked、Incompatible 与 Unsupported 的最强失败语义，并校验 capability/result 身份一致性。
- WPF 的控制器 A、鼠标双击、键盘 Enter、Agent slot 和相邻任务入口现在统一构造 `thread.open` `ActionRequest`；Codex adapter 将 Deep Link 接受映射为 `AcceptedUnverified`，只报告真实的 Transport evidence，不冒充线程已经打开。
- 删除 `IDeepLinks.OpenThread` 与 `CodexAgentTarget` 的旧直接入口，避免新旧路径并存；foreground、thread availability、undo snapshot 与本地反馈暂留 `MainWindow` 以保持现有行为。
- 新增 5 项 Application router 测试和 4 项 Codex executor 合同测试；完整 Release solution 测试 630/630（旧客户端 603、Application 5、Domain 15、Architecture 7）。
- README 的“按 A 打开任务”仍需实机复验后，才能把该路径视为用户侧验收完成。

### 2026-07-18：`thread.create` 第二个 Application 垂直切片

- WPF 的 Y → 十字键上入口现在构造 `thread.create` `ActionRequest`，原先位于 `MainWindow` 的多语言 UIA 控件匹配、ElementNotFound 判断和 `Ctrl+N` 回退策略已删除。
- Codex executor 保留原执行顺序；UIA 控件调用记录为 `UiObservation/thread.create.control-invoked`，快捷键注入记录为 `Transport/thread.create.shortcut-sent`，两者均返回 `AcceptedUnverified`，不冒充新任务已被界面确认。
- 新增 5 项 executor 合同测试，覆盖 UIA 成功、快捷键回退、注入失败、不可回退错误和 capability 缺失；完整 Release solution 测试 635/635（旧客户端 608、Application 5、Domain 15、Architecture 7）。
- README 的“Y 后按十字键上新建任务”仍需实机复验；现有 LT 语音键与右摇杆问题不在本切片内。

### 2026-07-18：Composer submit/clear 垂直切片

- X 发送与动作面板双确认清空现在都经由统一的 WPF → Application action 网关和同一个 Codex Composer executor；没有为每个 Composer 动作复制独立 executor 类。
- `composer.submit` 的 Ctrl+Enter 注入只返回 `AcceptedUnverified` 与 `Transport/composer.submit.shortcut-sent`；`composer.clear` 只有在旧服务完成文本清空 readback 后才返回 `Succeeded` 与 `UiObservation/composer.clear.verified`。
- `composer.clear` 在 executor 边界拒绝低于 `ConfirmationRequired` 的请求，保留并加固原有双 A 确认；`IComposerAutomation.Submit/Clear` 及 Codex/null-object adapter 的旧入口已删除。
- composition root 现在只加载一次当前设置，WPF 和所有 action executor 共享同一实例，避免执行时重新读取磁盘产生瞬时配置漂移。
- 新增 9 项 executor 合同测试；完整 Release solution 测试 644/644（旧客户端 617、Application 5、Domain 15、Architecture 7）。README 的 X 发送和双确认清空仍需实机复验。
- `Cancel` 暂不迁移：它承载短按 B 的菜单关闭、录音中止、导航撤回和 UIA/Escape fallback，必须先拆清本地取消与业务 Action，而不能直接等同于停止任务。

### 2026-07-18：`turn.stop` 高风险垂直切片

- B 键语义被明确拆分：三秒长按完成后才构造 `turn.stop` `ActionRequest`；短按仍优先处理录音、本地选择、导航撤回和菜单关闭，不会误用停止任务 Action。
- `ComposerAutomationResult` 新增 `Channel` 与 `StateVerified`，旧服务现在明确报告 UIA、键盘输入和清空状态回读；成功但缺少预期通道时 executor 失败关闭为 `action.evidence.missing`。
- `turn.stop` 在 executor 边界要求 `HighRisk`，UIA Stop/Cancel 按钮调用只返回 `AcceptedUnverified` 与 `UiObservation/turn.stop.control-invoked`，不宣称任务状态已经停止。
- `StopCurrentTurn` 中直接调用 `IComposerAutomation.InvokeAction` 的旧执行路径已删除；短按所用 `CancelComposer` 暂留，因为其 UIA/Escape fallback 属于不同的本地取消语义。
- 新增 3 项合同测试，并加强 Submit、Clear、Create 对通道证据的断言；完整 Release solution 测试 647/647（旧客户端 620、Application 5、Domain 15、Architecture 7）。README 的三秒长按停止仍需实机 readback 验收。

### 2026-07-18：`thread.fork` 多通道回退切片

- Command/Turn 面板的 Fork 现在统一发射 `thread.fork` routine 请求；Codex executor 保留原顺序 Micro → 用户配置快捷键 → 多语言 UIA 控件，并在 Bridge/前台门禁失败时不触碰任何执行通道。
- Micro bool 同时覆盖 broker Accepted 与 OutcomeUnknown，因此只报告 `Transport/thread.fork.micro-requested`；快捷键报告 `Transport/thread.fork.shortcut-sent`，UIA 报告 `UiObservation/thread.fork.control-invoked`，三者均为 `AcceptedUnverified`。
- `MainWindow` 内的三层选择逻辑和仅供 Fork 使用的 `TryExecuteMicroInput` 已删除；保留原有 Micro/快捷键快速路径日志与 UIA 反馈行为。
- 四个 Codex executor 共享仅处理 adapter 协议样板的 `CodexActionExecutorBase`；具体 action capability、安全等级和 fallback 顺序不进入基类，避免用继承隐藏业务策略。
- 新增 6 项 executor 合同测试，覆盖顺序短路、两级 fallback、UIA action names、门禁、NotSent 与 capability 缺失；完整 Release solution 测试 653/653（旧客户端 626、Application 5、Domain 15、Architecture 7）。Fork 仍需实机确认最终新任务 readback。

### 2026-07-18：动作面板 shell action 切片

- 前进、后退和切换侧边栏现在分别发射 `navigation.forward`、`navigation.back` 与 `sidebar.toggle`；`MainWindow` 不再保存 `Ctrl+]`、`Ctrl+[`、`Ctrl+B` 的 Codex 快捷键映射。
- 三个 routine action 共用 `CodexShellActionExecutor`，并与 Fork 复用 composition root 的 Bridge/前台门禁；快捷键注入只返回 `AcceptedUnverified` 与对应的 `Transport/*.shortcut-sent` evidence，注入失败返回 `NotSent`。
- 新增 7 项 executor 合同测试，覆盖全部映射、门禁短路、注入失败、transport 缺失和未知 action；完整 Release solution 测试 661/661（旧客户端 634、Application 5、Domain 15、Architecture 7）。动作面板仍需实机复验 Codex 的历史导航与侧边栏状态。

### 2026-07-18：会话轮次短按导航切片

- `ConversationTurnInputMap` 不再暴露 `Alt+Up` / `Alt+Down`，而是把短按意图映射为 `conversation.previous-user-message` / `conversation.next-user-message`；具体快捷键由现有 `CodexShellActionExecutor` 追加映射。
- D-pad down edge 仍同时启动原有 boundary hold 计时；只有持续 4 秒/3 秒才分别进入回顶/到底路径，本切片没有迁移或修改该长按自动化。
- shell executor 新增 2 组映射合同数据，完整 Release solution 测试 663/663（旧客户端 636、Application 5、Domain 15、Architecture 7）。README 的短按与长按组合仍需实机验收。

### 2026-07-18：会话边界长按 action 切片

- 4 秒回顶与 3 秒到底的 hold lifecycle 仍由原协调阶段判断阈值和提前释放；阈值满足后分别发射 `conversation.scroll-top` / `conversation.scroll-bottom`，WPF 不再直接调用 Composer UIA port。
- `CodexActionExecutorBase` 的核心执行模板改为 `ValueTask`，同步 executor 继续返回 completed result，新 `CodexConversationActionExecutor` 则等待真实 UIA Task 与取消令牌，没有同步阻塞 Dispatcher。
- 滚动服务成功现在明确报告 `UiAutomation + StateVerified`；executor 只有同时得到这两项证据才返回 `Succeeded` 与 `UiObservation/*.verified`，否则以 `action.evidence.missing` 失败关闭。
- 旧 `IComposerAutomation.ScrollConversationAsync`、Codex adapter 转发和 null fallback 已删除；新增 14 个测试案例后，完整 Release solution 测试 677/677（旧客户端 650、Application 5、Domain 15、Architecture 7）。README 的短按/长按组合仍需实机验收。
