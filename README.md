# Agent Controller

[![README in English](https://img.shields.io/badge/README-English-blue.svg)](README.md)
[![简体中文说明](https://img.shields.io/badge/README-简体中文-red.svg)](README.zh-CN.md)

![version](https://img.shields.io/badge/version-0.7-blue) ![platform](https://img.shields.io/badge/platform-Windows-lightgrey)

---

Drive AI coding agents with a game controller. Agent Controller is a Windows desktop app that maps an XInput gamepad (currently tested with the 8BitDo Ultimate 2) onto the Codex desktop app: navigate tasks with the sticks, hold LT to talk, and send with X — plus haptics and an on-screen overlay.

Inspired by Codex Micro, the tiny dedicated keyboard for Codex — but controller control? Much better. Two sticks, a d-pad, rumble, and it already fits your hands.

> ⚠️ **Security notice — read before use**
>
> This experimental v0.7 prototype was produced in one day with **Codex using GPT-5.6 Sol** and has not received an independent human code or security audit. UI Automation and shortcuts can break or perform the wrong action after a Codex update. The binary is unsigned. Review the source, test with non-critical tasks, and use it entirely at your own risk. What the app does on your machine:
>
> - sends keyboard shortcuts and UI Automation commands **to the Codex window** (gated, by default, to when Codex is in the foreground; a connected controller auto-enables after returning to neutral);
> - reads Codex local task data (`~/.codex`) **read-only**;
> - writes its own settings under `%LOCALAPPDATA%`, and can append fallback hotkey bindings (F17/F18/F20/F22) to Codex's keybindings file;
> - optionally registers itself to start with Windows (off by default);
> - makes **no network requests** (the only web touchpoint is opening vendor/Codex links in your browser).

Agent Controller is an independent experiment and is not affiliated with or endorsed by OpenAI, Codex, or Work Louder.

### Requirements

- Windows 10 (build 19041+) or Windows 11
- Codex desktop app installed
- An XInput-compatible controller (8BitDo Ultimate 2 is the tested device; other XInput pads should largely work — Xbox and Flydigi are on the test roadmap)

### Install from Releases

1. Download the latest zip from [Releases](../../releases).
2. Extract anywhere and run `AgentController.exe`.
3. The binary is unsigned, so Windows SmartScreen may warn: **More info → Run anyway** (see the security notice above — build from source if you prefer).
4. Connect your controller in XInput mode and launch Codex. The Device page should show `LIVE`. The v0.7 Windows package is self-contained and does not require a separate .NET runtime.

### Default controls

| Input | Action |
| --- | --- |
| Menu | Wake and foreground Codex when needed; control auto-arms after a connected controller returns to neutral while Codex is already foreground |
| Left stick ↑↓ | Move through Agent Controller's stable sidebar wheel without opening |
| Left stick ← | Return from project tasks; right has no Base action |
| L3 | Cycle roots: pinned tasks → pinned projects → projects → projectless tasks |
| Y | Open the action panel; D-pad ↑ creates a new task |
| D-pad ↑ / ↓ | Previous / next user message; hold ↑ 4 seconds to jump to the top, or ↓ 3 seconds to jump to the bottom |
| Right stick (Simple) | ←→ steps Codex's live Power control; ↑ selects Standard; ↓ selects Fast |
| Right stick (Advanced) | ←→ chooses Model / Effort / Speed; ↑↓ changes an account-provided option |
| R3 tap / hold | Open the matching picker; tap again or press B to close it / open controller settings |
| A | Enter the focused project or open the focused task |
| LT (hold) | Push-to-talk dictation; release to finish |
| X | Send the prompt |
| B | Short press closes menus or undoes recent navigation; hold 3 seconds to cancel the active turn with an on-screen countdown |

Holding a right-stick direction builds momentum over about two seconds: the first detent is immediate, then repeat speed ramps smoothly; deeper tilt permits a higher final speed.

When Simple Power is requested while the live selection is Sol Max, AgentController asks whether to switch to Advanced mode: A switches, B keeps Simple mode, and Standard / Fast remains available either way.

The interface can switch between Simplified Chinese, English, or follow the Windows display language.

### Build from source

```powershell
dotnet build app/AgentController.csproj -c Release
dotnet test app.Tests/AgentController.Tests.csproj -c Release
./scripts/package-release.ps1
```

The build output lands in `app/bin/Release/net9.0-windows10.0.19041.0/`. The packaging script creates a self-contained x64 zip and SHA-256 checksum under `dist/`.

### Repository layout

Going forward this repository only tracks:

- `app/` — the Windows (WPF) application, the single source of truth for behavior;
- `app.Tests/` — regression tests for controller, localization, navigation, and adapter behavior;
- `scripts/` — reproducible release packaging;
- `public/docs/` — current command reference, release notes, and experimental plans;
- `todo.md` — roadmap notes.

### Credits

Controller artwork is derived from CREATRBOI's "White XBOX Controller" model; the license and attribution files ship next to the app (`THIRD-PARTY/` in releases).
