# Agent Controller 交互规范 v0.4b

## 标准 XInput 与四背键增强手柄映射

| 项目 | 内容 |
| --- | --- |
| 状态 | Draft |
| 规范版本 | v0.4b |
| 上位规范 | [v0.4a 语义模型与频率](interaction-spec-v0.4-controller-mapping.md) |
| 本版范围 | 实体输入、手势、层、上下文覆盖和增强手柄映射 |
| 基础设备 | 标准 Xbox 布局 XInput 手柄 |
| 增强设备 | 能独立上报四个背键的手柄 |
| 核心原则 | Codex 语义优先；快捷键只作为主机端执行兜底 |

本规范把 v0.4a 的 F0–F4 频率、U0–U3 上下文优先级和 Codex Micro 虚拟表面落实为实体手柄方案。

它定义的是：

```text
物理输入 / 手势 → 虚拟 Codex Micro 输入或上下文角色
```

它不定义：

```text
物理输入 → Codex 内部命令 ID 或固定 F 键
```

---

# 第一篇　总：映射结论

## 1. 推荐方案概览

![前上方手柄展示视图](../app/Assets/controller.png)

主视图采用前上方、手柄向玩家方向前倾的视角，使正面控制区可读，同时露出左右肩部台阶：

```text
左侧：LT / LB    |    右侧：RT / RB
```

四个顶部输入必须分开处理：

- `LT`、`RT` 是模拟扳机，具有进入阈值、退出回滞和连续量。
- `LB`、`RB` 是数字肩键。
- 不得把 `LT+LB` 或 `RT+RB` 合并成一个输入。

推荐职责如下：

| 输入面 | 推荐职责 |
| --- | --- |
| 左摇杆 | 通用导航、焦点移动、进入与返回 |
| 十字键 | Codex Micro 四向模拟输入 |
| 右摇杆 + R3 | 虚拟旋钮 |
| A / B / X / Y | Primary、Cancel/Stop、Dispatch、Secondary |
| LB | 上一个任务；长按进入 Agent 槽层 |
| RB | 下一个任务；长按进入 Command 槽层 |
| LT | Push-to-talk |
| RT | Turn / Follow-up 层 |
| View | 任务总览；层内作为第 5 槽 |
| Menu | 唤醒与解锁；层内作为第 6 槽 |
| 四背键 | 可选加速入口，不承担唯一可达路径 |

## 2. 映射目标

本方案必须同时满足：

1. 所有 F0 动作直接可达。
2. Steer、Queue、Stop 和审批在对应上下文中快速可达。
3. 六个 Agent slots 只经过一个稳定层。
4. 六个 Command slots 只经过一个稳定层。
5. Codex Micro 四向输入和旋钮语义均可达。
6. 标准手柄不依赖背键即可完成所有核心操作。
7. 四背键版不改变基础肌肉记忆，只增加免松摇杆的副本。
8. Codex 更新只修改主机动作目录和执行器，不修改控制器固件。

## 3. 物理位置命名

内部使用物理位置，不使用厂商印刷字母作为稳定 ID：

| Xbox 显示 | 稳定名称 |
| --- | --- |
| A | `FaceSouth` |
| B | `FaceEast` |
| X | `FaceWest` |
| Y | `FaceNorth` |
| LB | `LeftShoulder` |
| RB | `RightShoulder` |
| LT | `LeftTrigger` |
| RT | `RightTrigger` |
| L3 | `LeftStickPress` |
| R3 | `RightStickPress` |

Nintendo、PlayStation 或厂商自定义字形只改变显示标签，不改变物理语义。

## 4. 层级优先级

从高到低：

1. Safety / Approval / Question / Modal 上下文
2. Dictation
3. RT Turn 层
4. RB Command 层
5. LB Agent 层
6. Base

同时按下多个修饰输入时：

- `RT` 高于 `RB`，`RB` 高于 `LB`。
- v0.4b 不定义 `LB+RB` 或三修饰键组合。
- 未定义的多修饰组合必须无动作并显示冲突提示。
- 修饰键进入 Candidate 时，立即冻结可能冲突的 Base 动作。

---

# 第二篇　分：标准 XInput 映射

## 5. Base 层完整映射

### 5.1 双方向输入面原则

左摇杆和十字键是两套独立输入面，不能因为它们都有“上下左右”就先合并成同一个方向事件。

| 特性 | 左摇杆 | 十字键 |
| --- | --- | --- |
| 原始输入 | 连续二维轴 | 四个离散按钮 |
| 内部事件源 | `LeftStickAxis` | `DPadButton` |
| Base 主要职责 | 焦点、列表和层级导航 | Codex Micro 四向直接动作 |
| 重复 | 越过阈值后可重复 | 默认一按一步 |
| 方向稳定 | 死区、回滞、主轴锁定 | 无死区，不合成对角 |
| Agent 层 | 不选择 Agent slot | 选择 slot 1–4 |
| 上下文菜单 | 连续移动或滚动 | 离散移动一个选项 |

必须遵守：

- 输入服务必须先报告来源，再由 ContextResolver 决定是否映射到共同的 `ContextNavigate`。
- 即使两者最终都成为 `ContextNavigate`，事件仍必须保留 `source`、`repeatPolicy` 和原始输入。
- 不得把 D-pad 转换成伪左摇杆轴，也不得用左摇杆方向冒充 Codex Micro 的 Virtual Analog。
- Base 层中，左摇杆导航不会触发 Plan、Back、Forward 或 Sidebar；这些动作只属于十字键。
- LB Agent 层中，只有十字键选择 slot 1–4；左摇杆不得因为同方向而误选槽位。
- 菜单中同时出现十字键按压和左摇杆偏转时，先处理一次离散 D-pad 动作，并暂停左摇杆重复，直到左摇杆回中。
- overlay 必须用不同字形显示 `LS ↑` 与 `D-pad ↑`，不能都写成笼统的“上”。

### 5.2 正面与系统键

| 物理输入 | 虚拟角色或动作 | Base | 主要上下文覆盖 |
| --- | --- | --- | --- |
| A / FaceSouth | `ContextPrimary` | 打开或选择焦点项 | Approve、选择答案、菜单确认 |
| B / FaceEast | `ContextCancel` | 取消局部状态 | RunningTurn → Stop；菜单 → 关闭 |
| X / FaceWest | `composer.dispatchDefault` | 发送并开始 turn | RunningTurn → 按默认 Steer/Queue |
| Y / FaceNorth | `ContextSecondary` | 打开动作面板 | Approval → Decline |
| View | `task.openOverview` | 打开任务总览或任务搜索 | Agent 层 → Agent slot 5 |
| Menu | `controller.wakeOrArm` | 唤醒、前台和解锁控制 | 已解锁时显示控制器状态；层内为 slot 6 |
| Guide | Reserved | 不绑定 | 由操作系统或平台保留 |

### 5.3 左摇杆

| 手势 | Base 行为 | 菜单/问题行为 |
| --- | --- | --- |
| 上 / 下 | 移动侧边栏或当前列表焦点 | 上下移动选项 |
| 左 | 返回上一层或退出项目范围 | 返回父级；不等于 Decline |
| 右 | 无动作；由 A 确认 / 进入 / 打开 | 展开或进入子级 |
| L3 单击 | 循环导航域 | 不执行 |

约束：

- 左摇杆使用死区、主方向锁定和重复。
- 左向返回必须等回中后才能再次触发；Base 层右向不执行动作。
- 上下可以重复，确认动作不得重复。
- L3 只改变导航域，不直接执行 Codex 动作。
- A 是滚轮当前焦点的唯一 Base 确认键。

#### 5.3.1 自有导航滚轮目录

左摇杆不得把“下一步会到哪里”完全交给 Codex 数据库的实时
`updated_at` 排序。运行中的任务会持续写入活动时间；如果控制器每次
刷新都据此重排，用户眼前的相邻关系会在没有输入时改变。

Agent Controller 必须维护一份可见的、会话内稳定的导航滚轮目录：

- 开始导航时以 Codex 当前任务为锚点；
- 顺序按 `置顶任务 → 置顶项目 → 普通项目 → 未归项目任务` 展开；
- 根目录与每个项目分别保存自己的滚轮顺序和焦点；
- 进入项目后使用该项目自己的任务滚轮，退出后恢复根目录焦点；
- 项目任务直接沿用项目目录顺序，置顶只显示徽标，不改变项目内位置；
- 自动状态刷新只更新标题、运行状态和徽标，不改变已有槽位；
- 新增或移除条目使用稳定合并，不得因某个任务继续运行而把它移到首位；
- 用户主动刷新、进入或退出项目都不得重排幸存条目；只有单独、明确的
  “重新按当前来源排序”动作才允许整体重建目录；
- 上下移动只改变滚轮与 Codex 侧边栏焦点，不自动打开任务。

底部中央提示至少显示上一项、当前项、下一项、所属分区，以及下一步
是否会跨越分区边界。此滚轮目录是控制器的明确导航契约；当 Codex
原生列表因活动时间或折叠状态产生歧义时，以滚轮显示的相邻关系为准。

### 5.4 十字键：Codex Micro 四向输入

Base 层严格保留官方默认：

| 输入 | Virtual Analog | 默认 CodexAction |
| --- | --- | --- |
| D-pad Up | `AnalogDirectionEnter(up)` | `mode.togglePlan` |
| D-pad Right | `AnalogDirectionEnter(right)` | `navigation.forward` |
| D-pad Down | `AnalogDirectionEnter(down)` | `panel.toggleSidebar` |
| D-pad Left | `AnalogDirectionEnter(left)` | `navigation.back` |

上下文覆盖：

- Menu、Question、Approval 需要选项导航时，十字键变为离散的 `ContextNavigate(source=DPad)`。
- 同一上下文中的左摇杆仍是 `ContextNavigate(source=LeftStick)`，但保留连续重复策略。
- 上下文退出后必须等待十字键全部释放，不能把同一次按压落回 Base。
- 开关型动作禁止自动重复；列表导航可以重复。

### 5.5 右摇杆：虚拟旋钮

| 手势 | 虚拟输入 | 行为 |
| --- | --- | --- |
| 右推 | `DialNavigate(right)` | 菜单关闭时切换下一个 composer 控件；打开时进入子级 |
| 左推 | `DialNavigate(left)` | 菜单关闭时切换上一个 composer 控件；打开时返回上级 |
| 上推 | `DialNavigate(up)` | 菜单打开时选择上一行 |
| 下推 | `DialNavigate(down)` | 菜单打开时选择下一行 |
| R3 单击 | `DialPress()` | 打开当前控件；默认候选为模型按钮 |
| A | `DialSelect()` | 选择当前具体项 |
| B | `DialCancel()` | 一次关闭整个选择器或取消二次确认 |
| R3 长按 500 ms | `DialHold()` | 打开虚拟 Codex Micro 设置 |

约束：

- 右摇杆在选择器中作为二维菜单导航器，不直接连续调整参数。
- 每次越过阈值先产生一个 step，持续保持后才开始重复。
- 菜单打开后 B 始终用于取消，不能映射为 Stop 或 Decline。
- 可展开的 Model / Effort / Speed 根行用右推进入，A 只选择具体项。
- Full access 选择后若出现二次确认，A 只调用 Confirm，B 只调用
  Cancel，右摇杆冻结。

新任务页的 Project 选择器属于 `ComposerControl`，不占用独立物理键：

- 菜单关闭时右摇杆左/右可把焦点移动到 Project 控件；
- R3 打开项目选择器；
- 选择器打开后，右摇杆上/下遍历已有项目、New project 和
  Don't work in a project；
- A 确认当前项；
- B 只关闭选择器，必须走 `DialCancel()` / Escape，不得复用会优先
  查找 Stop 的 Base Cancel；
- 项目搜索框的自由文本输入仍由键盘或语音完成，旋钮只负责遍历已有项。

## 6. 四个顶部输入

### 6.1 LB：任务切换与 Agent 层

| 手势 | 行为 |
| --- | --- |
| 短按并释放 | 上一个 Agent task |
| 持续 180 ms | 进入 Agent 层 |
| Agent 层已使用后释放 | 退出层，不再触发“上一个任务” |

LB 按下后立即进入 `Candidate`，冻结十字键、View 和 Menu 的 Base 行为。若在 180 ms 内释放且没有按下层内目标，才执行“上一个任务”。

### 6.2 RB：任务切换与 Command 层

| 手势 | 行为 |
| --- | --- |
| 短按并释放 | 下一个 Agent task |
| 持续 180 ms | 进入 Command 层 |
| Command 层已使用后释放 | 退出层，不再触发“下一个任务” |

RB Candidate 必须立即冻结 A/B/X/Y、View、Menu，避免层进入过程中先触发 Base 发送或取消。

### 6.3 LT：Push-to-talk

| 手势 | 行为 |
| --- | --- |
| 越过 0.35 | `CommandKeyDown(pushToTalk)` |
| 退回 0.20 以下 | `CommandKeyUp(pushToTalk)` |
| 350 ms 内双拉 | 锁定免手持录音 |
| 锁定时再次拉动 | 停止录音 |

约束：

- LT 专用于 PTT，不兼作普通层键。
- 录音时 B 取消录音；X 只有在文本就绪后才能发送。
- 设备断开、Codex 退出或麦克风状态丢失时必须停止录音。
- 扳机深度可以驱动视觉反馈，但不能改变录音音量。

### 6.4 RT：Turn / Follow-up 层

| 阈值 | 行为 |
| --- | --- |
| 越过 0.55 | 进入 Turn 层 |
| 退回 0.35 以下 | 退出 Turn 层 |
| RT 单独按下 | 无动作，只显示 Turn 层提示 |

Turn 层映射：

| 组合 | CodexAction | 结果 |
| --- | --- | --- |
| RT + X | `turn.steer` | 显式加入当前 turn |
| RT + Y | `turn.queue` | 显式排入下一 turn |
| RT + B | `turn.stop` | 停止当前 turn |
| RT + A | `task.fork` | 在新任务继续当前任务 |

RT Candidate 出现后必须立即冻结 A/B/X/Y 的 Base 行为。只有越过 0.55 并稳定后，才接受层内按键。

无活动 turn 时：

- RT+X、RT+Y、RT+B 返回 `Unavailable`。
- RT+A 只有存在可 Fork 的当前任务时可用。
- 不得把 RT+X 降级成普通发送。

## 7. LB Agent 层

### 7.1 六个 Agent slots

按住 LB 后：

| 目标输入 | Agent slot |
| --- | --- |
| D-pad Up | 1 |
| D-pad Right | 2 |
| D-pad Down | 3 |
| D-pad Left | 4 |
| View | 5 |
| Menu | 6 |

### 7.2 触发语义

- 层内目标单击：切换到该任务，不强制 ChatGPT 前台。
- 保持 LB，350 ms 内双击同一目标：切换并把 ChatGPT 带到前台。
- Custom 模式空槽：打开新任务，任务开始后绑定该槽。
- B：取消 Agent 层，不切换任务。
- 其它输入：无动作。

### 7.3 反馈

进入 Agent 层时 overlay 必须显示六槽：

```text
↑ 1    → 2    ↓ 3    ← 4    View 5    Menu 6
```

每槽显示：

- 任务标题；
- Idle / Thinking / Complete / Requires input / Error / Empty；
- 当前选中状态；
- 单击或双击等待状态。

## 8. RB Command 层

### 8.1 六个物理槽

按住 RB 后：

| 目标输入 | Command slot | 默认动作 |
| --- | --- | --- |
| Y / FaceNorth | 1 | Toggle Fast |
| A / FaceSouth | 2 | Approve |
| B / FaceEast | 3 | Decline |
| X / FaceWest | 4 | Fork |
| View | 5 | Push-to-talk |
| Menu | 6 | Dispatch composer |

这种分配保留了语义联想：

- A 是肯定；
- B 是否定；
- X 是分支；
- Y 是模式切换；
- 两个系统键承载 PTT 与发送。

### 8.2 槽位与动作分离

用户重新分配 Command Key 时：

- 物理槽身份不变；
- overlay 显示新的动作与图标；
- 不修改控制器固件；
- 不把新动作写成 F 键固件宏；
- 高风险动作仍受 v0.4a 风险规则保护。

### 8.3 Command 层取消

- 持有 RB 时按 L3：关闭层，不执行任何 Command。
- RB 释放：关闭层。
- 层内按键执行一次后，必须等待该目标键释放。
- 不允许通过按住目标键连续重复 Approve、Decline、Fork 或 Dispatch。

## 9. 直接动作与 Command 槽的关系

部分官方 Command actions 同时拥有高频直接入口：

| 动作 | Command 槽入口 | 直接入口 |
| --- | --- | --- |
| Push-to-talk | RB + View | LT |
| Dispatch default | RB + Menu | X |
| Approve | RB + A | Approval 中 A |
| Decline | RB + B | Approval 中 Y |
| Fork | RB + X | RT + A |
| Fast | RB + Y | 无默认直接副本 |

直接入口是频率优化，不改变六个 Command slots 的完整性。

---

# 第三篇　支：上下文分支

## 10. Base Composer

| 输入 | 行为 |
| --- | --- |
| X | `composer.dispatchDefault` |
| LT | PTT |
| A | 选择当前焦点控件 |
| B | 关闭当前 composer 控件或撤销候选状态 |
| Y | 动作面板 |
| RT+X | 无活动 turn 时 Unavailable |
| RT+Y | 无活动 turn 时 Unavailable |

`composer.dispatchDefault`：

- 无活动 turn → StartedTurn。
- 有活动 turn、默认 Steer → SteeredCurrentTurn。
- 有活动 turn、默认 Queue → QueuedNextTurn。
- 无法检测默认行为 → `Degraded / UnknownDispatch`。

## 11. RunningTurn

| 输入 | 行为 |
| --- | --- |
| B | Stop |
| X | 按 Follow-up behavior 执行默认 Steer 或 Queue |
| RT+X | 显式 Steer |
| RT+Y | 显式 Queue |
| RT+B | 显式 Stop |
| RT+A | Fork |
| LB / RB 短按 | 查看上一个 / 下一个任务，不停止当前任务 |

反馈必须区分：

- `已加入当前运行`
- `已排入下一轮`
- `已停止当前运行`
- `已在新任务中继续`

## 12. Approval

上下文稳定 300 ms 后：

| 输入 | 行为 |
| --- | --- |
| A | Approve |
| Y | Decline |
| B | 仅在 UI 明确提供 Dismiss / Later 时取消，否则无动作 |
| 十字键 | 离散地移动一个审批选项 |
| 左摇杆 | 连续移动焦点；左右只用于层级明确的审批 UI |
| X | 禁用，不能当作发送或批准 |
| RB+A | Approve 的 Command 槽入口 |
| RB+B | Decline 的 Command 槽入口 |

高风险批准：

- A 或 RB+A 必须持续 500 ms；
- overlay 显示确认进度；
- 中途释放取消；
- 不得用 Enter 猜测。

## 13. Question

| 输入 | 行为 |
| --- | --- |
| 十字键 | 每次按压移动一个问题或答案选项 |
| 左摇杆 | 连续移动焦点或滚动长问题列表 |
| A | 选择当前答案 |
| Y | 切换次要选项或多选状态 |
| B | 仅在允许跳过/取消时生效 |
| X | 只有自由文本 composer 已聚焦且非空时才 Dispatch |

## 14. Menu / Listbox / ComposerControl

| 输入 | 行为 |
| --- | --- |
| 左摇杆 | 连续 Navigate / Scroll |
| 十字键 | 离散 Navigate，一按一步 |
| A | Select |
| B | Cancel |
| 右摇杆左右 | 菜单关闭时切换控件；菜单打开时 Back / Enter |
| 右摇杆上下 | 菜单打开时 Navigate |
| R3 | Open |
| A | Select |

此上下文中：

- B 绝不解释为 Stop 或 Decline。
- X、Y 默认冻结。
- 菜单关闭后必须等待所有输入回中。
- Full access 二次确认中 A=Confirm、B=Cancel，其他菜单导航冻结。

## 15. Dictation

| 输入 | 行为 |
| --- | --- |
| LT 释放 | 结束录音 |
| B | 取消录音 |
| LT 再次拉动 | 结束锁定录音 |
| X | 只在转写文本 ready 后 Dispatch |
| 其它层键 | 冻结 |

---

# 第四篇　增强：四背键方案

## 16. 能力要求

四背键版只适用于四个背键能够独立上报的设备。

必须先区分：

| 类型 | 含义 |
| --- | --- |
| Independent | 四键可分别被主机识别 |
| Mirrored | 固件只把背键复制为 ABXY 等现有输入 |
| Partial | 只有两个独立辅助输入 |
| Unknown | 无法确认 |

只有 `Independent` 可以启用完整四背键预设。

当前项目的 `LeftAuxiliary` / `RightAuxiliary` 只能表达两个输入。实现 v0.4b 增强版前必须扩展为：

```text
RearLeftUpper
RearLeftLower
RearRightUpper
RearRightLower
```

## 17. 推荐四背键预设

四背键不替换基础入口，只增加副本：

| 物理位置 | 推荐动作 | 基础替代路径 |
| --- | --- | --- |
| RearLeftUpper | 上一个 Agent task | LB 短按 |
| RearLeftLower | 下一个 Agent task | RB 短按 |
| RearRightUpper | `composer.dispatchDefault` | X |
| RearRightLower | `turn.dispatchAlternate` | RT+X 或 RT+Y |

`turn.dispatchAlternate` 表示单次执行用户默认 Follow-up behavior 的另一项：

| 用户默认 | RearRightUpper | RearRightLower |
| --- | --- | --- |
| Steer | Steer | Queue |
| Queue | Queue | Steer |
| 无活动 turn | 开始新 turn | Unavailable |

这样无需修改用户默认设置，也能在运行中直接选择 Steer 或 Queue。

## 18. 四背键使用原则

- 左背键负责任务流，右背键负责 turn 流。
- 拇指可以持续留在摇杆上。
- 背键动作必须与基础路径产生相同 `CodexAction`。
- 背键不得获得基础手柄无法触达的唯一核心动作。
- Mirrored 设备只显示其镜像目标，不显示为独立动作。
- 背键标签使用设备 profile 映射到 P1–P4、L4/R4、M1–M4 等厂商名称。
- 任何背键重映射都在主机配置完成；不得要求固定固件宏。

## 19. 可选安全变体

用户更看重停止而非显式 alternate follow-up 时，可以把：

```text
RearRightLower → turn.stop
```

该变体必须明确命名为 `Safety`，不能静默替换推荐预设。

---

# 第五篇　实现与验收

## 20. 手势参数

| 参数 | 默认值 |
| --- | --- |
| LB / RB tap 最大时长 | 180 ms |
| Agent Key 双击窗口 | 350 ms |
| PTT 双拉窗口 | 350 ms |
| 上下文 Armed 稳定时间 | 300 ms |
| 高风险确认长按 | 500 ms |
| Dial settings 长按 | 500 ms |
| LT 进入 / 退出 | 0.35 / 0.20 |
| RT 进入 / 退出 | 0.55 / 0.35 |

所有时间和模拟阈值由主机端 profile 管理，不写入业务固件协议。

## 21. 组合键解析

以 RB+A 为例：

```text
RB Down
  → Command Candidate
  → 立即冻结 A 的 Base Primary
  → 180 ms 后 Command Armed
  → A Down
  → Command slot 2
  → 执行一次
  → 等待 A Up
  → RB Up
  → 返回 Base
```

如果 A 在 RB 进入 Candidate 前已经按下：

- 不得把后续 RB 解释为 RB+A；
- A 保持原 Base 语义；
- overlay 提示“请先按住层键”。

## 22. 可达性矩阵

| 能力 | 标准 XInput | 四背键增强 |
| --- | --- | --- |
| Primary | A | A |
| Cancel / Stop | B | B |
| Dispatch default | X | X / RearRightUpper |
| Explicit Steer | RT+X | RT+X / 右背键按当前默认解析 |
| Explicit Queue | RT+Y | RT+Y / 右背键按当前默认解析 |
| PTT | LT | LT |
| Previous / Next task | LB / RB tap | 左侧两个背键 |
| Agent slots 1–6 | LB layer | LB layer |
| Command slots 1–6 | RB layer | RB layer |
| Micro analog directions | D-pad | D-pad |
| Dial step / press / hold | RS / R3 | RS / R3 |

## 23. 冲突检查

- [ ] LB Candidate 不会先触发十字键的 Plan/History/Sidebar。
- [ ] 左摇杆方向不会触发十字键的 Virtual Analog 动作。
- [ ] LB+左摇杆不会误选 Agent slot 1–4。
- [ ] 菜单中 D-pad 与左摇杆同时输入时不会双步进。
- [ ] RB Candidate 不会先触发 A/B/X/Y Base 动作。
- [ ] RT Candidate 不会先触发 X Dispatch 或 B Stop。
- [ ] Approval 出现时，X 立即冻结。
- [ ] B 在菜单中只 Cancel，不 Stop。
- [ ] B 在 Approval 中不自动 Decline。
- [ ] Agent slot 双击不会同时执行单击前台行为。
- [ ] PTT 双拉不会产生两段短录音。
- [ ] 背键镜像不会被误认为独立输入。
- [ ] 设备断开会清除所有层、长按、重复和录音状态。

## 24. UI 展示要求

主视图必须：

- 保持 A/B/X/Y、双摇杆和十字键可辨。
- 同时露出 LT/LB 与 RT/RB 的实体轮廓。
- 为 LT、LB、RB、RT 提供独立标签和实时状态。
- Trigger 使用强度反馈；bumper 使用按下反馈。
- 层激活时显示当前层及其目标键。

四背键必须：

- 使用单独后视图或独立映射卡；
- 不把四个标记叠在正面图上；
- 在设备只支持两个辅助输入时显示“两背键模式”；
- 在 Mirrored 模式中显示“镜像 A/B/X/Y”，而非独立动作。

## 25. 实施顺序

1. 完成 Base、LB、RB、LT、RT 手势解析。
2. 建立 Agent layer 与 Command layer overlay。
3. 实现 Turn layer 的 Steer、Queue、Stop、Fork。
4. 把 D-pad 接到 Virtual Analog。
5. 把右摇杆接到 Virtual Dial。
6. 扩展四背键逻辑位置与设备能力探测。
7. 实现 `turn.dispatchAlternate`。
8. 完成上下文冲突测试和真实设备测试。

## 26. v0.4b 验收

- [ ] 标准手柄能完成全部核心动作。
- [ ] 六个 Agent slots 全部只需 LB 一个层。
- [ ] 六个 Command slots 全部只需 RB 一个层。
- [ ] LT 支持按住说话和双拉锁定。
- [ ] RT+X 与 RT+Y 能明确区分 Steer 和 Queue。
- [ ] X 遵循用户 Follow-up behavior。
- [ ] A、B、X、Y 在 Approval、Question、Menu 中没有语义泄漏。
- [ ] LB/RB tap 与 hold 不双触发。
- [ ] 右摇杆和 R3 完成旋钮 step、press、hold。
- [ ] 四背键版只增加副本，不改变基础映射。
- [ ] 四背键设备能力不足时自动降级。
- [ ] 所有动作经语义执行器解析，不依赖固件中的 Codex 快捷键。

## 27. 当前实现差距

截至本文编写时，项目仍需要：

- 把现有直接 A/X/B/Y 处理迁移到上下文角色。
- 为 LB、RB、LT、RT 增加完整行为路由。
- 实现 Agent layer、Command layer 和 Turn layer。
- 实现显式 Steer、Queue 与 Follow-up behavior 探测。
- 把右摇杆从当前 composer 参数逻辑抽象为 Virtual Dial。
- 把两个 auxiliary 位置扩展为四个后置物理位置。
- 为层与肩键增加 overlay 状态。

这些是实现清单，不影响本文件作为 v0.4b 映射基线。
