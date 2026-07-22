# Agent Controller v1.1

Agent Controller 1.1 is a Codex Micro interaction and visual-quality release. It integrates the virtual Micro surface into Agent Controller, makes controller and on-screen Micro input more reliable, and brings the surface closer to the Paper reference while preserving the existing HID/RPC contract.

## Highlights

- Embedded the virtual Codex Micro surface into Agent Controller and kept it interoperable with the shared per-user Micro Broker.
- Reworked the 4×4 surface, rotary encoder, joystick, command keys, microphone key, and Agent keys from the Paper reference. Agent lighting now uses layered, naturally diffused light below the keycaps, including current-session distribution and a white fallback when the current task has no status color.
- Added contextual Plan-card cancellation for the virtual Agent 0 key and the Agent Controller Back gesture. A verified Codex request card receives one native Escape; ordinary menus and missing cards continue through `AG00` without duplicate input.
- Kept the Codex key dedicated to composer submission, while rotary press confirms the selected item and rotary drag or wheel movement changes selection.
- Agent keys now foreground Codex even when the selected slot is pressed again, including repeated `AG00` presses.
- Improved encoder press/release recovery, microphone down/up feedback, joystick threshold and neutral handling, and Micro HID routing across current Codex builds.
- Added the macOS foundation preview, platform capability contracts, GameController input groundwork, and macOS packaging documentation. The downloadable v1.1 artifact remains the supported Windows x64 build.

## Validation

- All 903 tests in the Release solution pass.
- The Agent Controller suite passes 751/751 and the virtual Micro desktop suite passes 75/75.
- The Paper surface and Agent lighting states have dedicated off-screen rendering regression coverage.

The Windows x64 package is self-contained and does not require a separate .NET Runtime.

## Known limits

- The request-card cancellation compatibility layer applies to the virtual Micro and Agent Controller gesture. A physical Micro still depends on the upstream Codex bridge for `AG00` behavior in request cards.
- Device Support remains an optional unsigned developer component and is not a production-signed driver package.
- Codex Micro integration follows an observed private contract that may change in future Codex releases.

---

# Agent Controller v1.1（简体中文）

Agent Controller 1.1 聚焦 Codex Micro 的交互可靠性与视觉品质。本版本把虚拟 Micro 面板整合进 Agent Controller，强化手柄与屏幕面板输入，并依据 Paper 参考重做界面，同时保持现有 HID/RPC 协议不变。

## 主要变化

- 将虚拟 Codex Micro 面板内嵌到 Agent Controller，并继续通过当前用户唯一的共享 Micro Broker 协同工作。
- 依据 Paper 参考重做 4×4 面板、旋钮、摇杆、命令键、语音键和 Agent 键。Agent 灯光改为位于键帽下方的分层自然漫射光，并区分当前会话分布；当前任务没有状态颜色时显示白光。
- 为虚拟 Agent 0 和 Agent Controller 返回手势加入 Plan 提问卡片取消兼容层：确认目标为 Codex request card 后只发送一次原生 Escape；普通菜单或不存在卡片时仍走 `AG00`，不会重复注入输入。
- Codex 键始终用于提交输入；旋钮中心用于确认选项，拖动旋钮或滚轮用于移动选择。
- 点击 Agent 键会将 Codex 移到前台；重复点击已经选中的槽位（包括 `AG00`）也会生效。
- 改进旋钮按压释放恢复、语音键 down/up 反馈、摇杆阈值与回中，以及当前 Codex 版本下的 Micro HID 路由。
- 新增 macOS 基础预览、平台能力合同、GameController 输入基础和 macOS 打包文档；v1.1 下载资产仍是正式支持的 Windows x64 版本。

## 验证

- Release 配置下主解决方案 903 项测试全部通过。
- Agent Controller 测试 751/751、虚拟 Micro 桌面测试 75/75 通过。
- Paper 面板与 Agent 灯光状态具备专门的离屏渲染回归覆盖。

Windows x64 包为自包含版本，不需要另行安装 .NET Runtime。

## 已知限制

- request-card 取消兼容层只覆盖虚拟 Micro 与 Agent Controller 手势。实体 Micro 在 request card 中的 `AG00` 行为仍取决于 Codex 上游 bridge。
- Device Support 仍是可选的未签名开发者组件，不是生产签名驱动包。
- Codex Micro 集成基于观察所得的私有协议，未来 Codex 版本可能调整该行为。
