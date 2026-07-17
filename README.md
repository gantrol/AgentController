# Agent Controller

[![README in English](https://img.shields.io/badge/README-English-blue.svg)](README.md)
[![简体中文说明](https://img.shields.io/badge/README-简体中文-red.svg)](README.zh-CN.md)

![version](https://img.shields.io/badge/version-0.7-blue) ![platform](https://img.shields.io/badge/platform-Windows-lightgrey)

> Turn an XInput gamepad into a handheld control surface for the Codex desktop app: browse tasks, dictate and send prompts, adjust model settings, and act on running turns without reaching for the keyboard.

---

Codex Micro sold out quickly. It is a tiny keyboard made specifically for Codex, and perhaps you wanted one. But consider the evidence:

- Codex Micro has one dial and one stick; a gamepad has two sticks.
- Codex Micro has twelve keys; even a gamepad without rear paddles has more controls.
- Codex Micro is not exactly an ergonomic masterpiece.
- Its price plus shipping can buy several gamepads.
- It has to be shipped to you.
- Most importantly, it cannot play games.

The gamepad wins. QED. (To be fair, very few controllers can compete with six gloriously distracting color-changing lights.)

> One catch: you still need a microphone for voice input. Neither device usually records audio by itself.

That led to another line of thought:

- Codex Micro provides an SDK for integrations;
- Codex can write code, build models, and respond to shortcuts;
- many gamepads can also be programmed, remapped, and modeled.

So there could be software that lets a gamepad stand in for Codex Micro.

> Why should your next keyboard have to be a keyboard?

I directed Codex to build a prototype. Two hours later it worked; another day went into refining the interaction and wrestling with the Codex interface. The result is Agent Controller.

- Press **Menu** (☰ on an Xbox controller, also called Start or `+` on some gamepads) to wake or foreground Codex when needed.
- Use the **left stick** to walk the task tree: up/down moves between siblings, right enters a project, and left returns to the parent level. Press **A** to open a task. Click **L3** to cycle between pinned tasks, pinned projects, projects, and projectless tasks.
- Use the **right stick** for the current model controls. Simple mode adjusts Power with left/right and selects Standard/Fast with up/down; tap **R3** to open the official model list and choose models such as 5.6 Sol Max. Advanced mode—which is still rough in the current Codex UI—switches between Model, Effort, and Speed with left/right, then changes the selected option with up/down.
- Hold **LT** to dictate and release it to stop.
- Press **X** to send.
- To clear the composer, press **Y**, then **A** twice to confirm.
- To cancel an active turn, hold **B** for three seconds and wait for the on-screen countdown. A short press closes menus or undoes recent navigation when applicable.
- Press D-pad up/down to move to the previous/next user message. Hold up for four seconds to jump to the top, or down for three seconds to jump to the bottom.
- To start a new task, press **Y**, then D-pad up.

That is enough for controller-first vibe coding.

The first public version was tested with an 8BitDo Ultimate 2, an Xbox Series controller, and a Flydigi Vader 4 Pro. A community report also confirmed that an inexpensive GameSir controller connected without trouble. Other XInput-compatible controllers should work, but have not all been validated end to end.

And the six Agent keys from Codex Micro? Hold **LB**, then use the four D-pad directions, View (⧉), or Menu (☰) to choose one of the six visible Agent slots.

> ⚠️ **Security notice — read before use**
>
> This experimental v0.7 prototype was produced in one day with **Codex using GPT-5.6 Sol** and has not received an independent human code or security audit. A Codex update can change shortcuts or the accessibility tree, causing UI Automation to fail or perform the wrong action. The binary is unsigned. Review the source, test only with non-critical tasks, and use it entirely at your own risk. What the app does on your machine:
>
> - sends keyboard shortcuts and UI Automation commands to the **Codex window**; controller input is normally gated to Codex being in the foreground, and turning the Bridge off blocks controller actions;
> - reads local Codex task data under `~/.codex`; if fallback bindings are enabled, it can append F17/F18/F20/F22 bindings to Codex's keybindings file;
> - writes its own settings under `%LOCALAPPDATA%`;
> - can register itself to start with Windows (off by default);
> - makes no network requests; its only web-related action is opening vendor or Codex links in your browser.
>
> Agent Controller is an independent experiment and is not affiliated with, authorized by, or endorsed by OpenAI, Codex, or Work Louder.

### Requirements

- Windows 10 (build 19041+) or Windows 11
- The Codex desktop app
- An XInput-compatible controller
- A microphone if you want to use push-to-talk dictation

The tested controllers are the 8BitDo Ultimate 2, Xbox Series controller, and Flydigi Vader 4 Pro. Compatibility with other XInput devices depends on their XInput implementation and still needs physical validation.

### Install from Releases

1. Download the latest zip from [Releases](../../releases).
2. Extract it anywhere and run `AgentController.exe`.
3. Because the binary is unsigned, Windows SmartScreen may warn you. Choose **More info → Run anyway** only after reviewing the security notice above; alternatively, build from source.
4. Connect the controller in XInput mode, launch Codex, and make sure the Bridge is enabled. When connected, the Device page shows the controller name and a localized **Live input** / **实时输入** badge.

The v0.7 Windows package is self-contained and does not require a separate .NET runtime.

### Control reference

#### Base controls

| Input | Action |
| --- | --- |
| Menu | Wake and foreground Codex when needed. If Codex is already in front, controller input arms after the connected pad returns to neutral. |
| Left stick ↑ / ↓ | Move through Agent Controller's stable sidebar directory without opening an item. |
| Left stick or D-pad ← / → | Leave / enter a project directory. |
| L3 | Cycle roots: pinned tasks → pinned projects → projects → projectless tasks. |
| A | Open the focused task. Projects are entered with right. |
| X | Send the current composer text. The fallback uses the configured submit binding, never Enter. |
| B | Close a menu or undo recent navigation when applicable; otherwise hold for three seconds to cancel the active turn. Releasing early stops the countdown. |
| Y | Open the action panel. |
| D-pad ↑ / ↓ | Previous / next user message; hold ↑ for four seconds to jump to the top or ↓ for three seconds to jump to the bottom. |
| Right stick (Simple) | ← / → steps the live Power control; ↑ selects Standard; ↓ selects Fast. |
| Right stick (Advanced) | ← / → selects Model, Effort, or Speed; ↑ / ↓ chooses an option actually exposed by the current account and model. |
| R3 tap / hold | In Simple mode, tap to open the official model list, use directions to select, A to confirm, and B/R3 to close. Advanced mode opens the full settings menu. Hold for 500 ms to open Agent Controller settings. |
| LB / RB tap | Open the previous / next available task. |
| LT hold | Start push-to-talk dictation; release to finish. |

#### Y action panel

| Input after Y | Action |
| --- | --- |
| D-pad ↑ | New task |
| D-pad → / ← | Codex history forward / back |
| D-pad ↓ | Show or hide the Codex sidebar |
| A, then A again | Clear the composer after confirmation |
| X | Project context: enter the owning project, or toggle all/pinned within a project |
| B or Y | Close the panel |

#### Hold layers

| Layer | Inputs |
| --- | --- |
| Hold LB — Agent | D-pad ↑ / → / ↓ / ← selects Agent slots 1–4; View selects slot 5; Menu selects slot 6; B cancels the layer. |
| Hold RB — Command | Y toggles Fast; A approves; B declines; X forks; View is push-to-talk; Menu dispatches through the current Send, Steer, or Queue control. |
| Hold RT — running turn | X explicitly Steers; Y explicitly Queues; hold B for three seconds to Stop the current turn; A Forks. Releasing B early aborts the countdown; actions fail safely if Codex does not expose the matching control. |

Holding a right-stick direction builds momentum over about two seconds. The first step is immediate, repeat speed then ramps smoothly, and a deeper tilt allows a higher final rate.

If the current selection exposes Speed but no Simple Power control—as Sol Max currently does—Agent Controller asks whether to switch modes. Press **A** for Advanced or **B** to remain in Simple; Standard/Fast stays available either way. After you decline, the same model/effort selection is not prompted again until the selection changes.

The interface supports Simplified Chinese, English, or the Windows display language.

For implementation status and edge cases, see the [v0.7 controller command reference](public/docs/controller-command-reference-v0.7.md) and [v0.7 release notes](public/docs/release-v0.7.md) (both currently in Chinese).

### Known limitations

- Most actions depend on Codex's current shortcuts and accessibility tree. A Codex UI update can break them.
- The Simple model list uses the official command shortcut; conflicts are blocked. Restart Codex once if it does not hot-load a newly written binding.
- Unit tests and a successful Release build do not replace physical end-to-end testing against the current Codex app, account, and model options.
- Agent slots currently use the first six tasks in the live snapshot; Agent and Command slots are not yet user-configurable.
- Hands-free double-pull dictation, the base View action, Plan-mode controller routing, and a virtual HID bridge are not included in v0.7.

### Beyond Codex

Only Codex is supported today, but the controller, capability, and Agent-target layers are intended to leave room for additional adapters. If there is enough demand, Agent Controller could expand to command-line workflows and other coding agents such as Claude Code.

That work would require a dedicated adapter with its own task discovery, command execution, state detection, and safety checks. Current Codex compatibility should not be taken as compatibility with those tools today.

### Build from source

Install the .NET 9 SDK, then run:

```powershell
dotnet build app/AgentController.csproj -c Release
dotnet test app.Tests/AgentController.Tests.csproj -c Release
./scripts/package-release.ps1
```

Build output is written to `app/bin/Release/net9.0-windows10.0.19041.0/`. The packaging script creates a self-contained Windows x64 zip and SHA-256 checksum under `dist/`.

### If you want to modify the source

In principle, emulating Micro's interaction protocol would be faster and less error-prone. But GPT-5.6 Sol kept refusing, arguing that it would be unstable and that UI Automation was the better approach. Its way of making the UI “stable” was to add 700–1,400 ms of latency—not something a human can tolerate as an interaction. To save time, I let it carry on that way at first.

This morning I finally lost patience and called it out, because right-stick model adjustment had already taken far too long. Roughly: “You've gotten this wrong more than five times, and you're still insisting UIA is better??? If you'd used the Micro protocol, this would have been finished ages ago—why are you still arguing?” Then it finally started emulating Micro.

### Repository layout

Key paths in the repository are:

- `app/` — the Windows WPF application and source of truth for runtime behavior;
- `app.Tests/` — regression tests for controller input, localization, navigation, bridge safety, and Codex integration policies;
- `scripts/` — reproducible Release packaging;
- `docs/` — interaction specifications and active design/consultation notes;
- `public/docs/` — user-facing command references, release notes, and experimental plans;
- `todo.md` — roadmap and validation notes.

### Credits

Controller artwork is derived from CREATRBOI's "White XBOX Controller" model. License and attribution files ship with the app under `THIRD-PARTY/`.
