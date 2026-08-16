/** Versioned local protocol shared by the host and browser halves. */

/** Same-origin SSE endpoint used by the browser half. */
export const MICRO_EVENTS_ENDPOINT = '/integrations/codex-micro/events'

/** Same-origin browser-to-host state and acknowledgement endpoint. */
export const MICRO_REPORT_ENDPOINT = '/integrations/codex-micro/report'

/** Browser/Host audio stream. Audio bytes never cross the named-pipe process. */
export const MICRO_VOICE_ENDPOINT = '/integrations/codex-micro/voice'

/** External plugin-owned settings and write-only credential API prefix. */
export const MICRO_SETTINGS_ENDPOINT = '/integrations/codex-micro/settings'

/** Windows named-pipe name consumed by AgentController's Codex Micro surface. */
export const MICRO_PIPE_NAME = 'deepseek-harness-micro-v1'
export const MICRO_CONTROL_ENDPOINT = '/__agentcontroller/micro/request'

/** Current line-protocol version. */
export const MICRO_PROTOCOL_VERSION = 1 as const

interface MicroRequestBase {
  version: typeof MICRO_PROTOCOL_VERSION
  source: 'codex-micro'
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

/** Open plugin-owned voice settings, or begin/finish push-to-talk. */
export interface MicroVoiceRequest extends MicroRequestBase {
  action: 'voice/configure' | 'voice/start' | 'voice/stop'
}

/** Request accepted by the local named-pipe adapter. */
export type MicroRequest =
  | MicroActivationRequest
  | MicroStateRequest
  | MicroSessionActivationRequest
  | MicroActionExecutionRequest
  | MicroVoiceRequest

/** Capabilities exposed to the physical Micro surface. */
export interface MicroCapabilities {
  sessionList: boolean
  sessionActivation: boolean
  knobSettings: boolean
  voiceInput: boolean
  actions: MicroActionId[]
}

/** One recent DeepSeek Harness session displayed on an Agent key. */
export interface MicroSessionSummary {
  id: string
  displayTitle: string
  running: boolean
  updatedAt: number
}

export interface MicroComponentSnapshot {
  adapter: 'ready'
  browser: 'connected' | 'disconnected'
  voiceSetup: 'required' | 'ready'
  voiceRuntime: 'not-configured' | 'stopped' | 'starting' | 'ready' | 'error'
  voiceMessage: string
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

/** Plugin voice command delivered to the browser that owns the mic UI. */
export interface MicroVoiceFrame {
  version: typeof MICRO_PROTOCOL_VERSION
  type: 'voice/configure' | 'voice/start' | 'voice/stop'
  requestId: string
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
  requestId?: string
  success?: boolean
  message?: string
}

/** Host-to-browser event carried by the bridge's SSE stream. */
export type MicroBrowserFrame =
  | MicroActivationFrame
  | MicroSessionActivationFrame
  | MicroActionExecutionFrame
  | MicroVoiceFrame
