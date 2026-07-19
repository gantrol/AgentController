# Power / Fast 诊断结论与已落地修复

> 对应问题文件:`docs/power-simple-advanced-consultation-context.zh-CN.md`(基线 `7609e05`)。
>
> 本文既是诊断输出,也是修复记录:下述"已实现"部分已在当前工作树完成,`dotnet test` 542 通过(基线 518,新增 24)。`README.zh-CN.md` 未触碰。

## 一、根因排序

### 确定事实(代码即可证明)

1. **同一个官方菜单存在两种本地所有权状态,摇杆语义不同。**R3 显式打开会 `SetVirtualDialMenuOpen(true)` 进入 Dial 独占上下文,此后左右摇杆走原生菜单导航而非 `StepSimplePowerAsync`;而 Power/Fast 直达操作顺带打开的同一菜单只标记 `_composerPickerMenuLikelyOpen`,摇杆继续走 Simple 直达。"直接拨"与"R3 后拨"必然行为不同,用户感知即"Power 时灵时不灵"。
2. **Dial 独占上下文冻结 Y 并整体跳过 `ProcessRadialInput`。**官方 picker 打开期间按 RB 无法建立 Command 层,RB+Y Fast 被静默吞掉,无任何反馈。
3. **"持续加速"只存在于请求侧。**`AxisRepeater` 满幅 2 秒后约 79 ms/次,但消费端单步走完整 UIA(`PreparePicker` 全树扫描 + 最长 700 ms 名称回读,当天实测无 CacheRequest 的全树扫描约 470 ms/遍),串行 gate、待处理步数 clamp ±2、且一次只消费 1 步——实际应用速率跟不上请求速率,超额请求被丢弃。
4. **Sol Max 提示按模型名 token(`Sol`+`Max`)触发,且拒绝后不抑制。**模型改名/本地化即失效;选择"保持简易"后,下一次左右拨动会再次弹出同一提示。
5. **Fast 简易文案分类窗口极窄**(仅 `Enable fast` / `Turn on fast` 等),Codex 文案漂移即失败;RB+Y Toggle 的"当前值"判断依赖菜单 action 的存在性推断。
6. **既往结论(必须优先排除):**app 启动后手柄会话处于 Locked,未按 ☰(Start)解锁时,右摇杆输入在 `ProcessControllerState` 的 armed 检查处被静默丢弃。任何"没触发"的排查都要先确认已解锁。

### 高概率推断(与用户观察的对应)

- "简易模式 Power 没触发" = 事实 1(R3 后语义被移交)与事实 6(会话未解锁)的组合,视操作序列而定。
- "RB+Y 没触发" = 事实 2(picker 开着)或事实 6。
- "Power 附近的 Fast 项没触发" = 事实 5(文案分类失败后虽有 Advanced fallback,但 Toggle 的当前值判断也可能失败)。
- "无法确认是否有持续加速" = 事实 3;结论是此前**没有**真实的端到端加速。

### 仍需实机证据

- 当前 Codex 简易 picker 中 Power 是否仍暴露为 `MenuItem` + 后代 RangeValue;Fast/Standard 菜单项的真实文案;composer 按钮 Name 的实际格式(语义回读依赖"末尾速度后缀"这一约定,`FindSpeed` 与新增的 `TryParseSpeedSuffix` 同源)。
- `composer.increase/decreaseReasoningEffort` 与简易 Power 滑条是否一一对应(尤其两端边界处),`composer.toggleFastMode` 是否即时生效;keybindings.json 注入后 Codex 是否需要重载。

## 二、拍板的输入契约(已实现)

按问题文件第 5 条"手柄可靠性优先",对文档"希望模型拍板的设计问题"逐条裁决:

| # | 问题 | 裁决 |
|---|------|------|
| 1 | R3 在简易模式的语义 | **纯视觉开关**:R3 打开/再按关闭官方 Simple picker,但摇杆语义永不移交——左右恒为 Power,上下恒为 Standard/Fast,picker 开着时也一样。"直接拨"与"R3 后拨"从此等价。高级模式维持原有独占导航。 |
| 2 | RB+Y 与已打开 picker | 简易模式下任何时刻可用(picker 打开也可用)。高级 picker 独占期间不可用,但按肩键会得到一次性"先按 B 或 R3 关闭选择器"提示,不再静默吞掉。选择"明确反馈"而非"抢占",避免拆掉用户正在进行的高级导航会话。 |
| 3 | "持续加速"的定义 | 定义为**成功应用速率**。输入侧照旧按幅度/时长加速;消费侧每轮批量排空全部待处理步数并一次性应用。clamp ±2 保留,作为回中后的过冲保护。 |
| 4 | 逐步提交 vs 目标合并 | Power 采用"批量步数一次提交"(RangeValue 单次 `SetValue(current + N*SmallChange)` 或连发 N 次方向键);Fast 采用"目标式设置"(读当前值→只在需要时切换),天然幂等。 |
| 5 | Max 判定依据 | **能力判定**:简易视图缺 Power 项(`composer-picker-view:simple` 且按钮名非空)即提示,模型名只用于展示。拒绝(B/超时)后按"抑制键"(按钮名去掉速度后缀)静音,后续左右拨只给普通失败反馈;换模型后重新提示。 |
| 6 | 本地高级树数据源 | 评估通过但**本次不实现**(见第六节)。 |
| 7 | 是否先收缩 | 是:本次全部投入简易模式可靠性;高级模式仅加"肩键不可用"反馈,其余未动。 |

## 三、传输层重构:语义快捷键优先,UIA 只读验证(已实现)

依据当天实测结论(UIA 适合验证、不适合驱动;Codex 有语义命令层且 app 已注入 F17-F22):

- **Power step**:优先发送已注入的 `composer.increase/decreaseReasoningEffort`(默认 F18/F17)× N,然后轮询 composer 按钮 Name 变化(≤550 ms)确认;无需打开任何菜单。
- **Fast**:先读按钮名末尾速度后缀确定当前值;已是目标值→直接成功;否则发送 `composer.toggleFastMode`(默认 F20),轮询后缀翻转(≤700 ms)确认。RB+Y 的 Toggle 与摇杆上下的 Set 共用这条路径。
- **回退**:回读失败自动落回原 UIA 菜单路径(打开 picker→找项→RangeValue/键盘→回读),行为与 v0.7 相同。
- **健康状态机 `ComposerShortcutHealth`**(三态):未证实→回读成功=已证实;未证实且"快捷键无变化但菜单路径成功"=可疑(30 s 冷却后重试)。已证实后,"无变化"直接按边界上报(复用 `composer-power-no-change-*` 文案),**不再**触发菜单回退——防止 UI 更新慢时把同一步应用两次。Fast 因为是目标式设置,重复应用无害,故总是允许回退。
- **逃生门**:在设置页清空对应快捷键即可显式禁用语义路径,完全回到 UIA 行为。
- **时序预期**:键击约数 ms + 回读轮询,单批 ~85–200 ms,配合批量合并可跟上 79 ms 的满幅请求节奏;菜单路径仍是数百 ms 级。

## 四、修改清单(精确到文件/函数)

新增:

- `app/Services/ComposerShortcutHealth.cs` — 快捷键健康三态(未证实/已证实/可疑+30 s 冷却),时钟可注入。
- `app.Tests/ComposerShortcutHealthTests.cs` — 上述状态机 5 项测试。

修改:

- `app/Services/CodexComposerService.cs`
  - `StepSimplePowerAsync/Core`:签名 `(int steps, bool allowShortcutFastPath, …)`;新增 `StepPowerViaShortcut`(语义键 ×N + 按钮名回读),原菜单逻辑改名 `StepSimplePowerViaMenu` 并支持批量步数;`TryStepPowerRangeValue` 改为 `current + steps*SmallChange` 单次写入;键盘分支连发 N 次。
  - `SetSimpleSpeedAsync/Core`:签名加 `allowShortcutFastPath`;新增 `SetSpeedViaShortcut`(后缀判读 + F20 + 后缀翻转回读);菜单直达/Advanced fallback 保持,成功且此前语义路径未读到变化时标记可疑。
  - `ToggleSpeedAsync/Core`:签名加 `allowShortcutFastPath`;优先 `TryReadComposerSpeed()`(按钮名后缀)确定当前值并转为目标式设置;后缀不可读时才走原菜单推断。
- `app/Services/ComposerSpeedSelectionPolicy.cs` — 新增 `TryParseSpeedSuffix(string?) → bool?`。
- `app/Agents/AgentContracts.cs`、`app/Agents/Codex/CodexAgentTarget.cs`、`app/Agents/AgentCapabilityFallbacks.cs` — `IComposerAutomation` 三个方法的签名同步。
- `app/MainWindow.xaml.cs`
  - `OpenComposerPickerAsync`:仅 Advanced 视图 `SetVirtualDialMenuOpen(true)`;Simple 视图只标记 `_composerPickerMenuLikelyOpen`(视觉化,不夺摇杆)。
  - `HandleComposerDialShortPress`:新增 `_composerPickerMenuLikelyOpen → CloseComposerPickerOnly()` 分支,R3 成为开/关切换。
  - `ProcessControllerState` 的 Dial 独占分支:肩键按下时给一次性"选择器已打开,先按 B/R3 关闭"反馈(`_dialContextShoulderHintShown`,菜单关闭时复位)。
  - `PumpSimplePowerStepsAsync`:`Interlocked.Exchange` 批量排空 `_pendingSimplePowerSteps`,按 `!_composerPickerMenuLikelyOpen && !IsVirtualDialContextActive` 决定是否允许语义路径。
  - `AdjustSimpleSpeed`/`SetSimpleSpeedAsync`:捕获 `menuWasLikelyOpen`,语义开关取反传入;"操作后重开菜单"仅在操作前菜单本就打开时执行。
  - `ExecuteFastToggleAsync`:同样传入语义开关。
  - Sol Max 提示:`BeginSimpleModeUpgradePrompt(title, modelValue)` 记录触发值,标题改为按模型名展示;`KeepSimpleModeFromPrompt` 写入 `_simpleModeUpgradeDeclinedKey`;`SwitchSimpleModePromptToAdvanced` 清除;`PresentSimplePickerResult` 把抑制键传给策略。
- `app/Controllers/SimpleModeCompatibilityPrompt.cs` — `ShouldOfferAdvanced` 改能力判定 + 抑制键;新增 `SuppressionKey`(归一化并剥离末尾速度 token);删除模型名 token 检查。
- `todo/90-v0.7-maintenance.md` — 新增两条实机验证项。

测试更新:

- `app.Tests/SimpleModeCompatibilityPromptTests.cs` — 重写:能力触发(含非 Max 模型)、空值不触发、抑制与换模型重提示、`SuppressionKey` 归一化。
- `app.Tests/ComposerSpeedSelectionPolicyTests.cs` — 新增后缀判读正反例。
- `app.Tests/AgentTargetTests.cs` — 适配新签名。

## 五、测试现状与实机验证清单

自动化:`dotnet test -c Release` → **542 通过 / 0 失败**(基线 518)。

仍缺的自动化(与问题文件一致,未在本次解决):`MainWindow.ProcessControllerState` 的分流合同测试需要先把手势编排提取为可测的 coordinator(`todo/90-v0.7-maintenance.md` v0.4b 既有条目);真实 UIA 树快照 fixture 仍缺。

实机验证步骤(按顺序,全部使用非 Max 模型起步):

1. **前置**:启动后先按 ☰ 解锁会话;确认运行的是新构建(`Get-Process AgentController | Select Path, StartTime`);确认 `~/.codex/keybindings.json` 已含 F17/F18/F20 三条注入且 Codex 在注入后重启过。
2. 菜单关闭,直接左右拨:应看到 composer 按钮文本直接变化(不弹菜单),Device 页显示新值。首次成功即"已证实"。
3. R3 打开 picker → 左右拨:应仍走 Power(菜单开着时走 UIA 路径,滑条可见移动);再按 R3 应关闭 picker。
4. 满幅保持右摇杆 ≥4 秒:记录按钮文本变化次数/秒,对比半幅;应能观察到"越拨越快"且松手后过冲 ≤2 档。到达最高档后应出现"可能已到最高档"反馈且不再重复应用。
5. 菜单关闭按上/下:Standard/Fast 应在不弹菜单的情况下切换(按钮后缀翻转);picker 打开时按上/下也应生效。
6. RB+Y:菜单关闭时应切换 Fast;简易模式 picker 打开时也应生效;高级 picker 打开时应看到"肩键不可用"提示而非无反应。
7. 把 `ReasoningUpShortcut` 临时清空再重复步骤 2:应自动回到菜单路径(弹出 picker、滑条移动),验证逃生门与回退。
8. 切到 Sol Max:左右拨应出现一次 A/B 提示;按 B 后再左右拨不再弹提示、只给失败反馈;上/下与 RB+Y 仍能切 Standard/Fast;换回其他模型后 Power 恢复。
9. 采集一份当前 picker 的 UIA 快照(Name/ControlType/IsOffscreen/patterns),归档为 fixture,补齐文案兼容性测试的数据源。

## 六、本地树状气泡 + 最终一次提交(问题 6/7 的裁决)

**本次不实现,按问题 7 收缩。**理由:简易模式的直达路径已经绕开"逐步遍历官方菜单"——语义键既不需要打开菜单也不逐项等待,收益中最大的一块(浏览阶段不依赖官方菜单响应)在简易模式已经拿到;高级模式使用频率与风险不成比例,等简易端到端在实机上稳定后再动。

可行性评估(供下一步):

- **数据源已齐**:`models_cache.json`(模型 + `supported_reasoning_levels` + 优先级)、`config.toml`(当前 model/effort/service tier)、composer 按钮名(当前展示值),以及 `SelectAsync(kind, target)` 的一次性精确提交。缺的只是 Overlay 与状态机。
- **Overlay 骨架可复用**：`SidebarNavigationMenuOverlayWindow` 的 previous/current/next 短时展示模式最接近；不要硬塞 `RadialMenuState`（六槽位假设），新建专用 tree state。
- **状态机采纳问题文件的建议**:浏览只动本地 optimistic cursor(立即渲染 + 震动);回中稳定或 A/R3 确认时只提交最后目标;generation/last-write-wins,旧目标取消;成功回读才提交本地 selected,失败回滚 last confirmed 并在 Overlay 显示原因;catalog 启动加载 + Codex 前台/账户变化重载 + 选择失败强制重扫。
- **消除不了的依赖**:最终应用仍需 UIA 或官方快捷命令;若 Codex 语义层未来暴露模型切换命令,提交环节也可改为纯键路径,UIA 退居回读。
