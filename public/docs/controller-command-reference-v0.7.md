# Agent Controller 手柄指令清单（v0.7）

> 更新日期：2026-07-16  
> 对照范围：当前工作区源代码；`docs/interaction-spec-v0.4b-physical-controller-mapping.md`
> 只作为历史映射基线。  
> 本文把“当前已有路由”和“规范目标”分开记录；出现“已实现”只表示
> 源代码中存在对应路径，不等于已经通过当前版本 Codex UI 的真机验收。
>
> 风险声明：这个实验性 v0.7 原型由 Codex 使用 GPT-5.6 Sol 在一天内完成，
> 未经独立人工代码/安全审计。Codex 更新可能导致 UI Automation 或快捷键
> 失效、误操作；发布包未签名。请审查源码，仅以非关键任务试用并自行承担风险。

## 1. 状态说明

| 标记 | 含义 |
| --- | --- |
| 已实现 | 当前源代码中已有输入路由和执行路径 |
| 部分实现 | 已有路径，但语义、上下文检测或真机稳定性仍不完整 |
| 规划 | 后续交互方案已定义，当前代码尚未实现 |
| 不一致 | 当前代码与 v0.4b 规范或最新产品决定不同 |
| 需要桥接 | `BridgeEnabled=true` 且控制器会话可用时才能控制 Codex |

## 2. “启用桥接”是全局总开关

### 2.1 最终产品语义

`BridgeEnabled=false` 时，所有手柄输入一律不得控制 Codex：

- Menu 不能唤醒、置前或解锁 Codex；
- LT 不能开始或停止新的语音识别；
- 左右摇杆、十字键、A/B/X/Y、LB/RB/RT、View、L3/R3
  都不执行 Codex 动作；
- 组合层和虚拟旋钮不得打开；
- 不提供任何“仍可通过手柄重新开启桥接”的例外。

桥接只能通过 Agent Controller 桌面界面重新开启。托盘可以恢复
Agent Controller 窗口，但不能绕过桥接开关直接控制 Codex。

桥接关闭后仍可继续的只有被动状态与安全清理：

- 发现手柄、识别 Profile、显示实时按键和连接状态；
- 清空按键边沿、等待摇杆和扳机回中；
- 取消尚未提交的本地任务；
- 若关闭桥接时正在录音或持有 Agent Controller 打开的菜单，
  做一次停止/释放清理，防止录音或按键状态卡住；
- 鼠标和键盘操作 Agent Controller 自身的设置页。

### 2.2 当前实现

控制器入口现已在 Menu、LT、组合层、摇杆和所有 Base 动作之前检查
`BridgeEnabled`：

- 关闭时整帧输入被排空，不产生按键边沿；
- Menu 不再能够绕过总开关唤醒 Codex；
- 活动的录音、组合层和虚拟旋钮状态会在关闭时做一次安全清理；
- 状态栏和首次提示统一使用“桥接已关闭”，不再称为“安全预览”；
- 服务层仍保留二次门控，作为纵深保护。

当前增加了独立的 `BridgeInputGate` 策略测试，覆盖 Menu、Y、R3、
摇杆和扳机意图。由于 `MainWindow` 仍未拆成可注入的控制器协调器，
完整的窗口级副作用测试仍属于后续测试债务。

### 2.3 建议提示文案

关闭桥接时显示一次持久状态或合并提示，不要在每次按键时重复弹出：

```text
桥接已关闭
手柄不会控制 Codex。请在 Agent Controller 顶部重新启用。
```

英文：

```text
Bridge is off
The controller will not control Codex. Re-enable it in Agent Controller.
```

不再把该状态称为“安全预览”。“安全预览”容易让用户误以为按键仍会
执行某种预览操作。

## 3. Base 层：当前实际指令

除“桥接开关”一栏另有说明外，下列 Codex 动作都需要桥接。

| 输入 | 当前实际行为 | 状态与差异 |
| --- | --- | --- |
| 左摇杆上/下 | 移动 Agent Controller 自有侧边栏目录焦点；可重复；同步尝试聚焦 Codex 侧边栏 | 已实现 |
| 左摇杆左 | 退出当前项目任务目录 | 已实现 |
| 左摇杆右 | 进入当前项目目录 | 已实现；与 v0.4b 最新文字“A 确认、右推无动作”不一致 |
| L3 | 循环根目录：置顶任务、置顶项目、项目、未归项目任务 | 已实现 |
| A | 打开当前选中的任务；项目本身不会作为任务打开 | 已实现 |
| X | 直接发送当前 composer 文本 | 已实现；先尝试官方 Micro `ACT12`，确定 NotSent 时直接使用 `composer.submit` 配置键（默认 F22），不再先 Invoke UIA 按钮；可能已提交的结果禁止双发，绝不降级为 Enter；发送前非空且发送后清空才报成功 |
| B | 菜单、语音、待提交选择或导航撤回场景短按立即处理；其他 Base 场景长按 3 秒并显示倒计时后取消当前会话 | 已实现；松开即中止倒计时 |
| Y | 打开动作面板；面板中再次按 Y 或 B 关闭 | 已实现 |
| 十字键上/下 | 短按跳到上一条/下一条用户消息；按住上 4 秒置顶、按住下 3 秒置底 | 已实现；短按注入 Codex 原生 `Alt+↑` / `Alt+↓`，成功时不额外显示 popup；长按使用 UI Automation 滚动容器并验证位置 |
| 右摇杆上/左 | 官方 Micro encoder 上一档：`ENC_CW` | Micro-first；两种物理方向是同一语义 |
| 右摇杆下/右 | 官方 Micro encoder 下一档：`ENC_CC` | Micro-first；两种物理方向是同一语义 |
| R3 单击 | 发送 `ENC`：打开 Advanced、进入子菜单或确认当前项 | R3 是唯一进入/确认动作，不再负责关闭 |
| B（Micro 菜单会话） | 发送 `AG00`，由官方 bridge 上下文转换为 Escape | 只有由 R3 建立的会话可以发送，防止无菜单时误切 Agent 1；仅 `NotSent` 可降级 |
| A（选择器打开时） | 可保留为辅助确认别名，但教程与主合同以 R3 为准 | 不得替代 R3 的官方 encoder press 语义 |
| R3 长按 500 ms | 打开 Agent Controller 设置页 | 已实现；桥接关闭后按最终语义也不响应手柄 |
| LB 短按 | 打开上一个可用任务 | 已实现 |
| RB 短按 | 打开下一个可用任务 | 已实现 |
| LB 按住 | Agent 六槽层 | 已实现但槽位仍是当前快照前六项，不是完整可配置 Agent 适配器 |
| RB 按住 | Command 六槽层 | 已实现，固定映射 |
| LT 按住 | 超过 0.35 开始语音识别，低于 0.20 结束 | 部分实现；没有实现双拉锁定免手持录音 |
| RT 按住 | 超过 0.55 打开运行中操作层，低于 0.35 关闭 | 已实现；动作可用性依赖 Codex 当前 UI |
| View | Base 层保留键，当前无动作 | 后续可能用于切换当前受控 Agent；实现前保持 fail closed |
| Menu | 需要时唤醒并置前 Codex | 已实现；Codex 已在前台时，连接手柄回中后自动启用，不必再按 Menu；桥接关闭时仍被总门控拦截 |
| Guide | 无动作 | 保留给系统 |

### 3.1 十字键

| 输入 | 当前实际行为 | v0.4b 原规范 | 状态 |
| --- | --- | --- | --- |
| 上 | 短按跳到上一条用户消息；按住 4 秒置顶 | 切换 Plan | 已实现；短按使用带 `KEYEVENTF_EXTENDEDKEY` 的 Codex 原生 `Alt+↑`；长按只操作已验证的会话滚动容器 |
| 下 | 短按跳到下一条用户消息；按住 3 秒置底 | 切换侧边栏 | 已实现；短按使用带 `KEYEVENTF_EXTENDEDKEY` 的 Codex 原生 `Alt+↓`；长按只操作已验证的会话滚动容器 |
| 左 | 与左摇杆左相同：退出项目目录 | 历史后退 | 不一致 |
| 右 | 与左摇杆右相同：进入项目目录 | 历史前进 | 不一致 |

新建任务、侧边栏和历史前进/后退保留在 Y 动作面板，避免与 Base 层的
长对话浏览和项目目录导航冲突。Plan 入口在 v0.7 暂停，等待可验证的实现。

置顶/置底不使用 `Ctrl+Home` / `Ctrl+End` 降级，避免误移动 composer 光标；找不到
可验证的会话滚动容器时明确失败。上一条/下一条跳转成功时沿用 Codex 界面自身响应，
不再额外显示成功 popup。

## 4. 上下文层

### 4.1 LB：Agent 六槽

LB 按下后进入候选层；180 ms 内直接释放时执行“上一个任务”，继续
按住则显示 Agent 层。

| LB + 输入 | 当前行为 |
| --- | --- |
| 十字键上 | Agent slot 1 |
| 十字键右 | Agent slot 2 |
| 十字键下 | Agent slot 3 |
| 十字键左 | Agent slot 4 |
| View | Agent slot 5 |
| Menu | Agent slot 6 |
| B | 取消 Agent 层 |

当前限制：

- 六槽取自 `_snapshot.Threads.Take(6)`；
- 尚无置顶/最近/自定义分配模式；
- 尚无官方 Micro 所述的单击后台聚焦、双击置前语义；
- 尚无 Idle/Thinking/Complete/Requires input/Error 的实时灯光状态。

### 4.2 RB：Command 六槽

RB 按下后进入候选层；180 ms 内直接释放时执行“下一个任务”，继续
按住则显示 Command 层。

| RB + 输入 | 当前行为 | 实现方式 |
| --- | --- | --- |
| Y | Toggle Fast | 优先读取并调用当前 Codex 的实时 Standard / Fast 控件并回读验证；RB+Y 不再盲发 F20 |
| A | Approve | 查找 Codex 当前可见批准按钮 |
| B | Decline | 查找 Codex 当前可见拒绝按钮 |
| X | Fork | 查找 Codex 当前可见 Fork/Branch 按钮 |
| View | Push-to-talk | 按下开始，松开 View 结束 |
| Menu | Dispatch | 调用当前 composer 的发送/Steer/Queue 按钮 |
| L3 | 取消 Command 层 | 仅关闭本层 |

当前为固定槽位，尚未提供用户自定义 Command Profile。

### 4.3 RT：运行中操作

| RT + 输入 | 当前行为 |
| --- | --- |
| X | 显式 Steer，加入当前运行 |
| Y | 显式 Queue，排到下一轮 |
| B | 持续 3 秒后 Stop；提前松开取消倒计时 |
| A | Fork |

Stop 只可由长按完成路径调用。当前代码按按钮名称执行，并未建立完整、稳定的 Approval/Question/
RunningTurn 上下文状态机。Codex 没显示对应按钮时会返回失败，不应
降级成普通发送。

### 4.4 LT：按住说话

当前路径：

1. LT 超过 0.35；
2. 若桥接、会话和前台条件通过，尝试关闭虚拟旋钮菜单；
3. 通过 UI Automation 查找 Dictate/开始听写；
4. 找不到时，开始动作允许使用配置快捷键兜底；
5. LT 回到 0.20 以下，查找 Stop dictation/停止录音；
6. 录音期间 B 请求取消。

未实现：

- 350 ms 内双拉锁定免手持录音；
- 锁定后再次拉动停止；
- 真实麦克风状态丢失的完整恢复；
- 跨 Codex 版本的端到端真机回归测试。

## 5. 模型旋钮

右摇杆现在整体模拟官方 Micro encoder，不再由 Agent Controller 维护一套简易/高级模型控制状态机。Codex 当前 encoder mode 与官方 bridge 决定旋转时调整 Power 还是遍历 Composer；Agent Controller 只保存物理手势到 Micro 信号的固定投影。

| 上下文 | 操作 | 当前行为 |
| --- | --- | --- |
| 任意 | 上或左 | 发送 `ENC_CW act=2`，由 Codex 解释为上一项/上一档 |
| 任意 | 下或右 | 发送 `ENC_CC act=2`，由 Codex 解释为下一项/下一档 |
| 菜单关闭或打开 | R3 | 发送 `ENC` down/up，打开、进入或确认；成功后本地持续持有菜单会话 |
| Micro 菜单会话 | B | 发送 `AG00` down/up；官方 bridge 关闭菜单并阻止 Agent 1 切换 |
| 任意 | R3 长按 | 打开 Agent Controller 设置，不发送短按 `ENC` |

档位与能力规则：

- 当前选择为 Sol Max、Codex 无法提供简易 Power 时，显示手柄确认提示：A 切换到高级模式，B 保持简易模式；无论是否切换，Standard / Fast 仍可用，不再把整个简易模式误报为不可用；
- 不维护 Max、Ultra 或固定模型清单；简易模型列表完全由 Codex 官方 picker
  决定，高级模式只使用当前账户此刻打开的 Model / Effort / Speed 子菜单。
- 原生输入 ACK 不携带菜单层级或关闭状态；R3 后继续锁定 Micro 菜单会话，避免 UIA 回读延迟时把 B 或摇杆误路由。选择完成后用 B 显式退出。
`models_cache.json` 只用于选中后的状态显示，绝不用于生成或补齐可切换档位；

- 驱动明确 `NotSent` 时才允许进入键盘/UIA 兼容回退。`Accepted`、`OutcomeUnknown` 或 `Rejected` 后禁止双发；
- 简易模式不推断 Power stops，只让 Codex 自己按当前账户、模型和功能开关
  决定下一格；Plus 没有 Ultra、部分 Pro 没有 Max 都不会被越权补出；
- Fast 同样以菜单中是否存在 `Enable fast mode` / `Enable standard mode`
  为准，不根据订阅类型猜测；
- Codex 可能在当前 Model＋Effort 无法由简易 Power 表示时自行强制显示
  Advanced，并把折叠入口替换为会修改选择的 `Reset to default`。程序不会
  自动触发这个重置，而会明确报告简易菜单不可用；
- 高级模式操作前会把原生菜单展开到 Advanced；简易模式则折叠到 Power
  界面。两种模式都不会进入 Full access 或 Project 选择器。

## 6. Y 动作面板

Y 现在打开一个持久的直接按键面板。它不是“移动游标再按 A”的普通
列表；手柄图直接标出下一步要按的实体键：

| 面板输入 | 行为 | 执行方式 |
| --- | --- | --- |
| 十字键上 | 新建任务 | 优先调用 Codex 当前可见的 New task / 新建任务按钮；找不到按钮时使用当前 Codex 官方默认 `Ctrl+N` |
| 十字键右 | Codex 历史前进 | `Ctrl+]` |
| 十字键下 | 显示/隐藏 Codex 侧边栏 | `Ctrl+B` |
| 十字键左 | Codex 历史后退 | `Ctrl+[` |
| A | 清空当前 composer 文本 | 第一次按下进入确认；2.5 秒内再次按 A 执行 |
| X | 项目上下文 | 进入所属项目；项目内切换全部/仅置顶 |
| B 或 Y | 关闭动作面板 | 不执行动作 |

保护与显示：

- 清空输入先通过 UI Automation 验证 composer 非空并聚焦正确编辑器；
  优先使用 `ValuePattern`，不支持时才发送 `Ctrl+A` + Backspace，
  最后再次读取文本验证结果；
- Plan 模式入口在 v0.7 不发布；旧的自动化与设置字段只为兼容已有配置保留，
  不再有手柄路由，待 TODO 中的可验证执行通道完成后再评估恢复；
- 打开动作面板时冻结 Base 发送、取消、录音和虚拟旋钮输入；
- 动作面板、LB、RB、RT 的提示统一使用实际手柄示意图与实体键徽标，
  不再使用抽象圆环；
- Bridge 关闭时 Y 与其他所有手柄输入一样被入口总门控拦截。

## 7. v0.7 之后的主要待办

- 完整 Approval、Question、Menu 上下文解析和优先级覆盖；
- Base 十字键的最终映射；
- 评估使用 Base View 切换当前受控 Agent；
- LT 双拉锁定录音；
- Agent slots 配置、后台聚焦/双击置前和实时状态；
- Command slots 配置；
- 四个独立背键的能力探测与映射；
- 根据真实 Follow-up behavior 稳定区分 X 的 Steer/Queue 结果；
- 右摇杆菜单选择的跨版本稳定性。

## 8. 测试口径

当前已有单元测试主要覆盖：

- LT 模拟阈值与回滞；
- PTT 冻结部分 Base 按键的策略；
- LB/RB/RT 层的静态按键映射；
- 虚拟旋钮输入策略、游标与菜单选项选择策略；
- Composer/Sidebar/Shortcut 服务各自的 Bridge 安全检查；
- 控制器状态缓冲、手势路由和侧边栏稳定目录。

这些测试不能替代以下验收：

- 真实手柄从物理输入到 Codex UI 的端到端测试；
- 当前 Codex 版本的 UI Automation 元素发现、聚焦和点击；
- Bridge 关闭时每个物理输入都不能产生 Codex 副作用；
- LT 实际开始/停止录音；
- R3 打开官方模型列表并选择 5.6 Sol Max、Power 左右单步、Fast/Standard 上下切换；
- 高级菜单在不同账户、模型下只呈现并选择实际可用项目；
- 长时间运行、前后台切换和 Codex UI 重挂载后的恢复。

因此构建通过和单元测试通过，只能证明策略与部分服务没有回归，不能
表述为“手柄功能已经完整覆盖”。

本次 hotfix 工作区全量测试：588 项通过，0 项失败。由于模型、Effort、Ultra、
Fast 等能力由账户与功能开关决定，仍需分别使用实际 Plus / Pro 账户做
真机冒烟，不能把某个账户出现过的档位当成通用验收清单。
