# AgentController DeepSeek Harness bridge

English | [中文](README.zh.md)

An external, dual-face DeepSeek Harness bundle for AgentController Micro. It is installed into a Harness profile and does not modify the DeepSeek Harness source tree.

## Features

- Runs the Harness/terminal process in WSL2. Windows Micro uses a loopback-only HTTP control endpoint, while native Windows mode retains the named-pipe fallback; no keyboard or pointer simulation.
- Opens/focuses one guarded Harness app window, lists recent sessions, activates an exact session, and executes native Harness actions.
- Toggles Conversation / Trajectory through the registered per-session view store without DOM queries or clicks.
- Lets the Micro encoder walk only live composer controls, including controls
  injected later by other plugins, with a single visible blue highlight. Once
  a popup opens, navigation is trapped in its topmost menu.
- Synchronizes menu depth to Micro. AG00 temporarily becomes a red Back key
  while a menu is open, and the remaining Agent keys are locked.
- Uses the Harness model directory directly for reasoning-only rotation and
  two configurable quick models; no simulated input is involved.
- Exposes a native Goal action that opens `/goal` without overwriting an
  unrelated draft.
- Adds a microphone button beside the Harness composer and a full **Voice input** settings page.
- Accepts Micro push-to-talk edges (`voice/start` and `voice/stop`) and writes final transcripts into the session that owned the recording.
- Supports three explicit speech providers:
  - **Local streaming Qwen**: continuous PCM over the same `dsh-stream-v1` WebSocket contract, with an optional managed startup script and health check.
  - **System/browser**: `SpeechRecognition` when the current browser exposes it.
  - **Remote WebSocket**: the documented `dsh-stream-v1` PCM16 protocol over `wss://` (or loopback `ws://`).
- Stores non-secret settings under the DSH storage directory. API keys are write-only references managed by the Harness credential service and are never returned to the browser.

## Install into WSL2

Install WSL2/Ubuntu on Windows, then run the installer once from PowerShell. It verifies and installs an isolated Linux Node 24 plus pnpm, copies Harness onto WSL's ext4 filesystem, migrates a copy of the user profile, and installs this external bundle. The original Windows checkout and `%USERPROFILE%\.dsh` remain untouched:

```powershell
wsl.exe --distribution Ubuntu --exec bash /mnt/d/AgentController/micro-bridge/DeepSeekHarness/scripts/install-dsh-wsl-runtime.sh
```

The stable runtime entry point is:

```powershell
wsl.exe --distribution Ubuntu --exec bash /mnt/d/AgentController/micro-bridge/DeepSeekHarness/scripts/start-dsh-wsl.sh
```

Micro's built-in DeepSeek target invokes that entry point and prefers
`http://127.0.0.1:3080/__agentcontroller/micro/request`. Node, Bash, PTY, and terminal inspection therefore run in Linux, while the web surface and Micro remain native Windows windows.

Set `DSH_SOURCE_CHECKOUT`, `AGENTCONTROLLER_ROOT`, or `WINDOWS_USER_NAME` when the checkouts or Windows profile use non-default locations. Migration converts drive-qualified paths only in profile/storage JSON. Existing history is retained, but start a fresh WSL session for continued terminal work.

## Local protocol

WSL mode carries the same bounded UTF-8 JSON contract over a loopback HTTP POST. Native Windows mode can still use one line on `\\.\pipe\deepseek-harness-micro-v1`. Protocol version is `1`, source is `codex-micro`, and typed actions include:

- `activate`
- `state/read`
- `session/activate`
- `action/execute`
- `voice/start` / `voice/stop`

The browser half performs operations through Harness services (`sessions`, `workspaces`, `conversation`, and `layout`) plus the registered conversation view store. The Host queues a frame only when no browser is connected and prevents repeated Micro presses from spawning multiple web windows. The default four keys are New, Conversation / Trajectory, Stop, and Fork.

Composer navigation uses `composer/select-previous`, `composer/select-next`,
`composer/activate-selection`, and `composer/back`. `state/read` reports
`navigationDepth` as `0` outside a menu, `1` at its root, and `2` in a model or
reasoning-effort pane.

Audio never crosses control HTTP or the named pipe. Local and remote capture uses mono 16 kHz PCM16 between the browser and the plugin's same-origin WebSocket gateway. Local stream and health endpoints must stay on loopback; public remote streaming must use `wss://`.

## First-use voice setup

An unverified profile cannot start microphone capture. The first microphone press opens the plugin-owned setup dialog, where the user selects a provider and passes its preflight. Local mode can start a PowerShell script or executable on demand, with Harness startup, or remain manually managed. The plugin validates the script path, working directory, health endpoint, model-loading timeout, and the provider's `ready` handshake before marking setup complete. Repeated starts join one in-flight process operation.

The “Bundled Qwen launcher” fills in `scripts/start-qwen3-asr-stream.ps1` from
the installed plugin. It starts `qwen3-asr-stream-server.py` inside WSL and
adapts Qwen3-ASR's native vLLM streaming state to `dsh-stream-v1`; it does not
disguise completed HTTP transcription as streaming. Qwen's official package
currently supports incremental inference through its vLLM backend; see the
[official streaming documentation](https://github.com/QwenLM/Qwen3-ASR#streaming-inference).

### Local setup from zero

1. Install WSL2 and Ubuntu on Windows and verify that the NVIDIA GPU is visible inside WSL. The plugin never changes Windows features or drivers for the user.
2. Create an isolated Python 3.12 environment containing `qwen-asr[vllm]`, `aiohttp`, and `numpy`. The recommended path is `~/.local/share/dsh-qwen-asr/venv`; the launcher also detects the reference application's `~/.local/share/catai-qwen-asr/venv`.
3. Optionally pre-download the model to `~/.local/share/dsh-qwen-asr/models/Qwen3-ASR-0.6B`. Otherwise the official library downloads `Qwen/Qwen3-ASR-0.6B` on first start, so increase the configured startup timeout when necessary.
4. Press the microphone once, choose Local streaming Qwen, select **Use** beside the bundled launcher, then choose **Save and verify**. Setup completes only after both the health check and WebSocket `ready` handshake succeed.
5. Choose on-demand or Harness-start warmup. Shutdown terminates the complete PowerShell/WSL/model process tree only when the plugin started it; externally managed services are left alone.

Failures remain actionable through stable codes and a bounded log tail: missing WSL/distribution, Python packages, script or work directory, occupied port, model download/load timeout, GPU OOM, failed health check, or streaming-ready timeout. A failed path is never displayed as ready and never silently falls back to another provider.

## Streaming contract

The client sends a `start` JSON frame, binary PCM16 frames, then `stop`. The remote service responds with JSON frames named `ready`, `partial`, `final`, `done`, or `error`. The configured credential is sent as a Bearer header by the Host, not exposed to browser JavaScript.

## Build and test

```powershell
pnpm run verify
```

This runs strict TypeScript checking, standalone browser/pipe/WSL loopback-HTTP/settings tests, and both Host and browser bundle builds.

## Limits

- Browser foreground focus remains subject to Windows/browser policy; Micro performs the final native window activation without simulated input.
- The legacy OpenAI-compatible `/audio/transcriptions` chunk path is no longer used for microphone input. Use the bundled Qwen adapter, or provide a custom local service that implements the streaming contract and emits partial/final frames.
- System recognition quality and whether audio leaves the device are controlled by the browser/OS. The plugin never silently changes providers.
- Micro stores encoder behavior independently per Harness: composer control
  navigation by default, reasoning-only control, or recent-session selection.

## Model experience

The bridge changes only the addressed session's next-step model selection and
reasoning effort through Harness's shared model directory. It never assembles
or proxies LLM provider requests itself.
