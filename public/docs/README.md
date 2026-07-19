# Agent Controller 公开设计索引

这里提供面向使用者和贡献者的简要设计入口。它描述产品合同、输入映射和当前观测到的 Codex Micro 协议；不以单元测试代替真实 Codex + 实体手柄验收。

- [架构与输入链路](architecture-and-input-flow.md)：手柄、Micro 设备平面、Codex 接收链路，以及驱动依赖结论。
- [手柄操作列表](controller-operations.md)：基础层、组合层和安全规则。
- [Codex Micro 指令参考](codex-micro-command-reference.md)：`ENC_CW`、`ENC_CC`、`ACT*`、`AG*`、RPC 与 64-byte report。

更详细或偏实现的材料：

- [v0.7 当前手柄指令清单](controller-command-reference-v0.7.md)
- [Codex Micro VHF/状态/输入协议证据](../../docs/codex-26.707.12708-vhf-status-input.zh-CN.md)
- [ADR-0002：原生 Micro 兼容决策](../../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md)
- [控制器输入已知问题与实机复现](../../todo/91-controller-input-known-issues.md)

> 注意：Codex Micro HID/RPC 来自特定 Codex Desktop 构建的只读观测，不是公开稳定 ABI。未知构建、未知布局或未知设备身份必须停止原生发送，不能猜测后继续操作。
