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
