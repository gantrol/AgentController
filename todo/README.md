# Agent Controller 路线图

本目录按“大任务”组织未来工作。数字前缀表示建议阅读和实施顺序，不代表所有任务必须串行完成。

## 工作原则

- 产品定位是“任意控制器驱动的 Agent 控制面”，不是只复制某一款硬件。
- 全面采用 Micro 的交互模型，但私有 HID ABI 只存在于版本化兼容适配层。
- Thread、Turn、审批和流式状态优先使用 Codex App Server 等权威接口。
- Domain 不引用 WPF、Win32、UI Automation、XInput、USB 报文字节或 Codex 私有类型。
- 保持单仓库和模块化单体，不拆成网络微服务。
- 使用渐进迁移；旧 WPF 客户端在新客户端达到功能等价前继续维护。
- 未验证的传输成功不能显示为业务成功，高风险动作不得盲目自动降级。

## 文件索引

| 顺序 | 大任务 | 当前阶段 | 主要依赖 |
| --- | --- | --- | --- |
| 00 | [产品方向与商业验证](00-product-direction-and-business.md) | In Progress | 无 |
| 01 | [核心架构拆分](01-core-architecture.md) | In Progress | 00 的定位结论 |
| 02 | [Codex App Server 集成](02-codex-app-server.md) | Planned | 01 的 Action/State 契约 |
| 03 | [Codex Micro 兼容层](03-codex-micro-compatibility.md) | Research / Partial | 01 的执行器契约 |
| 04 | [自定义按键与设备 Profile](04-custom-bindings-and-device-profiles.md) | Planned | 01 的输入和 Action 契约 |
| 05 | [桌面 UI/UX 与 Avalonia](05-desktop-ui-ux-and-avalonia.md) | Planned | 01、04 |
| 06 | [macOS 平台](06-macos-platform.md) | Planned | 01、02、05 |
| 07 | [安全、发行与商业化](07-security-packaging-and-commercialization.md) | Planned | 全局 |
| 08 | [测试、诊断与发布工程](08-testing-observability-and-release.md) | Planned | 全局 |
| 09 | [渐进迁移与兼容策略](09-migration-and-compatibility.md) | In Progress | 01–08 |
| 90 | [v0.7 维护清单](90-v0.7-maintenance.md) | Maintenance | 当前 WPF 版本 |

## 建议执行阶段

### Phase A：先冻结产品与核心契约

- 完成 00 中的定位、用户验证和许可证决策。
- 完成 01 的 Domain/Application 骨架、ActionResult 和 StateObservation。
- 保持 v0.7 对用户可用，不在此阶段重写 UI 或安装驱动。

### Phase B：建立可靠能力

- 用 02 完成一个 App Server 垂直切片。
- 用 03 把现有 Micro codec 移入独立兼容模块，补齐双向协议和指纹门禁。
- 用 04 建立可持久化、可迁移的自定义映射模型。

### Phase C：重做产品体验

- 按 05 的流程先完成信息架构、原型和设计系统，再实施 Avalonia。
- Windows 新客户端达到功能等价后，才进入 06 的 macOS 垂直切片。

### Phase D：发行与可持续维护

- 07、08 贯穿所有阶段，不在最后补安全和测试。
- Windows VHF 与 macOS 虚拟 HID 分别通过 Go/No-Go 门禁后，才考虑正式分发。

## 状态维护约定

每个文件顶部维护 `Status`、`Priority` 和 `Depends on`。任务完成时同时更新：

1. 文件内复选框；
2. 本索引的阶段；
3. 相关 ADR、测试或验收证据链接。

不要仅凭代码已合并将大任务标记完成；必须满足文件中的“完成门槛”。
