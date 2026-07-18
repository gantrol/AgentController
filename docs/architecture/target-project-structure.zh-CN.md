# Agent Controller 目标项目结构

> Status: Architecture baseline
> Migration style: Modular monolith, incremental strangler

## 目的

项目保持单仓库和单进程优先，但把业务契约、用例、Agent 适配器、平台能力与桌面 UI 分开。目录边界必须能由项目引用测试验证，而不是只依赖命名约定。

最终结构不要求一次性创建或搬迁。现有 `app/` 与 `app.Tests/` 在迁移期间继续承担可发布的 WPF v0.7 客户端；新模块先进入 `src/` 和 `tests/`，旧代码只随已验收的垂直切片逐步迁移。

## 目标树

```text
AgentController.sln
Directory.Build.props
Directory.Packages.props
global.json                         # 锁定已验证的 .NET 10 SDK feature band

src/
  AgentController.Domain/
  AgentController.Application/
  AgentController.Platform.Abstractions/

  AgentController.Adapters.Codex.AppServer/
  AgentController.Adapters.Codex.Automation.Windows/
  AgentController.Adapters.Micro/

  AgentController.Platform.Windows/
  AgentController.Platform.MacOS/

  AgentController.Desktop/          # Avalonia，共享 Windows/macOS UI
  AgentController.Desktop.Wpf/      # 旧客户端达到等价后才由 app/ 迁入

tests/
  AgentController.Domain.Tests/
  AgentController.Application.Tests/
  AgentController.Architecture.Tests/
  AgentController.Adapters.Codex.Tests/
  AgentController.Adapters.Micro.Tests/
  AgentController.Platform.Windows.Tests/
  AgentController.Desktop.Tests/
  AgentController.E2E.Tests/

native/
  windows/                           # 仅在必须隔离驱动/进程位数时建立 helper
  macos/                             # 权限、IOKit/DriverKit 边界 helper

packaging/
  windows/
  macos/

docs/
  adr/
  architecture/
  research/
  ux/

scripts/
public/
todo/

app/                                # 迁移期保留的当前 WPF 可发布项目
app.Tests/                          # 迁移期保留的当前回归测试
```

## 项目职责与依赖

| 项目 | 只负责 | 可以引用 |
| --- | --- | --- |
| `Domain` | 输入、手势、Action、风险、结果、证据、状态观察和 Agent 无关实体 | BCL |
| `Application` | 用例、路由、协调、状态聚合和业务端口 | Domain、Platform.Abstractions |
| `Platform.Abstractions` | 窗口、前台、输入设备、权限、生命周期等 OS 能力合同 | Domain |
| `Adapters.*` | Codex App Server、Windows UIA、Micro codec/transport 等外部系统实现 | Application/Domain 中拥有的端口、必要的平台抽象 |
| `Platform.Windows/macOS` | XInput/Raw HID、Win32、IOKit、权限和本地 IPC | Platform.Abstractions、Domain |
| `Desktop` | Avalonia View、ViewModel、presentation state 和 composition root | Application、Platform.Abstractions |
| `Desktop.Wpf` | 迁移期 WPF presentation 与 composition root | Application、Windows adapter |

强制依赖方向：

```text
Desktop.Wpf ─┐
Desktop ─────┼──> Application ──> Domain
             │          │
             │          └──────> Platform.Abstractions ──> Domain
Adapters ────┴────────────────────────────────────────────> ports/contracts
Platform implementations ────────────────────────────────> Platform.Abstractions
```

禁止 Domain 出现 WPF、Avalonia、Win32、UIA、XInput、USB report、F17、`v.oai.*` 或 Codex App Server DTO。Adapter 负责翻译，不能把外部协议类型向内泄漏。

## 项目内部组织

优先按能力组织，而不是在每个项目中复制巨型 `Models/Services/Helpers`：

```text
AgentController.Domain/
  Inputs/
  Gestures/
  Actions/
  Observations/
  Agents/
  Devices/

AgentController.Application/
  ControlSurface/
  Sidebar/
  Composer/
  Threads/
  Approvals/
  Diagnostics/
```

一个 Feature 可以包含其 Command、State、Policy 和 Handler。共享类型只有在至少两个 Feature 形成稳定共同语义后才上移，避免提前建立 `Common` 垃圾桶。

## 现有代码的迁移归属

| 当前区域 | 目标归属 | 迁移规则 |
| --- | --- | --- |
| `app/Controllers` 中纯手势和映射策略 | Domain 或 Application | 先补纯单元测试，再消除 WPF/Win32 引用 |
| `app/Core/Bridge` | Application/ControlSurface | 保留 event epoch、drain 与 foreground 语义 |
| `app/Services/Codex*` | Codex App Server 或 Automation adapter | 按 WindowLocator、PopupProbe、CommandExecutor、ResultVerifier 拆分 |
| `app/Services/Micro` | Adapters.Micro | codec、transport、设备指纹和 ABI 版本彼此分离 |
| `app/Native`、`XInputService` | Platform.Windows | 只向上暴露平台抽象和逻辑输入快照 |
| `app/Views`、`ViewModels` | Desktop.Wpf；未来替换为 Desktop | ViewModel 只调用 Application facade |
| `MainWindow.xaml.cs` | composition root + 待抽离职责 | 不按文件整体搬迁，按可回滚垂直切片缩小 |

## 测试组织

- `Domain.Tests` 不需要 Windows TFM，也不访问文件系统、网络或 UI。
- `Architecture.Tests` 读取程序集依赖，禁止 Domain/Application 反向引用桌面、平台实现和 adapter。
- Adapter contract tests 使用固定版本 fixture；真实 Codex/UIA 观察进入 Integration/E2E，而不是伪装成单元测试。
- `E2E.Tests` 对应 README 实机验收步骤，记录控制器、Codex build、OS、结果 readback 和证据。
- 迁移期 `app.Tests` 保持原位置；测试随被迁移的生产能力一起迁入新项目，不做纯目录大搬家。

## 渐进落地顺序

1. 在 net9 不改行为的前提下建立 solution、共同构建属性和集中包版本。
2. 单独升级到 .NET 10 LTS并锁定 SDK；该提交只允许构建/包兼容修复。
3. 创建 Domain、Application、Platform.Abstractions 与 Architecture.Tests 空骨架。
4. 选择一条可验证动作路径，先定义合同，再从 WPF 中抽离协调器并由旧 UI 调用。
5. 将 Codex App Server、UIA 和 Micro 作为可替换执行器接到同一 Action/Result/Evidence 契约。
6. 自定义绑定和新 UI 只消费稳定 Application facade；达到功能等价后再移动/删除旧 `app/`。

任何阶段都必须保持当前发布路径可构建、基础实机验收可执行并能回滚到上一个结构。

## 实施状态

### 2026-07-17：核心骨架与合同基线

- `AgentController.Domain`、`AgentController.Application` 和 `AgentController.Platform.Abstractions` 已以跨平台 `net10.0` 项目加入 solution。
- `AgentController.Architecture.Tests` 已强制核心项目引用白名单、无 Windows TFM/WPF/WinForms 依赖以及 Domain 无第三方包依赖。
- Domain 已建立动态 ControlId/ActionId、Gesture、InputContext、BindingRule、ActionRequest、七态 ActionResult、三类 ActionEvidence 与 StateObservation 合同。
- Application 已建立 `IActionExecutor` 和带状态/优先级的 capability probe 合同；尚未实现路由器或接入旧 WPF。
- 自动化证据：旧客户端 587 tests、架构规则 7 tests、Domain 合同 15 tests，共 609 passed、0 failed、0 skipped。
- .NET 10 CLI build、609 项测试、NuGet audit 和 win-x64 自包含发布均通过；发布 ZIP 为 81,762,632 bytes，SHA-256 为 `3afa0fbe79355c51d8504e0d9bfa644a3ab9508480dfef6fee6ecf5aff1cbe17`，验证后已清理临时产物。IDE 构建要求 Visual Studio 2026/MSBuild 18；VS 2022 不列入 net10 开发工具支持矩阵。

### 2026-07-18：旧 WPF 输入协调器迁移缝

- `ControllerInteractionCoordinator` 已接管旧客户端的状态缓冲、基础/物理按钮历史、LT 滞回、摇杆回中门禁和 repeat timing，并由 `AppServices` 注入 `MainWindow`。
- 该类暂留在旧 `app/` 中：它仍消费迁移期 `ControllerState` 和策略类型，等逻辑输入快照与上下文合同稳定后，再整体迁往 Application/Domain，避免用一次大搬家混入行为变化。
- `MainWindow` 仍拥有 foreground/session、radial/virtual-dial、长按和 Action 执行，所以“抽离手柄状态机”仍为进行中，而非完成。
- 自动化证据更新为旧客户端 592 tests、Domain 15 tests、Architecture 7 tests，共 614 passed、0 failed、0 skipped；新增的 5 项合同覆盖缓冲顺序、双按钮历史、LT 滞回、回中门禁和 repeat reset。

### 2026-07-18：基础按钮到意图的边界

- 基础层的 L3/R3、D-pad、ABXY 与 B release 现由协调器解析为有序的值类型 `ControllerInteractionIntent`，`MainWindow` 只负责把意图分发到仍未迁移的 WPF/Agent 动作实现。
- 意图解析保持原执行顺序与 dial/suppression/release 语义；没有改动 LT 阈值、右摇杆轴路由、模型选择器或 Agent 自动化通道。
- 中性和 held 帧复用空意图集合，避免在控制器高频轮询路径上引入每帧集合分配。
- 自动化证据更新为旧客户端 600 tests、Domain 15 tests、Architecture 7 tests，共 622 passed、0 failed、0 skipped。

### 2026-07-18：`thread.open` Application 垂直切片

- 旧 WPF 项目已引用 Application，并将打开任务表达为包含来源、上下文、幂等键和 thread id 的 Domain `ActionRequest`。
- Application `ActionRouter` 根据 capability priority 选择执行器并保留最强失败状态；Codex Deep Link adapter 返回 `AcceptedUnverified` 和 `Transport/thread.open.requested` evidence，不将进程启动结果描述为线程状态成功。
- `IDeepLinks.OpenThread` 旧直接入口已删除；控制器 A、鼠标双击、键盘 Enter、Agent slot 与相邻任务入口共用同一 Action 路径，不保留双写执行通道。
- 为避免行为漂移，foreground gate、workspace availability、navigation undo snapshot 与本地反馈仍由 WPF 管理，后续随 sidebar state/use case 一起迁移。
- 自动化证据更新为旧客户端 603 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 630 passed、0 failed、0 skipped；README 实机打开任务步骤尚待复验。

### 2026-07-18：`thread.create` Application 垂直切片

- 新建任务现在与打开任务共用 Domain `ActionRequest` → Application `ActionRouter` → Codex executor → `ActionResult` 路径；`MainWindow` 不再选择 UIA 与快捷键执行器。
- Codex executor 内聚原有的多语言 New task 控件调用和仅在 ElementNotFound 时启用的 `Ctrl+N` 回退，避免迁移时改变安全门禁与错误分支。
- UIA 调用与快捷键发送分别报告 `UiObservation` 和 `Transport` evidence，成功请求均为 `AcceptedUnverified`；是否真的进入空白任务仍需后续状态观察器确认。
- 自动化证据更新为旧客户端 608 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 635 passed、0 failed、0 skipped；README 实机新建任务步骤尚待复验。

### 2026-07-18：Composer submit/clear Application 垂直切片

- `composer.submit` 与 `composer.clear` 共享 Codex Composer executor，因为二者已形成稳定的同通道结果映射；没有为每个 Action 建立继承层级或独立样板 executor。
- WPF 内四条已迁移动作统一通过一个 action gateway 构造来源、上下文、幂等键、安全级别和时间戳，并将路由异常收敛为 presentation failure；窗口不再直接调用 Submit/Clear adapter。
- 清空请求必须携带 `ConfirmationRequired`，且旧 UI 保持双 A 确认；发送只报告快捷键已注入，清空则要求 UIA 文本 readback，二者因此分别映射为 `AcceptedUnverified` 与 `Succeeded`。
- `AppServices` 持有唯一当前设置实例并同时提供给 WPF 与 executor，避免每次动作重新读盘；它仍是迁移期 composition root，后续再将可变设置状态移入 Application state。
- 自动化证据更新为旧客户端 617 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 644 passed、0 failed、0 skipped；README 的发送与清空步骤尚待实机复验。

### 2026-07-18：`turn.stop` 与执行通道证据

- `ComposerAutomationResult` 在迁移期 adapter 边界增加 `Channel` 和 `StateVerified`；Submit 报告 KeyboardInput，Clear 报告 UIA + readback，Stop 报告 UIA invocation，Unknown 成功不再被 executor 接受。
- 三秒 B 长按完成后由 WPF 发射 `turn.stop` HighRisk 请求；Composer executor 同时执行安全级别复核，低风险请求在触碰 UIA 前即返回 Blocked。
- Stop 的 UIA 调用是请求证据而非状态证据，因此结果为 `AcceptedUnverified`；后续应由 App Server 或独立状态观察器确认任务确已停止。
- 短按 B 的本地取消状态机没有并入 `turn.stop`，避免菜单关闭、录音中止和导航撤回意外升级为高风险业务动作。
- 自动化证据更新为旧客户端 620 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 647 passed、0 failed、0 skipped；README 的长按停止步骤尚待实机复验。

### 2026-07-18：`thread.fork` fallback adapter

- Fork 的 Micro HID、用户配置快捷键与 UIA action names 现在属于 Codex adapter policy；WPF 只提供来源/上下文并消费最终 `ActionResult`，不再决定 executor 顺序。
- capability probe 在执行前应用 Bridge 和前台门禁；成功后只保留实际命中通道的一条 evidence，失败的前置尝试不被描述为已发送。
- Micro transport 的旧 bool 无法区分 Accepted 和 OutcomeUnknown，所以 evidence 使用保守的 `micro-requested`，并保持 `AcceptedUnverified`；后续 Micro adapter 应直接暴露三态结果而非 bool。
- `TryExecuteMicroInput` 随唯一调用方一起删除；新的 Fork executor 是具备真实三通道策略的 adapter，不是为单个 Action 建立的空壳层。
- 四个真实 Codex executor 出现稳定重复后，公共的取消检查、结果/evidence 构造和自动化错误映射才下沉到 `CodexActionExecutorBase`；各动作的 capability 与 fallback policy 仍留在具体 adapter 中。
- 自动化证据更新为旧客户端 626 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 653 passed、0 failed、0 skipped；Fork 最终状态仍需实机 readback。

### 2026-07-18：动作面板 shell action adapter

- Application 增加 `navigation.back`、`navigation.forward` 与 `sidebar.toggle` 合同；它们描述用户意图，不暴露 Windows 快捷键。
- `CodexShellActionExecutor` 负责三项意图到 `Ctrl+[`、`Ctrl+]`、`Ctrl+B` 的映射，并在执行前应用 Bridge/前台 capability gate；WPF 只消费 `ActionResult` 和呈现本地反馈。
- 键盘注入成功是 transport evidence，不是 UI 状态 readback，因此统一返回 `AcceptedUnverified`；后续 App Server 或平台观察器可在不改 UI 调用方的情况下替换权威执行通道。
- 自动化证据更新为旧客户端 634 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 661 passed、0 failed、0 skipped。

### 2026-07-18：会话短按导航 action

- D-pad 上/下短按现在发射 `conversation.previous-user-message` / `conversation.next-user-message`，legacy input map 不再知道 `Alt+Up` / `Alt+Down`。
- 两项意图复用 Codex shell adapter 与 transport evidence；对应的 4 秒回顶、3 秒到底仍是单独的 hold lifecycle，避免短按迁移改变长按边界。
- 自动化证据更新为旧客户端 636 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 663 passed、0 failed、0 skipped。

### 2026-07-18：异步会话边界 action adapter

- `conversation.scroll-top` / `conversation.scroll-bottom` 进入 Application action 链；旧 UI 的 hold threshold 与 release cancellation 保持不变，阈值后的执行策略移入 Codex adapter。
- executor 公共模板支持异步 `ValueTask` 核心并透传 cancellation；同步 Open/Create/Fork/Composer/Shell executors 仍使用 completed result，不为异步能力复制第二套协议样板。
- Conversation executor 等待 UIA 滚动百分比 readback，只有 `UiAutomation + StateVerified` 才产生 `Succeeded`；旧 `IComposerAutomation.ScrollConversationAsync` 迁移接口随唯一调用方删除。
- 自动化证据更新为旧客户端 650 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 677 passed、0 failed、0 skipped。

### 2026-07-18：routine UI command adapter

- `approval.decline`、`turn.steer`、`turn.queue` 进入 Application action 链；多语言 UIA control names 从 WPF switch 移入 `CodexUiCommandActionExecutor`。
- 三项调用只产生 `UiObservation/*.control-invoked` 与 `AcceptedUnverified`，不把按钮 Invoke 冒充业务状态已改变；缺失 UIA channel 时以 `action.evidence.missing` 失败关闭。
- `RouteCapability` 统一 adapter 的 supported/route available/block gate 样板，Create、Fork、Shell、Conversation 与 UI command 仍各自声明真实通道和 fallback policy。
- 该切片暂未迁移 Approve：产品安全规范要求先具备二次确认或长按，因此当时有意保留单一 legacy seam，随后由下一个高风险切片收口。
- 最新 688 项基线以 687 项 solution 排除已知竞态后全绿，加该竞态测试隔离 1/1 通过；Release 构建 0 warnings、0 errors。

### 2026-07-18：Approve 双确认与高风险 action 边界

- `approval.accept` 已进入现有 Application action 链；旧 WPF 第一次按确认键只进入 2.5 秒待确认态，第二次同动作才构造 `HighRisk` 请求，任何中间动作、超时或 radial layer reset 都会清除确认权。
- `RadialActionConfirmationState` 同时服务 Approve 与清空输入，窗口只管理展示和 timer lifetime；后续迁出 radial coordinator 时无需再拆两套确认标志。
- `CodexUiCommandActionExecutor` 持有 Approve/Accept/Allow 等 UIA action names，并复用 `RouteCapability` 的安全等级检查；低安全级别在自动化前返回 `action.high-risk-confirmation-required`。
- 成功 Invoke 只生成 `AcceptedUnverified` 和 `UiObservation/approval.accept.control-invoked`。真正的批准状态、选中任务和 approval context 仍须由后续 App Server/ContextResolver 提供权威观察。
- `MainWindow.ExecuteApproveAction` 及最后一条 named UIA 同步直连路径已删除；当前剩余的语音与模型选择自动化属于已记录的独立兼容问题，本切片没有改动。
- 自动化证据为旧客户端 670 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 697 项；采用 696 项排除已知竞态后全绿 + 该项隔离 1/1 的可审计拆分，Release 构建 0 warnings、0 errors。

### 2026-07-18：navigation undo 语义执行边界

- `navigation.undo` 表达“撤回刚刚确认到达的任务跳转”，与动作面板的通用 `navigation.back` 分开：前者保留语义 Back 控件 UIA，后者仍使用官方快捷键 `Ctrl+[`。
- WPF 继续负责当前标题与目标标题一致性校验、确认轮询、有效期和反馈，但通过 Application action 网关执行撤回；后续可在不改 executor 的前提下把这些状态移入 navigation use case。
- Codex adapter 成功只产生 `UiObservation/navigation.undo.control-invoked` 和 `AcceptedUnverified`，不把按钮调用当作已返回上一任务；失败继续保留 Blocked、NotSent、Unsupported 与 Failed 的终态区分。
- 旧 `ISidebarAutomation.GoBack` 展示层端口及两套 adapter 样板已删除；UIA 服务由 composition root 直接注入 action executor。
- 自动化证据为旧客户端 678 tests、Application 5 tests、Domain 15 tests、Architecture 7 tests，共 705 项；采用 704 项排除已知竞态后全绿 + 该项隔离 1/1，Release 构建 0 warnings、0 errors。

### 2026-07-18：Application dispatcher facade

- `ActionDispatcher` 位于 Application，接收 action id、设备/控件来源、输入上下文、幂等 scope、安全级别和参数；它统一生成 request id、时间戳以及最终 `ActionRequest`。
- `ActionRouter` 保持为纯 executor selection 组件，不承担展示异常处理或 UI feedback；composition root 构造 router 后只把 dispatcher 提供给旧 WPF。
- `MainWindow` 不再引用 `ActionSource`、`ControlId`、`InputContext`，也不再自行拼接 `{scope}:{requestId}`。当前保留的 `TryExecuteActionAsync` 仅把 Application 异常收敛为本地失败反馈。
- 这一层是未来自定义按钮、Avalonia 与 macOS UI 的稳定发射入口；平台 UI 无需知道 Codex executor registry 或 request metadata 生成规则。
- 自动化证据为旧客户端 678 tests、Application 8 tests、Domain 15 tests、Architecture 7 tests，共 708 项；采用 707 项排除已知竞态后全绿 + 该项隔离 1/1，Release 构建 0 warnings、0 errors。

### 2026-07-18：navigation undo session

- 旧窗口内嵌 mutable state 与静态 press policy 已收敛为 `NavigationUndoSession`，由单一对象维护目标 identity、arrival confirmation、expiry 和 queued undo request。
- WPF 只调用 `MarkConfirmed` 与 `RequestUndo`，不再直接组合 `Confirmed` / `ExpiresAt` / `UndoRequested` 字段；Queue、Execute、Expire 三态现在具有独立合同测试。
- session 暂留旧 `app/Controllers`，因为当前 title observation 仍依赖 Codex UIA 且反馈仍是 WPF 本地化逻辑；等 observer port 稳定后再整体移入 Application navigation use case。
- 自动化证据为旧客户端 680 tests、Application 8 tests、Domain 15 tests、Architecture 7 tests，共 710 项；采用 709 项排除已知竞态后全绿 + 该项隔离 1/1，Release 构建 0 warnings、0 errors。

### 2026-07-18：Composer catalog/config 边界

- `CodexComposerService` 的第一条拆分边界选择无副作用的 catalog/config 读取：新增 `CodexComposerCatalogService`，持有模型缓存、TOML 偏好、标签格式化与初始选项解析。
- UIA 自动化服务不再直接访问 `CODEX_HOME` 或解析 JSON/TOML；其 `LoadCatalog` 暂为迁移转发，后续 composition root 可把目录端口直接提供给 UI/Application。
- 公共选择值规范化收敛到 `ComposerChoiceNormalizer`，保证目录解析与尚未迁出的 dial/picker 路径使用同一匹配规则。
- 真实临时目录夹具覆盖过滤、排序及选择优先级；最新 711 项基线采用 710 项排除已知竞态后全绿 + 该项隔离 1/1，Release 构建 0 warnings、0 errors。

### 2026-07-18：Application thread navigation use case

- `AgentController.Application.Navigation` 新增 `ThreadNavigationCoordinator`：以 primitive/delegate ports 接收 foreground、workspace availability 与当前标题观察，不引用 WPF、UIA 或 Codex 数据类型。
- `thread.open` 的门禁、dispatch、唯一标题判断、连续到达确认和撤回 session 现在构成一个完整 Application use case；`navigation.undo` 仍通过既有 action router 选择 Codex adapter。
- UI 订阅类型化 notice 并负责本地化文本、列表刷新与震动；窗口不再拥有 navigation session、确认 CTS、轮询 deadline 或 executor outcome 分支。
- 6 个 Application 场景测试替代 6 个旧客户端 session 测试，基线保持 711 项：710 项排除已知 dispose/queue 竞态后全绿 + 该项隔离 1/1；Release 构建 0 warnings、0 errors。
