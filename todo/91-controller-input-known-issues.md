# 91 — 控制器输入已知问题与实机复现

> Status: Code remediation complete; real-device acceptance pending
> Priority: P0 for right-stick navigation; P1 for intermittent input loss
> Depends on: 03-codex-micro-compatibility, 08-testing-observability-and-release

> 2026-07-19 实机回归重新发现两条 P0：右摇杆横向完全无动作，以及菜单键只显示 Agent Controller 反馈、无法把 Codex 置前。代码根因已分别在 `c7754f7`、`be0d67e` 修复；多 Codex 主窗口循环选择在 `af81c9a` 增补。仍须用新构建复验，不能因自动化与一次本机激活探针通过而关闭本条目。

## 目的

集中记录真实 Codex Desktop + 实体手柄的控制器输入问题、代码修复与实机验收。这里的现象优先于单元测试结论；没有真实 HID/Micro 发送记录、界面状态变化和实机复现，不得把问题标记为完成。

2026-07-18 早先的右摇杆试验分支已全部丢弃。当前 `codex/fix-controller-input-91` 从 `main` 重新按故障层实施：先固定物理手势边界，再冻结纵横轴语义，随后处理 PTT、readback、横向执行器、异步 session，最后引入单 Broker。下面保留修复前基线，并把代码证据与仍未完成的实机证据分开记录。

## 问题基线（修复前）

| ID | 场景 | 期望 | 修复前实际 |
| --- | --- | --- | --- |
| RS-01 | Power 横条 | 上/左产生上一档，右/下产生下一档；全部由官方 encoder bridge 决定实际 Power 变化 | 旧实现把横向交给另一套 Left/Right executor，形成两套语义 |
| RS-02 | `Approve for me` | R3 进入；四向只分为 previous/next；B 返回 | 旧实现把右当进入、左当退出，菜单内没有可靠可见高亮，输入容易表现为卡住 |
| RS-03 | `Add files and more @` | R3 进入；上/左上一项，右/下下一项；B 返回 | 旧横向动作可能反向或落到错误目标，菜单选择不可靠 |
| RS-04 | 模型选择器 | R3 打开/进入/确认；四向遍历；B 返回 | 左/右行为仍不正确，上/下与左/右容易混用 |
| RS-05 | Composer 主控件 | 四向遍历 Advanced、Fast、Power 等控件；R3 进入/确认 | 旧实现让纵向选择、横向调整/进入，当前控件与实际焦点会失配 |
| RS-06 | 连续或斜向拨动右摇杆 | 一次手势只能由一个轴拥有，回中后下一手势重新判定 | 纵横指令偶发混淆；斜向起步、快速换向和回中附近更容易出现 |
| RS-07 | 模型控制器长期使用 | 每次手势都能继续控制当前模型界面 | 偶发完全失去手柄控制；打开模型选择器后又恢复，恢复动作只是复现线索，不是解决方案 |
| LT-01 | LT 按住说话 | 按下开始、松开停止，任何退出或断连都补发 release | 偶发语音键无效；尚未确认是边沿丢失、策略阻止、自动化失败还是 Codex 未读回 |
| SRC-01 | 实体控制器 + `virtual-micro` 模拟器 | 两者可同时连接，由 Broker 串行化输入并分别管理 held/neutral 生命周期 | 当前看起来只能有一个输入源正常占用；共享句柄、sequence、output/RPC 所有权尚未统一 |
| RS-08 | 2026-07-19 新构建，右摇杆纯左/纯右 | `Closed` 基态也必须投递屏幕同向的横向动作；上下仍只产生 `ENC_*` | 横向 intent 被“必须有非空 UIA ItemName”的门禁永久挂起，左右均毫无反应 |
| WAK-01 | 菜单键唤醒 | Agent Controller 显示反馈后，真实 Codex 主窗口成为前台；若已有多个主窗口，再按菜单键切换下一个 | popup 正常出现，但旧实现按最新进程的 `MainWindowHandle` 选窗并从工作线程直接 `SetForegroundWindow`，Windows 拒绝置前 |
| BACK-01 | B 退出 Micro 菜单 | 在 R3 建立的菜单会话中发送 `AG00` tap，由官方 bridge 上下文转换为 Escape | 旧实现依赖本地 popup/UIA ownership；驱动已接受 `ENC` 但回读尚未识别菜单时，B 会落入普通取消路径或退出失败 |

## 从 `virtual-micro` 更新确认的事实（2026-07-19）

- 旋钮旋转仍只有 `ENC_CW` / `ENC_CC`、`act=2`；旋钮按压仍为 `ENC` down/up；模拟摇杆是独立的 `v.oai.rad`。Agent Controller 不再发明“横向旋钮”协议，而是把摇杆四向折叠成同一个 encoder：上/左 → `ENC_CW`，下/右 → `ENC_CC`。
- 当前 26.715.4045 bridge 对菜单中的 Agent 键有明确上下文逻辑：`AG00..AG05` 按下都会阻止普通 Agent 切换，其中只有 `AG00` 向当前 Composer 菜单发送 Escape。因此 B 的 Micro-first 投影是已知菜单会话中的 `AG00` tap，不是裸 Escape。
- `fix(micro): make encoder input responsive` 使用有界 accumulator、约 24 ms 的档位间隔和 180 ms 过期时间，不在每个档位发送前同步等待 UIA。Agent Controller 的纵向实现继续遵守这套原则。
- `virtual-micro` 的 `CodexWindowActivator` 不信任 `Process.MainWindowHandle`：它枚举 Codex 顶层窗，按 tool-window、owner、窗口类和面积评分，再通过 `AttachThreadInput`、`BringWindowToTop`、`SetForegroundWindow`、`SetActiveWindow`、`SetFocus` 与 `SwitchToThisWindow` 置前。Agent Controller 已移植同一策略，并补上工作线程消息队列初始化。
- v1.0.1/v1.0.2 的新 build 兼容与屏幕灯光保持逻辑不改变上述输入映射；单 Broker 仍是实体控制器与模拟器并存时唯一允许的驱动 owner。

## 代码修复状态（2026-07-18）

| ID | 已实施修复 | 自动化证据 | 实机状态 |
| --- | --- | --- | --- |
| RS-01 / RS-02 / RS-03 / RS-04 / RS-05 | 两个摇杆轴统一投影为官方 encoder：上/左 previous，下/右 next；R3 独占进入/确认，不再运行横向 current-control executor | `VirtualDialInputPolicyTests`、`ComposerDialNativeInputPolicyTests` | 待真实 Power、Approve、Add files、模型、Advanced 顺序复验 |
| RS-06 | state buffer 保留跨区、换向和完整 neutral；手势期间同时锁定轴与方向，回中后才重新判定 | `ControllerStateBufferTests`、`StickGestureRouterTests` | 待慢速、快速、斜向和未完全回中矩阵 |
| RS-07 | encoder intent 有界合并且 180 ms 过期；横向 intent 绑定 generation 且 450 ms 过期；readback 合并请求，不再通过 cancellation 互相饿死；Broker 在应用启动时后台预热且失败重连退避 | `EncoderStepAccumulatorTests`、`CurrentControlIntentBufferTests`、`BrokerCoexistenceTests` | 待长期重复与“打开模型选择器后恢复”复验 |
| LT-01 | PTT 改为 Micro-first down/up；release 不确定时补发一次；下一次 press 先恢复 neutral；断连/退出保留 release | `MicroRpcCodecTests`、`PushToTalkAutomationStateTests`、`ControllerStateBufferTests` | 待短按、口述、菜单、失焦、断连复验 |
| SRC-01 | Agent Controller 与模拟器都改为 named-pipe client；唯一 Broker 独占 `CodexMicroVhfUm`、统一 sequence 和 output/RPC reader；重叠 held key 只投递第一次 down/最后一次 up；analog owner 释放时恢复最近仍活跃来源；请求执行/完成续期/lease expiry 原子化；缓存 request response 防止超时重放双发 | `BrokerCoexistenceTests`、`ClientInputStateTests`、`MicroInputBatchTests`、`MicroDriverOwnershipRulesTests` | 三客户端 fake-driver 仲裁与 lease 边界验收通过；真实驱动双进程仍待复验 |
| RS-08 | `Closed` 是“已确认无打开 surface”，不再要求虚构的非空 `ItemName`；横向策略直接保留 literal Left/Right，执行器仍要求 Codex 前台。任何已识别的具体 surface 继续要求真实焦点与目标名称一致 | `CurrentControlActionPolicyTests`、`VirtualDialInputPolicyTests`、`StickGestureRouterTests` | 25 项定向测试通过；待新构建纯左/纯右实机复验 |
| WAK-01 | 统一枚举并评分 Codex 顶层窗；排除/降权 owned/tool window，恢复最小化主窗，附加输入线程后置前；UIA locator 复用同一主窗选择结果。菜单键在 Codex 已前台且存在多个主窗口时只循环主窗口，普通快捷键聚焦不循环 | `Win32InputTests`；后台 Chrome → Codex 一次性实机激活探针 | 探针从 `chrome` 前台成功切至 `ChatGPT/Codex`；仍待菜单键实体手柄复验与双主窗口复验 |
| BACK-01 | R3 成功后持有本地 Micro 菜单会话；会话内 B 优先发送官方 `AG00` 上下文返回，只有 `NotSent` 才进入原生退出回退；回读不能提前释放会话 | `ComposerDialNativeInputPolicyTests` | 待 R3 → Advanced → 子菜单 → B 一次退出实机复验 |

分层提交：

1. `f809b90` — 保留手势边界；
2. `1030dff` — 纵向固定走 Micro encoder；
3. `b114dd3` — PTT Micro-first 与恢复状态机；
4. `f054aca` — 只读菜单/readback 验证；
5. `40ab80f` — 已验证横向控件执行器；
6. `65a1dfa` — generation、TTL 与 readback 饥饿修复；
7. `38b42f8` — 单 Broker、多客户端 lease；
8. `119a815` — 架构测试禁止桌面进程重新直接打开驱动；
9. `750e653` — request response cache，重复非幂等请求不双发；
10. `2465499` — 应用启动时后台预热 Broker，输入路径不承担首次连接等待；
11. `d2879ae` — 重叠 held key 引用合并与 analog 最近活跃 owner 恢复；
12. `7a41fa4` — 请求执行、完成续期与 lease expiry 原子化。
13. `c7754f7` — 修复 `Closed` 基态横向 intent 被空名称门禁吞掉；
14. `be0d67e` — 移植并强化 `virtual-micro` 主窗口枚举与前台激活；
15. `af81c9a` — 菜单键在多个 Codex 主窗口之间显式循环，普通聚焦保持稳定。

当前自动化基线为主解决方案 796 项、`CodexMicro.Protocol` 5 项；共享工作区中包含待提交 `virtual-micro` 兼容更新时，`CodexMicro.Desktop` 47 项，全部通过。这个结果只证明可重复的代码故障层已经被覆盖，不替代下方未勾选的实机矩阵。

## 不可变交互合同

- 右摇杆模拟 Micro 左上角旋钮的完整交互，不再维持另一套“简易/高级模型控制”状态机。
- 上/左是同一个 previous 手势，产生 `ENC_CW act=2`；下/右是同一个 next 手势，产生 `ENC_CC act=2`。两个物理轴不再拥有不同业务语义。
- R3 短按表示旋钮按压，长按打开 Agent Controller 设置；二者都不得被解释为方向动作，且长按必须抑制同一次手势的短按。教程必须说明 R3 是“垂直按下右摇杆帽”。
- R3 是唯一的打开、进入与确认键。B 是唯一的菜单返回键：会话内发送 `AG00` tap，由官方 bridge 转成 Escape；R3 不再兼任退出。
- 菜单打开后必须有可见选择或可验证 readback。只有“菜单已打开”而没有当前项身份，不算导航成功。
- Micro 驱动返回 `Accepted`、`OutcomeUnknown` 或 `Rejected` 后不得再注入第二套 UIA/键盘动作；只有明确 `NotSent` 才允许降级。
- neutral、key-up 和 PTT release 不能被模拟量合并丢弃；断连时只释放该输入源持有的状态。

## 右摇杆到 Codex 的端到端 UML

### 修复前链路（历史问题基线）

修复前实现不是一条稳定的 Micro 链路，而是根据菜单状态在 Micro、原生方向键和 UIA 之间切换：

```mermaid
flowchart LR
    subgraph PAD["物理手柄"]
        A["右摇杆<br/>RightX / RightY"]
    end

    subgraph INPUT["Agent Controller 输入层"]
        B["XInputService<br/>每 16 ms 轮询<br/>XInput / WGI / Raw HID"]
        C["ControllerStateBuffer<br/>合并模拟量快照"]
        D["UI Dispatcher<br/>ProcessControllerState"]
        E{"输入门禁<br/>Bridge / 前台 / LT / Radial<br/>等待回中"}
        F["StickGestureRouter<br/>死区 + 主轴判定<br/>锁轴直到回中"]
        G["AxisRepeater<br/>right-x / right-y"]
        H["VirtualDialInputPolicy<br/>Up / Down / Left / Right"]
        I["pending navigation<br/>异步 Pump"]
    end

    subgraph ROUTE["当前 Composer 路由"]
        J["CodexComposerService<br/>DialNavigate"]
        K{"菜单状态与方向"}
        L["菜单打开 + 上/下<br/>SendEncoderSteps"]
        M["菜单打开 + 左/右<br/>发送原生方向键"]
        N["菜单关闭 + 左/右<br/>DialStep"]
        O["MicroInputService<br/>ENC_CW / ENC_CC<br/>act = 2"]
        P["Legacy fallback<br/>UIA 控件探测 / 聚焦"]
    end

    subgraph DEVICE["Windows Micro 设备平面"]
        Q["MicroRpcCodec<br/>v.oai.hid + LF<br/>64-byte Report 0x06/0x02"]
        R["VhfMicroReportTransport<br/>sequence + IOCTL_SUBMIT_INPUT"]
        S["VHF source driver<br/>VhfReadReportSubmit"]
        T["Windows HID stack<br/>虚拟 vendor HID"]
    end

    subgraph CODEX["Codex Desktop 私有接收链"]
        U["主进程<br/>codex-micro-service<br/>监听 HID"]
        V["Renderer<br/>codex-micro-bridge<br/>映射 Micro 事件"]
        W["Composer / Popup<br/>移动高亮或调整控件"]
    end

    A --> B --> C --> D --> E
    E -->|"允许"| F --> G --> H --> I --> J --> K
    E -->|"阻止"| X["丢弃或等待 neutral"]

    K -->|"打开 + 上/下"| L --> O
    K -->|"打开 + 左/右"| M --> W
    K -->|"关闭 + 左/右"| N --> O
    K -->|"关闭 + 上/下"| Y["当前无动作 / unsupported"]

    L -->|"仅 NotSent"| M
    N -->|"仅 NotSent"| P --> W
    O --> Q --> R --> S --> T --> U --> V --> W
```

### 修复前轴语义冲突（已移除）

菜单状态会改变哪个物理轴被转换成 `ENC_*`，所以“上下、左右混淆”不一定只来自摇杆斜向噪声：

```mermaid
flowchart LR
    Y["右摇杆上/下"] -->|"菜单打开"| E1["ENC_CW / ENC_CC"]
    X["右摇杆左/右"] -->|"菜单关闭时 DialStep"| E2["ENC_CW / ENC_CC"]
    E1 --> SAME["同一个 Micro Encoder 命令入口"]
    E2 --> SAME
```

### 修复后实际链路

```mermaid
flowchart LR
    State["ControllerState<br/>RightX / RightY"] --> Buffer["ControllerStateBuffer<br/>保留换向与 neutral"]
    Buffer --> Router["StickGestureRouter<br/>锁定轴 + 方向直到回中"]

    Router -->|"Vertical: 上/下"| Projection["Micro-first projection<br/>上 = +1 / 下 = -1"]
    Router -->|"Horizontal: 左/右"| Projection2["Micro-first projection<br/>左 = +1 / 右 = -1"]
    Projection --> Encoder["EncoderStepAccumulator<br/>有界 + 180 ms TTL"]
    Projection2 --> Encoder
    Encoder --> Micro["MicroInputService<br/>ENC_CW / ENC_CC act=2"]
    Micro --> Client["MicroBrokerClient<br/>clientId + requestId"]
    Client --> Pipe["current-user named pipe"]
    Pipe --> Broker["唯一 Micro Broker<br/>global sequence / lease / output reader"]
    Broker --> Driver["CodexMicroVhfUm<br/>UMDF2 / VHF"]
    Driver --> Codex["Windows HID → codex-micro-service → bridge"]

    R3["R3 短按"] --> MicroPress["Micro ENC down/up<br/>打开 / 进入 / 确认"] --> Client
    B["B · 已持有菜单会话"] --> AgentBack["Micro AG00 down/up<br/>官方 bridge 上下文 Back"] --> Client
```

两个轴都不读取 popup，也不再走 `CurrentControlIntentBuffer` 或 `CurrentControlExecutor`。UIA 只提供异步反馈，不能决定发送哪一类输入。R3 成功投递后建立本地菜单会话；B 只在该会话内发送 `AG00`，避免菜单其实不存在时误切 Agent 槽位 1。

### 修复后原生 Micro 时序

右摇杆四向固定表示同一个 Micro 旋钮旋转；路由不得读取或猜测 Codex popup 来改变方向语义：

```mermaid
sequenceDiagram
    autonumber

    actor User as 用户
    participant Pad as 实体手柄
    participant Input as Controller Input
    participant Gesture as PhysicalGestureEngine
    participant Projection as Micro Projection
    participant Codec as Micro Codec
    participant Client as Broker Client
    participant Broker as Micro Broker
    participant Driver as CodexMicroVhfUm / UMDF2 VHF
    participant HID as Windows HID
    participant Service as codex-micro-service
    participant Bridge as codex-micro-bridge
    participant Composer as Codex Composer

    User->>Pad: 右摇杆向上
    Pad->>Input: RightY 超过 engage threshold
    Input->>Gesture: 原始快照 + deviceId + timestamp
    Gesture->>Gesture: 锁定 Vertical 轴
    Gesture->>Projection: EncoderStep(+1)

    Projection->>Codec: 编码 ENC_CW, act=2
    Codec-->>Client: 64-byte Report, ID 0x06 / Channel 0x02
    Client->>Broker: named-pipe submit + clientId + requestId
    Broker->>Driver: IOCTL SubmitInput(batch + global sequence)
    Driver->>HID: VhfReadReportSubmit
    HID->>Service: HID input report
    Service->>Bridge: Micro encoder event
    Bridge->>Composer: composer-navigation 向上选择
    Composer-->>User: 可见高亮移动

    Note over Broker,Composer: Transport Accepted 只证明驱动接收；可见高亮或 readback 才证明操作生效

    User->>Pad: 摇杆完全回中
    Pad->>Gesture: neutral
    Gesture->>Gesture: 释放 Vertical ownership
```

目标映射固定如下：

| 手柄动作 | 类型化意图 | Codex 最终输入 |
| --- | --- | --- |
| 右摇杆上 | `EncoderStep(+1)` | `ENC_CW, act=2` |
| 右摇杆左 | `EncoderStep(+1)` | `ENC_CW, act=2` |
| 右摇杆下 | `EncoderStep(-1)` | `ENC_CC, act=2` |
| 右摇杆右 | `EncoderStep(-1)` | `ENC_CC, act=2` |
| R3 短按 | `EncoderPress` | `ENC` down/up |
| R3 长按 | `OpenAgentControllerSettings` | 本地应用动作；不发送 `ENC`，并抑制同一次短按 |
| B（Micro 菜单会话） | `ContextualBack` | `AG00` down/up；官方 bridge 转换为 Escape |
| 回中 | `Neutral` | 释放轴 ownership，不得被快照合并丢失 |

## 已处理的故障层与仍待确认部分

代码 fixture 已把问题拆到以下确定层，不再用一个“右摇杆偶发失灵”概括全部故障：

| 故障层 | 修复 |
| --- | --- |
| Input buffer | 不能跨 gesture region 合并；按钮、LT 阈值、方向和 neutral 边界必须保留 |
| Gesture | 一次手势只有一个轴和一个方向；换轴、反向都必须先完整回中 |
| Projection | 四向折叠到一个 encoder：上/左为 `ENC_CW`，下/右为 `ENC_CC`；R3 为 `ENC`，B 会话返回为 `AG00` |
| Session | encoder 有界合并；R3 建立、B 结束 Micro 菜单会话；readback 使用 coalescing gate 且不能提前释放会话 |
| Verification | popup visible 不等于成功；UIA 只做反馈。B 的 `AG00` 必须由本地会话门禁，防止无菜单时误切 Agent 1 |
| Window activation | 枚举并评分所有 Codex 顶层窗，不再按进程启动时间猜 `MainWindowHandle`；工作线程先创建消息队列并附加输入线程；菜单循环只包含无 owner、非 tool 的主窗口 |
| PTT | down/up、OutcomeUnknown、release retry、restart neutral 与断连清理进入同一状态机 |
| Transport ownership | 单 Broker 独占 handle、sequence 与 output reader；客户端状态按 lease 隔离；同键引用合并，analog 按最近活跃 owner 恢复；活跃请求不会被 lease sweeper 中途摘除 |

以下部分仍必须由实机记录确认，不能从自动化测试推断：

- 实体控制器采样与 WPF Dispatcher 长时间繁忙时，是否仍能观察到完整 neutral，`DroppedStateCount` 是否增加。
- 每个真实手势是否只出现一个轴、一个方向和一个执行通道；Micro 返回非 `NotSent` 后是否仍有第二次原生按键。
- 当前 Codex build 的菜单 selection、UIA focus 与可见高亮是否一致；不一致时是否仍只影响反馈而不产生第二套输入。
- popup 打开/关闭、失焦/恢复和鼠标先操作后，generation/TTL 是否阻止旧 intent 重放。
- 当前 Codex build 对 Approve、Add files、模型列表、Advanced、Fast、Power 暴露的 readback 结构是否覆盖现有观察器。
- 两个真实桌面进程是否都只连接 Broker，驱动 batch sequence 是否单调，output/RPC 是否只有一个 reader；重叠 PTT 是否只有一次物理 down/up，analog 后来 owner 释放后是否恢复前一来源。
- 菜单键从 Chrome、桌面、最小化 Codex 等不同前台状态唤醒时，真实主窗口是否每次置前；存在两个 Codex 主窗口时是否只在两者间循环而永不命中工具窗。

## 下一次排查必须采集的证据

对每次手势使用同一个 correlation/session id，按时间顺序记录：

1. 原始控制器快照：X/Y、dead zone、时间戳、设备 ID、是否出现完整 neutral。
2. 手势判定：获胜轴、方向、enter/repeat/exit、轴 ownership 的建立和释放原因。
3. 路由上下文：composer 控件、popup/menu 类型、进入/退出、当前 session epoch、pending/cancel 状态。
4. 实际执行通道：Micro intent/report、batch sequence、四态发送结果，或明确的 fallback 原因；禁止只写“已处理”。
5. Codex 前后状态：popup 是否可见、当前项/控件 identity、可见高亮、值变化和 readback。
6. 多输入源状态：客户端 lease、held keys、neutral、output/RPC reader 与断连清理归属。

日志应能回答“一次物理手势为什么生成这条指令、由哪个通道执行、最终改变了哪个可见控件”，并提供默认关闭、可脱敏导出的诊断包。

## 最小复现矩阵

- [ ] 在 Power 横条验证纯上、左、下、右各 10 次，确认上=左、下=右且每次只有一个 encoder 档位。
- [ ] 从 composer 主界面用四向各 10 次遍历 Advanced、Fast、Power，确认只有 previous/next 两种方向语义。
- [ ] 分别进入 `Approve for me`、`Add files and more @` 和模型选择器，验证 R3 进入/确认、四向移动、B 通过 `AG00` 返回。
- [ ] 对每个场景覆盖：缓慢单轴、快速单轴、斜向起步、未完全回中换轴、完全回中后换轴、长时间重复。
- [ ] 覆盖 popup 打开/关闭、Codex 失焦/恢复、鼠标先打开菜单、R3 打开模型列表、菜单中途关闭。
- [ ] 复现模型控制失活，并比较选择器打开前后完整状态快照；不得只记录“打开后恢复”。
- [ ] 对 LT 覆盖短按、正常口述、菜单开关、失焦、断连，并确认 down/up 是否都到达每一层。
- [ ] 单独连接实体控制器、单独连接模拟器、先后交换连接顺序、同时持续输入；重点复验同一 PTT 的重叠 down/up 与 analog owner 交接，验证 SRC-01 的 lease、恢复顺序与 sequence。
- [ ] 从 Chrome、桌面、最小化 Codex 三种状态各按菜单键 10 次；再打开两个 Codex 主窗口连续点按菜单键，确认按主窗口顺序循环且不命中 tool/popup。
- [ ] 至少在 Xbox Series、Flydigi Vader 4 Pro、8BitDo Ultimate 2 和当前支持的 Codex build 上保存实机结果。

## 完成门槛

- RS-02 至 RS-07、LT-01 和 SRC-01 各有确定失败层、可重复 fixture 和修复前失败/修复后通过的证据。
- [`08-testing-observability-and-release.md`](08-testing-observability-and-release.md) 的基础实机验收通过，且自动化测试不能替代真机记录。
- 右摇杆和 PTT 在 Micro 可表达时走驱动；legacy UIA 仅保留 `NotSent` 的有期限降级路径。
- 实体控制器与模拟器通过 [`03-codex-micro-compatibility.md`](03-codex-micro-compatibility.md) 定义的单 Broker、多客户端 lease 验收。
- UI、日志和反馈明确显示当前通道与已验证结果，不再把 transport ACK 或 popup visible 当作操作成功。
