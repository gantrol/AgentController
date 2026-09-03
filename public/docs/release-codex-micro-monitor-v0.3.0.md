# Codex Micro Monitor 0.3.0 / Codex Micro 监控器 0.3.0

## English

Download and extract:

`Codex-Micro-Monitor-v0.3.0-win-x64.zip`

Run `CodexMicro.exe`. Windows requires the .NET 10 Desktop Runtime x64.

### Compatibility correction: model switching in new tasks (2026-09-02)

- Treat blank-draft quick-model switching in 0.3.0 as unavailable on current
  Codex Desktop builds. E2E on `26.831.1445.0` showed that the config-plus-dial
  path changed Sol's effort but did not switch the renderer-owned draft to
  Luna; it stopped before submitting probe text and restored the config.
- Codex Desktop has since updated to `26.901.1978.0`. Its model picker contains
  a separate `Default` entry and radio-button model rows; a separate `Power`
  control exposes a discrete Slider state. The adapter computes the absolute
  effort position and writes RangeValue once when available; otherwise it
  derives one target click from the live bounds of the unique `N of M` track.
  It does not sweep left and then right, and fails closed when neither direct
  action is safe. Old indexes, coordinates, and combined model/effort
  assumptions are invalid.
- The accepted replacement is a build-gated foreground Composer transaction
  for blank drafts only. It selects model and effort by accessible semantics,
  verifies both values, shows a loading state, and rejects repeated clicks.
- An Ultra plus Full access warning is always left to the user in Codex. The
  monitor does not choose a permission outcome; it waits and verifies again
  after a dialog caused by that operation closes. A pre-existing warning is
  not adopted by a new operation.
- Once a real task exists, switching continues to use the authoritative
  per-task owner/follower IPC path. That existing-task path remains separate
  from the pending blank-draft adapter.
- The replacement is staged in the current worktree but is not part of 0.3.0.
  Final `26.901.1978.0` regression remains pending under a non-submitting test
  policy: DEBUG E2E may not type/submit text or activate/navigate Codex. See
  [ADR-0003](../../docs/adr/0003-codex-blank-draft-model-switch.zh-CN.md).

### Virtual HID driver

- This release updates only Codex Micro Monitor. The virtual HID driver is
  unchanged and is not bundled in the application ZIP.
- If the existing driver installation completed with `Ready`, do not reinstall
  it. If it is missing or did not reach `Ready`, install it separately by
  following the [virtual HID driver guide](https://github.com/gantrol/AgentController/blob/codex-micro-monitor-v0.3.0/virtual-micro/UNSIGNED-DRIVER.md).

## 中文

下载并解压：

`Codex-Micro-Monitor-v0.3.0-win-x64.zip`

运行 `CodexMicro.exe`。Windows 需要安装 .NET 10 Desktop Runtime x64。

### 兼容性更正：新会话模型切换（2026-09-02）

- 当前 Codex Desktop 构建中，应把 0.3.0 的空白草稿快捷模型切换视为不可用。
  `26.831.1445.0` 的 E2E 证明 config + 旋钮路径只改变了 Sol 的 effort，没有把
  renderer 草稿切到 Luna；流程在提交探针文本前停止并恢复了配置。
- Codex Desktop 随后更新到 `26.901.1978.0`。模型菜单新增独立 `Default` 项及
  RadioButton 模型行；adapter 根据独立 `Power` 的档位状态计算绝对目标，有可写
  RangeValue 时一次设置，否则根据唯一 `N of M` 滑轨的实时矩形只点击目标刻度一次。
  不再先向左、再向右扫描，无法安全直接操作时失败关闭。旧列表序号、坐标和
  模型/effort 合并结构假设全部失效。
- 接受的替代方案只对空白草稿执行带构建门禁的前台 Composer 界面事务：按可访问性
  语义分别选择并核验模型与 effort，期间显示加载状态并拒绝重复短按。
- Ultra + Full access 警告始终交给用户在 Codex 中处理；监控器不代选权限结果，弹窗
  由本操作触发并关闭后重新核验；新操作不接管操作前已经存在的警告。
- 真实任务创建后仍使用权威的 per-task owner/follower IPC；该已存在任务路径与尚未
  完成的空白草稿 adapter 相互独立。
- 替代实现已暂存在当前工作树，但不属于 0.3.0。`26.901.1978.0` 最终回归仍须遵守
  非提交测试策略：DEBUG E2E 不得输入/提交文本，也不得激活或导航 Codex。详见
  [ADR-0003](../../docs/adr/0003-codex-blank-draft-model-switch.zh-CN.md)。

### 虚拟 HID 驱动

- 本版本只更新 Codex Micro Monitor；虚拟 HID 驱动没有变更，也不会捆绑进应用 ZIP。
- 已有驱动安装结果为 `Ready` 时不要重装。尚未安装或未达到 `Ready` 时，请按
  [虚拟 HID 驱动安装指南](https://github.com/gantrol/AgentController/blob/codex-micro-monitor-v0.3.0/virtual-micro/UNSIGNED-DRIVER.zh-CN.md)
  单独安装。
