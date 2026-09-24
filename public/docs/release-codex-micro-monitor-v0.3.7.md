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

- The selected Agent task now keeps a stronger, consistent status glow on both the control and monitor pages. An idle or unlit selected task uses white light in the key center instead of the former selection ring.
- Agent key wells and the lower crystal plate have a softer, deeper surface treatment.
- The asset exporter can render the control and monitor lighting pages directly from the current WPF layout with `--lighting-pages`.

## Download

Download `Codex-Micro-Monitor-v0.3.7-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Verification

- Windows x64 Release builds of the desktop host and asset exporter completed with 0 warnings and 0 errors.
- Executable ProductVersion: `0.3.7`; FileVersion: `0.3.7.0`.
- Package size: 1,315,503 bytes (1.25 MiB).
- SHA-256: `be941679c666b94b5eb3ac18ef21393f002ae19e17e8a68c05949131996cb54d`.
- The archive contains `THIRD-PARTY/CsWinRT-LICENSE.txt` and no driver or signing material.
- The installed startup executable and desktop copy match the packaged executable by SHA-256. The updated startup executable launched the app and broker. No manual UI test was run.

## 中文说明

- 当前选中的 Agent 任务在控制页和监控页均显示更明显且一致的状态光效；选中空闲或无状态灯的任务时，键帽中心改用白光标识，不再显示原来的选中光圈。
- Agent 键位凹槽与下方水晶面板调整了底色和层次。
- 资源导出工具新增 `--lighting-pages`，可直接从当前 WPF 布局导出控制页和监控页的灯光图。
