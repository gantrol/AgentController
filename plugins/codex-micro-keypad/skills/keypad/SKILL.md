---
name: keypad
description: Show a standalone Codex Micro keypad or control Codex chats through native software interfaces without a virtual HID driver. Use for 小键盘, model/reasoning controls, sending a requested message, stopping a turn, and replying to a specific approval.
---

Call `get_keypad_capabilities` before presenting supported operations. Call `show_keypad` to display the original Codex Micro WPF keypad with its existing keycaps, rotary control, joystick, and tray. This edition has one window with control and monitor panels. DeepSeek, external Harnesses, Qwen3 ASR, and local voice-service startup are removed. Do not replace the keypad with a new dashboard or form. Use `list_keypad_threads` to resolve the intended chat and `get_keypad_state` to inspect its current model, turn, and pending approvals. Every MCP chat control requires an explicit thread ID; do not substitute the most recent chat for an ambiguous target.

The software adapter supports chat selection, new drafts, forks, Fast, single command/file approvals, and the reasoning dial and configured quick-model pair on existing chats. Message submission with explicit text and exact-turn interruption are MCP tools. Native composer submit, menus, scrolling, voice, default joystick navigation, blank-draft model changes, and arbitrary UI commands are unavailable. Unsupported physical controls return NotSent. Do not describe this version as complete Micro feature parity. Compilation does not establish live UI behavior.

The window reads native Windows accessibility selection metadata and matches the selected sidebar title to a unique local thread ID. A hidden sidebar or duplicate title leaves the target unavailable. It rechecks the selection before a mutation. Question answers are observed over IPC; skipping a visible question is observed through a read-only accessibility event subscription. These readers do not simulate input or require a virtual HID driver.

Use `get_keypad_models` before choosing a model or reasoning effort. Changes affect the next turn; preserve other thread settings and permissions. The backend reuses the current desktop owner for live actions. It never sends HID, keyboard, mouse, UIA, or browser events.

Opening the window or installing the plugin does not authorize sending messages, forking chats, stopping a turn, or approving commands. Perform those actions only when requested. Before an approval, read the current request and pass its exact request ID and the user's decision. Never automatically approve an unknown request, a batch, or a future request. Before stopping, pass the exact active turn ID.

Do not automatically retry mutations after a timeout or disconnection; report the unknown outcome and read the current state. A protocol mismatch requires an update, not input simulation. The standalone window and MCP tools share the same control implementation, but each has an explicit target.
