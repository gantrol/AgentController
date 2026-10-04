# Agent Controller 公开设计索引

这里提供面向使用者和贡献者的设计入口。资料按“系统—操作—界面—协议—验证”分层，避免把产品合同、历史实现和私有协议证据混在同一张表中；单元测试也不能替代真实 Codex + 实体手柄验收。

## 当前设计合同

| 资料 | 内容 |
| --- | --- |
| [系统设计、UML 与指令映射](system-design-and-command-map.md) | 系统上下文、核心类、状态机、Action ID、执行器、物理输入到 wire 的总映射 |
| [手柄操作列表](controller-operations.md) | Base、Y、LB/RB/RT 层和安全规则；面向使用者的操作事实源 |
| [界面与交互设计](interface-design.md) | 主窗口信息架构、Overlay 家族、状态、反馈、主题、无障碍与验收清单 |
| [架构与输入链路](architecture-and-input-flow.md) | 手柄、Micro 设备平面、Broker、驱动、Codex 接收链路与依赖结论 |
| [Codex Micro 实体键盘接入、逆向证据与 UML](codex-micro-physical-connection.md) | USB/Bluetooth、vendor HID、build 指纹、wire/RPC、只读复现，以及实体与虚拟 Micro 的边界 |
| [Codex Micro 指令参考](codex-micro-command-reference.md) | `ENC_CW`、`ENC_CC`、`ACT*`、`AG*`、RPC 与 64-byte report |

## 免驱版实施设计

- [AgentController 免驱版 UML](../../docs/architecture/agent-controller-driverless-uml.zh-CN.md)：现状、目标组件、执行时序、状态机与迁移顺序；共享软件后端及打开任务 / 新建草稿已接入，其余动作待迁移。

## Codex Micro 平台方向

- [当前平台方向](../../docs/architecture/platform-direction.zh-CN.md)：Micro 面向 macOS 桌面版；旧 AgentController Avalonia Foundation Preview 已废弃。
- [设置、键帽编辑与尺寸规则](../../docs/architecture/codex-micro-settings-interaction.zh-CN.md)：macOS 桌面交互提案。
- [历史 UIKit 架构与 UML](../../docs/architecture/codex-micro-ios-uikit-uml.zh-CN.md)：保留先前 iOS 原型记录，不是当前交付目标。

## 历史、证据与验收

- [Codex / Deepseek Keypads v0.2.7 正式版说明](release-keypads-v0.2.7.md)
- [Deepseek Harness Keypad v0.2.6 正式版说明](release-deepseek-harness-keypad-v0.2.6.md)
- [Deepseek Harness Keypad v0.2.5 正式版说明](release-deepseek-harness-keypad-v0.2.5.md)
- [Deepseek Harness Keypad v0.2.4 正式版说明](release-deepseek-harness-keypad-v0.2.4.md)
- [v0.7 手柄指令清单](controller-command-reference-v0.7.md)：仅用于追踪旧实现与规范差异，不作为最新合同。
- [Codex Micro VHF/状态/输入协议证据](../../docs/codex-26.707.12708-vhf-status-input.zh-CN.md)
- [ADR-0002：原生 Micro 兼容决策](../../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md)
- [控制器输入已知问题与实机复现](../../todo/91-controller-input-known-issues.md)

> 注意：Codex Micro HID/RPC 来自特定 Codex Desktop 构建的只读观测，不是公开稳定 ABI。未知构建、未知布局或未知设备身份必须停止原生发送，不能猜测后继续操作。
