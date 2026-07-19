# Codex Micro Desktop Simulator

[简体中文](./README.zh-CN.md)

An independent Windows desktop simulator for Codex Micro:

- `CodexMicroVhfUm.dll` is a UMDF2 HID source driver built on Microsoft's
  official Virtual HID Framework (VHF). The system `Vhf.sys` enumerates
  `VID 303A / PID 8360 / UsagePage FF00 / Usage 01 / ReportId 06`.
- The same UMDF2 source creates a second restricted VHF keyboard child
  (`VID 303A / PID 8361`) for the Full access confirmation dialog. Its control
  contract permits only Tab, Shift+Tab, and Enter; it cannot inject text or
  arbitrary keys.
- `CodexMicroSimulator.exe` connects directly to that HID, sends key, encoder,
  and joystick reports to Codex, and receives Agent lighting output.
- The transparent WPF window renders only the keypad body and supports uniform
  resizing, dragging, the taskbar, Always on Top, and a notification-area menu.
- The window opens at 75% of the 590 x 610 design surface and can be resized
  from approximately 60% upward.
- A named single-instance guard keeps exactly one global pointer hook and one
  driver-output consumer alive, preventing duplicate encoder steps and split
  Agent-light snapshots.
- The app and notification-area entry use an original rounded-square keypad icon
  with a physical knob in its upper-left corner, not an OpenAI or Codex app icon.
- The simulator accepts pointer input without activating itself. Use the mouse
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

## Portable desktop app

The [`v1.0.0` release](../../../releases/tag/codex-micro-v1.0.0) includes a
self-contained Windows x64 portable app and a separate unsigned developer
driver package. The app does not require a separate .NET runtime and does not
import a self-signed root certificate. The unsigned driver package saves the
C/C++ compilation step, but must be signed locally before installation. See
[`UNSIGNED-DRIVER.md`](./UNSIGNED-DRIVER.md) for the exact signing order.

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
- .NET SDK 9 for the desktop app and tests;
- the reproducible driver route is Visual Studio/Build Tools 2022 with
  **Desktop development with C++**, MSVC v143 x64/x86 build tools, x64/x86
  Spectre-mitigated libraries, Windows SDK `10.0.26100.0`, and MSBuild;
- internet access for the first restore of pinned NuGet package
  `Microsoft.Windows.WDK.x64` `10.0.26100.6584`;
- an elevated PowerShell only for the installation step.

Visual Studio 2026 can drive the build when the v143 toolset and matching
26100 SDK components are also installed. The project intentionally pins the
26100 toolchain instead of silently moving to a newer WDK.

From the repository root, build the desktop app first:

```powershell
dotnet restore .\virtual-micro\src\CodexMicro.Desktop\CodexMicro.Desktop.csproj
dotnet build .\virtual-micro\src\CodexMicro.Desktop\CodexMicro.Desktop.csproj -c Release --no-restore
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

- Desktop app: `src/CodexMicro.Desktop/bin/Release/net9.0-windows10.0.19041.0/`
- Microsoft UMDF2 VHF driver package: `driver/CodexMicroVhfUm/x64/Release/`
- Native installer: `tools/CodexMicro.DriverInstaller.Native/bin/Release/`

## Install the virtual HID

Run from an elevated PowerShell:

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

The installer creates a local code-signing certificate named
`CN=Codex Micro Simulator Driver`, adds it to the LocalMachine `Root` and
`TrustedPublisher` stores, signs the VHF source driver and catalog, refreshes
only the existing simulator device when its report descriptor changes, creates
`Root\CodexMicroHidUm`, and verifies device health. The installation log is
written to `virtual-micro/driver-install.log`.

The generated certificate and signed build outputs are local development
artifacts. Do not publish the private key or reuse this test certificate as a
production signing identity.

For a downloaded unsigned package, this is also the simplest local secondary
signing path: the script signs the DLL first, regenerates the catalog so it
contains the signed DLL hash, signs the catalog, verifies the PnP device, and
installs it. To sign with an existing development certificate or verify every
step manually, follow [`UNSIGNED-DRIVER.md`](./UNSIGNED-DRIVER.md).

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
