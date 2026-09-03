# ADR-0003：空白新会话快捷模型使用受观察的 Composer 界面事务

> Status: Accepted; implementation staged, final non-submitting UI regression pending
> Date: 2026-09-02
> Scope: Codex Micro Monitor / `virtual-micro` 的空白 Codex 新会话
> Related: [ADR-0002](./0002-codex-micro-native-compatibility.zh-CN.md)

## 决策摘要

快捷模型切换按目标是否已有真实 `threadId` 分流：

- 已有真实任务时，继续使用该任务 owner 的语义设置通道，不打开模型菜单；
- 空白新会话没有真实 `threadId`，只在这个阶段使用前台 Codex Composer 的受观察界面事务；
- 不再把 `config/batchWrite` 成功、renderer 配置失效通知或 Micro encoder 已投递当成空白草稿模型已切换；
- 不以 `thread/start` 预建空任务作为当前主方案；若 OpenAI 后续提供可绑定现有桌面草稿的公开 API，再重新评估。

这里的“界面事务”不是固定坐标、固定菜单序号或盲发按键。实现必须锁定同一前台 Codex 主窗口、同一 Composer、同一模型触发器及其弹出面，并按可访问性角色、名称、可用状态和选中状态选择精确目标。第一轮提交产生真实任务后，此例外租约立即失效，后续全部回到真实任务的语义通道。

该窄 adapter 允许对 ownership 已证明的官方模型与 effort 控件执行其公开的可访问性 action；这也是 ADR-0002“UIA 只读”规则的唯一已接受例外。控件没有可调用 action、语义不唯一或版本指纹不匹配时直接失败关闭，不降级为坐标点击或 encoder 猜测。

## 官方接口边界

[Codex App Server 官方文档](https://learn.chatgpt.com/docs/app-server)公开了以下能力：

- `thread/start` 创建新 thread，并可在创建时指定模型；
- `turn/start` 必须接收 `threadId`，可为该 turn 覆盖模型和 effort；
- `config/value/write` 与 `config/batchWrite` 更新磁盘上的 `config.toml`。

公开文档没有提供“修改尚未成为 thread 的桌面空白草稿”接口，也没有列出 `thread/update`。对前一桌面构建随包 CLI `0.152.0` 生成 schema 的本机检查发现实验性 `thread/settings/update`，但它仍要求真实 `threadId`，且不在当前公开方法清单中。因此它可解释旧任务通道为什么有效，不能作为空白草稿的稳定公开解法。

`thread/start` 能创建指定模型的新 thread，但独立 App Server 创建成功不等于当前 Desktop renderer 已附着并显示该 thread。采用它还需要迁移当前草稿内容、导航 Desktop、处理空任务持久化及权限状态，产品语义已经从“切换当前空白草稿”变成“创建另一个任务”。当前不选这条路径。

## 已验证现状

### 2026-09-02 E2E 复现

在 Codex Desktop `26.831.1445.0` 上，内置 `--e2e-toggle-quick-model` 对空白新会话稳定失败：

1. 当前状态为 `Sol / max`，目标为 `Luna / max`；
2. 目标模型与 effort 成功写入用户配置；
3. renderer 收到的原生 encoder 操作依次把当前模型的 effort 从 `Sol / xhigh` 改为 `Sol / high`、`Sol / medium`；
4. 模型没有变为 Luna，最终错误为 `draft-renderer-seed-not-applied`，耗时约 `1.331 s`；
5. E2E 在输入或提交探针文本前停止，并把配置恢复为 `Sol / max`。

这证明失败不是“Luna 出错率高”，而是空白草稿的 renderer 模型状态保持在原模型。磁盘配置写入与 query invalidation 没有调用该草稿自己的模型 setter；继续增加等待、重试或 encoder 步数不会修复所有权错误。

### 当前安装构建与界面变化

当前安装版本已经更新为：

| 项目 | 当前值 |
| --- | --- |
| Codex Desktop AppX | `26.901.1978.0` |
| `ChatGPT.exe` product/file version | `152.0.7977.64` |
| 先前 E2E 构建 | `26.831.1445.0` |

`26.901.1978.0` 的 Composer 与先前界面不同：

- 收起状态可显示组合摘要，例如 `5.6 Sol Ultra`；
- 模型菜单顶部新增 `Default`，它也是可选择项，但不是 Sol、Terra 或 Luna 的别名；
- `Select model` 打开的模型行暴露为带 checked/selected 状态的 RadioButton，列表顺序不能作为身份；
- effort 由独立 `Power` 菜单项中的离散滑块承载，并公开当前位置及 `Use Left and Right arrow keys to adjust power` 辅助说明；
- 当前可访问状态文本包括 `Light`、`Standard`、`Extended`、`Extra High`、`Max`、`Ultra`，例如 `5.6 Luna Light, 1 of 5.`；
- `Use Ultra with Full access?`、`Use Full access`、`Continue` 在当前构建中仍是独立权限警告及其动作。

因此兼容适配必须按构建记录界面指纹，并按语义查找目标。禁止把 `Default` 计入模型序号后发送固定次数，也禁止沿用旧构建的坐标、菜单深度或“模型与 effort 一次完成”的假设。当前构建已完成静态结构核对和分段交互取证；按下述无提交、无前台抢占约束执行的最终回归仍待完成。

### 2026-09-03 实现与验证状态

- 新的 `CodexDraftComposerModelSelector` 已替换空白草稿的 config + encoder seed 路径；模型按精确 RadioButton 身份选择，effort 根据公开档位总数计算绝对目标。若同一 Power 菜单面公开可写 RangeValue，则一次设置；当前构建未公开时，从带 `N of M` 状态的唯一滑轨节点读取实时矩形并只点击目标刻度一次，随后恢复鼠标位置。状态滑轨与名为 `Power` 的 MenuItem 是兄弟节点，查找作用域必须是整个菜单而不是 MenuItem 子树；两种直接动作都不可用时失败关闭，不再先向左、再向右扫描；
- 一次早期 E2E 已把空白草稿切到 Luna / max，并在真实首轮任务 `01a064fb-661f-7960-8546-a00a50416483` 上读回 `gpt-5.6-luna / max`；这条历史证据解释实现方向，不再通过自动发送文本复测；
- 当前构建已实际观察到 `WaitingForUserDecision`、人工关闭警告、重新读取 `Sol / ultra`，以及后续 Luna / max 的界面终态确认；
- 复测同时发现：新操作若接管上一操作遗留的警告，会错误继承旧草稿上下文。现已改为“操作开始前已有警告则失败关闭；只有本操作因果触发的警告才可等待”；
- 警告关闭后的 renderer lease 续签失败现在会把整个切换结果降为未完成，不再允许 DEBUG E2E 继续；
- DEBUG E2E 已删除 Unicode 文本注入和 `composer.submit`，也删除主动激活与循环抢回 Codex 前台的逻辑。它只允许从用户事先打开且已在前台的空白草稿开始，失焦立即停止，并在同一草稿内做不提交的 A→B→A 回读；
- 上述代码已在 `26.901.1978.0` 对应工作树完成 Debug 编译（0 warning / 0 error）。按“不得自动输入、不得抢占前台”的新约束，最后一次无提交、无前台抢占的交互回归尚未执行，因此不能把弹窗路径标记为完全 E2E 通过。

## 界面事务

一次空白草稿切换固定经过以下状态：

1. `Acquiring`：确认 Codex 在前台、IPC 已初始化、没有可见真实任务，并取得短期草稿租约；
2. `SelectingModel`：打开与该 Composer 几何及 ownership 关联的模型菜单，按精确模型身份选择，明确跳过 `Default`；
3. `SelectingEffort`：重新解析当前 Composer 的独立 Power 菜单面，从公开的当前位置/总档数计算目标档位；优先一次设置 RangeValue，否则根据唯一状态滑轨的实时矩形只点击目标刻度一次；
4. `WaitingForUserDecision`：若出现权限警告，停止自动输入并等待用户在 Codex 中处理；
5. `Verifying`：重新读取收起摘要、模型选中态、effort 选中态和草稿租约；
6. `Committed` 或 `Cancelled`：只有同一草稿显示精确目标才提交本地记忆，否则不报告成功。

事务开始时设置 `_quickModelSwitching`，额度环显示循环加载动画；再次短按快捷模型键直接忽略，不排队、不合并为第二次切换。只有进入 `Committed`、`Cancelled`，或窗口/草稿 ownership 失效后才解除门禁。

模型菜单关闭、窗口切换、草稿变成真实任务或控件 identity 改变时，当前事务立即失效。结果未知时不得继续发送第二套输入，也不得仅依据磁盘默认值更新小键盘上的当前模型。

## 警告弹窗

当前构建存在 `Use Ultra with Full access?` 警告。它在 Full access 下首次切到 Ultra 时出现，并可能让用户在以下结果之间选择：

- 保留 Full access；
- 改用更受限的权限模式后继续；
- 关闭弹窗并取消本次选择。

这是权限决策，不是普通的模型菜单确认。Codex Micro Monitor 必须遵守：

1. 不自动点击 `Use Full access`、`Continue` 或弹窗默认按钮；
2. 弹窗存在期间进入 `WaitingForUserDecision`，保持加载动画并拒绝重复快捷模型输入；
3. 用户关闭弹窗后重新取得 Composer 控件和草稿状态，不能从弹窗出现前缓存的菜单位置继续；
4. 只有目标模型、目标 effort 和有效草稿租约同时成立才报告成功；用户取消或最终状态不匹配时报告未完成；
5. 锁定模型的访问/升级弹窗、未知弹窗和没有已登记指纹的弹窗全部失败关闭，不自动购买、升级、放宽权限或确认；
6. 窗口或任务变化时取消等待，不能把随后出现的另一个弹窗归到旧事务。
7. 事务开始前已经存在的 Ultra 警告没有本次操作的因果 ownership，必须失败关闭；用户处理后可重新发起切换，不能由新操作接管旧弹窗。

只要已登记的警告弹窗仍可见且 ownership 未变，等待用户不是超时失败；用户仍可直接操作 Codex。等待期间只冻结 Codex Micro Monitor 发起的同类快捷切换，不拦截用户对 Codex 弹窗的鼠标或键盘输入。

现有 `CodexMenuSelectionObserver` 仍只负责 HUD readback。空白草稿 adapter 由 `CodexDraftComposerModelSelector` 独立识别新 Ultra 警告；两者不得共享可执行弹窗动作，且 selector 从不调用 `Use Full access` 或 `Continue`。

## 被否决的方案

### 继续配置写入并等待

否决。E2E 已证明配置文件可正确变化而当前 renderer 草稿仍保持 Sol；等待只能延后同一错误。

### 通过 encoder 探针证明模型切换

否决。encoder 改变的是 renderer 当前持有模型的 effort；它能证明输入到达，不能证明模型 setter 已执行。

### 固定坐标、固定菜单序号或固定步数

否决。`Default`、独立 effort 控件、锁定行和版本更新都会改变菜单结构；警告弹窗还会改变焦点与 ownership。

### 先扫到一端再反向寻找目标 effort

否决。当前状态已经给出当前位置和总档数；双向扫描会产生无意义的来回动画并增加中途触发弹窗的机会。实现必须计算绝对档位，只提交一次 RangeValue 或一次由实时滑轨矩形导出的目标刻度点击；不得退回方向键探测。

### 用 `thread/start` 预创建并跳转

暂不采用。它会创建真实任务，却没有公开机制把现有 Desktop 空白草稿原位绑定到该 thread；仍需额外导航和草稿迁移，并改变用户可见的历史与权限行为。

## 完成门槛

- 为 `26.901.1978.0` 建立模型触发器、`Default`、RadioButton 模型行、`Power` 控件、选中态和警告弹窗的版本指纹；
- Sol、Terra、Luna 不依赖列表序号，模型与 effort 分两阶段选择并回读；
- Ultra + Full access 进入人工等待，三种用户结果均不误报、不重复输入；
- 未知/锁定弹窗失败关闭；
- 加载动画覆盖整个事务，重复短按不排队；
- 当前构建完成空白草稿 A→B→A 非提交 E2E，并证明没有输入或提交探针文本、没有主动激活/抢回 Codex 前台、没有命中后台任务、没有遗留错误全局默认值；
- 第一轮真实 turn 后再次切换，确认自动恢复为该真实任务的语义设置通道。
