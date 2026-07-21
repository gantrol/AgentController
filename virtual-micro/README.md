# Codex Micro Surface and Virtual HID

[简体中文](./README.zh-CN.md)

The Windows device support and hostable Micro surface used by Agent Controller:

- `CodexMicroVhfUm.dll` is a UMDF2 HID source driver built on Microsoft's
  official Virtual HID Framework (VHF). The system `Vhf.sys` enumerates
  `VID 303A / PID 8360 / UsagePage FF00 / Usage 01 / ReportId 06`.
- The same UMDF2 source creates a second restricted VHF keyboard child
  (`VID 303A / PID 8361`) for the Full access confirmation dialog. Its control
  contract permits only Tab, Shift+Tab, and Enter; it cannot inject text or
  arbitrary keys.
- `AgentController.exe` hosts the transparent WPF surface. The surface and the
  physical controller use separate logical client IDs and held-input leases,
  while one process-wide Broker child owns the HID and output stream.
- The surface renders only the keypad body and supports uniform resizing,
  dragging, Always on Top, and non-activating pointer input. It is opened from
  Agent Controller's title bar or tray; closing it hides it.
- The window opens at 75% of the 590 x 610 design surface and can be resized
  from approximately 60% upward.
- Agent Controller's existing single-instance lifetime keeps exactly one
  pointer hook owner and Broker output consumer alive. There is no standalone
  Micro executable, mutex, or second notification-area icon.
- The surface accepts pointer input without activating itself. Use the mouse
  wheel or a vertical drag on the white encoder to move through Codex options;
  click once to open or confirm. Right-clicking the white encoder does nothing.
- A compact live capsule beside the encoder mirrors the focused Codex item as
  `position / count · label`, including model options and the Full access
  confirmation buttons. Accessibility is read-only; every action remains HID.
- Encoder input never waits for accessibility feedback. Wheel and drag packets
  are reduced to a bounded net intent (at most three pending detents), opposite
  motion cancels queued motion, and stale history is discarded before a press;
  intents older than 180 ms and backlog accumulated during a stalled send are
  discarded. Accessibility remains an asynchronous, read-only status display.
- Hovering an Agent key shows `project › task title` when Codex's local recent
  task index can be matched to the default `recent` slot source. This UI-only observer reads
  `~/.codex/session_index.jsonl` and `.codex-global-state.json`; VHF lighting
  remains the independent source of slot state and neither file is modified.
  Non-recent sources stay generically labelled unless their mapping is locally provable.
- The lower-left black knob is the only Codex Micro settings entry. Its left
  click sends the real 650 ms `ENC` hold used by Codex to navigate to
  `/settings/codex-micro`; its right click reconnects the virtual HID.
- Hover cards show each control's title, current mapping, gesture, or live state.
- The six action keys observe `desktop.codex-micro-layout` in
  `~/.codex/config.toml` read-only, so Codex mapping changes update icons and
  help text automatically.
- Codex artwork comes from the installed Codex desktop resources instead of
  shipping duplicated brand bitmaps.

The complete visual, driver, interaction, and acceptance specification is in
[`DESIGN.zh-CN.md`](./DESIGN.zh-CN.md).

## Legacy standalone release

The historical [`v1.0.0` release](../../../releases/tag/codex-micro-v1.0.0)
included a standalone portable simulator. Current source no longer builds or
ships that executable; the surface is part of Agent Controller. The separate
unsigned developer driver package remains useful, but must be signed locally
before installation. See [`UNSIGNED-DRIVER.md`](./UNSIGNED-DRIVER.md).

## Unsigned developer driver package

`CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip` contains the prebuilt
UMDF2 DLL, INF, unsigned catalog, native PnP installer, source needed for audit,
and the local signing/install script. It contains no certificate or private
key and is not installable as a production driver in its downloaded state.

Use this package when you want to audit and locally re-sign the exact binaries
without compiling C/C++. See the [unsigned package guide](./UNSIGNED-DRIVER.md).

## Build locally

The Git repository intentionally stores driver source only. Generated `.dll`,
`.sys`, `.cat`, `.cer`, and installer binaries remain ignored and must not be
committed back to the repository. The separately attached Release asset is the
only prebuilt developer-driver distribution.

Prerequisites:

- Windows 11 x64 is the tested build host;
- .NET SDK 10 for Agent Controller, the hosted surface, and tests;
- the reproducible driver route is Visual Studio/Build Tools 2022 with
  **Desktop development with C++**, MSVC v143 x64/x86 build tools, x64/x86
  Spectre-mitigated libraries, Windows SDK `10.0.26100.0`, and MSBuild;
- internet access for the first restore of pinned NuGet package
  `Microsoft.Windows.WDK.x64` `10.0.26100.6584`;
- an elevated PowerShell only for the installation step.

Visual Studio 2026 can drive the build when the v143 toolset and matching
26100 SDK components are also installed. The project intentionally pins the
26100 toolchain instead of silently moving to a newer WDK.

From the repository root, build Agent Controller and its hosted surface first:

```powershell
dotnet restore .\AgentController.sln
dotnet build .\app\AgentController.csproj -c Release --no-restore
```

Then locate the installed 64-bit MSBuild and compile both the UMDF2 source
driver and its native installer:

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
  -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) { throw 'Visual Studio MSBuild was not found.' }

& $msbuild '.\virtual-micro\driver\CodexMicroVhfUm\CodexMicroVhfUm.vcxproj' `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 /m

& $msbuild '.\virtual-micro\tools\CodexMicro.DriverInstaller.Native\CodexMicro.DriverInstaller.Native.vcxproj' `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 /m
```

Main outputs:

- Hosted surface: `src/AgentController.MicroSurface.Wpf/bin/Release/net10.0-windows10.0.19041.0/`
- Microsoft UMDF2 VHF driver package: `driver/CodexMicroVhfUm/x64/Release/`
- Native installer: `tools/CodexMicro.DriverInstaller.Native/bin/Release/`

## Install the virtual HID

Exit AgentController, then run from an ordinary PowerShell:

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

The script opens UAC itself and completes local signing, installation/update,
and the health check. The driver does not check Codex or AgentController
versions; its INF version is only for Windows driver-package updates. See the
[short installation guide](../docs/CodexMicroSimulator-installation.md) for
verification commands and [`UNSIGNED-DRIVER.md`](./UNSIGNED-DRIVER.md) for
build and certificate details.

## Verify

```powershell
dotnet test .\virtual-micro\tests\CodexMicro.Protocol.Tests\CodexMicro.Protocol.Tests.csproj -c Release
dotnet test .\virtual-micro\tests\CodexMicro.Desktop.Tests\CodexMicro.Desktop.Tests.csproj -c Release
```

Desktop tests cover the Codex TOML layout, unknown-key fallback, off-screen
keycap rendering, square geometry, silkscreen safe areas, borderless resizing,
joystick geometry, encoder gestures, and repeated encoder animation.
They also cover selection-label formatting and the restricted VHF keyboard
wire contract.

Set `CODEX_MICRO_PREVIEW_PATH` to an absolute PNG path before running the
desktop tests to produce an off-screen visual preview.
