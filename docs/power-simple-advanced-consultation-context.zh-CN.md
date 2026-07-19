# Power / Fast / 简易与高级模式：外部模型求助上下文

> 用途：把本文件连同仓库交给更强模型，让它先做架构诊断和方案设计，再决定是否改代码。
>
> 基线：`main` / commit `7609e05`，AgentController v0.7，WPF + .NET 9 + Windows UI Automation。
>
> 当前工作树另有用户自己的 `README.zh-CN.md` 修改，与本问题无关，请勿覆盖或回退。

## 2026-07-17 最新状态（先读这一节）

这轮已经修复并实机验证“默认仍是高级、切到简易不生效”，不要再把它当作未定位问题：

- `app/Models/AppSettings.cs:5`：新配置版本为 v3，默认 `ComposerDialMode = simple`（定义在同文件 `:14-15`）。
- `app/Services/SettingsService.cs:11,116-124`：v1/v2 配置无条件做一次 `Advanced → Simple` 迁移；v3 之后才保留用户显式选择。
- `app/MainWindow.xaml.cs:206-207,6378-6404`：设置页模式属性变化会立即写入 `_settings`、原子保存，并调用 `ApplyComposerDialMode(forceReset: true)`；不再等另一个“保存”动作。
- `app/MainWindow.xaml.cs:3989-4015`：切换时清理旧 picker/输入状态，Simple 强制回到 `RightControlMode.Dial`，Advanced 强制回到 `Model`。
- 已用真实 Debug WPF 窗口验证：冷启动显示“简易模式”；切 Advanced 后设备页立即显示“模型”；切回 Simple 后立即显示“简易模式”。落盘为 `Version=3, ComposerDialMode=simple`。
- Debug、Release 各跑完整测试：561/561 通过；`app/bin/Debug/net9.0-windows10.0.19041.0/AgentController.exe` 已重建。

当前真正未完成的是 **Codex Micro 私有报文的端到端传输**。必须严格区分以下两件事：

1. **已完成：报文与应用侧优先路由。**
   - `app/Services/Micro/MicroRpcCodec.cs:12-98`：64 字节 Report、`0x06/0x02` 头、61 字节 UTF-8 分包、换行结束的 `v.oai.hid` 编码。
   - `app/Services/Micro/MicroInputService.cs:8-76`：`ACT06` Fast、`ACT09` Fork、`ENC/ENC_CW/ENC_CC` Power、`AG00` 退出的白名单映射。
   - `app/Services/CodexComposerService.cs:5657-5741,6094-6182`：Power/Fast 先尝试 Micro；不可用时立即走 F17/F18/F20，不再在每个输入上等待 550–1400 ms UIA 扫描/回读。
   - `app/MainWindow.xaml.cs:1184-1210`：Fork 先尝试 `ACT09`，再立即走 F21；只有 SendInput 自身失败才用 UIA 命名动作。
2. **未完成：把这些 Report 真正注入 Codex。**
   - `app/Services/Micro/MicroReportTransport.cs:42-172` 目前只是命名管道客户端。
   - `app/Services/AppServices.cs:83-84` 会连接 `AgentController.VirtualMicro.v1`。
   - 仓库里没有对应的 `VirtualMicroBroker.exe`，也没有通过 Windows VHF 暴露 `VID 0x303A / PID 0x8360 / UsagePage 0xFF00` 的驱动。因此当前机器上 `TrySend` 会快速返回 false，随后走语义快捷键；不能把这描述成“已经实际走了 Micro HID”。

### 现在请更强模型回答的问题

请不要再建议 UIA-first，也不要重复解决默认模式。请基于微软 VHF 正式接口和当前 Codex 26.707.12708.0 的观测协议，审查并补全一个**独立、可编译但不静默安装**的 M4 PoC：

- KMDF/VHF 驱动的最小 HID report descriptor、`VHF_CONFIG`、`EvtVhfAsyncOperationWriteReport`、`VhfReadReportSubmit` 和清理生命周期应该怎样写？
- 用户态 Broker 如何安全地把现有命名管道 batch 转成 input report，同时重组 Codex 发出的 output RPC，并响应 `device.status`、`sys.version`、`v.oai.rgbcfg`、`v.oai.thstatus`？
- 如何做单客户端 ACL、固定 schema/长度上限、断线回中、队列背压，以及“提交成功但效果未知时禁止双执行”的结果契约？
- 如何用本仓库已下载/可还原的 WDK NuGet 构建并产出 `.sys/.inf/.cat`，但把管理员安装、测试签名和系统安全设置明确留给用户决定？
- 在没有真实 Micro 硬件 descriptor 抓包的条件下，哪些字段可以从现有包体确定，哪些必须阻止进入 Release？

要求输出精确文件结构、关键 C/C# 代码、构建命令、测试步骤、卸载/故障恢复方案；不得修改 Codex `app.asar`、注入 renderer、伪造 Electron IPC，或用 UIA 成功代替 Micro 链路验收。

## 可直接复制给模型的问题

请阅读本文件列出的代码位置，诊断并给出一套可落地的修改方案。不要只解释单个函数；请沿着“手柄采样 → 手势路由 → 重复/加速 → 异步队列 → Codex UI Automation → 回读 → 本地 Overlay”整条链路分析。

目标与优先级如下：

1. 修复简易模式下 Power 没有可靠触发的问题，并说明“直接拨右摇杆”与“先按 R3 打开官方选择器再拨动”为什么走了不同路径。
2. 修复/明确 Fast 的两种入口：简易模式右摇杆向下，以及 RB+Y 快捷操作。现在观察到 Power 区域里的 Fast/快速项或 RB+Y 好像没有触发。
3. 验证 Power 是否真的能按摇杆偏转幅度和保持时长持续加速。不能用纯 timing 单测代替端到端结论；要考虑每次 UIA 操作的耗时、串行 gate 和有界队列。
4. 明确 Sol Max 下简易模式的产品行为。Sol Max 没有原生 Simple Power，但 Standard/Fast 应继续可用。
5. 如果简易/高级两套模式的耦合难以可靠维护，可以收缩产品范围：手柄只保留简易模式；不要为了兼容高级模式破坏 Power/Fast 的可靠性。
6. 如果高级模式可做，优先考虑不逐步遍历 Codex 官方菜单：AgentController 自己维护 `Model → Effort → Speed` 树和气泡 Overlay，摇杆上下左右只移动本地光标；确认后按目标值一次性应用。请评估现有 `models_cache.json` catalog、`SelectAsync` 精确目标选择和 Overlay 基础设施能否复用。

请输出：

- 根因排序（确定事实 / 高概率推断 / 需要实机证据分开写）；
- 推荐的状态机和输入契约；
- 最小改动方案，以及更理想的重构方案；
- 精确到文件/函数的修改清单；
- 可自动化测试清单和必须实机验证的步骤；
- 对“本地树状气泡 + 最终一次提交”的可行性、数据新鲜度和失败回滚策略。

## 用户观察与期望

原始问题整理如下：

1. 模型选择器里的 Power 组件在“简易模式”下没有触发，也无法确认是否实现了按手柄摆动幅度、保持时间持续加速。
2. Power 组件附近有一个 Fast/快速模式入口，但看起来没有触发；手柄还有 RB+Y Fast 快捷入口，也需要核对。
3. 当前模型是 Sol Max 时，简易模式应该怎样处理？
4. 如果简易/高级模式关系过于复杂，可以让手柄仅考虑简易模式；模式设置本身能切换并不是当前难点。
5. 如果保留高级模式，希望不要每一步都遍历/等待官方菜单；改为本地维护树状气泡，用摇杆上下左右选择，最后才向 Codex 应用目标值。

## 当前产品契约

README 声称：

- 简易模式：右摇杆左右调 Power；上选 Standard；下选 Fast。
- 高级模式：左右选 Model / Effort / Speed；上下按屏幕顺序调档位。
- 右摇杆保持同方向约 2 秒逐渐加速，偏转越深，最终重复越快。
- Sol Max 缺少 Simple Power 时，提示 A 切高级、B 保持简易；两种选择下 Standard/Fast 均应可用。

对应位置：

- `README.zh-CN.md:60-80`
- `todo/90-v0.7-maintenance.md` 明确写着“尚未在非 Max 模型上做 Power/Fast 物理端到端验证”。

## 当前调用链

### 1. 手柄采样与持续帧

- `app/Services/XInputService.cs:67-75`：16 ms 周期轮询。
- `app/Services/XInputService.cs:228-260`：除状态变化外，只要存在活动模拟量，也会持续发布 `StateChanged`。因此“摇杆保持不动完全没有 tick”不是当前代码的事实。
- `app/MainWindow.xaml.cs:282-298`：状态先进入 `ControllerStateBuffer`，再由 Dispatcher 消费。
- `app/Services/ControllerStateBuffer.cs:52-80,104-115`：Dispatcher 忙时会合并连续模拟帧，但按钮/扳机区域边沿会保留。

结论：持续加速有运行时 tick 来源；但 UI 线程拥塞时，中间模拟帧可能被合并。

### 2. 简易/高级模式分流

- `app/Models/AppSettings.cs:14-15`：默认 `ComposerDialMode = simple`。
- `app/Models/ComposerDialModes.cs:3-27`：只有 `simple` / `advanced` 两值，未知值回落到 simple。
- `app/MainWindow.xaml.cs:3863-3915`：读取设置并把 `_rightMode` 在 `Dial` 与高级的 Model/Reasoning/Speed 之间切换。
- `app/Views/SettingsPageView.xaml:98-108`：设置页 ComboBox 直接绑定该模式。

### 3. 右摇杆的关键路由

- `app/MainWindow.xaml.cs:625-650`：左右摇杆经过 `StickGestureRouter`；右摇杆在径向菜单、语音、R3 按住或输入保护期内会被阻断。
- `app/MainWindow.xaml.cs:662-723`：
  - 简易模式、菜单上下文未激活：右摇杆上下调用 `AdjustSimpleSpeed`；左右通过 `UpdateAnalog` 调用 `QueueSimplePowerStep`。
  - 高级模式：上下调用 `QueueAdvancedPickerStep`，左右只切 Model/Effort/Speed 本地控制类别。
  - 官方菜单上下文激活：上下左右改走 `QueueVirtualDialNavigation`，不再走简易 Power/Fast 直达路径。
- `app/MainWindow.xaml.cs:3188-3204`：R3 短按在简易模式显式打开 Simple picker，在高级模式显式打开 Advanced picker。
- `app/MainWindow.xaml.cs:4100-4126`：显式打开 picker 成功后调用 `SetVirtualDialMenuOpen(true)`。
- `app/MainWindow.xaml.cs:741-755`：`_virtualDialMenuOpen` 会让 `IsVirtualDialContextActive` 成立。
- `app/MainWindow.xaml.cs:4322-4355,4557-4568`：Power/Fast 直达操作返回 `IsMenuOpen` 时只更新 `_composerPickerMenuLikelyOpen`，不会设置 `_virtualDialMenuOpen`。这个“菜单大概率已开”状态只用于 B 关闭菜单（`app/MainWindow.xaml.cs:3367-3387`），不改变右摇杆分流。

这形成了一个非常重要的行为差异：

- 不按 R3，直接左右拨右摇杆：调用 `StepSimplePowerAsync`。
- 先按 R3 打开官方菜单，再左右拨：调用 Virtual Dial 的原生菜单导航，不调用 `StepSimplePowerAsync`。
- 由一次 Power/Fast 直达操作顺带打开的同一个官方菜单：因为只标记 `_composerPickerMenuLikelyOpen`，后续摇杆仍继续走 Simple 直达路径。

因此“简易模式 Power 没触发”必须先记录用户操作序列；外观看似相同的菜单，当前可能处于两种不同的本地 ownership/state，输入行为并不等价。

### 4. Power 持续加速与实际吞吐

- `app/MainWindow.xaml.cs:714-723`：只有简易模式右摇杆 X 轴接入模拟重复器并投递 Power step。
- `app/Services/AxisRepeater.cs:43-77,91-150`：方向首次进入立即触发；后续按动态 delay/interval 重复。
- `app/Services/AnalogRepeatTimingPolicy.cs:7-60`：
  - 加速时间默认 2 秒；
  - 偏转幅度经 dead-zone 归一化和 SmoothStep；
  - 默认 360/220 ms 设置在满幅、2 秒后约变成 137/79 ms。
- `app/MainWindow.xaml.cs:3918-4013`：Power step 进入异步 pump。
- `app/MainWindow.xaml.cs:4570-4585`：待处理步数被限制在 `[-2, 2]`。
- `app/MainWindow.xaml.cs:4188-4203`：所有 composer picker 自动化通过 `_dialAutomationGate` 串行执行。
- `app/Services/CodexComposerService.cs:5476-5582`：一次 Power step 需要展开/识别菜单、尝试 RangeValue 或焦点+左右键，并最多等待 700 ms 回读。

关键风险：重复器可以每约 79 ms 产生一次回调，但 UIA 消费端每步可能耗时 35–700+ ms，且只有一个串行消费者、积压最多 2 步。当前实现更像“输入动量会加快请求产生”，并不能证明“Codex 的实际 Power 变化能按同样速率持续加速”。超过消费能力的重复会被合并/丢弃。

另外，简易模式的 Standard/Fast 是二元选择，不应该持续重复：

- `app/MainWindow.xaml.cs:4016-4039` 用 `_simpleSpeedHeldDirection` 保证同一方向保持期间只发一次。
- `app/Controllers/SimpleSpeedInputPolicy.cs:3-11`：上 = Standard，下 = Fast。

### 5. Power UI Automation

- `app/Services/CodexComposerService.cs:6218-6286`：`PreparePicker` 查找 Codex、查找模型按钮、展开并确保目标 view。
- `app/Services/CodexComposerService.cs:6799-6838`：模型按钮查找依赖“按钮名称以数字开头 + 特定 class token”。Codex UI 文案/结构变化会使入口失效。
- `app/Services/CodexComposerService.cs:6926-6964`：菜单枚举只收集 `ControlType.MenuItem`，并扫描主窗口和同进程顶层窗口。
- `app/Services/ComposerPickerViewPolicy.cs:5-50`：Power 只按名称前缀 `Power/功率/强度` 识别。
- `app/Services/CodexComposerService.cs:6310-6364`：Simple view 判定依赖可见菜单名；Simple 返回时会额外保留不可见但 enabled 的 Power/Fast action。
- `app/Services/CodexComposerService.cs:5499-5540`：找到 Power item 后，先尝试 RangeValue；失败才聚焦 item 并发送左右键。
- `app/Services/CodexComposerService.cs:6418-6478`：RangeValue 会在 Power item 本身及后代中查找，按 `SmallChange` 修改。
- `app/Services/CodexComposerService.cs:6366-6415,6481-6553`：焦点确认包含 SetFocus、点击、祖先与矩形命中等多重兜底。
- `app/Services/CodexComposerService.cs:5543-5582`：通过 composer 按钮 Name 的变化回读；RangeValue 确认变化时即使按钮 Name 未变也返回成功。

需要更强模型重点判断：

- 当前 Codex 的 Power 是否仍暴露为 `MenuItem`，名字是否仍以前述 token 开头；
- Power 实际可写控件是否是后代 RangeValue，还是只能用键盘/Invoke；
- 点击 Power 行是否会“选择模型/关闭菜单”而不是只聚焦滑条；
- 700 ms Name 回读是否可靠，是否应直接回读 RangeValue/Selection 状态；
- `PreparePicker` 每一步重新扫整棵 UIA 树是否是主要延迟源。

### 6. Fast 的两条入口

#### 简易右摇杆上下

- `app/MainWindow.xaml.cs:686-713,4016-4097`：上/下映射到 Standard/Fast，异步调用 `SetSimpleSpeedAsync`。
- `app/Services/CodexComposerService.cs:5663-5688`：先尝试 Simple 直达；如果 Simple view、speed option 或 readback 失败，则自动尝试 Advanced Speed。
- `app/Services/CodexComposerService.cs:5690-5765`：Simple 直达只识别“Enable/Turn on Fast”和“Enable Standard/Turn off Fast”类 action，Invoke 后重新展开并检查相反 action 是否出现。
- `app/Services/ComposerPickerViewPolicy.cs:88-102`：目前支持的 Fast/Standard 文案模式很窄。
- `app/Services/CodexComposerService.cs:5768-5919`：Advanced fallback 展开 Speed，直接 Invoke 目标项并回读。

风险：如果当前 Codex 的简易项仅叫 `Fast`、`Fast mode`、`Standard`，而非 `Enable fast` / `Turn on fast`，现有分类会失败；现有单测只验证预设文案，没有真实 UIA 快照。

#### RB+Y Command 快捷入口

- `app/MainWindow.xaml.cs:789-839`：RB down 打开 Command radial layer。
- `app/Controllers/RadialInputMap.cs:122-139`：Command layer 中 Y 解析为 `ToggleFast`。
- `app/MainWindow.xaml.cs:894-964,1142-1155`：接收径向操作、先显示确认，再执行 `ExecuteFastToggle`。
- `app/MainWindow.xaml.cs:1451-1509`：最终调用 `_composerAutomation.ToggleSpeedAsync` 并展示结果。
- `app/Services/CodexComposerService.cs:5585-5661`：Toggle 先判断 Simple 当前 action，失败时切 Advanced Speed 判断当前值并选择相反值。

但当 R3 打开的 Virtual Dial 上下文还在时：

- `app/MainWindow.xaml.cs:39-45`：Dial 独占上下文冻结 Y 等基础键，只放行 R3/A/B。
- `app/MainWindow.xaml.cs:529-540`：Dial 上下文激活时完全跳过 `ProcessRadialInput`。

所以在官方 picker 打开期间按 RB+Y，Command radial 根本不会建立，Y 也被冻结。这是“Fast 快捷键没触发”的高概率解释之一。需要决定产品契约：RB+Y 是应该先关闭 picker 再 Toggle，还是在 picker 开着时明确不可用并反馈。

## Sol Max 的现状

- `app/Controllers/SimpleModeCompatibilityPrompt.cs:13-31`：只有以下条件同时成立才提示切高级：
  - 当前设置不是高级；
  - 操作失败；
  - `ErrorDetail == composer-picker-view:simple`；
  - composer 按钮名称中存在独立 token `Sol` 和 `Max`。
- `app/MainWindow.xaml.cs:4206-4320`：提示 30 秒；A 切高级，B 或超时保持简易；切换会保存设置并要求摇杆回中。
- `app/MainWindow.xaml.cs:4322-4404`：Simple picker 失败结果转为提示或错误反馈。
- `app/Services/CodexComposerService.cs:5663-5688`：即使保持简易，Standard/Fast 仍可通过 Advanced Speed fallback 应用。

现有行为的边界问题：

- Max 识别依赖按钮文案严格包含 `Sol` 与 `Max` 独立 token；改名/本地化后不会提示。
- 用户选择“保持简易”后没有按当前 model/selection 持久抑制提示；下一次左右拨 Power 可能再次弹同一提示。
- “简易模式”在 Max 下实际上是：Power 不可用，但 Speed 操作可能在内部临时切到 Advanced UIA 路径。UI 概念与实现路径并不完全一致。
- 如果决定手柄只保留简易模式，建议把 Max 明确定义为“左右无动作并给一次性原因；上下 Standard/Fast 继续工作”，不要每次要求用户理解高级 picker。

## 高级模式现状与本地树状气泡方案

### 现有高级 step 为什么慢

- `app/MainWindow.xaml.cs:4407-4509`：每个摇杆 step 都进入高级异步 pump。
- `app/Services/CodexComposerService.cs:5922-6105`：每个 step 都会：
  1. `PreparePicker(Advanced)`；
  2. 找 category；
  3. 展开 submenu；
  4. 枚举 option 并按屏幕坐标排序；
  5. Invoke 相邻目标；
  6. 最多 900 ms 重复重新打开 picker 做回读。
- `app/Services/CodexComposerService.cs:6107-6192`：option 枚举最多轮询 20 次，每次 60 ms，按 popup container 和当前/selected 状态选组。

这不是单纯的“菜单键盘遍历”，而是每一步重新发现、展开、枚举、选择、回读，天然不适合高频摇杆浏览。

### 已有的本地树数据

- `app/Services/CodexComposerService.cs:22-44`：`ComposerCatalog` 已表达 `Models`，每个 model 带 `Efforts`，另有当前 model/effort/speed。
- `app/Services/CodexComposerService.cs:1630-1667`：从 Codex home 加载 catalog，并结合 composer 按钮与 config 计算当前选择。
- `app/Services/CodexComposerService.cs:7044-7113`：从 `models_cache.json` 读取当前账户可见模型、优先级和 `supported_reasoning_levels`。
- `app/Services/CodexComposerService.cs:7116-7229`：从 `config.toml` 和 composer 按钮读当前 model/effort/service tier。
- `app/Services/CodexComposerService.cs:1670-1678,6627-6730`：已有按明确 `kind + target` 一次性选择的 `SelectAsync`；它仍使用官方 UIA，但不需要用户每个 detent 都等待相邻 step 完成。

### 可复用 Overlay 基础

- `app/Views/RadialMenuOverlayWindow.xaml.cs:18-65`：非激活 Overlay、线程切换、底部居中定位模式可复用。
- `app/Views/SidebarNavigationMenuOverlayWindow.xaml.cs`：本地 previous/current/next 状态和短时展示模式可参考。
- `app/ViewModels/RadialMenuViewModel.cs:17-31,105-125`：固定方向槽位和一次性状态更新模式可参考。
- `app/Models/RadialMenuState.cs:11-59`：现有 radial state 最多六个固定物理槽，不适合直接承载任意长度模型树；建议新建专用 tree/bubble state，而不是硬塞进 `RadialMenuState`。

### 建议让更强模型评估的交互状态机

可考虑如下方向，但请模型验证而不是盲目照抄：

1. R3 打开 AgentController 自己的 Tree Bubble，不打开 Codex picker。
2. 根层横向或上下选择 `Model / Effort / Speed`；进入子层后上下浏览本地选项；左右负责返回/进入。具体方向应固定，避免与当前 simple Power 契约冲突。
3. 摇杆浏览只更新本地 optimistic cursor，立即渲染和震动，不做 UIA。
4. 在摇杆回中稳定一小段时间、按 A/R3 确认，或显式提交时，只把最后一个目标送入 `SelectAsync(kind, target)`。
5. UIA 执行时使用 generation/last-write-wins；旧目标取消，禁止排队执行已经过时的中间选项。
6. 成功回读后提交本地 selected state；失败则回滚到 last confirmed，并在 Overlay 上显示原因。
7. catalog 需要 freshness/version：启动时加载、Codex 前台/账户变化时重载、选择失败或目标缺失时强制重扫一次。

仍无法完全消除的依赖：Codex 没有公开实时设置 API 时，最终应用 live composer selection 仍需 UIA 或官方快捷命令。这里能消除的是“浏览阶段每一步依赖官方菜单响应”，而不是最终提交依赖。

## 测试现状

在基线 commit 上执行：

```text
dotnet test app.Tests/AgentController.Tests.csproj -c Release --no-restore
Passed: 518, Failed: 0, Skipped: 0
```

现有相关单测：

- `app.Tests/AnalogRepeatTimingPolicyTests.cs:7-109`：幅度/时长到 delay/interval 的纯函数。
- `app.Tests/AxisRepeaterTests.cs:7-145`：首次立即动作、2 秒加速、回中重置等。
- `app.Tests/SimpleSpeedInputPolicyTests.cs:7-24`：上下到 Standard/Fast 的映射。
- `app.Tests/ComposerPickerViewPolicyTests.cs:7-76`：Simple/Advanced 和 Fast 文案分类。
- `app.Tests/ComposerSpeedSelectionPolicyTests.cs:7-66`：Speed option/category 文案匹配。
- `app.Tests/SimpleModeCompatibilityPromptTests.cs:9-92`：Sol Max 提示条件和 A/B 选择。
- `app.Tests/ComposerPickerVisualOrderPolicyTests.cs:7-35`：高级列表屏幕顺序。
- `app.Tests/RadialInputMapTests.cs:45-59`：RB Command layer 的 Y → ToggleFast 映射。

自动化缺口：

- 没有覆盖 `MainWindow.ProcessControllerState` 的模式/菜单上下文整条分流。
- 没有覆盖“R3 打开 simple picker 后，右摇杆不再进入 Simple Power/Fast”的契约测试。
- 没有覆盖“picker 打开时 RB+Y 被 Dial 独占上下文吞掉”的测试。
- 没有把 `AxisRepeater` 与 `QueueBoundedStep + UIA pump latency` 放在一起测，所以无法证明实际持续加速。
- 没有保存当前 Codex UIA tree snapshot/fixture，文案和 ControlType 的兼容性测试都是人工构造字符串。
- 没有非 Max 模型的 Power/Fast 物理端到端测试；`todo/90-v0.7-maintenance.md` 已明确承认。

建议的实机证据采集：

1. 记录当前模型按钮 Name、picker 中每个元素的 Name / ControlType / IsOffscreen / BoundingRectangle / 支持的 UIA patterns。
2. 分别测试：菜单关闭直接左右；R3 后左右；菜单关闭上下；R3 后上下；菜单关闭 RB+Y；R3 后 RB+Y。
3. 用非 Max 模型从最低 Power 满幅保持右摇杆至少 4 秒，记录每次 repeater callback、queue depth、UIA start/end、RangeValue 前后值和 composer button readback 时间。
4. 用半幅与满幅对比“实际成功变更次数/秒”，不是只比较请求次数。
5. Sol Max 下验证左右只给一次说明，上/下和 RB+Y 都能切换并有可靠回读。

## 希望模型最终拍板的设计问题

1. R3 在简易模式下究竟应打开官方 picker 并进入二维原生导航，还是只展示本地 Power/Speed Overlay？不能继续让用户以为 R3 后的左右仍等价于 Simple Power。
2. RB+Y 是否应具有高优先级，能抢占/关闭已打开 picker 后 Toggle Fast？若不能，必须给明确不可用反馈。
3. “持续加速”应定义为请求频率、成功 UIA 提交频率，还是本地光标浏览频率？在当前 UIA 吞吐下三者不同。
4. Power 应采用连续/离散本地目标值合并，然后低频提交最新值，还是继续逐 step 提交？若 RangeValue 可稳定写，优先考虑直接设置目标值而非逐步 UIA。
5. Max 的提示应按“能力缺失”判断，还是按模型名字判断？推荐能力检测优先，模型名仅作展示。
6. 本地高级树的数据源是否以 `models_cache.json` 为主、UIA 快照为校验；账户切换和 Codex 更新时如何失效。
7. 是否先收缩到“手柄仅简易模式 + Max 无 Power + Speed fallback”，等 Simple/Fast 端到端稳定后再引入本地高级树。
