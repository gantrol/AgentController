# 05 — 桌面 UI/UX 与 Avalonia

> Status: Planned
> Priority: P1
> Depends on: 01-core-architecture, 04-custom-bindings-and-device-profiles

## 目标

采用正式的产品设计流程重做桌面体验，并以 Avalonia 共享 Windows/macOS UI，同时保留平台原生菜单、托盘、权限和窗口行为。

## 信息架构

```text
Onboarding / Live Control / Bindings / Integrations / Diagnostics / Settings
```

## 待办

### 研究与设计

- [ ] 为首次连接、日常控制、配置映射和故障恢复绘制用户旅程。
- [ ] 对现有 UI 做启发式评估，记录信息层级、密度、反馈和可达性问题。
- [ ] 先做低保真线框和可点击原型，再写生产 XAML。
- [ ] 用至少 5 名目标用户测试首次连接和自定义一个背键。
- [ ] 记录任务成功率、完成时间、误触和恢复路径。

### 弹出菜单与动作面板

以下为使用 GPT Image 生成的原创概念稿，用于表达信息层级、视觉密度、置顶/未置顶任务和 popup 避让规则；生产界面仍以原型测试与设计 token 为准：

![侧边栏与动作菜单原创概念稿](../docs/assets/ux-sidebar-action-menu-concept.png)

![LB / RB / RT / Y / RS Micro 风格总览](../docs/ux/overlay-family-gpt-image-2.png)

- [x] 将 previous/current/next “转盘”改为 `SidebarNavigationMenuOverlayWindow` 紧凑菜单卡片：纵向邻项、区域边界标签、柔和选中底色、当前项勾选，以及动态手柄 glyph 操作提示。
- [ ] 导航信息架构必须完整覆盖置顶任务、置顶项目、项目和未归项目任务四个区域；L3 在区域间切换时保留每个区域最近的项目路径与任务焦点。
- [ ] 未置顶任务不是降级状态：项目内普通任务通过清楚的 `Projects → Project → Task` 层级访问，未归项目任务有独立入口，任何流程都不得假设任务已置顶。
- [ ] 支持“一级菜单卡片 + 相邻子菜单卡片”的分级浏览；一次只突出当前行和当前路径，返回、确认、不可用、加载中与执行失败都必须有一致且可读的状态。
- [x] LB Agent 使用专用六灯面板；RB Command、RT Turn、Y Action 复用同一套菜单卡片、行状态和按键提示。面键同时显示实际 glyph 与物理方向，Learning 模式提供位置图例；转盘不再作为通用容器。
- [ ] 为 Overlay 建立明确的锚点定位契约：以 Codex 目标 popup 的屏幕边界为输入，控制器菜单优先出现在 popup 上方，并保留至少一个 spacing token 的安全间距，任何情况下都不得覆盖或与目标 popup 重叠。
- [ ] 上方空间不足时，先采用限高滚动、卡片重排或同侧位移；仍无法容纳才降级到 popup 侧边，并继续保证安全间距。定位必须限制在当前显示器 work area 内，正确处理多显示器、负坐标和 100%/125%/150%/200% DPI。
- [ ] 区分“任务未置顶”和“Overlay 窗口未置顶”：前者由四区域导航解决；后者在 `TopMost=false`、被 Codex 遮挡或用户关闭悬浮反馈时，改在 Agent Controller 伴随窗口同步显示当前菜单、路径和执行结果，不抢夺 Codex 焦点，也不报告虚假的可见成功。
- [ ] 子菜单不得遮住主菜单的当前行或目标 Codex popup；靠近屏幕边缘时自动向可用侧展开，并保持清楚的父子层级与焦点归属。
- [ ] 用截图回归和交互测试覆盖：popup 上方常态定位、四边空间不足、不同 DPI/显示器、长文本、中英文、浅色/深色背景、快速连续输入，以及 popup 移动、关闭或失去所有权后的同步收起。
- [ ] 在可点击原型中验证菜单扫描速度、误选率和层级返回成本，再确定卡片宽度、行高、圆角、阴影、间距与动效 token。

### 设计系统

- [ ] 将颜色、字体、间距、圆角、阴影和动效收敛到单一 token 源。
- [ ] 从 token 源生成 Avalonia Resource，而不是手工维护多份数值。
- [ ] 建立 Button、Card、Status、Keycap、Controller、Overlay 等组件目录。
- [ ] 建立组件 Gallery，展示所有状态、语言、缩放和深浅主题。
- [ ] 支持高对比度、键盘导航、屏幕阅读器和 reduced motion。
- [ ] 中英文长文本和动态系统字体进入布局验收。

### Avalonia 技术验证

- [ ] 建立最小 Windows/macOS 双平台 shell。
- [ ] 验证透明置顶 Overlay、点击穿透、多显示器、DPI 和全屏应用行为。
- [ ] 验证平台原生菜单、托盘/Menu Bar、单实例和后台启动。
- [ ] 决定最低 Windows/macOS 版本并写入支持矩阵。
- [ ] Windows 先完成一个真实 Live Control 垂直切片，再迁移全部页面。

### Feature 组织

- [ ] 每个 Feature 包含 View、ViewModel、State、Commands 和本地测试。
- [ ] Desktop 只使用 Application facade，不直接引用 Win32/UIA/Micro。
- [ ] Overlay 与主窗口共享 presentation model，不复制业务状态机。
- [ ] 建立路由和导航状态，替代 `MainWindow` 手动切换页面。

### WPF 退役

- [ ] 建立 Windows 功能等价矩阵和逐项验收证据。
- [ ] 新客户端达到等价前，WPF 只接收高优先级维护修复。
- [ ] 完成设置迁移、并行安装和回滚方案后再移除旧客户端。

## 完成门槛

- Windows 新客户端完成当前核心动作且通过真实手柄验收。
- macOS shell 可运行，平台服务可替换而无需修改 Domain/Application。
- 主要页面具备截图回归、键盘导航和中英文验收。
- UI 中不再存在业务执行器选择或协议状态推断。
