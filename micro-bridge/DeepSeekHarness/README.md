# AgentController DeepSeek Harness bridge

English | [中文](README.zh.md)

This external bundle connects Agent Controller and the optional Codex Micro
keypad to DeepSeek Harness without modifying the Harness source tree and
without simulating keyboard or pointer input.

## Responsibilities

- Opens or focuses one guarded Harness app window.
- Accepts direct gamepad control from Agent Controller when DeepSeek Harness is
  selected with the base-layer View button.
- Lists recent sessions, activates an exact session, and executes native
  Harness actions through its services.
- Navigates live composer controls, toggles Conversation / Trajectory, changes
  reasoning/model selection, and opens Goal through Harness APIs.
- Adds exactly one microphone button beside the DeepSeek composer.
- Relays that button's toggle request to the Micro keypad and relays
  keypad-recognized text back to the addressed composer.

The plugin does **not** capture audio, request microphone permission, run speech
recognition, connect to an ASR service, store voice settings, or store voice
credentials. Those responsibilities belong exclusively to the Codex Micro
keypad process. The **voice path** sends final text only. Harness image
attachments remain a separate native path and work only when the selected
provider model advertises image input.

## Installation modes

### Program-managed mode

The DeepSeek-tailored Micro package can create a dedicated WSL distribution
named `CodexMicro-DeepSeek` and invoke
`scripts/install-dsh-wsl-runtime.sh`. The installer uses pinned compatible
Node, pnpm, and `@deepseek-ai/dsh` versions and installs this bridge. Runtime
files live below the dedicated Linux user's
`~/.local/share/codex-micro/deepseek`.

The managed rc.8 installer adds `deepseek-v4-flash-vision-exp` to Harness's
DeepSeek model catalog with `text` and `image` input modalities. It preserves
the user's other model and locale settings and does not make the experimental
model the default. Availability still depends on the official API account and
the model returned by that provider.

When an older managed DSH version is detected, Micro offers an explicit,
rollback-safe upgrade. The old runtime is retained as a full backup; only the
credential store, settings, and anonymous installation id are copied into a
fresh rc.8 home. rc.8's incompatible session/storage data is never imported.
The swap is committed only after both the Web page and this Bridge pass their
health checks. User-managed Harness installations are never upgraded by Micro.

`scripts/start-dsh-wsl.sh` accepts `--port`; the official default is 3080.

### User-managed Harness

Install this built bundle from the environment that runs Harness:

```text
dsh plugin --profile web add <absolute path to this bundle>
```

Run `dsh web` normally. Agent Controller uses the default loopback control
address below directly; the optional keypad can save a different address. The
default is `http://127.0.0.1:3080/__agentcontroller/micro/request`.

## Local protocol

WSL mode uses bounded UTF-8 JSON over loopback HTTP. Native Windows mode can
use one JSON line on `\\.\pipe\deepseek-harness-micro-v1`. Protocol version is
`1`; accepted sources are `agent-controller` for direct gamepad control and
`codex-micro` for the optional keypad.

Normal DSH tabs can report state, but they never receive physical-control focus,
session, action, or dictation frames. If no confirmed dedicated surface is
connected, the bridge returns `opening` so the Windows client can open an
Edge-first app-mode window without depending on WSL executable interop.

General actions:

- `activate`
- `state/read`
- `session/activate`
- `action/execute`

Voice-button relay actions:

- `voice/request` — the keypad long-polls for a DeepSeek button toggle.
- `voice/result` — the keypad completes that toggle.
- `voice/status` — the keypad projects listening state onto the button.
- `composer/dictate` — the keypad sends recognized text to one composer.

No audio or API key crosses this protocol. The Bridge connects only DeepSeek
and the keypad; the keypad connects directly to its configured voice provider.

## Build and test

```powershell
pnpm run verify
pnpm run test:e2e
```

This runs strict TypeScript checking, browser/pipe/loopback-HTTP tests, and
builds both Host and browser bundles.

## Limits

- Browser foreground focus remains subject to Windows/browser policy; Micro
  performs the final native window activation.
- Voice-provider setup and troubleshooting are documented with the
  [Micro keypad](../../virtual-micro/README.md), not in this plugin.
- Model changes use Harness's shared model directory and never proxy LLM
  provider requests.
- Harness accepting an image does not add vision to a text-only backend; the
  selected provider model must actually accept the native image request.
