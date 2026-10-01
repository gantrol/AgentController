## Highlights

- Departing task keys retain their previous key and status-light appearance, then shrink, slide down, and fade out as the roster updates.
- Agent keys open the chat shown in the displayed roster, avoiding mismatches with native AGxx slot order. Tapping the current chat preserves its selection, including in double-tap mode.
- Restores quota hover details with remaining percentages, reset times, available usage-limit resets, and the last update time.

## Visual changes

![mint-keyglow-comparison](https://raw.githubusercontent.com/gantrol/AgentController/codex-micro-monitor-v0.3.10/virtual-micro/Assets/LightingStudies/mint-keyglow-comparison.png)

![mint-keyglow-center-detail](https://raw.githubusercontent.com/gantrol/AgentController/codex-micro-monitor-v0.3.10/virtual-micro/Assets/LightingStudies/mint-keyglow-center-detail.png)

## Download

Download `Codex-Micro-Monitor-v0.3.10-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Known issue / 驱动要求

> [!IMPORTANT]
> 完整 Micro 功能需要单独安装虚拟 HID 驱动。Monitor ZIP 不包含也不会安装驱动。
>
> Download the [driver package](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip) and its [SHA-256 file](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip.sha256). Follow the [English driver guide](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.md) or [中文驱动说明](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.zh-CN.md), close Codex plus Agent Controller, run `./Install-CodexMicroDriver.ps1`, and continue only after the installer reports `Ready`. Exit code `3010` requires a Windows restart.
>
> This is an unsigned developer driver. Keep Windows driver-signature enforcement enabled. An existing `Ready` installation does not need reinstalling; the driver is unchanged in this release.

## 中文说明

- 离开的任务键保留原键位和状态灯光，再缩小、下移并淡出。
- Agent 键按当前显示的会话列表打开对应会话，避免原生 AGxx 槽位顺序不一致；点击当前会话会保留选中状态，双击模式的首次点击也适用。
- 恢复额度悬停详情，显示剩余额度、重置时间、可用额度重置次数和最近更新时间。

## Verification

- Windows x64 Release packaging completed without warnings or errors.
- Executable ProductVersion: `0.3.10`; FileVersion: `0.3.10.0`.
- Package size: 1,312,782 bytes (1.25 MiB).
- SHA-256: `45f17e3f7fb7fccc2bc26bb21ab197efe17ccd3d504d19e94e4311b17ffe8f83`.
- Archive contents, executable identity, version metadata, and checksum were verified. The archive contains no driver, signing, or debug files.
- No test suite, UI automation, or manual UI test was run during release preparation.
