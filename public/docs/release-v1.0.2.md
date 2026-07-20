# Agent Controller v1.0.2

Agent Controller 1.0.2 is a visual refinement release. It modernizes the desktop workspace and controller overlays while preserving the v1 input mapping and Micro-first behavior.

## Highlights

- Reworked the main window into a modern, frameless, minimal workspace with clearer hierarchy and less persistent instructional text.
- Introduced a restrained crystal-backplate material for the application shell, notifications, action panels, Agent panels, and sidebar navigation overlays.
- Kept physical controls visually distinct as keycaps while simplifying non-key surfaces into quiet frosted panels and separators.
- Improved controller glyphs, connected tutorial tabs, compact English labels, scalable text-size presets, and full-message hover details.
- Removed redundant ABXY and navigation instruction strips from controller popups.
- Fixed long tooltip clipping by wrapping plain-text hover content without changing structured tooltips.

No controller mapping or Micro protocol behavior is intentionally changed in this release.

## Validation

- All 804 tests in the main solution pass.
- The crystal action, Agent, sidebar, and notification surfaces were rendered off-screen and visually checked.

The Windows x64 package is self-contained and does not require a separate .NET Runtime. Device Support remains an optional, separately reviewed component.

---

# Agent Controller v1.0.2（简体中文）

Agent Controller 1.0.2 是一次以界面美化为主的版本更新。它重新整理了桌面工作区与手柄弹层的视觉层级，同时保持 v1 手柄映射和 Micro-first 行为不变。

## 主要变化

- 主窗口改为现代、无框、极简的工作台布局，减少常驻教程文字并强化信息层级。
- 为应用外框、通知、动作面板、Agent 面板和侧边栏导航弹层统一加入克制的水晶底板材质。
- 只有真实“键”语义的控件保留键帽外观，其余区域改为安静的霜面板与线性分隔。
- 优化手柄 SVG、联动教程 Tab、简洁英文文案、四档字号，以及 hover 完整信息显示。
- 移除弹层中多余的 ABXY 与导航教学条。
- 修复长 Tooltip 被裁切的问题；普通文本自动换行，结构化 Tooltip 保持原样。

本版本不计划改变手柄映射或 Micro 协议行为。

## 验证

- 主解决方案 804 项测试全部通过。
- 动作、Agent、侧边栏与通知水晶面板均完成离屏渲染和视觉检查。

Windows x64 包为自包含版本，不需要另行安装 .NET Runtime。Device Support 仍是需要单独审查的可选组件。
