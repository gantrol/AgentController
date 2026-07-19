# 03 — Codex Micro 原生兼容层

> Status: Accepted / Planned
> Priority: P0
> Depends on: 01-core-architecture, 02-codex-app-server
> Decision: [ADR-0002](../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md)

## 产品结论

Windows 完整模式必须暴露一个会被 ChatGPT/Codex Desktop 原生识别的双向 Micro 类 HID 设备。UIA、快捷键和 App Server 都不能冒充这项完成度：

- Micro 能表达的 Agent Key、Command Key、Analog、Dial、PTT、状态和灯光优先走原生设备信号；
- Thread/Turn/Steer/Interrupt、任意任务树和 Micro 无法表达的动作继续走 App Server；
- 驱动被管理员或企业策略阻止时，基础应用可进入 Limited mode，但不得把 Limited mode 宣传成完整体验，也不得继续扩建平行 UIA 仿制层。

## 事实基线

- [ADR-0002：原生 Micro 主路径、方案对比、安装与发布 Gate](../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md)
- [26.707.12708 VHF/状态/输入协议证据](../docs/codex-26.707.12708-vhf-status-input.zh-CN.md)
- [OpenAI 官方 Codex Micro 行为](https://learn.chatgpt.com/docs/features/codex-micro)

这些证据记录的是指定 Codex 构建和当前公开行为，不是公开稳定 HID ABI。未知构建必须熔断。

### `virtual-micro/` 参考审计（2026-07-18）

`virtual-micro/` 是独立实验台，只作为驱动、协议、映射和安装证据；不直接复制它的 UI 或构建产物，也不在本任务里修改它。

- [x] 核对设备身份与 framing：VID `0x303A`、PID `0x8360`、Usage Page `0xFF00`、Report ID `0x06`、64-byte report、61-byte payload、64 KiB message 上限。
- [x] 核对 Device→Codex LF/UTF-8 分包和 Codex→Device 无 LF output 重组差异。
- [x] 核对输入语义：key down/up、encoder `act=2`、`v.oai.rad(a,d)`、center/neutral、sequence 和四态发送结果。
- [x] 核对默认布局：ACT06 Fast、ACT07 Approve、ACT08 Decline、ACT09 Fork、ACT10/11 Mic、ACT12 Codex。
- [x] 确认 `composer-navigation` 能遍历输入框工具；右摇杆/virtual-dial 不应继续硬编码 Reasoning/UIA popup。
- [x] 确认 PTT 必须保持 down/up，并在退出/断连时释放；模拟杆中间帧可合并但 neutral 不可丢。
- [x] 发现 KMDF/VHF、UMDF2/VHF、直接 UMDF HID 三套实现并存，README/DESIGN 与安装脚本选择不同。
- [x] 本机验证 UMDF2/VHF devnode 正常、旧 KMDF 服务停止，Driver Store 残留多版自签 INF；现有安装路径不能面向用户。
- [x] 核实微软 VHF 总览仍声明 HID source 仅支持 kernel mode，而 VHF DDI 又包含 UMDF FileHandle；UMDF2/VHF 只能作为认证实验。
- [x] 核实 `0x303A` 属于他方 VID，正式发行前必须解决设备身份和品牌授权，不能只解决驱动签名。

## 首要阻塞 Gate

### Gate A：设备身份

- [ ] 向 OpenAI/Work Louder/VID 持有人确认第三方兼容或 allowlist 路线，并保存书面结果。
- [ ] 分别用项目自有 ID 与目标 ID 验证 Codex detection 条件，确认过滤依赖 VID/PID、Usage、device type 还是组合指纹。
- [ ] 选定项目自己的 root Hardware ID，例如 `ROOT\AgentController\MicroCompat`，以及独立 Provider、产品名和 interface GUID。
- [ ] 身份未授权前，目标 VID/PID 只允许隔离研究，不进入公开二进制、商店或商业宣传。

### Gate B：唯一 Windows 驱动

- [ ] 将 KMDF/VHF 冻结为当前正式候选；驱动只处理 descriptor、input/output report、有界队列和连接清理。
- [ ] 以目标 WDK 做一次 UMDF2/VHF HLK preflight 并寻求微软支持边界确认；没有双重证据就不改选。
- [ ] 若 KMDF 认证不可行，评估官方支持的直接 UMDF2 HID minidriver，不把它与 UMDF2/VHF 混为一谈。
- [ ] 冻结 x64/ARM64 INF、DriverVer、升级排名、devnode、接口 ACL、卸载与回滚规则后，删除另外两套生产构建入口。

### Gate C：签名与发行能力

- [ ] 取得 EV 代码签名证书并注册 Windows Hardware Developer Program。
- [ ] 正式包完成 HLK/WHCP 和 Hardware Dev Center Microsoft signing；attestation/preproduction 只用于受控测试。
- [ ] 安装器 EXE 与 driver package 分别签名；用户机不编译、不制包、不导入自签根证书。
- [ ] 建立 Driver Verifier、HVCI、Secure Boot、Windows 10/11、x64/ARM64、升级和 OS feature update 矩阵。

## 改造待办

### M1：唯一纯协议项目

- [ ] 新建 `AgentController.Protocols.Micro`，迁入稳定常量、framing、codec、RPC DTO 和 golden vectors。
- [ ] 分开实现 Device→Codex LF/UTF-8 分包与 Codex→Device 无 LF output assembler。
- [ ] 实现 request id 关联、每个请求恰好一次 response、超时、64 KiB 总长和未知 channel 拒绝。
- [ ] 实现 `sys.version`、`device.status`、`v.oai.rgbcfg`、`v.oai.thstatus` 最小状态机。
- [ ] 为 0/1/60/61/62 bytes、多字节 UTF-8、截断、乱序和超时建立 golden tests。
- [ ] 删除 `app/Services/Micro` 与 `virtual-micro` 之间的重复协议副本；实验台改为引用或同步同一生成源。

### M2：控制意图与默认映射

- [ ] 建立不含 HID byte/`v.oai.*` 的 `MicroControlIntent`：KeyDown/Up/Tap、EncoderStep/Press/Hold、Analog、Neutral。
- [ ] 从 `MainWindow` 移出 LT PTT、virtual-dial/右摇杆、R3 suppression 与 held lifecycle。
- [ ] 默认映射 LT→ACT10 down/up、X→已验证 Codex slot、右摇杆纵向→ENC、横向→当前控件 Left/Right、R3→press/hold、RB+右摇杆→Analog。
- [ ] 将 `composer-navigation` 作为默认 Dial 模式；Reasoning-only 由 Codex layout 决定，不由 WPF 猜测。
- [ ] 六个 Micro Agent slot 可发送 AG00..AG05；任意任务树仍走 App Server，不伪造无限 Agent Key。
- [ ] 自定义 Profile 区分“绑定到 Micro control”和“绑定到 semantic action”；首版禁止 raw report/RPC。

### M3：兼容性与布局门禁

- [ ] 建立 `compat/codex-desktop/<build>/manifest.json`，记录关键 bundle SHA-256、VID/PID、Usage、Report ID、RPC shape、encoder mode 和已验证 slot。
- [ ] 任一指纹未知时返回 Incompatible，不发送试探性设备动作。
- [ ] 只读观察 `desktop.codex-micro-layout`；不写回私有配置。
- [ ] slot control fidelity 与 semantic mapping 分开；映射未知时不把 ACT12 猜成 Submit、ACT07 猜成 Approve。
- [ ] SlotOnly、RosterProof 和命名任务状态使用不同类型；没有 roster proof 不显示伪任务名。

### M4：Broker 与传输结果

- [x] 新建低权限、当前用户 `AgentController.MicroBroker`，独占驱动接口；不默认安装 Windows Service。
- [x] IPC 已包含 protocol version、driver epoch、request id、长度上限、当前用户 pipe、单客户端心跳 lease 和有界并发；请求执行、完成续期和 lease expiry 互斥；全局 batch sequence 只由 Broker 驱动端点分配。
- [x] 每个 client lease 保留 32 条有界 response cache；相同 request id + 相同 payload 只返回缓存结果，不再次触碰驱动，相同 id + 不同 payload 或已淘汰的 stale id 会被拒绝。
- [ ] “控制器 + virtual-micro 模拟器同时使用”的 fake-driver 验收已通过：单一 connection epoch、全局 sequence 所有者、单点 output/RPC 和灯光广播；真实驱动双进程与交换启动顺序仍待实机记录。
- [x] 不用共享句柄冒充多客户端支持；held key、PTT 和 analog 按客户端持有 lease；重叠同键只向驱动发送第一次 down/最后一次 up，analog owner 释放时恢复最近仍活跃来源；客户端断开时不会误释放另一客户端的状态。
- [x] 保留 `NotSent / Accepted / OutcomeUnknown / Rejected`、requested/accepted report count、native status 和 detail，禁止压成 bool。
- [x] Accepted/OutcomeUnknown 后禁止自动执行第二通道；非幂等动作只有明确 NotSent 才能安全 fallback。
- [ ] held key、analog neutral、进程退出、控制器断连、Codex 退出、睡眠/唤醒均有确定状态机。
- [x] 输入 request、output RPC、Broker event 和 transport ACK 使用独立消息类型；UI readback 与 ACK 分开，不能用投递成功冒充业务完成。

### M5：KMDF/VHF 正式候选

- [ ] 驱动使用项目唯一 root ID，VHF child descriptor 只在身份 Gate 通过后冻结。
- [ ] 驱动不解析 JSON；只验证固定 batch header、64-byte report、Report ID/channel/length 和有界队列。
- [ ] 私有接口采用最小 ACL、exclusive open 与活动会话规则；桌面 UI 不直接打开 driver。
- [ ] 完成 node-hid/目标 Codex 对 64-byte report、output callback 和双向 RPC 的真实验证。
- [ ] 每类 Agent/Command/Analog/Dial 动作连续 1000 次无丢失、重复、卡键或遗漏 neutral。

### M6：一键 Device Support

- [ ] 安装器固定支持 `status / install / update / repair / uninstall / diagnose`，输出 versioned JSON 和明确 reboot/policy/error 分类。
- [ ] 不接受任意 INF/Hardware ID；只安装签名 manifest 指定的固定 x64/ARM64 包。
- [ ] 普通 Install/Update 使用 Windows driver ranking，删除默认 `INSTALLFLAG_FORCE`；仅受控 Repair 可强制重装本产品精确版本。
- [ ] 健康检查覆盖 root devnode、published INF/signature、VHF child descriptor、Broker ping、双向 report、RPC 和 Codex 实际打开设备。
- [ ] 新装失败移除新 devnode；升级失败恢复旧签名包；成功后才精确删除本产品旧 INF。
- [ ] 设置页提供“安装 / 更新 / 修复 / 卸载 / 导出诊断”；正常 app/Broker 更新不请求 UAC。
- [ ] 提供企业离线包、静默参数、Hardware ID/签名者 allowlist 材料；不绕过 WDAC/Group Policy。

### M7：移除维护地狱

- [ ] Micro 已验证可表达的 PTT、Dial、Fast、Fork、Approve/Decline、Submit、Analog 不再维护 UIA 主执行器。
- [ ] App Server 成为 Thread/Turn 权威语义通道；UIA 只保留二者都无法表达的 Limited mode 能力并设删除期限。
- [ ] UI 显示实际通道、映射状态和证据，不把 transport ACK 描述为业务完成。
- [ ] 未知 Codex build、driver 缺失和 policy block 的兼容数据进入发布仪表，而不是触发更多 UIA 猜测。

## 用户不应承担的事情

普通用户永远不需要：

- 安装 Visual Studio/WDK；
- 运行 PowerShell、Inf2Cat、SignTool、PnPUtil 或设备管理器手工步骤；
- 自行编译 driver；
- 导入项目自签名根证书；
- 开启 TESTSIGNING，关闭 Secure Boot/Memory Integrity，或修改企业设备策略；
- 手工清理 `oem*.inf`。

如果项目没有能力提供 Microsoft-signed driver，Windows 软件版只能停留在开发/受控测试，不能把“请用户自行编译驱动”当作发行方案。此时唯一的完整免驱备选是预刷写物理 USB/BLE bridge，但它仍需解决设备身份授权和硬件运营。

## 明确禁止

- 修改 `app.asar`、注入 renderer 或伪造 Electron IPC。
- 复制或重新分发私有 Work Louder/OpenAI 包代码。
- 未经书面依据公开冒用第三方 VID/PID、产品名或设备身份。
- 将 Micro driver 说成完整模式的可删实验项。
- 在未知布局后发送默认 slot，再自动执行可能重复的 fallback。
- 让桌面 UI 传任意 HID report、RPC method、INF、Hardware ID 或删除目标。

## 完成门槛

- 身份授权、唯一驱动架构、EV/WHCP/Microsoft signing 三个发行 Gate 全部通过。
- 纯协议项目无需 hardware/driver/Codex 即可确定性测试，真实 E2E 绑定明确 Codex build。
- 原生 Agent/Command/Analog/Dial/PTT/状态/灯光全链路不依赖 UIA。
- 干净标准用户机器一次 UAC 可达 Ready；拒绝/策略阻止/重启/回滚/卸载均可恢复。
- app 兼容更新无 UAC，driver 变化才进入独立 Device Support 更新。
- 未知构建熔断，OutcomeUnknown 不重复非幂等动作，SlotOnly 不冒充任务 roster。
