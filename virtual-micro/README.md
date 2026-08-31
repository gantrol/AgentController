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

On its first DeepSeek click, the tailored package reuses an existing Harness
when possible. Otherwise it offers Use my existing DSH or Install DSH in
dedicated WSL (recommended). The flow assumes no
drive letter, source checkout, or pre-existing distribution name and never
runs `git clone`. The bundled
[Windows setup guide](./DEEPSEEK-WINDOWS-SETUP.zh-CN.md) is currently in Chinese
and links to upstream official instructions.

Version 0.2.9 pins program-managed installations to DeepSeek Harness
`v0.1.0-rc.8`, checks the actual installed package even after first-run setup,
and offers a health-checked, rollback-safe upgrade from older managed builds.
It also registers `deepseek-v4-flash-vision-exp` with native text/image input.
Images are pasted or dropped in Harness itself; this is independent of the
keypad's text-only voice relay and still requires a backend model that really
accepts images.

The release is a framework-dependent single-file app. The measured
`CodexMicro.exe` is about `24.9 MiB` and the zip is about `6.4 MiB`; packaging
enforces a `15 MiB` zip ceiling. This retains the exact WPF visuals without the
roughly `75 MiB` self-contained WPF bundle. Standalone use requires the
[official Microsoft .NET 10 Desktop Runtime x64 installer](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.10-windows-x64-installer).

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
  Show/Hide, Restart, and Exit commands. Restart first releases voice capture,
  held Micro keys, joystick state, and Broker leases; the replacement process
  waits for the previous single-instance owner to exit before starting.
- Start with Windows can be enabled or disabled from the tray menu. When
  enabled, sign-in starts the keypad and displays its panel immediately.
- In Codex mode, Reverse dial direction is available in Micro software settings.
  It is saved per keypad and does not affect external Harnesses.
- Pointer input does not activate the keypad, preserving focus in Codex.

## Interface language

The tray's Language submenu offers Auto (Agent Controller / Windows), Simplified
Chinese, and English. Tray text, the body menu, hover help, status messages, and
errors update immediately. The choice is stored in
`%LOCALAPPDATA%\CodexMicro\settings.json`. Auto first reads Agent Controller's
interface language, then falls back to the Windows UI language when that setting
is also automatic or unavailable.

## Voice ownership and local Qwen auto-start

Voice belongs entirely to the keypad. The keypad owns microphone capture,
recognition settings, the service process, and remote API keys. The third status
LED is blue only when both the voice provider and a Windows recording device are
available. A healthy Qwen process with no active microphone is reported as a
device error instead of a ready state, and pressing the voice key shows a
persistent connect/enable-microphone instruction. Voice progress and recovery
instructions use the large bottom status area and the Settings-key gauge rather
than a tiny label below the voice key. If an owned local Qwen stream
breaks, the keypad restarts that service and retries the streaming handshake
once; externally managed services are left untouched and receive a manual
restart instruction. The Bridge only
relays a voice-toggle request plus incremental/final text between DeepSeek and the keypad.
The DeepSeek plugin adds exactly one voice button; it does not capture audio,
connect to Qwen, or expose voice settings.

If the user edits the composer during streaming dictation, the Bridge enters a
manual-edit protection mode. Later frames append only newly recognized suffixes;
ASR revisions and cancellation never overwrite or roll back the user's edit.

Starting DeepSeek voice input also verifies the dedicated DeepSeek window is in
the foreground. The keypad first restores an existing window and, if none is
available, runs the normal Harness activation path before opening the microphone.

Open Keypad software settings → Voice input, select Local streaming Qwen, and
choose Use local example. It selects Start with keypad by default; you can
change that to on-demand or detect-only. The portable defaults contain no drive
letter or user directory:

- stream: `ws://127.0.0.1:8765/v1/stream`;
- health: `http://127.0.0.1:8765/health`;
- launcher: `{AppDir}\voice\start-qwen3-asr-stream.ps1`;
- working directory: `{AppDir}\voice`;
- model: `Qwen/Qwen3-ASR-0.6B`;
- one-second recognition chunks by default for lower first-partial latency;
  standalone launcher runs can override this with `-ChunkSeconds`;
- WSL distribution: `Ubuntu` by default, editable to a name from `wsl -l -q`;
- WSL Python: empty by default so the launcher can find the keypad venv, an
  existing local Qwen venv, or finally `python3` dynamically.

Paths accept `{AppDir}`, `{LocalAppData}`, environment variables, and relative
values. They are resolved at runtime, so the profile does not store a
machine-specific absolute install path.

The start modes are Start on first use, Start with keypad, and Detect only.
To auto-start Qwen after Windows sign-in, enable the tray's Start with Windows
option and select Start with keypad here; Windows starts the keypad, and the
keypad then warms the voice service.
Before launching anything, the keypad requires `/health` to report `ready: true`
with `dsh-stream-v1`. It waits for model loading before opening the WebSocket.
On exit it can stop only a process that this keypad started; it never adopts or
terminates a service that was already running. Non-voice profile changes such as
window position or Harness selection do not cancel or duplicate an in-flight
warm-up. The WSL server also holds a non-blocking per-port instance lock, so a
duplicate launch exits before loading another model or reserving more GPU memory.

Qwen3-ASR streaming uses its vLLM backend. One example of preparing the current
WSL user is:

```powershell
wsl -d Ubuntu -- bash -lc 'python3.12 -m venv "$HOME/.local/share/codex-micro/qwen-asr/venv" && source "$HOME/.local/share/codex-micro/qwen-asr/venv/bin/activate" && python -m pip install -U "qwen-asr[vllm]" aiohttp numpy'
```

Change the distribution or Python field in keypad settings when the local
machine differs; no script edit is needed. A first model-ID launch may download
the model and take longer, so the ready timeout defaults to 600 seconds. See
the [official Qwen3-ASR quickstart](https://github.com/QwenLM/Qwen3-ASR#quickstart)
for backend, model, and package requirements. The adapter follows Qwen's
[official streaming example](https://github.com/QwenLM/Qwen3-ASR/blob/main/examples/example_qwen3_asr_vllm_streaming.py).

To validate the local environment without loading the model or starting the
service, run this from the extracted package directory:

```powershell
& ".\voice\start-qwen3-asr-stream.ps1" -Distribution Ubuntu -CheckOnly
```

## Controls

- Six Agent keys send `AG00` through `AG05` and render slot lighting returned
  by Codex.
- Four command keys send `ACT06` through `ACT09`. In Codex mode, the Codex
  key starts the app when needed or brings its main window to the foreground;
  that activation press never submits composer text. Once Codex is already in
  the foreground, the next press sends `ACT12`.
- In Codex mode, push-to-talk sends `ACT10 down` on press and `ACT10 up` on
  release. In an external Harness, the key directly controls keypad-owned audio
  capture and recognition.
- In Codex mode, the white encoder supports normal composer navigation or a
  reasoning-only mode. Reasoning-only rotation changes effort only, while a
  click toggles the configured quick models A/B through the current task's
  semantic settings channel.
- Codex software settings can reverse the dial direction for the current
  keypad; the setting is ignored by external Harnesses.
- In an external Harness, the white encoder is configured independently. Its
  default mode discovers only visible, enabled controls inside the live
  composer (including controls added later by plugins), highlights one in
  blue, and activates it on click. It can instead control reasoning only or
  select recent sessions.
- The joystick sends continuous analog input and neutralizes on release. For
  external Harnesses, its default spatial map is previous/next sidebar session
  on up/down, toggle left sidebar on left, and open details on right.
- The lower-left knob short-click toggles the active Agent's configured quick
  models. In Codex this uses the unique current task and its existing App
  Server without opening a menu or taking focus. A long-press opens the
  official Micro settings, and right-click opens the active Agent's settings.

## Virtual HID

`CodexMicroVhfUm.dll` is a UMDF2 source driver built on Microsoft's Virtual HID
Framework. System `Vhf.sys` enumerates:

- primary device: `VID 303A / PID 8360 / UsagePage FF00 / Usage 01 / ReportId 06`;
- restricted confirmation keyboard: `VID 303A / PID 8361`, limited to Tab,
  Shift+Tab, and Enter. The keypad no longer routes Codex dial input to this
  legacy child; Codex dial input is always sent as native Micro `ENC_*` reports.

The keypad never calls the private driver IOCTL directly. One current-user
Broker child owns the driver handle, input sequence, held-input leases, and
output stream. The keypad connects to an existing Agent Controller Broker when
present; otherwise `CodexMicro.exe --micro-broker` starts the same host. A
graceful disconnect from the last client stops that child immediately, while a
Broker shared with another running client remains available.

Only an unsigned developer-driver workflow is currently provided. Never turn
off Windows driver-signing enforcement or install an untrusted certificate.
See [`UNSIGNED-DRIVER.md`](./UNSIGNED-DRIVER.md) for build, signing, and install
details.

## Build and package

Use Windows 10/11 x64 and .NET SDK 10. From the repository root:

```powershell
dotnet build .\virtual-micro\src\CodexMicro.DesktopHost\CodexMicro.DesktopHost.csproj -c Release
.\scripts\package-micro.ps1 -Version 0.2.9
# Builds the general Codex Micro Monitor package.
.\scripts\package-micro.ps1 -Version 0.2.9 -Preset monitor
# Builds the DeepSeek-tailored bundle with its bridge and online setup entry.
.\scripts\package-micro.ps1 -Version 0.2.9 -Preset deepseek
```

User-facing releases publish one package per product:

- `dist/Codex-Micro-Monitor-v0.2.9-win-x64.zip`, the general Codex package
  without the DeepSeek Bridge or managed WSL setup;

- `dist/Deepseek-Harness-Keypad-v0.2.9-win-x64.zip`, including the Bridge and
  managed setup entry. First-run setup installs the pinned DSH online instead
  of bundling a full WSL root filesystem.

The following are maintainer build and diagnostic outputs, not ordinary user
download choices:

- single-file executable: `.artifacts/micro-release/0.2.9/publish/CodexMicro.exe`;
- standalone archive: `dist/CodexMicro-Keypad-0.2.9-win-x64.zip`;
- SHA-256: adjacent `.sha256` file.
- standalone existing-DSH plugin:
  `dist/Deepseek-Harness-Keypad-Bridge-v0.2.9.zip`, with its own checksum.

The release packages include `CodexMicro.exe`, the READMEs, the license, and the
keypad-side `voice` launcher/adapter. The Monitor package omits the DeepSeek
Bridge and managed WSL setup. Neither package includes a model or Python
environment. The `deepseek` preset builds a
runtime-only Bridge archive for maintainers and embeds the same payload for managed
WSL setup; neither copy contains source, tests, or development dependencies.
It also includes the path-independent installer and explicit first-run choices. It
defaults to DeepSeek and system speech recognition; local Qwen, remote
streaming, and a future offline WSL payload remain optional. Neither preset
copies the Desktop Runtime, debug symbols, or driver. The driver remains a
separately installed security boundary.

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
