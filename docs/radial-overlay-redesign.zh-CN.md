# 轮盘弹窗（LB / RB / RT / Y）美化与重构方案

> 状态：配色与 LB 键盘已实施；本地状态降级源已绑定；VHF 权威源待 M4 PoC
> 编写日期：2026-07-16
> 适用范围：`RadialMenuView` 及四个轮盘层（LB Agent / RB Command / RT Turn / Y Action）
> 事实基线：[Codex Micro 使用说明](https://learn.chatgpt.com/docs/features/codex-micro.md)、`public/docs/codex-micro-virtual-hid-bridge-plan.md`、`docs/codex-26.707.12708-vhf-status-input.zh-CN.md`

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

面板为 3×2 键位网格，布局镜像手柄物理位置（倒 T 方向簇的惯例）：

```text
[⧉ View·槽5]   [↑ 槽1]   [☰ Menu·槽6]
[← 槽4]        [↓ 槽3]   [→ 槽2]
```

每个键 = 一张"Stream Deck 式"键帽卡片：

- 左上：物理键 glyph 键帽章（沿用现有 `InputGlyph`）；
- 右上：状态灯条（LED bar）；
- 中部：任务标题（两行截断）；
- 底部：`槽 N · 状态` 小字；`ConfirmationProgress` 复用为键底进度线。

收益：

- Agent 层（最拥挤的一层）连线数量从 6 → 0，交叉问题从根上消失；
- 与实体 Micro 用户共享同一套灯语和心智模型；
- 面板约 560×340，比 980×560 轮盘遮挡更少的 Codex 界面。

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

### 第三步：RB / RT / Y 保留轮盘，但重绘 + 改连线逻辑

面键 Y/B/A/X 的菱形与轮盘方位天然同构，轮盘隐喻对这三层是对的，
问题只在皮肤和线：

1. **扇区 → 卡片**。去掉填充扇区、逐扇区投影、模糊椭圆和玻璃内圈；
   改为细虚线圆环作导轨 + 六张扁平卡片（`Brush.Bg.Surface`、
   0.5–1px `Brush.Border.Subtle`、圆角 10）。高亮 = sage 填充
   （`Color.Sage.*`）+ 深色文字，与应用内 Segments 一致。
2. **连线新逻辑：默认 0 条，高亮时 1 条**。键帽章（每张卡片已有
   glyph）承担"按哪个键"的说明职责；只有候选/高亮项绘制一条
   120ms 画入的焦点线（2px，sage）。Learning 模式可在弹出后前
   800ms 淡显全部线（15% 透明度）再淡出，作为一次性教学。
3. **颜色的正确用法**：不做"每条线一个颜色"的彩虹（会与状态灯语
   打架）。如必须常亮多条线，用"目标簇"分组着色（十字键一组、
   View/Menu 一组、面键一组），上限 3 色。
4. **中央手柄照片 → 单色线稿剪影**（Ink/Sand 描边，按下的控件以
   sage 填充点亮）。todo 中已有 input-overlay 资产的引子。
5. **标题栏瘦身**：左对齐紧凑条——[LB] 键帽章 + 标题。B 的提示按层
   显示：LB/Y 是短按关闭，RB+B 是 Decline，只有会终止 turn 的 Base B 与
   RT+B 才显示“长按 3 秒”。
6. **动效**：入场 120ms 缩放淡入（0.96→1）、出场 80ms，节奏取
   `Tokens.Motion.xaml`；高亮切换 150ms。

## 3. 实施拆分

| 序 | 内容 | 触点 | 量级 |
| --- | --- | --- | --- |
| 1 | 抽 Overlay 配色 token（替换 RadialDial.* 硬编码） | `Tokens.Colors.xaml`、`RadialMenuView.xaml` | 0.5 天 |
| 2 | 连线逻辑改"仅高亮一条"+ Learning 一次性淡显 | `RadialMenuView.xaml.cs`（`RefreshLeaderLines`/`AddLeaderPath`） | 0.5 天 |
| 3 | 扇区→卡片 + 标题栏模板 + 动效 | `RadialMenuView.xaml`(.cs) | 1 天 |
| 4 | `ThreadStatus` 枚举 + `RadialMenuItemState.Status` + LED 控件 | `Models`、`RadialMenuSlotViewModel` | 0.5 天 |
| 5 | Agent 层键盘视图（`RadialMenuLayerKind.Agent` 分流到键盘模板） | 新 `AgentKeypadView` 或 View 内模板切换 | 1–1.5 天 |
| 6 | 手柄线稿剪影资产 | `Assets/` | 0.5 天 |
| 7 | P1 状态源探测（独立排期） | `CodexDataService` / App Server | 另计 |

顺序建议 1→2 先行（一天内可见的"止丑"收益），3–6 为完整改版，
7 与 v0.4b 状态研究合并推进。

## 4. 测试关注点

- 键盘布局与 `AgentRadialSlotLayout.Bindings` 顺序的一致性
  （槽号 1–6 与物理键映射不因视图形态改变）；
- `ThreadStatus.Unknown` 不得渲染为任何"确定"状态；
- 高亮/取消/`ConfirmationProgress` 在键盘形态下的行为与轮盘等价；
- Learning / Always / Off 三种显示模式在两种形态下语义不变；
- 深浅背景（Codex 深色主题）下 overlay 的可读性。
