# Agent Controller v1.0.0

Agent Controller v1 maps a physical gamepad to Codex Micro controls. A single Broker and the optional `CodexMicroVhfUm` UMDF2/VHF device component pass input to Codex's own `codex-micro-service` and `codex-micro-bridge`. Thread, Turn, Steer, Queue, Stop, and other application-level actions that are not Micro device controls continue to use their corresponding adapters.

## Highlights

- The right stick follows one fixed Micro-first contract: up or left emits `ENC_CW`; down or right emits `ENC_CC`; a short R3 press emits `ENC` to open, enter, or confirm.
- In a Micro menu session opened through R3, B emits `AG00`. The current official bridge converts Agent key 1 to Escape in this context and suppresses Agent-slot switching. Native Back fallback is allowed only when driver delivery explicitly returns `NotSent`.
- LT push-to-talk, X submit, and verified Command/Agent slots prefer real Micro signals. No second UIA or keyboard action is injected after `Accepted` or `OutcomeUnknown`.
- Agent Controller and the desktop Micro simulator can coexist through one per-user Broker. The Broker exclusively owns the driver handle, global sequence, and output/RPC reader while separately managing client leases, held keys, PTT release, and analog neutral state.
- Menu enumerates and foregrounds real Codex main windows. When multiple main windows exist, repeated presses cycle between them without selecting tool windows or popups.
- The interactive home tutorial, hierarchical sidebar, action panel, six-slot Agent panel, and bilingual prompts now follow the v1 control contract.

## Installation

1. Download `AgentController-1.0.0-win-x64.zip` and its `.sha256` file, verify the SHA-256 value, and extract the archive.
2. Run `AgentController.exe`. The application is self-contained and does not require a separate .NET Runtime.
3. Connect an XInput gamepad, launch Codex Desktop, and enable the Bridge in Agent Controller.
4. For the complete Micro-first HID path, separately install the matching `CodexMicroVhfUm` Device Support component. This public release provides an unsigned developer-driver workflow only: users must review, build, or locally sign it. Do not disable Windows driver-signing enforcement or install certificates from untrusted sources.

Without the driver, Agent Controller attempts limited compatibility fallbacks only after an explicit `NotSent` result; this is not full Micro compatibility. See the [unsigned-driver guide](https://github.com/gantrol/AgentController/blob/v1.0.0/virtual-micro/UNSIGNED-DRIVER.md) and [English installation tutorial](https://github.com/gantrol/AgentController/blob/main/docs/CodexMicroSimulator-installation.md).

## Control summary

| Input | v1 behavior |
| --- | --- |
| Menu / Start | Launch, foreground, or cycle between Codex main windows |
| Left stick | Browse the task tree; left/right exits/enters a project |
| Right stick up or left | Previous Micro encoder item, `ENC_CW` |
| Right stick down or right | Next Micro encoder item, `ENC_CC` |
| R3 tap | Micro encoder press: open, enter, or confirm |
| B in a Micro menu | Contextual Back through `AG00` |
| LT hold/release | Micro PTT down/up |
| X | Submit the current input |
| LB plus six directional controls | Six Agent slots |

See the complete [control reference](https://github.com/gantrol/AgentController/blob/v1.0.0/public/docs/controller-operations.md), [architecture and input flow](https://github.com/gantrol/AgentController/blob/v1.0.0/public/docs/architecture-and-input-flow.md), and [Micro command reference](https://github.com/gantrol/AgentController/blob/v1.0.0/public/docs/codex-micro-command-reference.md).

## Validation

- All 797 tests in the main solution passed, including 739 WPF application regression tests.
- All 47 `virtual-micro` desktop tests and 5 protocol tests passed.
- The compatibility manifest includes Codex Desktop `26.715.4045.0`. Unknown newer builds enter a yellow unreviewed-compatibility state; a hash mismatch for a known build still blocks the connection.
- The tested controller baseline includes 8BitDo Ultimate 2, Xbox Series, and Flydigi Vader 4 Pro. Other XInput gamepads still require physical validation.

Automated checks do not replace end-to-end acceptance against the current Codex build, account, model, and physical controller. In particular, retest R3 → Advanced/submenu → B, PTT release, simultaneous dual-client input, and multi-window foreground activation.

## Security and limitations

- Neither the application nor the developer driver has commercial code signing. Windows SmartScreen or driver policy may block them.
- Codex Micro compatibility is based on an observed private contract, not a stable public ABI promised by OpenAI. Codex updates may change the HID bridge, layout, or accessibility tree.
- A transport ACK proves only that a report entered the device path; it does not prove that a UI or business action completed.
- This project is independent and is not affiliated with, authorized by, or endorsed by OpenAI, Codex, or Work Louder.

---

# Agent Controller v1.0.0（简体中文）

Agent Controller v1 将实体手柄映射为 Codex Micro 控件，并通过单一 Broker 与可选的 `CodexMicroVhfUm` UMDF2/VHF 设备支持，把输入交给 Codex 自带的 `codex-micro-service` 与 `codex-micro-bridge`。Thread、Turn、Steer、Queue 和 Stop 等不属于 Micro 设备的业务语义仍使用相应的应用层适配器。

## 主要变化

- 右摇杆采用固定的 Micro-first 合同：上或左发送 `ENC_CW`，下或右发送 `ENC_CC`；R3 短按发送 `ENC`，负责打开、进入和确认。
- B 在由 R3 建立的 Micro 菜单会话中发送 `AG00`。当前官方 bridge 会把菜单上下文中的 Agent 键 1 转换为 Escape，并阻止 Agent 槽位切换；只有驱动明确 `NotSent` 时才允许进入原生退出回退。
- LT 的按住说话、X 的提交以及已验证的 Command/Agent 槽位优先发送真实 Micro 信号；`Accepted` 或 `OutcomeUnknown` 后禁止双发。
- Agent Controller 与桌面 Micro 模拟器通过同一当前用户 Broker 共存。Broker 独占驱动句柄、全局 sequence 和 output/RPC reader，并分别管理客户端 lease、held key、PTT release 与 analog neutral。
- 菜单键会枚举真正的 Codex 主窗口并置前；存在多个主窗口时可以循环选择，不再误命中工具窗或 popup。
- 首页动态教程、侧边栏目录、动作面板、Agent 六槽面板和中英文提示已经同步到 v1 操作合同。

## 安装

1. 下载 `AgentController-1.0.0-win-x64.zip` 与对应 `.sha256`，核对 SHA-256 后解压。
2. 运行 `AgentController.exe`。应用为 self-contained，不需要另装 .NET Runtime。
3. 连接 XInput 手柄，启动 Codex Desktop，并确认 Agent Controller 中的桥接已开启。
4. 如需完整 Micro-first HID 路径，另行安装与当前版本匹配的 `CodexMicroVhfUm` Device Support。当前公开发行只提供未签名开发者驱动流程，必须由用户审查、构建或在本机签名；不要关闭 Windows 驱动签名强制或安装来源不明的证书。

驱动缺失时，应用只会在明确 `NotSent` 的操作上尝试有限兼容回退；这不是完整 Micro 兼容模式。请参阅[未签名驱动说明](https://github.com/gantrol/AgentController/blob/v1.0.0/virtual-micro/UNSIGNED-DRIVER.zh-CN.md)和[安装教程](https://github.com/gantrol/AgentController/blob/main/docs/CodexMicroSimulator-%E5%AE%89%E8%A3%85%E6%95%99%E7%A8%8B.zh-CN.md)。

## 操作摘要

| 输入 | v1 行为 |
| --- | --- |
| Menu / Start | 启动、置前或在多个 Codex 主窗口间循环 |
| 左摇杆 | 浏览任务树；左右进入/退出项目 |
| 上或左拨右摇杆 | Micro encoder 上一项，`ENC_CW` |
| 下或右拨右摇杆 | Micro encoder 下一项，`ENC_CC` |
| R3 短按 | Micro encoder press：打开、进入或确认 |
| B（Micro 菜单中） | `AG00` 上下文返回 |
| LT 按住/松开 | Micro PTT down/up |
| X | 提交当前输入 |
| LB + 六方向键位 | 六个 Agent slot |

完整操作见 [v1 操作清单](https://github.com/gantrol/AgentController/blob/v1.0.0/public/docs/controller-operations.md)、[架构与输入链路](https://github.com/gantrol/AgentController/blob/v1.0.0/public/docs/architecture-and-input-flow.md)和 [Micro 指令表](https://github.com/gantrol/AgentController/blob/v1.0.0/public/docs/codex-micro-command-reference.md)。

## 已验证范围

- 主解决方案自动化测试 797 项通过，其中 WPF 应用回归测试 739 项。
- `virtual-micro` 桌面测试 47 项、协议测试 5 项通过。
- 当前兼容清单包含 Codex Desktop `26.715.4045.0`；未知新构建只进入黄色的未审核兼容状态，已知构建哈希不匹配仍会阻止连接。
- 已实测的控制器基线包括 8BitDo Ultimate 2、Xbox Series 与 Flydigi Vader 4 Pro；其他 XInput 手柄仍需真机验证。

自动化通过不能替代当前 Codex build、账户、模型和实体手柄的端到端验收。尤其需要复验 R3 → Advanced/子菜单 → B、PTT release、双客户端同时输入以及多 Codex 窗口唤醒。

## 安全与限制

- 应用和开发者驱动均未提供商业代码签名；Windows SmartScreen 或驱动策略可能阻止运行。
- Codex Micro 是观察得到的私有兼容合同，不是 OpenAI 承诺的公开稳定 ABI。Codex 更新可能改变 HID bridge、布局或辅助功能树。
- transport ACK 只证明报告进入设备链路，不证明界面动作或业务操作已经完成。
- 本项目与 OpenAI、Codex、Work Louder 没有隶属、授权或背书关系。
