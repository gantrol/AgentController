## Highlights

- The compact quota knob uses seven-segment digits. Plus shows five-hour and weekly remaining percentages diagonally; Pro shows the weekly percentage alone.
- Hover shows the model version and family, with the two rings indicating reasoning effort. Model switching keeps its rotating activity indicator.
- Fixes model and reasoning-effort changes in existing Codex chats by supporting the v2 settings protocol and its `applied` result. Older Codex versions retain the v1 fallback when they explicitly reject v2.
- Pending questions now appear in task lighting, including while a task is still thinking.
- Adds a native WPF Storybook for maintainers to inspect quota, model, effort, loading, and scaling states with mock data. Run `npm run storybook:micro` from the source checkout.

## Download

Download `Codex-Micro-Monitor-v0.3.8-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

The Monitor archive does not include the virtual HID driver. First-time users can follow the [English driver guide](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.md) or [中文驱动说明](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.zh-CN.md) and download the [driver package](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip). An existing driver installation in the `Ready` state does not need reinstalling.

## 中文说明

- 额度旋钮使用七段数码字；Plus 的五小时、周剩余额度斜向排列，Pro 单独显示周额度。
- 悬停时分行显示模型版本与名称，双环表示思考强度；切换模型时保留转圈反馈。
- 适配 Codex v2 任务设置协议和 `applied` 回执，修复已有会话的模型、强度切换；旧版明确拒绝 v2 时回退至 v1。
- 待回答的问题会反映到任务灯光，即使任务仍处于思考状态。
- 维护者可通过原生 WPF Storybook 查看模拟额度、模型、强度、加载和缩放状态。

## Verification

- Windows x64 Release packaging and the native Storybook Release build completed without warnings or errors.
- Executable ProductVersion: `0.3.8`; FileVersion: `0.3.8.0`.
- Package size: 1,322,046 bytes (1.26 MiB).
- SHA-256: `4c3dc41bc9682bd05b7b4e3b54b97ce3f4897bce825f470a2d36a0864a4877cb`.
- Archive contents, executable identity, version metadata, and checksum were verified. The archive includes the CsWinRT license and contains no driver, signing, or debug files.
- No test suite, UI automation, or manual UI test was run during release preparation.
