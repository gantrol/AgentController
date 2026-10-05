# Codex Micro Monitor

**Keep your Codex chats in view.**

A Codex plugin that shows which chats are running, finished, or waiting for you—all in one floating desktop panel. Click a chat to pick up where you left off.

Inspired by the [Codex Micro hardware keypad](https://learn.chatgpt.com/docs/features/codex-micro). This is an independent project for Windows.

[简体中文](README.zh-CN.md) · [Get started](#get-started) · [Current limitations](#current-limitations)

## What you can do

- **Follow multiple chats.** Switch between the keypad and a monitor view with up to 14 chat slots.
- **Jump to the chat that needs you.** Click a chat key to open it; right-click a bound key to mark it unread.
- **Adjust your current chat.** Toggle Fast and Plan, switch between two configured models, and adjust reasoning effort.
- **Keep it nearby.** Move the panel, pin it on top, or hide it in the Windows tray.

## Get started

Requires Windows 10 (build 19041+) or Windows 11 x64, the signed-in Codex / ChatGPT desktop app, and the [Microsoft .NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

1. Install the plugin, currently listed as **Codex Micro Keypad** in the repository's **Agent Controller** marketplace. Local preview setup is below.
2. Ask Codex: **“Open the Codex Micro keypad.”**
3. Select an existing chat in the panel and wait for its status to sync.

<details>
<summary>Install the local plugin preview</summary>

With .NET SDK 10.0.302 and the Codex CLI installed, run this from the repository root:

```powershell
.\scripts\package-codex-plugin.ps1 -Install
```

The script builds the plugin, registers the local marketplace, and installs `codex-micro-keypad@agent-controller`. Restart Codex after installation. See the [official marketplace documentation](https://developers.openai.com/plugins/build/plugins) for setup details.

</details>

<details>
<summary>Run the standalone app instead</summary>

With the same .NET SDK installed, run this from the repository root:

```powershell
.\scripts\package-micro.ps1 -Version 0.3.13 -Preset monitor
```

Extract `dist/Codex-Micro-Monitor-v0.3.13-win-x64.zip` and run `CodexMicro.exe` with Codex open.

Public [Monitor releases](https://github.com/gantrol/AgentController/releases) through v0.3.10 use the older driver-based integration; this README covers the current software-control preview.

</details>

## Quick controls

| Control | Action |
| --- | --- |
| Chat key | Open the displayed chat. Right-click to mark it unread. |
| Fast key | Toggle Fast for the current chat. |
| Stick up | Toggle Plan with the default assignment. |
| White dial | In **Reasoning only** mode, turn to adjust effort; press to switch quick models. |
| Lower-left dial | Press to switch quick models; right-click to configure models A / B. |
| CODEX key | Bring the Codex window forward. |

Model and reasoning options depend on your account. Use an existing chat for these controls.

Chat lights show **blue** for working, **green** for complete and unread, **amber** for input needed, and **red** for an error. The selected chat has its own highlight. The three small indicators show chat sync, the Codex connection, and the latest action result; a brief green action light means the action was confirmed.

Drag an empty part of the panel to move it. Right-click for the always-on-top option. Closing the window hides it in the tray; choose **Exit** from the tray menu to quit. Language and startup options are also in the tray menu.

## Current limitations

- Sending the composer draft, voice input, composer navigation, chat scrolling, sidebar/history navigation, and skill insertion are not implemented in the driver-free version. Use Codex for these actions. The CODEX key currently only brings the app forward.
- Stop, approval, fork, and Review actions have implementations, but their complete user flows still need verification.
- Blank drafts and chats that cannot be uniquely identified may not support setting changes. Open an existing chat with a distinct title first.
- Compatibility depends on Codex's desktop interfaces and can change after an update.

If the panel will not start, check that **Desktop Runtime x64** is installed. If it cannot connect, confirm Codex is running and signed in, then restart the keypad. Logs are in `%LOCALAPPDATA%\CodexMicro`: `plugin-startup.log` for the plugin and `keypad-startup.log` for the standalone app.

## License

[GNU General Public License v3.0 only (GPL-3.0-only)](https://github.com/gantrol/AgentController/blob/main/LICENSE).
