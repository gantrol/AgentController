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

- Hover over the lower-left quota ring to see **Usage limit resets**: the number of available resets and each reset's expiration in local time, ordered by expiration.
- Used and expired resets are excluded. Missing or invalid reset data appears as unavailable.
- Reset details refresh with quota data and follow the keypad's English or Chinese language setting.

## Download

Download `Codex-Micro-Monitor-v0.3.3-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Verification

- Windows x64 Release build completed without compiler warnings or errors.
- Executable ProductVersion: `0.3.3`; FileVersion: `0.3.3.0`.
- Package contents and SHA-256 verified; the ZIP contains the matching executable and voice runtime scripts.
- Driver package and installation-guide links verified. No driver or signing material is included in the Monitor ZIP.

## 中文说明

- 鼠标悬停左下角额度圆环，查看可用重置次数，以及按到期时间排序的每次重置；时间使用本机时区。
- 已使用、已过期的重置不计入可用次数；数据缺失或异常时显示暂不可用。
- 重置信息随额度自动刷新，支持中英文。
