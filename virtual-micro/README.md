# Standalone Codex Micro Keypad and Virtual HID

[简体中文](./README.zh-CN.md)

`CodexMicro.exe` is a Windows keypad that runs independently of Agent
Controller. The release window directly reuses the original WPF
`MainWindow.xaml`, resource dictionary, control templates, and animations. It
is no longer an approximation drawn by a second graphics stack, so its corners,
alpha, shadows, typography, and DPI rasterization use the same rendering path
as the original UI.

Agent Controller neither references nor hosts the keypad UI. Its title-bar and
tray commands only launch a standalone `CodexMicro.exe` from disk, or open the
Releases page when it is absent. The two products share only the current-user
Micro Broker protocol and HID driver, so either can run alone and they can
coexist.

The release is a framework-dependent single-file app. The measured
`CodexMicro.exe` is about `24.9 MiB` and the zip is about `6.4 MiB`; packaging
enforces a `15 MiB` zip ceiling. This retains the exact WPF visuals without the
roughly `75 MiB` self-contained WPF bundle. Standalone use requires the
Microsoft .NET 10 Desktop Runtime x64.

## Window and notification area

- Opens at a fixed 75% of the 590 x 610 design surface (`442.5 x 457.5`).
- The client area is transparent; no opaque rectangle surrounds the device.
- Drag empty body space to move the window. App-owned coordinate updates avoid
  the Windows move/size loop.
- There is no system resize frame, maximize box, or caption, and resizing is
  disabled. Dragging to a screen edge cannot invoke Snap layouts, docking, or
  automatic Windows resizing.
- Right-click empty body space to toggle Always on Top or hide the panel.
- Closing the window or pressing Alt+F4 hides it to the Windows notification
  area instead of terminating the process.
- Double-click the notification-area icon to show/hide the keypad. Its menu has
  Show/Hide and Exit commands.
- Start with Windows can be enabled or disabled from the tray menu. When
  enabled, sign-in starts the keypad quietly in the notification area without
  opening its panel.
- Pointer input does not activate the keypad, preserving focus in Codex.

## Interface language

The tray's Language submenu offers Auto (Agent Controller / Windows), Simplified
Chinese, and English. Tray text, the body menu, hover help, status messages, and
errors update immediately. The choice is stored in
`%LOCALAPPDATA%\CodexMicro\settings.json`. Auto first reads Agent Controller's
interface language, then falls back to the Windows UI language when that setting
is also automatic or unavailable.

## Controls

- Six Agent keys send `AG00` through `AG05` and render slot lighting returned
  by Codex.
- Four command keys send `ACT06` through `ACT09`; the Codex key sends `ACT12`.
- Push-to-talk sends `ACT10 down` on press and `ACT10 up` on release.
- The white encoder supports wheel, drag, and click for `ENC_CW`, `ENC_CC`, and
  `ENC`.
- The joystick sends continuous analog input and neutralizes on release.
- The lower-left knob sends the real 650 ms `ENC` hold that asks Codex to open
  its Micro settings.

## Virtual HID

`CodexMicroVhfUm.dll` is a UMDF2 source driver built on Microsoft's Virtual HID
Framework. System `Vhf.sys` enumerates:

- primary device: `VID 303A / PID 8360 / UsagePage FF00 / Usage 01 / ReportId 06`;
- restricted confirmation keyboard: `VID 303A / PID 8361`, limited to Tab,
  Shift+Tab, and Enter.

The keypad never calls the private driver IOCTL directly. One current-user
Broker child owns the driver handle, input sequence, held-input leases, and
output stream. The keypad connects to an existing Agent Controller Broker when
present; otherwise `CodexMicro.exe --micro-broker` starts the same host.

Only an unsigned developer-driver workflow is currently provided. Never turn
off Windows driver-signing enforcement or install an untrusted certificate.
See [`UNSIGNED-DRIVER.md`](./UNSIGNED-DRIVER.md) for build, signing, and install
details.

## Build and package

Use Windows 10/11 x64 and .NET SDK 10. From the repository root:

```powershell
dotnet build .\virtual-micro\src\CodexMicro.DesktopHost\CodexMicro.DesktopHost.csproj -c Release
.\scripts\package-micro.ps1 -Version 1.2.0
```

Outputs:

- single-file executable: `.artifacts/micro-release/1.2.0/publish/CodexMicro.exe`;
- standalone archive: `dist/CodexMicro-Keypad-1.2.0-win-x64.zip`;
- SHA-256: adjacent `.sha256` file.

The packaging script includes only `CodexMicro.exe`, the READMEs, and the
license. It does not copy the Desktop Runtime, debug symbols, or driver. The
driver remains a separately installed security boundary.

## Install the virtual HID

Exit Codex, Agent Controller, and CodexMicro, then run from an ordinary
PowerShell:

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

The script opens UAC itself and performs local signing, installation/update,
and a health check. A final `Ready` means the new driver is loaded; exit code
`3010` means Windows must be restarted first. See the
[installation guide](../docs/CodexMicroSimulator-installation.md).

## Verify

```powershell
dotnet test .\virtual-micro\tests\CodexMicro.Protocol.Tests\CodexMicro.Protocol.Tests.csproj -c Release
dotnet test .\tests\AgentController.MicroBroker.Tests\AgentController.MicroBroker.Tests.csproj -c Release
dotnet test .\virtual-micro\tests\CodexMicro.Desktop.Tests\CodexMicro.Desktop.Tests.csproj -c Release
```

Desktop tests freeze the original XAML's transparent window, fixed size,
`NoResize`, no-activation behavior, and key visual geometry. Broker and protocol
tests freeze shared leases, input batches, and RPC contracts.
