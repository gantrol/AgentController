# Codex Micro 虚拟 HID 桥接方案（已被 ADR-0002 取代）

> Status: Superseded on 2026-07-18
> Historical version: 2026-07-16 plan remains available in Git history

原方案把 Windows VHF 描述成硬件原型之后再决定是否投入的可选实验。`virtual-micro/` 已经证明 Micro 类双向 HID 链路可行，产品方向也已明确：原生 Micro 信号是完整控制体验的必需设备平面，不再用不断扩张的 UI Automation 仿制它。

当前有效方案：

- [ADR-0002：以原生 Codex Micro 信号作为完整控制体验主路径](../../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md)
- [Codex Micro 原生兼容层待办](../../todo/03-codex-micro-compatibility.md)
- [26.707.12708 VHF/状态/输入协议证据](../../docs/codex-26.707.12708-vhf-status-input.zh-CN.md)

新决策的核心边界是：

- Windows 软件版正式候选为极小 KMDF/VHF driver + 当前用户低权限 Broker + 独立提权 Device Support 安装器；
- App Server 继续负责 Thread/Turn 语义，不替代 Agent Key、Command Key、Analog、Dial、PTT、灯光和状态的原生设备信号；
- 驱动被策略阻止时只进入明确标记的 Limited mode，不能把 Limited mode 当完整产品；
- 正式发行先解决目标 VID/PID/产品身份授权，再完成 EV、HLK/WHCP、Microsoft signing、一键安装、回滚和精确卸载；
- 普通用户永远不需要 WDK、Visual Studio、PowerShell、自签根证书、TESTSIGNING 或自行编译 driver。

请勿再按历史 M3→M4“先做硬件、再决定是否做 VHF”的顺序执行；新的 Gate、方案对比、手柄映射、安装状态机和验收标准全部以 ADR-0002 为准。
