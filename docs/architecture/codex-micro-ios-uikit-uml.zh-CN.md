# Codex Micro iOS：UIKit 架构与 UML

> 状态：历史 iOS 原型；当前目标已改为 macOS 桌面版，见[平台方向](platform-direction.zh-CN.md)。下文保留旧实现与提案，不再作为当前交付计划。
> 日期：2026-10-03
> 范围：独立 Codex Micro；iPhone / iPad；UIKit 为主
> 源码参考：Windows 工作树及 `virtual-micro/ios`；不依赖 AgentController 产品工程

本轮已落地 [Xcode 工程与运行说明](../../virtual-micro/ios/README.md)。iOS 界面按 Windows 的 4×4 实体键盘布局实现，覆盖此前的 3×2 自适应草案。图标沿用桌面语义，在 iOS 统一线宽、圆角和留白。本文第 2–3 节反映当前 UIKit 结构；后续完整 Host 协议与配对设计不代表已实现。当前网络字段以 [v0.1 子集合同](../../virtual-micro/ios/PROTOCOL.md) 为准。

## 1. 版本边界

**iOS 是触摸控制面，Codex Micro Host 是电脑上的配套程序。** Host 连接运行中的 Codex、整理状态并执行命令；iOS 负责配对、选择会话、呈现任务键和发送明确的用户意图。

| 项目 | 首版设计 |
| --- | --- |
| 界面 | UIKit、Auto Layout、Core Animation；程序化视图与 scene 生命周期 |
| 系统基线 | iOS / iPadOS 17+；Xcode 15+ |
| 设备 | iPhone 优先，iPad 自适应；首版单 scene |
| 连接 | 已实现手动配置 WSS 客户端；Bonjour、二维码配对待实现 |
| Host | 独立 Codex Micro Host；现有源码可作为 Windows 适配参考，macOS 适配待实现 |
| 产品依赖 | 不依赖 AgentController 的输入、ActionRouter、Domain、窗口或发布流程 |
| 能力策略 | Host 按会话下发能力；iOS 根据实际能力启用操作 |
| 首版之外 | 公网中继、后台持续监控、APNs、原生 HID 仿真、PTT、桌面空白草稿控制 |

现有 Micro 软件链路使用电脑本机的 named pipe、CLI 子进程和桌面深链。这些端点不能直接作为手机网络接口；需要新增 Host 的网络协议层。iOS 原型默认使用明确标记的 DEMO 数据；WSS 客户端尚未与真实 Host 联调。

手机保存 `selectedThreadId`；Host 另报 `desktopVisibleThreadId`。两者分别呈现，任何命令都显式携带 `hostId + threadId`，不默认操作“电脑当前那个聊天”。

## 2. 部署与组件视图

```mermaid
flowchart TB
    subgraph IOS["iPhone / iPad：Codex Micro iOS v0.1"]
        UIKit["UIKit 控制面"] --> Store["MicroStore / 单向状态更新"]
        Store --> Session["MicroTransport"]
        Session --> Demo["DemoTransport"]
        Session --> Live["WebSocketTransport"]
        Live --> Socket["URLSessionWebSocketTask"]
        Settings["ConnectionViewController"] --> Keys["HostKeychain"]
        Settings --> Store
        Pairing["待实现：配对与 Bonjour"] -.-> Settings
    end
    subgraph HOST["电脑：Codex Micro Host（拟增）"]
        Gateway["WSS Gateway / 配对与设备授权"] --> Commands["CommandService / RequestLedger"]
        Gateway <--> State["StateHub / snapshot + delta"]
        Commands --> Adapter["Codex Adapter"]
        Adapter --> IPC["Desktop IPC / owner"]
        Adapter --> Server["本地 App Server"]
        Adapter --> Navigation["桌面深链 / 可见状态观察"]
        Adapter --> State
        Readers["本地任务 / 未读 / rollout 观察"] --> State
    end
    Socket <-->|"Micro Remote Protocol v1 / TLS"| Gateway
    Pairing -.->|"发现候选，不授予信任"| Gateway
    IPC <--> Codex["电脑上的 Codex"]
    Server --> Data["本地 Codex 会话与模型目录"]
    Navigation --> Codex
```

Host 对外只暴露有限的 Micro 业务命令；Desktop IPC 的 owner、版本号、私有 JSON 和桌面路径留在 Host。手机不获得任意 shell、任意 IPC method 或任意文件访问入口。Host 的传输层可以跨平台，具体 Codex IPC / 窗口适配仍须按桌面平台实现。

`URLSessionWebSocketTask` 支持 WebSocket 消息及 WSS；协议选择为本项目设计，不代表 Codex 已提供这个服务。[Apple：URLSessionWebSocketTask](https://developer.apple.com/documentation/foundation/urlsessionwebsockettask)

## 3. UIKit 界面与交互

| 区域 | UIKit 实现 | 交互与约束 |
| --- | --- | --- |
| 主控制面 | `MicroViewController` + `MicroDeviceView` | 控制页六个任务键；监控页十四键；固定 590×610 坐标按比例缩放 |
| 任务键 | `KeycapControl: UIControl` | 轻点选择手机目标；长按菜单中请求桌面打开或停止 |
| 任务状态 | `CALayer` / 环光 / 中心点 | 沿用桌面状态颜色；VoiceOver 读出状态；过期时熄灭实时灯效 |
| 命令键 | `KeycapControl` | Fast、批准、拒绝、分叉、Codex；能力缺失时禁用 |
| 旋钮 | `EncoderControl: UIControl` + 手势识别 | 拖动跨过档位产生离散步进；轻点切快捷模型；取消手势清空待提交步进 |
| 旋钮替代操作 | `UIAccessibility` adjustable | VoiceOver 增减推理强度 |
| 模型选择 | `UIAlertController` action sheet | 来源为 Host 目录；菜单切换模型；旋钮切换 effort |
| 审批 | `UIAlertController` alert | 展示当前审批摘要并绑定 approvalID；多审批选择待实现 |
| 连接 | `ConnectionViewController` | DEMO、WSS 地址、令牌、可选指纹、重连、移除凭据 |
| 触觉 | `UISelectionFeedbackGenerator` 等 | 档位反馈与结果反馈分开；触摸反馈不代表执行成功 |
| 四向控制 | `JoystickControl: UIControl` | 保留 Windows 黑色摇杆；向上切 Plan，其余方向未绑定 |
| 底部旋钮 | `QuotaControl` | 双额度环、七段数字、三灯；轻点切模型、长按菜单 |
| 语音键 | 双宽 `KeycapControl` | 保留 Windows 位置和外形，当前禁用 |

任务项使用稳定的 `(hostID, threadID)` 标识，不能以键位序号绑定命令。控制页与 Windows 采用同一 4×4 网格，包含旋钮、摇杆、六任务键、四命令键、底部额度旋钮、双宽语音键与 Codex 键。iPhone、横屏与 iPad 保持相同键位；可用高度不足时滚动。只呈现必要的标题、数值、状态与控件标签，不新增说明性文案区。

外层使用 Auto Layout 处理安全区，设备内部沿用桌面设计坐标；网络快照先进入 `MicroStore`，再由主线程更新固定数量的控件。当前没有 UICollectionView 或额外状态管理框架。

### UIKit 类图

```mermaid
classDiagram
    class SceneDelegate
    class MicroViewController {
        +render()
        +showConnection()
    }
    class MicroDeviceView {
        +render(MicroStore)
    }
    class ConnectionViewController
    class KeycapControl
    class EncoderControl
    class JoystickControl
    class QuotaControl
    class MicroStore {
        <<MainActor>>
        +snapshot
        +selectedID
        +pending
        +execute(kind, value)
        +resume()
        +suspend()
    }
    class MicroTransport {
        <<protocol>>
        +connect(receive)
        +send(command)
        +query(requestIDs)
        +disconnect()
    }
    class DemoTransport
    class WebSocketTransport
    class HostKeychain
    SceneDelegate --> MicroStore
    SceneDelegate --> MicroViewController
    MicroViewController *-- MicroDeviceView
    MicroViewController --> ConnectionViewController
    MicroViewController --> MicroStore
    MicroDeviceView *-- KeycapControl
    MicroDeviceView *-- EncoderControl
    MicroDeviceView *-- JoystickControl
    MicroDeviceView *-- QuotaControl
    ConnectionViewController --> MicroStore
    ConnectionViewController --> HostKeychain
    MicroStore --> MicroTransport
    DemoTransport ..|> MicroTransport
    WebSocketTransport ..|> MicroTransport
    MicroTransport --> MicroStore : MicroEvent
```

View / ViewController 处理布局和交互；`MicroStore` 在 `@MainActor` 保存已确认状态、所选目标、在途命令与 unknown 标记。两个 transport 实现共享协议；WSS 网络 I/O 通过异步 API 完成，不阻塞主线程。原型采用全局单在途命令；后续再增加独立 session actor、合并连续输入及更多任务列表。

## 4. 手机与 Host 的协议合同

以下第 4–7 节保留完整 Host 设计，包含原型尚未实现的能力；线上的消息、字段与限制须参考 [v0.1 子集合同](../../virtual-micro/ios/PROTOCOL.md)。

协议命名为 **Micro Remote Protocol v1**，与 Codex Desktop IPC 的版本独立。数据使用类型化 `Codable` DTO；手机不接收完整内部会话对象，只接收控制面需要的字段。

### 数据类图

```mermaid
classDiagram
    class HostSession {
        +UUID hostId
        +UUID hostEpoch
        +UUID clientId
        +UUID controlLeaseId
        +int protocolVersion
    }
    class ThreadSnapshot {
        +UUID hostId
        +string threadId
        +long revision
        +string title
        +ThreadStatus status
        +ThreadSettings settings
        +CapabilitySet capabilities
    }
    class RemoteCommand {
        +UUID requestId
        +UUID hostEpoch
        +UUID controlLeaseId
        +CommandKind kind
        +CommandTarget target
        +CommandPayload payload
        +ExpectedState expected
    }
    class CommandTarget {
        +UUID hostId
        +string threadId
        +string turnId
        +string approvalId
    }
    class CommandReceipt {
        +UUID requestId
        +CommandStatus status
        +string reasonCode
        +CommandEvidence evidence
    }
    class StateDelta {
        +UUID hostEpoch
        +string streamId
        +long baseRevision
        +long revision
        +StateChange changes
    }
    HostSession --> ThreadSnapshot
    RemoteCommand *-- CommandTarget
    RemoteCommand ..> ThreadSnapshot : ExpectedState
    RemoteCommand --> CommandReceipt
    StateDelta --> ThreadSnapshot : apply if contiguous
```

`turnId`、`approvalId` 只在相应动作中存在；`CommandKind` 与 `CommandPayload` 使用匹配的枚举分支，不能任意组合。任务键与网络请求均保留原始目标，列表重排或用户切换手机目标不重定向已发出的命令。

| 消息 | 用途与约束 |
| --- | --- |
| `hello / welcome` | 协商协议、Host 身份、epoch、能力版本、消息上限；版本不兼容时不进入 Ready |
| `session.acquire / renew / release` | 获取有期限的控制租约；Host 用自身单调时钟计时，不依赖手机时钟 |
| `threads.list` | 分页读取可用任务，返回稳定 ID；与六个常用键位分开 |
| `state.subscribe` | 订阅 roster 与所选任务；首次返回 snapshot，后续发送带 baseRevision 的 delta |
| `command.execute` | 明确目标、唯一 requestId、类型化参数、必要的预期状态 |
| `command.receipt` | 分别返回 accepted、applied、rejected、notSent、unknown |
| `command.status` | 查询原 requestId 的记录；重连只查询，不重新提交原写命令 |
| `capabilities.changed` | 动态禁用失效能力；旧 capability 不能使新请求绕过 Host 核验 |

模型 / effort / Fast / Plan 下发**绝对目标值**；旋钮的相邻增量在手机端计算为一个目标值。每个任务同类设置至多一个在途请求，后续输入只保留最新意图；前一请求回读完成后重新计算。结果 unknown 时停止队列，不能继续基于乐观状态改写。

Host 的 revision 校验只保护 Host 自己的观察版本，不能假定等价于 Codex 服务端的原子条件写入。适配器复用已有条件字段，并回读实际结果；不支持相应语义时降低能力声明。

## 5. 配对与首次同步时序

```mermaid
sequenceDiagram
    actor User as 用户
    participant Phone as iOS UIKit
    participant Pair as PairingCoordinator
    participant Host as Codex Micro Host
    participant Key as Keychain
    participant State as MicroStore
    User->>Host: 打开限时配对窗口
    Host-->>User: 二维码（地址、Host 身份、证书指纹、一次性令牌）
    User->>Phone: 扫描二维码
    Phone->>Pair: 解析配对数据
    Pair->>Host: 校验指定 Host TLS 身份后提交一次性令牌
    Host->>Host: 核验期限、单次使用、配对窗口
    Host-->>Pair: 绑定 clientId 与可撤销设备凭据
    Pair->>Key: 保存 Host 绑定与设备凭据
    Pair->>Host: WSS 鉴权 + hello
    Host-->>Pair: welcome / hostEpoch / capabilities
    Pair->>Host: acquire lease + subscribe
    Host-->>Phone: roster / selected thread snapshot
    Phone->>State: 应用同一 epoch 的快照
    State-->>Phone: Ready
```

Bonjour 仅用于发现。首次信任来自用户扫描电脑上的配对二维码，后续匹配已保存的 Host 身份；证书或身份变化回到配对流程，不接受任意证书。一次性令牌不放入 Bonjour TXT、不作为长期密码；鉴权凭据不放在 URL 中。Host 可以单独撤销某部设备，默认只授予本产品定义的操作。

本地网络访问需声明 `NSLocalNetworkUsageDescription`；浏览指定 Bonjour 服务需列出 `NSBonjourServices`，拟用 `_codexmicro._tcp`。拒绝权限时连接不可用；没有发现服务不等同于用户拒绝权限。发现 API 使用 `NWBrowser`。[Apple：NWBrowser](https://developer.apple.com/documentation/network/nwbrowser)、[本地网络隐私](https://developer.apple.com/documentation/technotes/tn3179-understanding-local-network-privacy)

扫码使用系统相机能力，首次使用时请求相机权限；可预留手动录入同一完整配对材料的入口。普通发现不应额外发送自定义广播或扫描整个子网。

## 6. 命令执行与不确定结果

```mermaid
sequenceDiagram
    actor User as 用户
    participant UI as UIKit / ViewModel
    participant S as MicroSession
    participant H as Host CommandService
    participant L as RequestLedger
    participant C as Codex adapter
    User->>UI: 对选中任务执行动作
    UI->>S: 明确目标 + 参数 + 预期状态
    S->>H: command.execute(requestId, epoch, lease)
    H->>H: 核验设备、租约、目标、能力和预期状态
    alt 核验失败
        H-->>S: rejected / notSent
    else 可以发送
        H->>L: 持久记录 requestId + payloadHash + dispatching
        L-->>H: 已记录
        H-->>S: accepted
        H->>C: 单次语义调用
        alt 有与动作关联的状态证据
            C-->>H: 结果与回读
            H->>L: 保存 applied / rejected
            H-->>S: 最终 receipt + state event
        else 写出后结果无法确认
            H->>L: 保存 unknown
            H-->>S: unknown
        end
    end
    S-->>UI: 更新该 requestId 的状态
    opt 手机断线后恢复
        S->>H: 重新鉴权、同步并查询 command.status(requestId)
        H->>L: 查原请求记录
        L-->>H: 最终状态或 unknown
        H-->>S: 原记录；不重复执行
    end
```

同一设备与 requestId 只对应同一 payload；冲突 payload 被拒绝。Host 必须先记录 dispatching 再调用 Codex。Host 重启后的未完成记录归为 unknown；这不保证底层 Codex exactly-once，目的是避免 Host 自己盲目重放。账本过期或缺失同样不能证明原命令未执行。

### 命令状态机

```mermaid
stateDiagram-v2
    [*] --> LocalPending
    LocalPending --> NotSent: 本地取消 / 失活 / 发送前失败
    LocalPending --> InFlight: 提交网络层
    InFlight --> Rejected: Host 明确拒绝
    InFlight --> Accepted: Host 接受本次请求
    InFlight --> Unknown: 断线且无法证明未送达
    Accepted --> Applied: 对应业务状态已确认
    Accepted --> Rejected: 明确业务拒绝
    Accepted --> Unknown: 写出后超时 / Host 重启
    Unknown --> Applied: 查询账本或关联回读确认
    Unknown --> Rejected: 查询得到明确拒绝记录
    NotSent --> [*]
    Rejected --> [*]
    Applied --> [*]
```

`accepted` 不显示成功灯。深链启动只表示打开请求已交付；没有桌面可见状态证据时保留未确认。停止绑定确切 `turnId`，审批绑定确切 `approvalId`，分叉回传新 threadId 后，即使桌面打开失败，也不再执行第二次 fork。

UI 取消、切换目标或退后台都不能撤销已经被 Host 接受的命令；这类请求继续保留记录，恢复后查询结果。发送、分叉、审批、停止均不进入离线队列。

## 7. iOS 生命周期与连接状态机

```mermaid
stateDiagram-v2
    [*] --> Unpaired
    Unpaired --> Pairing: 用户开始配对
    Pairing --> Disconnected: 配对凭据已保存
    Pairing --> Unpaired: 取消 / 失败
    Disconnected --> Connecting: scene 活跃且用户连接
    Connecting --> Syncing: 鉴权与协议协商成功
    Connecting --> Blocked: 权限 / 信任 / 版本不满足
    Syncing --> Ready: 有效租约 + 完整快照
    Ready --> Syncing: epoch 改变 / delta 断档
    Ready --> Disconnected: 网络或 Host 断开
    Connecting --> Disconnected: 网络失败
    Syncing --> Disconnected: 同步中断
    Ready --> Suspended: scene 失活或进入后台
    Connecting --> Suspended: scene 失活
    Syncing --> Suspended: scene 失活
    Suspended --> Connecting: scene 恢复活跃
    Blocked --> Connecting: 用户解决问题后重连
    Ready --> Unpaired: 凭据被撤销
    Disconnected --> Unpaired: 用户解除配对
```

scene 离开 active 时立即禁止新命令、取消按住与旋钮积累，尽力释放租约并暂停连接。断线时由 Host 租约期限兜底；不能依赖手机最后一个 release 一定送达。此前已经接受的命令仍按上一节处理。

后台只保留缓存呈现，标记状态过期；回到前台先鉴权、取新租约、重建 snapshot，再恢复操作。重连后的 delta 必须匹配 `hostEpoch + streamId + baseRevision`，否则重新同步。不得把 socket 重连成功直接当作业务状态已同步。

UIKit scene 进入后台后可能被挂起或断开，因此首版不承诺锁屏后的持续实时状态，也不以音频等后台模式维持控制连接。[Apple：Managing your app’s life cycle](https://developer.apple.com/documentation/uikit/managing-your-app-s-life-cycle)

## 8. 首版能力矩阵

以下等级是完整产品的实施顺序。v0.1 已有任务键、状态灯、模型 / effort、Fast / Plan、停止、审批、分叉和桌面打开的 UIKit 入口及 DEMO 行为；真实能力均待 Host 接入。连接仅有手动 WSS，更多任务列表、Review、文字发送、未读写入仍未实现。

| 能力 | iOS 语义 | Host 参考与范围 | 阶段 |
| --- | --- | --- | --- |
| 主机连接 | 发现、配对、撤销、重连 | 新增网络 Host 与设备身份 | P0 |
| 任务键 / 更多任务 | 显式选择 threadId | 现有列表与 roster 读取；手机顺序独立于原生 AGxx | P0 |
| 状态灯 | 运行、待输入、完成未读、错误、过期 | IPC 与本地状态观察；区分来源和时效 | P0 |
| 桌面打开 | `desktop.openThread` | 本机深链；与手机选择分别记录结果 | P0 |
| 模型 / effort | `thread.setModel` / `thread.setEffort` | 现有 owner settings 与目录；只使用 Host 返回的可用值 | P0 |
| Fast / Plan | `thread.setFast` / `thread.setPlan` | 绝对布尔目标；Host 转换为真实 tier / mode | P0 |
| 停止 | `turn.stop` | 指定 activeTurnId；手机长按或明确确认 | P1 |
| 审批 | `approval.reply` | 指定单个请求；仅 command / file 等实际支持类型 | P1 |
| 分叉 / Review | `thread.fork` / `desktop.openReview` | fork 用 App Server，Review 用深链；依赖账本与回读 | P1 |
| 手机文字发送 | `turn.startText` | 手机自己提供的文本；显式任务、空闲检查；不代指桌面草稿 | P1 |
| 未读与问题状态 | `thread.setUnread` / 状态订阅 | 后续整合现有观察与写入通道；问答不冒充审批 | P1 |
| 桌面新草稿与模型切换 | 待定义独立 desktop-draft capability | 需要 Host 证明前台 Composer 归属，首版不接入 | 后续 |
| PTT / 语音、Composer 导航、滚动、技能插入 | 待定义 | 现有软件通道未完整提供，不能用触摸 down/up 假装支持 | 后续 |
| 锁屏推送 / 公网远程 | 待定义 | APNs、中继、身份与远程部署是独立范围 | 后续 |

## 9. 工程边界与落地顺序

当前 iOS 工程已落在 `virtual-micro/ios`；Host 部分仍为拟议结构：

```text
virtual-micro/ios/
  CodexMicro.xcodeproj
  CodexMicro/
    App/                  AppDelegate、SceneDelegate
    UI/                   双页设备、键帽、旋钮、摇杆、连接 sheet
    Core/                 MicroModels、MicroStore、待确认命令记录
    Connection/           DemoTransport、WebSocketTransport、HostKeychain
    Assets.xcassets/      六个命令矢量图标
    Info.plist
  Design/                 图标预览
  README.md
  PROTOCOL.md             v0.1 实际字段与消息合同

codex-micro-host/
  Gateway/                配对、WSS、授权
  Commands/               命令核验、调度、RequestLedger
  State/                  StateHub、snapshot/delta 投影
  Codex/                  Desktop IPC、App Server、状态观察
  Platform/               Windows；macOS 后续适配

protocol/                 后续完整消息定义、能力语义、版本规则
```

Swift 数据类型只依赖 Foundation；UIKit 通过 Store 交互，Store 通过 MicroTransport 使用 DEMO 或 WSS。Host 使用独立合同，不引用手柄产品的动作类型。现有桌面实现可作为 Host 内部适配参考，但当前 C# 程序集仍有其他产品依赖，不能直接等同于独立 Host。

| 顺序 | 工作 | 交付边界 |
| --- | --- | --- |
| 1 | v0.1 子集合同已随工程提供 | Host 实现前仍需联调验证 requestID、epoch、revision、能力定义 |
| 2 | UIKit 控制面原型已创建 | Windows 布局、矢量图标、交互式 DEMO；静态检查完成，待 Mac 编译 |
| 3 | 最小 Host 与配对 | 单机配对、设备撤销、WSS、租约、roster snapshot；先不开放写操作 |
| 4 | 状态与设置闭环 | 任务状态、桌面打开、模型 / effort / Fast / Plan；以回读确认结果 |
| 5 | 特定请求操作 | 账本、断线恢复后再开放停止 / 审批 / 分叉 / 文字发送 |
| 6 | 设备和平台扩展 | iPad 细化、macOS Host、语音与远程连接按独立能力推进 |

## 10. 现有源码参考

这里只记录可借鉴的桌面实现，不构成 iOS 或 Host 的项目依赖。

| 内容 | 当前工作树路径 |
| --- | --- |
| owner 发现、设置、停止、审批、显式文本发送 | [KeypadController](../../src/AgentController.Adapters.Codex.Software/KeypadController.cs) |
| 当前用户 named pipe 与 Desktop IPC framing | [CodexPeerClient](../../src/AgentController.Adapters.Codex.Software/DesktopIpc/CodexPeerClient.cs) |
| 本地目录与 fork | [LocalAppServer](../../src/AgentController.Adapters.Codex.Software/AppServer/LocalAppServer.cs) |
| Micro 控件到软件动作映射 | [SoftwareMicroTransport](../../virtual-micro/src/AgentController.MicroSurface.Wpf/SoftwareControl/SoftwareMicroTransport.cs) |
| 模型状态与 revision 处理 | [CodexModelToggleService](../../virtual-micro/src/CodexMicro.Desktop/Services/CodexModelToggleService.cs) |
| 问答状态订阅 | [SoftwareQuestionObserver](../../virtual-micro/src/AgentController.MicroSurface.Wpf/SoftwareControl/SoftwareQuestionObserver.cs) |
| 桌面选择观察的局限 | [CodexSelectedThreadReader](../../virtual-micro/src/CodexMicro.Desktop/Services/CodexSelectedThreadReader.cs) |

已创建 UIKit 工程、DEMO 行为与 WSS 客户端子集，并完成语法、工程引用和资源格式静态检查。当前 Windows 环境没有 Xcode，尚未编译 iOS；未启动网络 Host、编写测试代码或运行 UI / 手动测试。
