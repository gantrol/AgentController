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

- The second Agent page now mirrors the same recent Codex tasks and status data as the first page, with sixteen task slots and direct task navigation.
- The current task is highlighted consistently on both pages, including a gray selection ring while the task is idle, unread, or has no colored activity light.
- Starting a new Codex conversation clears the previous task selection, so a stale ring cannot remain on the new draft.
- Agent roster refresh, task recovery, unread marking, and Debug Ctrl+C shutdown handling are more resilient.

## Download

Download `Codex-Micro-Monitor-v0.3.5-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Verification

- Windows x64 Release publish completed without compiler warnings or errors.
- The executable and archive version are `0.3.5`; the file version is `0.3.5.0`.
- Package size: 7.52 MiB.
- SHA-256: `46227c603f9fbe6005f81be2f7663dd9620bc0a27e39cb74c7abb3cf41839d19`.
- The Monitor ZIP contains no driver or signing material.

## 中文说明

- 第二个 Agent 页面现在与首屏共用同一份 Codex 最近任务和状态数据，提供 16 个任务槽位，并可直接打开任务。
- 两个页面都会稳定高亮当前任务；任务空闲、未读或没有彩色活动灯时使用灰色光圈。
- 进入新的 Codex 会话会清除旧任务选择，不再把旧光圈带到新草稿。
- Agent 列表刷新、任务恢复、未读标记和 Debug 下 Ctrl+C 退出处理更加可靠。
