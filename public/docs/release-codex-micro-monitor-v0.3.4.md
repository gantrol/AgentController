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

- Settings and quick model switching now follow the current Codex model catalog instead of a fixed three-model list.
- Model-specific reasoning efforts are loaded dynamically, including models and effort levels added by newer Codex builds.
- Stale or unavailable catalog data and uncertain renderer mutations fail closed and show an unknown state instead of reporting a stale model.

## Download

Download `Codex-Micro-Monitor-v0.3.4-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Verification

- Windows x64 Release publish completed without compiler warnings or errors.
- Executable ProductVersion: `0.3.4`; FileVersion: `0.3.4.0`.
- Package size: 7.51 MiB. SHA-256 is provided in the accompanying `.sha256` file.
- The Monitor ZIP contains no driver or signing material.

## 中文说明

- 设置页和快捷模型切换改为跟随 Codex 当前模型目录，不再固定只支持三个模型。
- 推理强度按模型动态读取，兼容新版 Codex 增加的模型和档位。
- 模型目录过期/不可用或渲染器变更结果不确定时会安全失败并显示未知状态，不再继续显示可能过期的模型。
