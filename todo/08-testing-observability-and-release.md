# 08 — 测试、诊断与发布工程

> Status: Planned
> Priority: P0 / Ongoing
> Depends on: All feature tracks

## 目标

建立能区分“协议正确”“平台正确”“真实 Codex 生效”和“用户体验可用”的测试体系，避免用单元测试或 transport ACK 代替端到端证据。

## 待办

### 测试分层

- [ ] Domain：手势、映射、路由、安全和状态聚合纯单元测试。
- [ ] Application：用 fake ports 验证完整 use case 和失败恢复。
- [ ] App Server：按 Codex 版本运行 schema/contract tests。
- [ ] Micro：golden packet、双向重组、指纹和状态机测试。
- [ ] Platform：Windows/macOS 输入、权限、IPC 和生命周期集成测试。
- [ ] Desktop：ViewModel、可达性和截图回归。
- [ ] E2E：真实控制器、真实 Codex build 和干净账户冒烟。

### 测试资产

- [ ] 建立控制器硬件矩阵、Codex 版本矩阵和操作系统矩阵。
- [ ] fixture 不包含用户 prompt、任务正文或凭据。
- [ ] 协议观察、截图和 UIA 快照绑定明确版本与来源。
- [ ] 失败案例进入可复现 regression fixture，不只写在 issue 描述中。

### 基础实机验收（README 基线）

以 [README 中文版](../README.zh-CN.md) 对用户承诺的基础操作为发布基线。每个 release candidate 至少在一个干净账户、当前支持的 Codex build 和真实手柄上逐项执行；高级模型控制可带已知限制发布，但失败现象和证据必须可追踪。

- [ ] 菜单键（Xbox ☰ / Start / `+`）能启动 Codex，或在 Codex 已运行时将它可靠地置于前台；重复按键不创建多余实例。
- [ ] 左摇杆上/下能在同级任务中移动，右进入项目、左退出项目，A 打开当前任务；L3 能依次访问置顶任务、置顶项目、项目和未归项目任务，并实际打开至少一个置顶任务和一个未置顶任务。
- [ ] 右摇杆上/下每次重复只产生一个 `ENC_CW` / `ENC_CC act=2` 档位并遍历 Advanced、Fast、Power 等控件；左/右保持实际屏幕方向且不得产生 ENC；短按 R3 只产生一对 `ENC` down/up，不再进入旧简易/高级状态机。
- [ ] 驱动 `NotSent` 时旋钮才允许走既有降级路径；`Accepted`、`OutcomeUnknown`、`Rejected` 都不得重复注入键盘/UIA，transport ACK 只证明投递，不能冒充界面 readback。
- [ ] 按住 LT 开始录音、松开停止；覆盖短按、正常口述、Codex 失焦和菜单打开场景，确认不会残留录音状态。
- [ ] X 能发送已有输入，并能区分发送成功、仍在输入框和正在运行三种结果。
- [ ] Y 打开动作面板后，连续两次 A 才能清空输入；第一次确认、超时、B 撤回和空输入框都不得误删。
- [ ] 短按 B 在适用场景关闭菜单或撤回最近导航；运行中长按 B 三秒必须显示完整倒计时并取消任务，提前松开不得取消。
- [ ] 十字键上/下能跳到上一轮/下一轮问答；按住上四秒回到顶部，按住下三秒回到底部，短按和长按不得串扰。
- [ ] Y 后按十字键上能新建任务，且不会残留动作面板状态或误触其他 Y 组合动作。
- [ ] 分别验证悬浮 Overlay 可见和 `TopMost=false`/Overlay 被遮挡两种反馈模式；后者必须在 Agent Controller 伴随窗口提供等价的焦点、路径、倒计时和结果反馈。
- [ ] 每项记录前置状态、实际按键序列、期望界面、最终 readback、手柄型号、Codex/应用版本和失败截图或日志；任一 P0/P1 路径失败即阻止 release candidate 晋级。

### 运行时诊断

- [ ] 每个 Action 生成 correlation id、executor、timing、result 和 evidence。
- [ ] 显示 App Server、Micro、Broker、权限和控制器 backend 健康状态。
- [ ] 使用有界 ring buffer，默认不记录敏感内容。
- [ ] 支持一键导出、用户预览和脱敏。
- [ ] 区分 NotSent、AcceptedUnverified、Unknown 和 Failed 的用户文案。

### CI 与发布

- [ ] Windows 和 macOS 分平台构建、测试和打包。
- [ ] PR 必须通过格式、编译、单元、合同和依赖规则测试。
- [ ] Release candidate 必须通过真机冒烟清单，不能只看 CI 绿色。
- [ ] 建立 Stable、Preview、Experimental Native Components 三个发行通道。
- [ ] 发布说明自动包含支持矩阵、已知限制、校验值和回滚方式。

## 完成门槛

- 任一失败能定位到 Input、Binding、Router、Executor、Transport 或 Verification 层。
- 私有协议版本变化会在发布前被合同测试发现。
- 正式发布同时具备自动化证据和真机验收记录。
- 诊断信息足以支持用户排错，但不泄露用户内容。
