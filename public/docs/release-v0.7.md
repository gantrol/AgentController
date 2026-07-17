# Agent Controller v0.7

发布日期：2026-07-16

## 定位与风险

v0.7 是实验性 Windows 原型，由 Codex 使用 GPT-5.6 Sol 在一天内完成。
代码与发布包没有经过独立人工代码/安全审计，二进制未签名。Codex 更新可能
改变快捷键或辅助功能树，使 UI Automation 失效或误操作。请先审查源码，
只在非关键任务中试用，并自行承担风险。

本项目是独立实验，与 OpenAI、Codex、Work Louder 没有隶属、授权或背书关系。

## v0.7 交付内容

- Codex 已在前台时，连接手柄回中后自动启用；Menu 只负责需要时唤醒、置前。
- 简易模式直接控制实时 Power、Standard 与 Fast；Sol Max 没有原生 Power 时
  明确询问是否改为高级模式。
- 高级模式严格按屏幕从上到下导航，低档在最上；R3 再按一次或 B 退出菜单。
- 右摇杆保持满推时约 2 秒逐渐加速，最终速度同时受摇杆倾斜深度影响。
- RB+Y 使用实时速度控件和界面回读，不盲发 Fast 快捷键。
- X 优先调用 Send/Submit，降级到 `composer.submit` 配置键并验证输入框清空，
  不使用 Enter。
- B 在普通 Base 场景需按住 3 秒，屏幕倒计时结束后才取消当前运行。
- 十字键上下短按切换用户消息，不显示额外成功 popup；长按上 4 秒置顶，
  长按下 3 秒置底。
- Y 动作面板的十字键上新建任务；Plan 手柄入口暂时移除并记入 TODO。
- v0.7 不包含或安装虚拟 HID 驱动；`v.oai.rad` 兼容研究仅保留为未来实验计划。

## 已知限制

- 当前实测设备包括 8BitDo Ultimate 2、Xbox Series 与 Flydigi Vader 4 Pro；
  更多 XInput 手柄仍需真机验收。
- Power、模型菜单、审批、Steer/Queue 等动作依赖当前 Codex 辅助功能树。
- Sol Max 的当前原生菜单只有 Model / Effort / Speed，不提供简易 Power；
  这是能力差异，不应通过 Reset to default 暗中修改用户选择。
- 单元测试和 Release 编译不能替代真实手柄到当前 Codex UI 的端到端测试。

## 发布包

运行 `scripts/package-release.ps1` 会生成自包含的 Windows x64 zip 与 SHA-256
校验文件到 `dist/`。自包含包无需另外安装 .NET Runtime，但仍要求 Windows 10
build 19041 或更新版本、Codex 桌面版与 XInput 兼容手柄。
