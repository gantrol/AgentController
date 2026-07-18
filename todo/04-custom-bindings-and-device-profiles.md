# 04 — 自定义按键与设备 Profile

> Status: Planned
> Priority: P0
> Depends on: 01-core-architecture

## 目标

让用户能把标准键、背键、Raw HID 按钮、组合层和模拟方向绑定到安全的语义 Action，并保持跨设备、跨平台、跨版本可迁移。

## 配置模型

```text
Device control -> Gesture -> Context/Layer -> Semantic Action -> Safety policy
```

## 待办

### 设备能力

- [ ] 用动态 `ControlId` 替代“所有按钮必须进入 LogicalInput 枚举”的限制。
- [ ] 区分标准控制、独立背键、镜像背键、Raw button 和未知能力。
- [ ] Profile 由 immutable built-in base + user override 组成。
- [ ] DeviceMatch 支持 VID/PID、名称、backend、平台和能力指纹。
- [ ] 同一物理设备在 Windows/macOS 的稳定身份建立迁移规则。

### 手势与冲突

- [ ] 支持 press、release、hold、double-press、axis direction、chord 和 layer。
- [ ] 明确 Candidate/Armed/Drain 优先级，避免 Base 动作泄漏。
- [ ] 建立绑定冲突、不可达绑定和循环层检测。
- [ ] 模拟量重复、迟滞和 dead zone 属于输入策略，不写入业务 Action。
- [ ] 高风险动作强制安全策略，不能由普通 Profile 静默取消确认。

### Action Catalog

- [ ] Catalog 描述 ActionId、参数 schema、风险等级、需要的能力和可验证性。
- [ ] 内置动作使用稳定语义名称，如 `turn.stop`、`composer.submit`。
- [ ] Codex 命令、Skill 和未来其他 Agent 通过适配器注册能力。
- [ ] 首版不允许用户从 UI 输入任意 HID byte、RPC method 或 shell command。

### Codex Micro 投影

- [ ] 按 [ADR-0002](../docs/adr/0002-codex-micro-native-compatibility.zh-CN.md) 区分 `Micro control binding` 与 `Semantic action binding`，不能用同一个下拉项混淆物理 slot 和业务动作。
- [ ] Micro control 可绑定 ACT06..ACT12、AG00..AG05、Encoder 和 Analog；Codex 设置拥有这些 control 的最终动作。
- [ ] Semantic action 只有在 build/layout 已验证可表达时才投影 Micro，否则使用 App Server 或报告 Unsupported，不猜默认 slot。
- [ ] Mapping Studio 显示 Codex 当前只读 layout、指纹和 `MappingVerified/MappingUnverified`，不写回私有 `desktop.codex-micro-layout`。
- [ ] 右摇杆默认 Profile 使用 Composer Dial；修饰层提供 Analog，PTT 保留 down/up，所有断连路径发送 neutral/release。

### Mapping Studio

- [ ] 提供“按下要配置的键”捕获模式。
- [ ] 同时显示物理位置、厂商 glyph、当前动作、上下文和冲突。
- [ ] 支持预设复制、单项恢复、全部恢复和撤销/重做。
- [ ] 支持 JSON 导入导出，导入前显示权限与高风险动作差异。
- [ ] 提供实时测试模式，但测试模式禁止执行高风险外部动作。

### 持久化

- [ ] 独立 `settings.json`、`devices/*.json`、`bindings/*.json` schema。
- [ ] 所有 schema 带 version、id 和迁移测试。
- [ ] 原子写入、损坏恢复、备份和未知字段保留策略。
- [ ] 将现有快捷键设置迁移为 executor 配置，不继续扩展平铺字段。

## 完成门槛

- 至少一个标准 XInput、一个带独立背键设备和一个 Generic Raw 设备可配置。
- 用户可以完成捕获、绑定、冲突修复、保存、重启恢复和导出导入。
- 配置中不包含平台执行细节。
- 同一个 Action 经不同输入触发时产生完全相同的安全与结果合同。
