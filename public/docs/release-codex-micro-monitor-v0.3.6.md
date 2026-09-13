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

- The monitor page adds model, reasoning-effort, and quota controls while keeping the sixteen task keys and shared task status.
- Shared tasks move between the two pages by task identity, with coordinated keycap and halo motion; page switching releases transient input capture safely.
- Quick controls keep verified values only, honor declared external-Harness capabilities, and protect model and effort writes with current-task checks.
- Quota values show available windows and stale-state indicators without treating missing data as an exhausted balance.

## Download

Download `Codex-Micro-Monitor-v0.3.6-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Verification

- Windows x64 Release publish completed without compiler warnings or errors.
- The executable and archive version are `0.3.6`; the file version is `0.3.6.0`.
- Package size: 7.23 MiB.
- SHA-256: `47182794826c6492417f1cba4732e5cc11e28029766c6537198aff5e5bfa51da`.
- The Monitor ZIP contains no driver or signing material.
- Interactive desktop, UIA, browser, and physical-controller checks were not run.

## 中文说明

- Monitor 页面增加模型、思考档位和额度控制，同时保留 16 个任务键及共享任务状态。
- 两页按任务身份移动共有任务，键帽和光晕同步过渡；切页会安全释放临时输入捕获。
- 快捷控制只显示已确认的值，遵循外部 Harness 声明的能力，并在模型与思考写入前复查当前任务。
- 额度显示实际可用窗口和旧读数标记，不把缺失数据当作耗尽额度。
