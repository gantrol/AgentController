/** Versioned local protocol shared by the host and browser halves. */

/** Same-origin SSE endpoint used by the browser half. */
export const MICRO_EVENTS_ENDPOINT = '/integrations/codex-micro/events'

/** Same-origin browser-to-host state and acknowledgement endpoint. */
export const MICRO_REPORT_ENDPOINT = '/integrations/codex-micro/report'

/** DeepSeek's single voice button; capture and ASR remain keypad-owned. */
export const MICRO_VOICE_BUTTON_ENDPOINT = '/integrations/codex-micro/voice-button'

/** Windows named-pipe name consumed by AgentController's Codex Micro surface. */
export const MICRO_PIPE_NAME = 'deepseek-harness-micro-v1'
export const MICRO_CONTROL_ENDPOINT = '/__agentcontroller/micro/request'

/** Current line-protocol version. */
export const MICRO_PROTOCOL_VERSION = 1 as const

interface MicroRequestBase {
  version: typeof MICRO_PROTOCOL_VERSION
  source: 'codex-micro' | 'agent-controller'
}

/** Focus or open the Harness browser surface. */
export interface MicroActivationRequest extends MicroRequestBase {
  action: 'activate'
}

/** Read the adapter capabilities and recent Harness sessions. */
export interface MicroStateRequest extends MicroRequestBase {
  action: 'state/read'
}

/** Open one exact Harness session through the browser runtime service. */
export interface MicroSessionActivationRequest extends MicroRequestBase {
  action: 'session/activate'
  sessionId: string
}

/** Stable actions implemented through Harness services or scoped composer controls. */
export type MicroActionId =
  | 'session/new'
  | 'session/fork'
  | 'session/archive'
  | 'turn/cancel'
  | 'view/toggle-chat-trajectory'
  | 'interaction/approve'
  | 'interaction/reject'
  | 'history/load-older'
  | 'layout/toggle-sidebar'
  | 'layout/open-details'
  | 'layout/close-details'
  | 'composer/select-previous'
  | 'composer/select-next'
  | 'composer/activate-selection'
  | 'composer/back'
  | 'composer/submit'
  | 'reasoning/decrease'
  | 'reasoning/increase'
  | 'model/toggle-quick'
  | 'goal/open'

/** Execute one Harness-native action, optionally against an exact session. */
export interface MicroActionExecutionRequest extends MicroRequestBase {
  action: 'action/execute'
  actionId: MicroActionId
  sessionId?: string
}

/** Long-poll the next DeepSeek voice-button request from the keypad. */
export interface MicroVoiceRequestPoll extends MicroRequestBase {
  action: 'voice/request'
}

/** Complete one DeepSeek voice-button request after keypad-side work. */
export interface MicroVoiceRequestResult extends MicroRequestBase {
  action: 'voice/result'
  requestId: string
  success: boolean
  active: boolean
  message: string
}

/** Publish keypad-owned voice state to DeepSeek's one button. */
export interface MicroVoiceStatusRequest extends MicroRequestBase {
  action: 'voice/status'
  active: boolean
  phase: 'idle' | 'starting' | 'restarting' | 'listening' | 'stopping' | 'error'
  message: string
  sessionId?: string
}

/** Write keypad-recognized text into one DeepSeek composer. */
export type MicroDictationPhase = 'partial' | 'final' | 'cancel'

export interface MicroDictationRequest extends MicroRequestBase {
  action: 'composer/dictate'
  text: string
  language?: string
  sessionId?: string
  autoSubmit?: boolean
  /** Stable id used to replace one live partial instead of appending copies. */
  dictationId?: string
  dictationPhase?: MicroDictationPhase
}

/** Request accepted by the local named-pipe adapter. */
export type MicroRequest =
  | MicroActivationRequest
  | MicroStateRequest
  | MicroSessionActivationRequest
  | MicroActionExecutionRequest
  | MicroVoiceRequestPoll
  | MicroVoiceRequestResult
  | MicroVoiceStatusRequest
  | MicroDictationRequest

/** Capabilities exposed to the physical Micro surface. */
export interface MicroCapabilities {
  sessionList: boolean
  sessionActivation: boolean
  knobSettings: boolean
  voiceInput: boolean
  actions: MicroActionId[]
}

/** Agent-key state projected from DeepSeek Harness's live session list. */
export type MicroSessionStatus = 'idle' | 'running' | 'completed' | 'waiting' | 'error'

/** One recent DeepSeek Harness session displayed on an Agent key. */
export interface MicroSessionSummary {
  id: string
  displayTitle: string
  /** Exact live state used by the keypad light; `running` remains for v1 compatibility. */
  status: MicroSessionStatus
  running: boolean
  updatedAt: number
}

/** Browser-owned state that is not present in the host's durable session-list RPC. */
export interface MicroBrowserSessionState {
  id: string
  status: MicroSessionStatus
}

export interface MicroComponentSnapshot {
  adapter: 'ready'
  browser: 'connected' | 'disconnected'
  /** Display name of the currently selected DeepSeek model, when known. */
  currentModel?: string
}

/** One browser-originated request consumed exactly once by a keypad. */
export interface MicroKeypadVoiceRequest {
  requestId: string
  command: 'toggle'
  sessionId?: string
}

/** State returned by `state/read`; selection is last known to this adapter. */
export interface MicroStateSnapshot {
  capabilities: MicroCapabilities
  sessions: MicroSessionSummary[]
  currentSessionId?: string
  /** Zero outside a popup; one for its root and two for a drilled pane. */
  navigationDepth: number
  components?: MicroComponentSnapshot
}

/** One-line acknowledgement returned to Codex Micro. */
export interface MicroResponse {
  success: boolean
  message: string
  status?: 'completed' | 'opening' | 'foreground' | 'background'
  windowProcessId?: number
  state?: MicroStateSnapshot
  voiceRequest?: MicroKeypadVoiceRequest
}

/** Browser event emitted when an already-connected Harness surface should focus. */
export interface MicroActivationFrame {
  version: typeof MICRO_PROTOCOL_VERSION
  type: 'activate'
  requestId: string
}

/** Browser event selecting one session through `ctx.sessions.open()`. */
export interface MicroSessionActivationFrame {
  version: typeof MICRO_PROTOCOL_VERSION
  type: 'session/activate'
  requestId: string
  sessionId: string
}

/** Browser event executing one capability-advertised Harness action. */
export interface MicroActionExecutionFrame {
  version: typeof MICRO_PROTOCOL_VERSION
  type: 'action/execute'
  requestId: string
  actionId: MicroActionId
  sessionId?: string
}

/** Keypad-recognized text delivered to DeepSeek's native composer service. */
export interface MicroDictationFrame {
  version: typeof MICRO_PROTOCOL_VERSION
  type: 'composer/dictate'
  requestId: string
  text: string
  language?: string
  sessionId?: string
  autoSubmit: boolean
  dictationId?: string
  dictationPhase?: MicroDictationPhase
}

/** Keypad-owned voice state projected onto DeepSeek's one voice button. */
export interface MicroVoiceStatusFrame {
  version: typeof MICRO_PROTOCOL_VERSION
  type: 'voice/status'
  active: boolean
  phase: 'idle' | 'starting' | 'restarting' | 'listening' | 'stopping' | 'error'
  message: string
  sessionId?: string
}

/** Browser presence plus an optional acknowledgement for one delivered frame. */
export interface MicroBrowserReport {
  version: typeof MICRO_PROTOCOL_VERSION
  browserId: string
  currentSessionId: string | null
  visible: boolean
  focused: boolean
  surface: 'tab' | 'dedicated'
  /** Current composer interaction depth owned by the browser plugin. */
  navigationDepth: number
  /** Display name of the currently selected DeepSeek model, when known. */
  currentModel?: string
  /**
   * Live sidebar states. In particular, `completed` and `waiting` exist only
   * in the browser runtime, so omitting this projection would make the
   * keypad's green and amber Agent lights impossible to synchronize.
   */
  sessionStates?: MicroBrowserSessionState[]
  requestId?: string
  success?: boolean
  message?: string
}

/** Host-to-browser event carried by the bridge's SSE stream. */
export type MicroBrowserFrame =
  | MicroActivationFrame
  | MicroSessionActivationFrame
  | MicroActionExecutionFrame
  | MicroDictationFrame
  | MicroVoiceStatusFrame
