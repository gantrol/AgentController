## Highlights

- Pending-question monitoring now updates immediately when an answer is accepted and remains consistent across rollout refreshes.
- Existing Codex chats recover their model/effort control connection and use the observed foreground draft selection before applying a change.
- Task keys animate into their new order while the monitor roster changes; page switching cancels in-flight task motion safely.
- Current-session selection uses a mint light treatment while preserving the existing Agent and task status lighting.
- Codex executable discovery now handles versioned Local AppData installs and PATH entries consistently across Agent Controller and Micro.

## Visual changes

![mint-keyglow-comparison](https://raw.githubusercontent.com/gantrol/AgentController/codex-micro-monitor-v0.3.9/virtual-micro/Assets/LightingStudies/mint-keyglow-comparison.png)

![mint-keyglow-center-detail](https://raw.githubusercontent.com/gantrol/AgentController/codex-micro-monitor-v0.3.9/virtual-micro/Assets/LightingStudies/mint-keyglow-center-detail.png)

## Download

Download `Codex-Micro-Monitor-v0.3.9-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Known issue / 驱动要求

> [!IMPORTANT]
> 完整 Micro 功能需要单独安装虚拟 HID 驱动。Monitor ZIP 不包含也不会安装驱动。
>
> Download the [driver package](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip) and its [SHA-256 file](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip.sha256). Follow the [English driver guide](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.md) or [中文驱动说明](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.zh-CN.md), close Codex plus Agent Controller, run `./Install-CodexMicroDriver.ps1`, and continue only after the installer reports `Ready`. Exit code `3010` requires a Windows restart.
>
> This is an unsigned developer driver. Keep Windows driver-signature enforcement enabled. An existing `Ready` installation does not need reinstalling; the driver is unchanged in this release.

## 中文说明

- 待回答问题在收到接受回执后立即从监控状态中消失，并在刷新时保持一致。
- 已有 Codex 会话会恢复模型/强度控制连接，并在应用修改前读取当前前台草稿选择。
- 监控任务键位重排时增加短动画；切页会安全取消尚未完成的任务动画。
- 当前会话使用薄荷色选中光，同时保留 Agent 与任务状态灯光。
- Agent Controller 与 Micro 统一支持版本化 Local AppData 安装和 PATH 中的 Codex 可执行文件。

## Verification

- Windows x64 Release packaging completed without warnings or errors.
- Executable ProductVersion: `0.3.9`; FileVersion: `0.3.9.0`.
- Package size: 1,341,363 bytes (1.28 MiB).
- SHA-256: `4dc4367b56396a81ddb7ff96eadad228abf5c91b86d7da2b3df4251707a2cd3d`.
- Archive contents, executable identity, version metadata, and checksum were verified. The archive contains no driver, signing, or debug files.
- No test suite, UI automation, or manual UI test was run during release preparation.
