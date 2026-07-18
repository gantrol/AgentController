# 03 — Codex Micro 兼容层

> Status: Research / Partial
> Priority: P1
> Depends on: 01-core-architecture

## 事实基线

- [`docs/codex-26.707.12708-vhf-status-input.zh-CN.md`](../docs/codex-26.707.12708-vhf-status-input.zh-CN.md)
- [`public/docs/codex-micro-virtual-hid-bridge-plan.md`](../public/docs/codex-micro-virtual-hid-bridge-plan.md)

这些文件记录的是指定 Codex 构建的兼容性观察，不是公开稳定 ABI。

### `virtual-micro/` 参考审计（2026-07-18）

`virtual-micro/` 是独立演进的小组件，只作为实现证据和协议参考；本项目不直接复制其构建产物，也不在本轮核心重构中修改它。

- [x] 核对设备身份与 framing：VID `0x303A`、PID `0x8360`、Usage Page `0xFF00`、Report ID `0x06`、64-byte report、61-byte payload、64 KiB message 上限。
- [x] 核对输入语义：key 的 `act=0/1/2`、dial 的 `a/d`、tap 的 down/up、编码器旋转，以及批量发送的 version/sequence/result。
- [x] 核对当前默认布局：ACT06 Fast、ACT07 Approve、ACT08 Reject、ACT09 Split、ACT10/11 Mic、ACT12 Codex。
- [x] 发现新增的 `composer-navigation` 编码器模式，可用于输入框工具遍历；它不能继续被主程序硬编码成 reasoning 调节。
- [x] 发现实现/文档漂移：安装脚本当前选择 `CodexMicroVhfUm`（UMDF2），README/DESIGN 仍以 `CodexMicroVhf.sys`（KMDF）为正式路径。
- [ ] 将稳定常量、codec 和 golden vectors 提取为唯一协议来源，避免主项目与 `virtual-micro/` 各自维护副本。
- [ ] 将物理输入先映射成设备无关 intent，再由 layout/context 决定 `composer-navigation`、reasoning 或自定义动作。
- [ ] 用含 NotSent、Accepted、OutcomeUnknown、Rejected 的结果替代当前 named-pipe `bool`，保留序号、计数与驱动状态。
- [ ] 在选定 KMDF 或 UMDF2 路径前，用目标 Windows/WDK 版本完成安装、重启、HVCI、Secure Boot 和卸载矩阵；同步删除另一套误导性文档与产物。

## 目标

把 Micro 的 Agent Key、Command Key、Analog 和 Dial 作为统一设备交互模型，同时将私有 HID 协议限制在可熔断、可替换、可独立测试的适配模块。

## 待办

### 纯协议层

- [ ] 将现有 codec 迁入独立 `AgentController.Protocols.Micro` 项目。
- [ ] 分开实现 Codex→Device 无 LF output 重组和 Device→Codex UTF-8/LF input 分包。
- [ ] 支持短字段 `m/p` 与完整字段 `method/params`，但输出保持明确版本策略。
- [ ] 实现 request id 关联、每个请求恰好一次 response、超时和总长度上限。
- [ ] 实现 `device.status`、`sys.version`、`v.oai.rgbcfg`、`v.oai.thstatus` 的最小设备状态机。
- [ ] 为 0/1/60/61/62 bytes、多字节 UTF-8、截断和未知 channel 建立 golden tests。

### 兼容性门禁

- [ ] 建立 `compat/codex-desktop/<build>/manifest.json`。
- [ ] 记录构建号、关键包 SHA-256、VID/PID、Usage Page、Report ID 和方法形状。
- [ ] 任一指纹未知时返回 Incompatible，不发送试探性动作。
- [ ] 将布局能力与协议能力分开；ACT12 未证明为 Submit 时拒绝该动作。

### 传输与结果

- [ ] IPC batch 增加 protocol version、sequence、长度和幂等去重。
- [ ] 保留 NotSent、Accepted、OutcomeUnknown，映射到统一 ActionResult。
- [ ] Accepted/OutcomeUnknown 后禁止盲目执行第二通道。
- [ ] 每个非幂等动作定义可验证结果；无法验证时诚实显示未确认。
- [ ] 输入与状态事件使用独立管道，避免旧 ACK 客户端误读。

### 状态与槽位

- [ ] `v.oai.thstatus` 默认只发布 SlotOnly。
- [ ] off/inactivity 不覆盖仍存在任务的最后有效状态。
- [ ] 实现 build、epoch、sequence、TTL 和已知颜色/effect 校验。
- [ ] 在没有独立同源 RosterProof 前，禁止把槽灯覆盖到命名任务。
- [ ] 保留 rollout + unread 作为无驱动降级源。

### Windows VHF PoC

- [ ] Driver、Broker 和桌面客户端保持三个独立组件。
- [ ] 驱动只处理 HID report 与有界队列，不解析 JSON。
- [ ] Broker 实现协议、指纹、ACL、背压、状态事件和诊断。
- [ ] 开发者可手动安装、测试签名和卸载；正式应用不静默导入自签名根证书、不启用 TESTSIGNING，也不更改 Secure Boot/HVCI。
- [ ] 完成 node-hid 64-byte 与 VHF `HID_XFER_PACKET` 长度回环验证。
- [ ] 连续 100 次方向/Command 动作无丢失、重复或卡住后再做发行评审。

## 明确禁止

- 修改 `app.asar`、注入 renderer 或伪造 Electron IPC。
- 复制或重新分发私有 Work Louder/OpenAI 包代码。
- 未经授权在商业发行中冒用第三方 VID/PID 或产品身份。
- 将 transport ACK 描述成业务完成。

## 完成门槛

- 纯协议项目在无硬件、无驱动、无 Codex 进程环境中可确定性测试。
- 未知版本会熔断且不影响 App Server/桌面降级通道。
- SlotOnly、RosterProof 和命名任务状态在类型上不可混淆。
- Windows VHF 是否发行已有明确 Go/No-Go 结论和证据。
