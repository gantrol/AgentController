# Agent Controller v0.7

发布日期：2026-07-16

Hotfix 1：2026-07-17（LB 状态、X 提交、B 长按、简易模型选择与管道降噪）

## 定位与风险

v0.7 是实验性 Windows 原型，由 Codex 使用 GPT-5.6 Sol 在一天内完成。
代码与发布包没有经过独立人工代码/安全审计，二进制未签名。Codex 更新可能
改变快捷键或辅助功能树，使 UI Automation 失效或误操作。请先审查源码，
只在非关键任务中试用，并自行承担风险。

本项目是独立实验，与 OpenAI、Codex、Work Louder 没有隶属、授权或背书关系。

## v0.7 交付内容

- Codex 已在前台时，连接手柄回中后自动启用；Menu 只负责需要时唤醒、置前。
- 简易模式直接控制实时 Power、Standard 与 Fast；短按 R3 通过官方
  `composer.openModelPicker` 选择模型（含 5.6 Sol Max），不枚举 UIA 列表；
  Sol Max 没有原生 Power 时仍会明确询问是否改为高级模式；快捷键冲突或写入失败
  会在输入前拦截，不会误进入模型菜单状态。
- 模型选择器是 Compact/Advanced/Model/Effort/Speed 层级菜单；A/Enter 后继续持有
  原生会话，直到 B/R3 显式结束，防止右摇杆在中间层错误降级成 F17/F18。
- 高级模式严格按屏幕从上到下导航，低档在最上；R3 再按一次或 B 退出菜单。
- 右摇杆保持满推时约 2 秒逐渐加速，最终速度同时受摇杆倾斜深度影响。
- RB+Y 使用实时速度控件和界面回读，不盲发 Fast 快捷键。
- X 在兼容的 VHF Broker 可用时优先发送官方默认 Micro `ACT12`；只有确定
  `NotSent` 才进入 `composer.submit` 快捷键降级。可能已经提交但 ACK 丢失时
  禁止双发；只有发送前观察到非空草稿、发送后观察到清空才报成功，未验证发送
  显示“发送未确认”，不再误报元素不支持。
- B 在普通 Base 和 RT Stop 场景都需按住 3 秒，屏幕倒计时结束后才取消当前
  运行；局部菜单、语音、选择和导航撤回仍是短按取消。
- LB Agent 键盘已绑定 rollout 生命周期与官方持久未读集合；精确审批/回应状态
  留给可选 VHF Broker。
- 十字键上下短按切换用户消息，不显示额外成功 popup；长按上 4 秒置顶，
  长按下 3 秒置底。
- Y 动作面板的十字键上新建任务；Plan 手柄入口暂时移除并记入 TODO。
- v0.7 不包含或安装虚拟 HID 驱动；`v.oai.rad` 兼容研究仅保留为未来实验计划。

## 已知限制

- 当前实测设备包括 8BitDo Ultimate 2、Xbox Series 与 Flydigi Vader 4 Pro；
  更多 XInput 手柄仍需真机验收。
- Power、审批、Steer/Queue 等动作仍依赖当前 Codex 辅助功能树；简易模型选择
  已改走官方命令快捷键与原生方向键/Enter/Escape。
- `SendInput` 成功不是 `composer.openModelPicker` 的动作回执。首次新增快捷键后若
  Codex 未热加载需重启一次；选择叶子后也需 B/R3 显式结束本地模型会话，仍需
  当前版本真机冒烟。
- Sol Max 的当前原生菜单只有 Model / Effort / Speed，不提供简易 Power；
  这是能力差异，不应通过 Reset to default 暗中修改用户选择。
- 单元测试和 Release 编译不能替代真实手柄到当前 Codex UI 的端到端测试。

## 发布包

运行 `scripts/package-release.ps1` 会生成自包含的 Windows x64 zip 与 SHA-256
校验文件到 `dist/`。自包含包无需另外安装 .NET Runtime，但仍要求 Windows 10
build 19041 或更新版本、Codex 桌面版与 XInput 兼容手柄。
