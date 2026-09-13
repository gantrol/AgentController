<!-- Codex Micro monitor driver reminder -->
> [!IMPORTANT]
> **Micro driver installation is required for full Codex Micro behavior / 完整 Micro 功能需要单独安装驱动**
>
> The Monitor archive does **not** include or install the virtual HID driver.
>
> 1. Download [CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip) and its [SHA-256 file](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip.sha256).
> 2. Extract the driver package and close Codex plus Agent Controller. Follow the [English driver guide](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.md) or [中文驱动说明](https://github.com/gantrol/AgentController/releases/download/codex-micro-v1.2.0/UNSIGNED-DRIVER.zh-CN.md), then run `./Install-CodexMicroDriver.ps1` from the extracted directory and approve its UAC prompt.
> 3. Continue only after the installer reports `Ready`. Exit code `3010` means Windows must be restarted first. Afterward, run `CodexMicro.exe` as a normal user.
>
> This is an **unsigned developer driver**. Keep Windows driver-signature enforcement enabled and install only the files linked from this repository's Release. The driver is unchanged; an existing `Ready` installation does not need reinstalling.
>
> **应用 ZIP 不包含驱动。** 首次安装请按上面的中文说明完成独立驱动安装，确认状态为 `Ready`；返回 `3010` 时先重启 Windows。该驱动为未签名开发版，请保持驱动签名强制检查开启。已有 `Ready` 驱动无需重装。

## Highlights

- The control and monitor pages now share the model control and `ACT12` key. The monitor page keeps fourteen task slots while the shared controls remain available on both pages.
- Page transitions animate matching task cards by stable task identity and pause conflicting input and refresh updates until the transition completes.
- Reasoning-only mode now supports encoder rotation and settings-knob wheel input for both blank drafts and real Codex tasks. It follows the current model catalog, verifies the resulting effort, and drops stale input when the page, harness, foreground task, or profile changes.
- Ultra plus Full access warnings are observed and handled with cancellation and current-task guards so pending input is not applied to a changed task.
- WPF publishing now embeds the required CsWinRT projection, uses `NAudio.WinMM`, and carries the CsWinRT third-party license in the archive.

## Download

Download `Codex-Micro-Monitor-v0.3.6-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Verification

- Windows x64 Release publish completed without compiler warnings or errors.
- Executable ProductVersion: `0.3.6`; FileVersion: `0.3.6.0`.
- Package size: 1,315,276 bytes (1.25 MiB).
- SHA-256: `4cce6f3912e09c97b756369fafecf8fd0f6f6ed3001f4ddbb7cf59371344dbb7`.
- The archive contains `THIRD-PARTY/CsWinRT-LICENSE.txt` and no driver or signing material.

## 中文说明

- 控制页与监控页现在共用模型控制和 `ACT12` 键；监控页保留 14 个任务槽位，共用控件在两页都可用。
- 页面切换按稳定任务身份匹配并播放卡片动画；动画期间暂停冲突输入和刷新，避免任务映射过期。
- 推理专用模式支持编码器旋转和设置旋钮滚轮，可用于空白草稿和已有 Codex 任务；它跟随当前模型目录，验证最终推理档位，并在页面、Harness、前台任务或配置变化时丢弃过期输入。
- Ultra 与 Full access 警告加入取消和当前任务保护，避免把待处理输入应用到已变化的任务。
- WPF 发布现在嵌入所需的 CsWinRT 投影，改用 `NAudio.WinMM`，并在压缩包中携带 CsWinRT 第三方许可证。
