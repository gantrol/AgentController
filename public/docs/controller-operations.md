# 手柄操作列表

> Status: Product interaction contract
> 这份表描述目标交互，不代表所有路径已经通过当前 Codex build 的真机验收。

## 通道状态对照（2026-08-31）

本节专门区分 App Server 的官方协议能力与当前 simulator 已接通的代码。官方协议能力见 [Codex App Server 官方文档](https://developers.openai.com/codex/app-server)；“已接通”不等于仅在协议上存在。

| 语义操作 | App Server 能力 | 当前 simulator | HID / 当前主路径 |
| --- | --- | --- | --- |
| 最近任务列表 | thread/list | ✅ 直接只读；最多读取 18 条，映射前 6 条 | AG00..AG05 只是六个物理槽位，没有任意任务树能力 |
| 新建任务 | thread/start | ❌ 未接入 | 本地 UI、快捷键或 thread.create 适配器 |
| 打开/恢复任务 | thread/read、thread/resume | ❌ 未接入 | deeplink、窗口/UI 适配器；Agent 键不携带 thread ID |
| Fork | thread/fork | ❌ 未接入 | ACT09 原生 Micro 信号；当前缺少 App Server fork readback |
| 开始 Turn / Submit | turn/start | ❌ 未接入 | ACT12 原生 Submit 信号，由 Codex bridge 解释 |
| Steer | turn/steer | ❌ 未接入 | Running 上下文的 Composer 命令/适配器，尚未直接传入当前 Turn ID |
| Queue | 项目语义动作 | ❌ 未接入 App Server | Composer Dispatch、ACT12 或语义适配器 |
| Stop | turn/interrupt | ❌ 未接入 | B 长按目标路径仍是 UI/适配器；没有专用 Stop HID |
| Approve / Decline | App Server 审批 server request | ❌ 未接入 | ACT07 / ACT08 原生信号；必须验证审批上下文 |
| Turn/Item 实时状态 | turn/*、item/*、thread/status/changed | ❌ 未建立持续事件流 | thstatus 只反映 Micro 槽位/灯光，不是权威 Turn 状态 |
| 模型/effort | model/list 与 Turn 设置 | ⚠️ 间接 | codex-ipc 更新桌面当前任务下一轮设置；旋钮导航仍走 ENC |
| 额度 | account/rateLimits/read | ✅ 直接只读 | HID 无等价能力 |
| Micro 设备控制 | App Server 无对应能力 | — | AG、ACT、ENC、v.oai.rad、灯光和设备状态全部走 HID |

当前真正直接调用的 App Server 业务方法只有 thread/list 和 account/rateLimits/read；initialize / initialized 只是连接握手。其余操作即使在官方协议中存在，也不能在产品状态或成功判据中标为已实现。

## 1. 基础层

| 输入 | 操作 | 首选通道 |
| --- | --- | --- |
| Menu / Start / `+` | 启动 Codex，或将已运行的 Codex 置于前台 | Window/Agent adapter |
| 左摇杆上/下 | 在同级任务或项目条目间移动 | 本地任务导航 |
| 左摇杆右/左 | 进入项目 / 退出项目 | 本地任务导航 |
| L3 | 循环置顶任务、置顶项目、项目、未归项目任务 | 本地任务导航 |
| A | 打开当前任务或确认当前本地选择 | App Server / deeplink / verified adapter |
| 右摇杆上或左 | 选择上一 Composer 控件或菜单项 | Micro `ENC_CW` |
| 右摇杆下或右 | 选择下一 Composer 控件或菜单项 | Micro `ENC_CC` |
| R3 短按 | 打开、进入或确认当前 Micro 旋钮项 | Micro `ENC` down/up |
| R3 长按 | 打开 Agent Controller 设置 | 本地应用动作；不得泄漏成方向输入 |
| LT 按住/松开 | 开始/停止按住说话 | Micro `ACT10` down/up |
| X | 发送当前 composer 内容 | 已验证 Command slot；默认 `ACT12`，否则语义 adapter |
| B 短按 | 关闭当前 Micro 菜单、返回或撤回最近导航 | 菜单会话优先发送 Micro `AG00`；其他上下文 Cancel |
| B 长按 3 秒 | 终止当前运行；提前松开取消 | App Server `turn/interrupt` 优先 |
| Y | 打开动作面板 | 本地 UI |
| 十字键上/下 | 上一轮/下一轮；长按到顶部/底部 | Codex semantic/verified navigation adapter |
| LB/RB 短按 | 上一个/下一个可用任务 | 本地任务导航 |
| View | 保留键；当前不执行操作，后续可能用于切换当前受控 Agent | 无；保持 fail closed |

L3/R3 都表示**垂直按下摇杆帽**，不是把摇杆向下拨。

## 2. Y 动作面板

| Y 后输入 | 操作 |
| --- | --- |
| 十字键上 | 新建任务 |
| 十字键右/左 | Codex 历史前进/后退 |
| 十字键下 | 显示/隐藏 Codex 侧边栏 |
| A，再按一次 A | 二次确认后清空输入框 |
| X | 项目上下文：进入所属项目，或切换全部/仅置顶 |
| B 或 Y | 关闭动作面板 |

## 3. 按住组合层

| 层 | 输入与操作 | 首选通道 |
| --- | --- | --- |
| LB — Agent | 十字键上/右/下/左选择槽 1–4；View 选择槽 5；Menu 选择槽 6；B 取消 | Micro `AG00..AG05`；任意任务树不伪造 Agent slot |
| RB — Command | Y Fast；A Approve；B Decline；X Fork；View PTT；Menu Dispatch | 当前布局中已验证的 `ACT*` Command slot |
| RT — Running | X Steer；Y Queue；B 长按 3 秒 Stop；A Fork | App Server/语义 adapter；只在已验证运行上下文开放 |

## 4. 右摇杆固定合同

| 物理方向 | 含义 | 禁止行为 |
| --- | --- | --- |
| 上 | 选择上一控件/菜单项，发送一个 `ENC_CW` 档位 | 不得进入或确认 |
| 左 | 与“上”相同，发送一个 `ENC_CW` 档位 | 不得返回或减小当前控件 |
| 下 | 选择下一控件/菜单项，发送一个 `ENC_CC` 档位 | 不得进入或确认 |
| 右 | 与“下”相同，发送一个 `ENC_CC` 档位 | 不得进入或增大当前控件 |
| 回中 | 结束当前轴手势并释放 ownership | neutral 不得被合并丢失 |

一次手势只能由一个轴持有；必须回中后才能把 ownership 切换给另一个轴。两个轴最终投影到同一个 Micro encoder，菜单打开与否不能改变方向语义。R3 是唯一的进入/确认键；B 在 Micro 菜单会话中投影为官方 bridge 的 `AG00` 上下文返回。

## 5. 安全与结果规则

- Bridge 关闭时，手柄不得对 Codex 产生动作，只允许被动显示与安全释放。
- Approval、Decline、Stop、Steer 等高风险动作必须先确认上下文和目标任务。
- transport `Accepted` 不是业务完成；必须用可见状态、App Server 事件或其他 readback 验证结果。
- 只有明确 `NotSent` 才能走第二通道；`OutcomeUnknown` 后禁止自动重试非幂等动作。
- 手柄断连、进程退出或 Broker lease 丢失时，补发 PTT release 和 Analog neutral，但不能释放其他客户端仍持有的状态。
