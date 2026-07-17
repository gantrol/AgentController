# Codex 26.707.12708.0：VHF、Micro 状态与手柄输入协议

> 状态：实现基线与 M4 PoC 接口契约  
> 冻结日期：2026-07-17  
> 适用版本：`OpenAI.Codex_26.707.12708.0_x64__2p2nqsd0c76g0`  
> 原则：Micro/VHF-first；不修改 `app.asar`；不把 UI Automation 当状态源；
> 不静默安装或启用内核驱动。

## 1. 结论与快速路径

LB 的六个状态不能继续硬编码成 `Unknown`，但也不能从控件颜色、按钮文字或
`UpdatedAt` 猜审批状态。本版本采用两层来源：

1. **已落地的本地降级源**：best-effort 增量读取 LB 最近六项 rollout 的
   `task_started / task_complete / turn_aborted / stream_error`，并读取
   `.codex-global-state.json` 中官方持久化的
   `unread-thread-ids-by-host-v1`。它在事件完整时提供 `Thinking`、`Idle`、
   `CompleteUnread`，仅在出现明确错误事件时提供 `Error`。这里的
   `Thinking` 只表示“turn 尚未闭合”，可能包含 renderer 正在等待审批/回应
   的时间，不能解释成精确 `working`。
2. **槽灯观测源（VHF M4）**：虚拟 Codex Micro 接收 Codex 发出的
   `v.oai.thstatus` output report，由 Broker 重组、应答并发布六槽灯光。
   非 `off` 帧可以权威回答“这个 Micro 槽请求显示什么状态”，但 report
   不含 `threadKey`；没有独立 roster proof 时不能声称它已绑定到具体任务。

合并优先级：

```text
兼容性和 roster proof 均通过的 VHF/Micro 槽状态
    > rollout 生命周期 + 官方未读集合
    > Unknown
```

当前应用已完成第 1 层，所以无驱动机器也不再全部显示 `Unknown`。第 2 层
必须作为独立、可编译、手动安装的 PoC 交付；当前仓库没有 Broker/driver，
也没有可证明槽位顺序的 sideband roster provider。它不是 v0.7 安装包的
隐式依赖。

## 2. 观测证据与兼容性指纹

本节结论来自已安装包体的只读解包；文件名和 SHA-256 用作升级门禁，不表示
这些私有实现是稳定公共 API。

| 包内文件 | SHA-256 |
| --- | --- |
| `.vite/build/codex-micro-service-CR6sUcZG.js` | `0bb261e3eed89ff69384754ab67df49c9f10dbd2fa567104c5859f43d026c911` |
| `webview/assets/codex-micro-slot-signals-SFcKxWqG.js` | `e5f0084a27fc0e908c4514a5d3bd0a90dba3f953a48521fb4ae2a43b1e5b28bb` |
| `webview/assets/codex-micro-bridge-D90_rd6W.js` | `df6063eb17046594e769050c6bbb3ed169b1352bbd5867fffb4d1f8c724f3e93` |
| `@worklouder/device-kit-oai/dist/rpc_api_oai/rpc_api_oai.js` | `80815366885246cd9644e13b770f38c7f9c0587db13cc8979310571ba0fa029a` |
| `@worklouder/device-kit-oai/node_modules/@worklouder/wl-device-kit/dist/index.js` | `f44d8d09e10a4608bf37f2860cd4807c3be9b0242f91d8258df540e277cd7548` |

包内依赖版本：

- `@worklouder/device-kit-oai`：`0.1.10`；
- `@worklouder/wl-device-kit`：`0.1.18`。

Codex 版本、任一关键散列、VID/PID、Usage Page、Report ID 或方法形状变化时，
Broker 必须报告 `Incompatible` 并拒绝 ACK 动作输入，不能继续猜测。

## 3. 官方 Agent 槽选择与状态推导

### 3.1 六槽来源

官方 `agentSource` 支持四种值：

| 值 | 语义 |
| --- | --- |
| `recent` | 所有可渲染线程按 recency/updated 倒序取前 6；当前默认值 |
| `pinned` | 固定任务与固定项目内任务取前 6 |
| `priority` | 按 attention 与 recency 排序取前 6 |
| `custom` | `AG00..AG05` 的用户固定分配 |

设置键为 `codex-micro-agent-source`；自定义分配键为
`codex-micro-custom-agent-assignments`。当前本机没有持久覆盖，因此采用默认
`recent`。VHF 槽状态只有在 Agent Controller 侧镜像同一来源和同一顺序时，
才允许按槽合并。

### 3.2 本地线程状态优先级

官方 renderer 对 local thread 的推导顺序是：

```text
localStatus.status == error       -> error
pendingChip == approval           -> awaiting-approval
pendingChip == response           -> awaiting-response
localStatus.status == loading     -> working
unread                            -> unread
otherwise                         -> idle
```

`loading` 来自 renderer 内存中的 side chat、thread runtime 或 response 运行信号；
`approval / response` 来自请求队列和 `waitingOnApproval /
waitingOnUserInput` active flags。这些字段不在 `state_*.sqlite` 的普通 threads
记录中，也没有完整写入 rollout，因此本地降级源不得伪造它们。

### 3.3 远程线程状态优先级

```text
latest turn == failed                  -> error
latest turn == pending | in_progress   -> working
unread                                 -> unread
otherwise                              -> idle
```

选中线程且 Codex 窗口聚焦时，官方会把该线程的 `unread` 显示为 `idle`，因为
用户正在阅读它。Agent Controller 当前拿不到官方 selected-thread 信号；自己的
本地选择不能替代它，因此 LB 保留 `unread`，只把本地选择用于边框高亮。

### 3.4 renderer 快照与 HID 的信息损失

renderer 交给 native service 的快照包含：

```text
{
  inactivityTimeoutMs,
  preserveSelectionLighting,
  snakingAmbientStatus,
  suspendDeviceStatusRefresh,
  slots: [{ id, pulsing, selected, status, threadKey }],
  voiceState
}
```

但是 native service 调用 `sendThreadsLighting` 前会转换成灯光模型；最终
`v.oai.thstatus` **只含数值槽号 `id`，不含 `threadKey` 或标题**。因此：

- VHF raw report 可以权威回答“槽 0 是蓝/琥珀/红”；
- VHF raw report 不能单独回答“槽 0 对应哪个 thread id”；
- 只有 Agent source、排序和自定义分配的镜像指纹一致时才能合并；不一致时
  保留 rollout 状态并显示诊断，不能把灯色错配到另一任务。

### 3.5 当前置顶项目 ID 映射

Codex 26.707.12708.0 的 `.codex-global-state.json` 不再保证
`pinned-project-ids`、`project-order` 和 `sidebar-project-thread-orders` 使用磁盘路径。
当前本地项目采用以下结构：

```json
{
  "local-projects": {
    "local-<opaque-id>": {
      "id": "local-<opaque-id>",
      "name": "AgentController",
      "rootPaths": ["D:\\AgentController"]
    }
  },
  "pinned-project-ids": ["local-<opaque-id>"],
  "thread-project-assignments": {
    "<thread-id>": {
      "projectId": "local-<opaque-id>",
      "path": "D:\\AgentController",
      "cwd": "D:\\AgentController"
    }
  }
}
```

读取规则：

- 项目顺序、置顶和 sidebar thread order 中的 `local-*` 必须先经
  `local-projects[id].rootPaths[0]` 解析为规范路径；
- 显示名称取 `local-projects[id].name`；
- thread assignment 优先采用 `path`，其次 `cwd`，最后才用映射后的 `projectId`；
- 无法解析的 `local-*` 是内部键，必须忽略，绝不能作为项目标题或路径显示。

## 4. 官方灯语与 `v.oai.thstatus`

### 4.1 状态、颜色与本地枚举

| 官方状态 | RGB 整数 | Hex | Agent Controller |
| --- | ---: | --- | --- |
| `off` | `0` | `0x000000` | 歧义：未绑定或灯光被暂停 |
| `idle` | `16777215` | `0xFFFFFF` | `Idle` |
| `working` | `3166206` | `0x304FFE` | `Thinking` |
| `unread` | `65356` | `0x00FF4C` | `CompleteUnread` |
| `awaiting-approval` | `16739584` | `0xFF6D00` | `RequiresInput` |
| `awaiting-response` | `16739584` | `0xFF6D00` | `RequiresInput` |
| `error` | `16711731` | `0xFF0033` | `Error` |

非选中槽使用 `solid`；选中或显式 `pulsing` 的槽使用 `breath`、速度 `0.4`。
槽位的 `breath 0.4` 随 selected/pulsing 状态持续；观测到的约 4 秒主要是临时
ambient/selection lighting 的恢复窗口。Effect 数值：

```text
off=0, solid=1, snake=2, rainbow=3,
breath=4, gradient=5, shallowBreath=6
```

`off` 不能从 raw HID 直接覆盖成 `Unassigned`：native service 的 inactivity
lighting 与 stop 流程也会把六槽统一写成 `off`。已有任务时，Broker 应发布
`lightingSuppressedOrAmbiguous`，应用保留 rollout/最后一个未过期的非 off
状态；只有 roster source 明确证明槽为空时才能显示 `Unassigned`。

### 4.2 请求形状

`v.oai.thstatus` 的 `params` 是数组。每项采用压缩字段：

```json
{
  "method": "v.oai.thstatus",
  "params": [
    {"id": 0, "c": 3166206, "b": 1, "e": 4, "s": 0.4,
     "sk": 0, "sa": 0}
  ],
  "id": 42
}
```

字段：`c=color`、`b=brightness`、`e=effect`、`s=speed`、
`sk=syncKeysLighting`、`sa=syncAmbientLighting`。设备/Broker 必须复用请求 id：

```json
{"id":42,"result":true}
```

`v.oai.rgbcfg` 的最小形状：

```json
{
  "method": "v.oai.rgbcfg",
  "params": {
    "ambient": {"e":0,"b":0,"s":0,"m":0,"c":0},
    "keys": {"e":0,"b":0,"s":0,"m":0,"c":0}
  },
  "id": 43
}
```

Broker 还必须响应：

- `sys.version` → `{ "version": "..." }`；
- `device.status` → `{ "version", "profile_index", "layer_index",
  "battery", "is_charging" }`。

## 5. HID 传输协议

| 字段 | 观测值 |
| --- | --- |
| VID | `0x303A`（12346） |
| PID | `0x8360`（33632） |
| 设备类型 | `project_2077` |
| Usage Page | `0xFF00` |
| Report ID | `0x06` |
| RPC channel | `0x02` |
| Debug channel | `0x01` |
| Report 总长度 | 64 bytes（含 Report ID） |
| 单 report 数据 | 最多 61 bytes UTF-8 |

报告布局：

```text
byte 0      report id = 0x06
byte 1      channel = 0x02
byte 2      payload length, 0..61
byte 3..63  payload chunk，剩余补零
```

两个方向的终止与分包规则不同，不能共用一个“按换行重组”的解析器：

- **Codex → 设备（Output）**：观测实现先 escape Unicode，再按最多 61 raw
  bytes 分片，不追加 CR/LF。Broker 按 channel 累加，并在每个 fragment 后尝试
  解析完整 JSON；当前队列一次只有一个 RPC in-flight。必须设置总长度与超时上限。
- **设备 → Codex（Input）**：host 会逐 report 做 UTF-8 decode 后拼接，所以
  发送端应避免在 report 边界拆开 UTF-8 标量；完整 notification/response 必须
  以 `\n` 结束，host 才会进入 RPC 分发。

RPC 没有 `"jsonrpc":"2.0"` 字段。

设备 → Codex 通知允许短字段：

```json
{"m":"v.oai.hid","p":{"k":"ACT12","act":1}}
{"m":"v.oai.hid","p":{"k":"ACT12","act":0}}
{"m":"v.oai.rad","p":{"a":0.75,"d":1}}
```

`v.oai.hid` 参数为 `{k, act, ag?}`；`v.oai.rad` 为 `{a, d}`。每条消息以
换行结束。

用于 VHF PoC 的最小 report descriptor 应声明 vendor-defined top-level
collection、Report ID `0x06`，以及各 63 byte 的 Input 与 Output report：

```c
static const UCHAR MicroReportDescriptor[] = {
    0x06, 0x00, 0xFF,       // Usage Page (Vendor 0xFF00)
    0x09, 0x01,             // Usage 1
    0xA1, 0x01,             // Collection (Application)
    0x85, 0x06,             // Report ID 6
    0x15, 0x00,             // Logical Minimum 0
    0x26, 0xFF, 0x00,       // Logical Maximum 255
    0x75, 0x08,             // Report Size 8
    0x95, 0x3F,             // Report Count 63
    0x09, 0x01,
    0x81, 0x02,             // Input (Data, Var, Abs)
    0x95, 0x3F,
    0x09, 0x01,
    0x91, 0x02,             // Output (Data, Var, Abs)
    0xC0
};
```

枚举成功仍需用当前 `node-hid` 做 64-byte 读写实测；descriptor 静态正确不等于
Codex 一定会选择该设备。

## 6. Microsoft VHF 正式接口约束

微软当前总览明确说明：本版本 VHF 的 HID source driver 支持路径是内核模式。
PoC 使用 KMDF，包含 `Vhf.h` 并链接 `VhfKm.lib`：

1. `WdfDeviceCreate` 后先初始化 nonpaged context、有界队列、锁和停止标志；
2. 用 `VHF_CONFIG_INIT` 填充配置，设置 `VendorID=0x303A`、
   `ProductID=0x8360`、descriptor，并注册
   `EvtVhfAsyncOperationWriteReport`；
3. 在 `PASSIVE_LEVEL` 调用 `VhfCreate`，成功后调用 `VhfStart`；
4. Broker 的 input report 通过受限 IOCTL/队列进入驱动，驱动构造
   `HID_XFER_PACKET` 并调用 `VhfReadReportSubmit`；
5. Codex 写 output report 时，VHF 调用 `EVT_VHF_ASYNC_OPERATION`。它可运行到
   `DISPATCH_LEVEL`，只能校验 report id/长度/buffer 并复制到 nonpaged 有界
   队列，不能解析 JSON、等待 pipe 或访问 pageable 数据；队列满用真实失败
   `NTSTATUS`，每个 operation 恰好调用一次 `VhfAsyncOperationComplete`；
6. 停止提交新报告后，在 `PASSIVE_LEVEL` 调用
   `VhfDelete(handle, TRUE)`；`FALSE` 是保留值，传入会导致未定义行为；
7. INF 把系统 `vhf` 声明为 lower filter。

驱动层不解析 JSON、不保存任务标题、不执行 Codex 命令。callback 可以在
`VhfStart` 返回前进入。PoC 首选 VHF 默认 input buffering；若注册
`EvtVhfReadyForNextReadReport`，每次 ready 只提交一次。分包、RPC、版本指纹、
超时、槽状态和日志全部留在低权限 Broker。

不能仅凭 node-hid 的 64-byte wire frame 推断 VHF packet 长度：
`HID_XFER_PACKET` 已单列 `reportId`。descriptor 的 63-byte payload、packet
`reportBufferLen` 与用户态 64-byte report 必须用 VHF↔node-hid 回环实测冻结。

正式参考：

- [Microsoft：使用 VHF 编写 HID source driver](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/virtual-hid-framework--vhf-)
- [`VHF_CONFIG`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/ns-vhf-_vhf_config)
- [`EVT_VHF_ASYNC_OPERATION`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/nc-vhf-evt_vhf_async_operation)
- [`VhfStart`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/nf-vhf-vhfstart)
- [`VhfReadReportSubmit`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/nf-vhf-vhfreadreportsubmit)
- [`VhfAsyncOperationComplete`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/nf-vhf-vhfasyncoperationcomplete)
- [`VhfDelete`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/vhf/nf-vhf-vhfdelete)
- [`HID_XFER_PACKET`](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidclass/ns-hidclass-_hid_xfer_packet)

## 7. Broker 与 Agent Controller 状态接口

建议保持驱动窄接口，Broker 对应用暴露两个方向：

```text
AgentController -> Broker: 64-byte input report batch
Broker -> AgentController: versioned slot-status snapshot/event
```

槽灯事件只陈述 Broker 从 VHF 看到的事实，最少包含：

```text
protocolVersion
codexBuild
compatibilityFingerprint
connectionEpoch
sequence
observedAt
slots[] { slotId, color, effect, lightingAmbiguous }
```

它的 `mappingKind` 默认为 `SlotOnly`，不能携带 Broker 无法独立证明的 thread id。
应用只在以下条件全部成立时接受这份槽灯诊断：

- Codex build 与指纹在白名单；
- connection epoch 未变化、sequence 单调递增且事件未过期；
- 六个槽 id 均在 `0..5`，颜色/effect 属于已知枚举。

要把槽灯合并到命名任务，必须另有独立 `RosterProof` 短租约：Codex 侧来源和
Agent Controller 各自从同一官方 roster source 计算 canonical
`(agentSource, ordered threadKeys)` SHA-256，且 build、hash、mapping epoch 全部
一致。把应用自己的列表发给 Broker 再由 Broker 回显，只能防 stale，不是证明。
当前 raw VHF、现有命名管道和本地数据都无法构造这份独立证明；`priority` 更依赖
renderer attention 内存。因此这一版的命名任务继续使用已实现的 rollout + unread
绑定，VHF 先作为 `SlotOnly` 诊断，不能错误覆盖任务状态。

现有 `AgentController.VirtualMicro.v1` 是同步 input batch 管道，不支持 Broker
主动推事件；状态 PoC 应另用 `AgentController.VirtualMicro.Status.v1` 长度前缀
事件管道和后台 reader，避免事件首字节被旧客户端误读成 ACK。

Broker 缺席是正常降级，不应靠抛出 `System.TimeoutException` 探测。客户端先调用
[`WaitNamedPipeW`](https://learn.microsoft.com/en-us/windows/win32/api/namedpipeapi/nf-namedpipeapi-waitnamedpipew)
做 1 ms 可用性检查，再建立托管连接；检查和连接之间仍可能竞态，所以连接异常
继续捕获，但空闲机器不再每次输入都制造 first-chance timeout。

## 8. 普通 X 发送：`ACT12`

当前默认 Micro layout 把 `ACT12` 的 `CODEX` 键映射到
`composer.submit`。因此普通 Base X 的首选路径是：

```text
X down
  -> ACT12 act=1
  -> ACT12 act=0
  -> NotSent：才允许进入旧的发送兜底
  -> Accepted / OutcomeUnknown：禁止 fallback；只有发送前观察到非空草稿、
     发送后观察到 composer 清空，才报告成功，否则显示“发送未确认”
```

管道 `0x06` ACK 只是 transport-level accepted，不是 `composer.submit` 的动作
完成回执；`v.oai.hid` notification 本身没有 action ACK。客户端现在使用
`NotSent / Accepted / OutcomeUnknown` 三态，只有写入前确定未发送才会兜底；
Accepted 与 OutcomeUnknown 都必须同时具备“发送前非空、发送后清空”的结果证据
才报告“已发送”。这样既不因 ACK 丢失双发，也不会把原本就空的 composer 误报为
已发送。
Broker 协议下一版还必须加入 batch sequence 与去重；partial submit、ACK 超时或
写后断线一律按 unknown 处理。

原“界面元素不支持该操作”不是 Windows 原生错误。正常发送态的 Send 按钮在
本版本没有稳定 aria-label，旧代码找不到命名按钮后发送 F22；900 ms 内无法
确认 composer 已清空时返回 `composer-submit-not-verified`，却被通用
`ElementUnsupported` 文案误译。现在 Base X 的降级路径直接调用已经绑定到官方
`composer.submit` 的快捷键，不再先枚举/Invoke Send 按钮；UIA 只做 editor
前置检查与发送后确认。确认同时识别 ProseMirror trailing break、零宽字符和
object replacement character，失败时显示“发送未确认”，不再显示“界面元素
不支持该操作”。

`ACT12` 可被用户自定义 layout 重映射。Broker 只有在兼容握手确认当前 layout
仍把 `ACT12` 映射为 `composer.submit` 时才可接受批次；否则必须在提交任何
report 前返回 `NotSent/Incompatible`，避免执行错误命令。

X 的上下文合同不能混用：

| 上下文 | X |
| --- | --- |
| Base | Submit（Micro `ACT12` first） |
| RB Command | Fork（`ACT09`） |
| RT Turn | Steer |
| Y Action | Project context |
| LB Agent | 不得泄漏为 Base Submit |

### 8.1 简易模式模型选择

当前 Codex build 暴露 `composer.openModelPicker` 命令。Agent Controller 把
`ModelPickerShortcut`（默认 `Ctrl+Shift+M`）写入 Codex `keybindings.json`，简易
模式短按 R3 后直接发送该官方命令对应快捷键：

```text
R3 -> composer.openModelPicker
方向键 -> 官方列表移动
A / Enter -> 切换视图、进入子菜单或选择叶子
B / R3 / Escape -> 结束模型选择会话
```

当前 picker 是层级菜单：默认 Compact Power、Advanced，以及 Advanced 下的
Model/Effort/Speed。VHF/`SendInput` 的成功结果只能确认输入已经交付，不能区分
Enter 是切换 Compact/Advanced、进入子菜单，还是已经选择叶子。因此
Agent Controller 在任意 Enter 后继续持有模型选择会话；只有 B/R3/Escape 明确退出
后才恢复简易模式的 Power 输入。这样右摇杆不会在 Codex 菜单仍开着时泄漏成
`composer.decreaseReasoningEffort`/`composer.increaseReasoningEffort`，也就是默认的
F17/F18。

B/R3 退出时会再次调用幂等的 `composer.openModelPicker`：菜单仍开着时该命令只把
焦点拉回根选择器，叶子已使菜单关闭时则重新打开根选择器；随后向这个已归属的根
选择器发送 Escape。这样既能一次关闭仍在显示的子菜单，也不会把裸 Escape 发给
基础 composer。

这条路径不枚举 picker 的 UIA 元素；结束会话后等待 180 ms 再重新加载
`models_cache.json`。显示名 `GPT-5.6-Sol-Max` 和 `GPT-5.6 Sol Max` 都归一化为
`5.6 Sol Max`，不会因 Sol Max 没有 Simple Power 档位而从模型列表消失。首次写入
新 keybinding 后若 Codex 没有热加载，需要重启一次 Codex。

每次配置时都会检查快捷键写入结果；若默认键被其他命令占用、配置文件无效或写入
失败，R3 会在输入前停止，不进入本地“菜单已打开”状态。`SendInput` 成功只证明键盘
事件已注入，不是 `composer.openModelPicker` 的动作回执；在不枚举 UIA picker 的约束
下，Codex 未热加载或丢弃命令仍无法被正式确认。此时 B/R3 可安全清理本地菜单状态，
首次写入后重启 Codex 是首版的明确验收步骤。

## 9. B 的长按终止合同

“取消局部 UI”和“终止当前 turn”是两个动作：

| 上下文 | B 行为 |
| --- | --- |
| Base，无局部状态 | 持续 3 秒后才尝试 Stop |
| Base，有效 navigation undo | 短按只返回上一任务 |
| Base，过期 navigation undo | 清理 undo，同一次按压进入 3 秒 hold |
| 语音 / dial / picker / 待提交选择 | 短按只关闭或撤销局部状态 |
| LB Agent | 短按关闭 Agent 层 |
| RB Command | 短按 Decline；不是 Stop |
| Y Action | 短按关闭面板 |
| RT Turn | B 只触发 `BeginStopHold`；持续 3 秒后才 Stop |

普通 Base / RT 菜单中的停止路径只能在 hold 倒计时完成后调用
`InvokeAction("Stop", ...)`。`CancelComposer` 仍保留供语音、菜单或待提交选择等
局部取消流程使用，因此不得把它接回普通 B 的短按路径。
松开 B、控制器断开、Bridge 关闭、会话暂停或（启用前台限制时）Codex 失去前台
都会解除倒计时。

## 10. 验证清单

- rollout 尾行分段写入、截断、轮转时不产生假状态；
- `task_started` 覆盖旧 unread，`task_complete + unread` 显示绿色；
- 无 VHF 时不生成 RequiresInput；未闭合 turn 仍是粗粒度 Thinking，不从 UI
  或文件 mtime 猜审批/回应；
- Codex→device 的无 LF escaped JSON 与 device→Codex 的 UTF-8/LF 两套方向规则
  分别有多 report、超时和未知字段测试；
- output request 每个 id 恰好应答一次，断开/重连不复用旧 sequence；
- ACT12 Accepted/OutcomeUnknown 后都不会发送 F22 或 UIA fallback；只有发送前非空、
  发送后清空才报告成功，空 composer 不得误报；
- 自定义 ACT12 layout 时 Broker 拒绝 ACK；
- `off`/inactivity 不覆盖已有任务；没有独立 RosterProof 时禁止按 thread 合并；
- B 在 2999 ms 不 Stop，3000 ms 只 Stop 一次；
- 有效/过期 navigation undo、RT+B、断连与前台丢失均有回归测试；
- 未手动启用测试签名和安装驱动时，正式应用仍能安全使用 rollout 降级源。
