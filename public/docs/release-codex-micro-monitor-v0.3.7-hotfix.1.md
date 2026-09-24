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

- Restores the translucent crystal background and the original neutral key surfaces that became opaque and grey-green in v0.3.7.
- Keeps the selected Agent's white center lamp, restores its visible center point, and uses a stronger pure-white halo without a blue tint.
- Fixes Agent-key session switching by opening the exact thread identity captured at click time instead of mixing foreground HID slot routing with background deep-link routing.
- Projects the requested selection briefly and refreshes it as soon as Codex confirms the visible task, preventing stale selection light after a switch.

## Download

Download `Codex-Micro-Monitor-v0.3.7-hotfix.1-win-x64.zip`, verify it with the accompanying `.sha256` file, extract it, and run `CodexMicro.exe`.

Requires Windows x64 and the .NET 10 Desktop Runtime x64.

## Verification

- Windows x64 Release packaging completed with 0 warnings and 0 errors.
- Executable ProductVersion: `0.3.7-hotfix.1`; FileVersion: `0.3.7.0`.
- Package size: 1,315,814 bytes (1.25 MiB).
- SHA-256: `9b3ef20281dca501d6b8217b6818911d57762105dc05c7ef9c424b10a7147c66`.
- The archive contains `THIRD-PARTY/CsWinRT-LICENSE.txt` and no driver or signing material.
- No manual UI test or test suite was run; the release build, archive contents, version metadata, and checksum were verified.

## 中文说明

- 恢复 v0.3.7 中意外变成不透明灰绿色的水晶背景与按键本体。
- 保留当前 Agent 的白色中心灯，恢复清晰中心点，并将外圈改为更强的纯白泛光，不再偏蓝。
- Agent 键统一按点击瞬间捕获的线程身份切换，避免前台走 HID 槽位、后台走线程链接造成错切会话。
- 切换期间短暂投影目标选中态，Codex 确认可见任务后立即收敛，避免白灯滞留在旧会话。
