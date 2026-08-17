# Micro Harness adapters

Codex Micro can route its `CODEX` key to Codex or to a directly integrated
agent Harness. External targets are discovered from JSON manifests in:

```text
%LOCALAPPDATA%\CodexMicro\harnesses\*.json
```

Example:

```json
{
  "id": "another-deepseek-harness",
  "displayName": "Another DeepSeek Harness",
  "description": "Direct Micro adapter",
  "pipeName": "another-deepseek-harness-micro-v1",
  "controlUri": "http://127.0.0.1:3080/__agentcontroller/micro/request",
  "projectPath": "D:\\project\\ai\\another-harness",
  "executable": "node.exe",
  "arguments": "--import tsx/esm apps/cli/src/bin.ts web",
  "workingDirectory": "D:\\project\\ai\\another-harness",
  "autoStart": true,
  "readyTimeoutMilliseconds": 60000
}
```

`id` and `displayName` are required, together with at least one of `pipeName`
or `controlUri`. An unauthenticated `controlUri` must be loopback `http://`;
it is preferred when both transports are present. `projectPath` is optional; if
present, the target is enabled only while that directory exists. IDs are
case-insensitively unique. Codex is always the first built-in target. DeepSeek
Harness is also registered built-in with pipe fallback
`deepseek-harness-micro-v1` and the official default Web port in its control
URL: `http://127.0.0.1:3080/__agentcontroller/micro/request`. The built-in
definition has no project path, executable, distribution name, or automatic
startup until first-run setup succeeds or the user saves those values.

Each external Harness has an in-app adapter card for the pipe/control URL, executable,
arguments, working directory, automatic startup, and readiness timeout. Edits
are stored per Harness in `%LOCALAPPDATA%\CodexMicro\harness-settings.json` and
override manifest defaults. An executable is started directly with
`UseShellExecute=false`; the adapter never invokes a command shell.
Readiness is checked with the side-effect-free `state/read` request before the
original action is dispatched once. The built-in DeepSeek cold-start timeout is
300 seconds (configurable from 1 to 600 seconds); stored 60- and 120-second
defaults migrate automatically. The latest cold-start timeout, final probe
result, launcher PID/state, and elapsed time are retained in
`%LOCALAPPDATA%\CodexMicro\harness-diagnostics.json` and shown on the adapter
settings card.

## Built-in DeepSeek Harness adapter

The matching external Cordis plugin is distributed beside a tailored release
at:

```text
plugins\DeepSeekHarness
```

The source-tree location is `micro-bridge\DeepSeekHarness`, relative to the
repository root; neither form implies a drive letter. Its Web composition
mounts the bridge without modifying Harness source.

Pressing the DeepSeek button first tries the saved endpoint and then the
official 3080 default. An unconfigured target opens the explicit first-run
choice. Existing-Harness mode leaves version, location, and startup under user
control. Managed mode creates the dedicated `CodexMicro-DeepSeek` WSL
distribution, installs the official npm release plus this bridge, selects a
loopback port, and only then persists automatic startup. No extra
AgentController manifest or source checkout is required.

Native Harness Web instances on non-default ports use a port-suffixed pipe name. For
example, port 3098 exposes `deepseek-harness-micro-v1-3098`; register that
value as an external manifest's `pipeName` when the instance should appear as
a separate Micro target.

If a browser page is already connected, activation is delivered to the best
focused/visible client through the bridge's same-origin SSE channel. The
browser reports its current session, visibility, focus result, and whether it
is the dedicated Micro surface to `POST /integrations/codex-micro/report`.
When the only client is a hidden tab (or no client exists), the bridge opens
one Edge/Chrome app-mode window tagged `codexMicroSurface=1`. A pending/open
guard prevents repeated button presses from multiplying tabs while it starts.
The WPF host then uses Win32 foreground activation on that dedicated browser
window without synthesizing keyboard or pointer input.

The ACT12 key displays the seven activation phases directly on the key. During
program-managed first use, the same key and setup window display the eight
setup phases. Selecting an external Harness no longer forces settings open;
the first ACT12 click either reuses a healthy bridge or offers managed setup
and existing-Harness configuration. Right-clicking the lower-left quota knob
still opens the software-settings section for the current target.

## Local control protocol version 1

The adapter accepts the same bounded JSON object through either a Windows named
pipe (one UTF-8 line) or an exact loopback HTTP POST. The supported actions are:

```json
{"version":1,"source":"codex-micro","action":"activate"}
{"version":1,"source":"codex-micro","action":"state/read"}
{"version":1,"source":"codex-micro","action":"session/activate","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"session/new"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"session/fork","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"session/archive","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"turn/cancel","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"view/toggle-chat-trajectory","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"interaction/approve","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"interaction/reject","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"history/load-older","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"layout/toggle-sidebar"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"layout/open-details"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"layout/close-details"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"composer/select-next"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"composer/activate-selection"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"reasoning/increase","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"model/toggle-quick","sessionId":"session-id"}
{"version":1,"source":"codex-micro","action":"action/execute","actionId":"goal/open","sessionId":"session-id"}
```

It returns one UTF-8 JSON line before closing the connection:

```json
{"success":true,"message":"DeepSeek Harness acknowledged activation.","status":"background"}
```

`state/read` adds a typed `state` object with declared capabilities, native
action ids, the browser-authoritative current session, and up to six recent
Harness sessions. Agent keys display only those sessions while the Harness is
active. The upper-left dial defaults to composer-scoped dynamic control
navigation with a browser highlight, and can be changed to reasoning-only or
recent-session selection in that Harness's Options;
`action/execute` uses the Harness's Workspace, Session, conversation-view,
pending-interaction, history, and layout services. The host uses bounded
connection and readiness timeouts and treats an absent, malformed, or negative
response as failure. Browser document focus is never treated as proof of native
foreground ownership; the Windows Micro host performs and verifies HWND
activation even when the Harness host itself runs under WSL.

## DeepSeek button audit and default four

The Web UI button inventory was grouped by the domain service behind each
control. Frequency is an interaction-design estimate for daily agent work,
not telemetry collection.

| UI group | Representative DeepSeek controls | Estimated frequency | Micro treatment |
| --- | --- | --- | --- |
| Main loop | New session, Send, Stop | very high | New and Stop are assignable; Send remains in the composer because it requires authored input |
| Permission / plan | Allow once, Reject, Approve plan, Decline plan | high when running tools | Allow/Approve and Reject/Decline are context-aware direct actions |
| Session | Open recent, Fork, Archive, Rename, Search | high to occasional | Agent keys/open, Fork, and Archive are assignable; Rename/Search require text and stay in Web |
| Navigation / history | Previous/next session, Load older, Sidebar, Details | medium | all stable, non-text actions are assignable |
| Queue / steering | Edit, remove, steer queued prompt | occasional | stays in Web because the intended queue item must be visible |
| Attachments / deliverables | Add/remove/open attachment, show folder | occasional | stays in Web; destructive or file-selection context is not safe for blind presses |
| Workspace / goals / jobs | Add workspace, pause/resume goal, jobs menu | low and contextual | stays in Web until a stable target-bearing protocol is defined |
| Settings / feedback | Model, access mode, reasoning, settings, feedback | occasional | stays in Web; Agent settings open directly from the quota knob |

The frequency-ranked physical defaults are:

1. `ACT06` — `session/new`
2. `ACT07` — `view/toggle-chat-trajectory`
3. `ACT08` — `turn/cancel`
4. `ACT09` — `session/fork`

The default joystick follows the app's spatial layout: up/down open the
previous/next sidebar session, left toggles the left sidebar, and right opens
the right details drawer. An untouched former joystick layout is migrated
direction-by-direction without resetting custom Agent or microphone keys.

Approve/reject compatibility actions, archive, older history, sidebar, details, session navigation, and
surface activation remain available in each Harness's independent key-map
drop-down. Legacy untouched DeepSeek defaults migrate to these four; a user
custom map is preserved.

Harness selection is a hard input boundary: while an external target is
active, every Agent, action, microphone, encoder, and joystick input is either
routed to a declared adapter capability or explicitly disabled. It never
falls through to Codex HID, simulated keyboard, pointer, sidebar, or window
input.

Every registered target appears in both the Codex key's right-click menu and
the shared Micro software-settings page. The actual running Micro remains the
live layout editor for external targets. Adapter details and keypad-owned voice
settings open as separate options instead of a flat mapping list.
