# 控制器弹窗（LB / RB / RT / Y）Micro 风格重构方案

> 状态：LB 键盘与 RB / RT / Y 通用动作菜单已实施；原生 Micro 状态源转入 todo/03 与 ADR-0002
> 编写日期：2026-07-16
> 适用范围：`RadialMenuView` 承载的四个控制层（LB Agent / RB Command / RT Turn / Y Action）
> 事实基线：[Codex Micro 使用说明](https://learn.chatgpt.com/docs/features/codex-micro)、[ADR-0002](adr/0002-codex-micro-native-compatibility.zh-CN.md)、`docs/codex-26.707.12708-vhf-status-input.zh-CN.md`

## 1. 问题诊断

当前弹窗"丑"和"线交织"不是一个问题，是三个：

1. **配色体系脱离主题**。`RadialMenuView.xaml` 内硬编码了 20 余个
   青蓝色（`#20BBB5`、`#169F9F`、`#D6EBF6` 等），而应用主题是暖色
   sand / sage（`Tokens.Colors.xaml`）。弹窗看起来像另一个产品，
   叠加毛玻璃椭圆、逐扇区投影、双层玻璃内圈后显得廉价。
2. **连线在结构上必然打结**。Agent 层六条线全部同色、常亮，其中四条
   从十字键这个约 30px 的小簇出发，扇形散射到直径 552px 的圆环上；
   View / Menu 的两条线还要横穿十字键区域。每条线又画两层
   （光晕 + 主线，共 12 条 Path）。换颜色只能帮助"分辨是哪条"，
   不能消除"一团线"的观感——根因是"六条常亮线"这个逻辑本身。
3. **中央写实手柄照片与扁平轮盘图形冲突**，视觉重心被照片抢走，
   槽位卡片浮在扇区分界上，层次混乱。

## 2. 方案总览（三步）

### 第一步：LB Agent 层改为"虚拟 Codex Micro 小键盘"（逻辑改动）

不再用轮盘 + 连线，改为模拟实体 Codex Micro 的 6 个磨砂 Agent 键。
官方设备行为可直接照搬：六个 Agent 键各自跟随一个会话并用灯光显示
状态；选中会话的键按其状态色脉冲。

面板为 2 列 × 3 行键位网格，按钮灯在左、任务标题在右：

```text
[↑ LED · 槽1 标题]    [→ LED · 槽2 标题]
[↓ LED · 槽3 标题]    [← LED · 槽4 标题]
[View LED · 槽5 标题] [Menu LED · 槽6 标题]
```

每个槽位 = 一枚 Micro 风格实体键帽 + 标题行：

- 键帽左上：物理键 glyph（沿用现有 `InputGlyph`）；
- 键帽中央：圆形状态灯及柔和 halo；
- 键帽右侧：任务标题（单行截断）；
- 底部：`ConfirmationProgress` 复用为细进度线，不再额外堆叠状态图例。

收益：

- Agent 层（最拥挤的一层）连线数量从 6 → 0，交叉问题从根上消失；
- 与实体 Micro 用户共享同一套灯语和心智模型；
- 面板内容约 804×344，但只占据透明 Overlay 的上部，给下方 Codex popup 留出空间。

### 第二步：状态灯采用官方 Micro 灯语

| 灯色 | 状态 | 建议 Token |
| --- | --- | --- |
| 白（暖白） | 空闲 Idle | 新增 `Brush.Led.Idle`（浅 sand 亮点 + 描边） |
| 蓝 | 运行中 Thinking | 新增 `Brush.Led.Working`（主题中无蓝色，正好独占语义） |
| 绿 | 完成未读 CompleteUnread | 基于 `Color.Green.600` 提亮 |
| 琥珀 | 需要批准/回应 RequiresInput | 基于 `Color.Amber.600` |
| 红 | 出错 Error | 基于 `Color.Red.600` |
| 灭 | 未绑定 Unassigned | 空灯槽（虚线描边），键整体 26% 透明 |
| 脉冲 | 当前选中 | 选中键 LED 以自身状态色做 1.6s 呼吸动画（Controller 视觉节奏，不是 Micro 协议时序） |

状态源分两层，禁止 UIA-first：

- **已实施降级层**：`CodexThread.Status` 由 rollout 生命周期增量尾读和
  `unread-thread-ids-by-host-v1` 组成，诚实提供 Idle、Thinking、
  CompleteUnread 及明确事件 Error；审批/回应不会冒充 RequiresInput，若 turn
  仍未闭合只能保留为粗粒度 Thinking。
- **P1 槽灯观测层**：Windows VHF 虚拟 Codex Micro 接收官方
  `v.oai.thstatus`，由用户态 Broker 发布匿名 SlotOnly 灯光。HID payload 不含
  `threadKey`；只有双方从独立同源 roster 获得的短租约 proof 一致时，才允许
  覆盖具体任务。当前版本没有该 proof，故命名任务仍使用已实施降级层。详见
  `docs/codex-26.707.12708-vhf-status-input.zh-CN.md`。

### 第三步：RB / RT / Y 改为同族的实体键帽动作菜单

轮盘不再作为通用容器。三层统一使用 `ActionMenuView`：

1. **统一行结构**：58×58 实体键帽、右侧动作标题、可选短说明、可选细进度线。
   六项菜单为 2 列 × 3 行；RT 只有四项时第三行真实折叠，面板随内容收缩。
2. **保留物理空间线索**：Y/B/A/X 等面键的键帽左上显示 `↑/→/↓/←`，
   Learning 模式底部额外显示 `↑Y →B ↓A ←X` 位置图例。字形来自当前
   `ControllerProfile`，不把 Xbox 标签硬编码给其他手柄。
3. **渐进式教学**：Learning 模式提供位置图例和按下高亮；Always 模式只保留
   键帽与动作文字。新手能按位置寻找，熟手不会长期承受说明文字噪声。
4. **选择与危险语义分离**：淡紫只表示当前焦点；确认进度使用琥珀；停止等
   破坏性操作仍由标题、说明和长按进度共同表达，不把整张面板染红。
5. **彻底删除旧轮盘资产**：不再创建扇区 Geometry、锚点、Bezier 连线、
   写实手柄图片或光晕内圈。`RadialMenuView` 只负责在 Agent 与 Action 两种
   专用组件之间切换。
6. **标题栏瘦身**：左侧为修饰键键帽和层标题，右侧保留取消/释放提示；
   面板固定出现在透明 Overlay 上部，避免占用下方 Codex popup 区域。

## 3. 实施拆分

| 序 | 内容 | 触点 | 量级 |
| --- | --- | --- | --- |
| 1 | 抽 Micro Panel / Physical Key / Progress 共用样式 | `Themes/Controls/Overlay.xaml` | 已完成 |
| 2 | 删除轮盘 Geometry、连线、锚点和写实手柄 | `RadialMenuView.xaml`(.cs) | 已完成 |
| 3 | RB / RT / Y 通用动作菜单 | `ActionMenuView.xaml` | 已完成 |
| 4 | `ThreadStatus` + LB 状态灯控件 | `Models`、`AgentKeypadView.xaml` | 已完成 |
| 5 | 手柄面键位置提示与 Learning 图例 | `RadialMenuSlotViewModel`、`ActionMenuView.xaml` | 已完成 |
| 6 | P1 状态源探测（独立排期） | `CodexDataService` / App Server | 待办 |

## 4. 测试关注点

- 键盘布局与 `AgentRadialSlotLayout.Bindings` 顺序的一致性
  （槽号 1–6 与物理键映射不因视图形态改变）；
- `ThreadStatus.Unknown` 不得渲染为任何"确定"状态；
- 高亮/取消/`ConfirmationProgress` 在 Agent 与 Action 两种专用面板中行为等价；
- Learning / Always / Off 三种显示模式在两种形态下语义不变；
- 四项菜单必须折叠未使用行；面键位置提示必须随当前手柄 glyph 更新；
- 深浅背景（Codex 深色主题）下 overlay 的可读性。
