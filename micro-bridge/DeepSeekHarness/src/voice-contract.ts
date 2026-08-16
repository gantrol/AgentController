/** Browser-safe voice settings and wire frames shared by both bundle halves. */

export const VOICE_PROVIDERS = ['local-qwen', 'system', 'remote-websocket'] as const
export type VoiceProvider = typeof VOICE_PROVIDERS[number]

export const LOCAL_START_MODES = ['on-demand', 'with-harness', 'manual'] as const
export type LocalStartMode = typeof LOCAL_START_MODES[number]

export const LOCAL_RUNNERS = ['powershell', 'executable'] as const
export type LocalRunner = typeof LOCAL_RUNNERS[number]

/** Bumped only when an existing completed setup must be run again. */
export const VOICE_SETUP_VERSION = 1 as const

/** One provider-owned model used by the hardware quick-toggle pair. */
export interface QuickModelRef {
  provider: string
  model: string
}

export interface VoiceSettings {
  provider: VoiceProvider
  language: string
  autoSubmit: boolean
  setupCompleted: boolean
  setupVersion: number
  localStreamUrl: string
  localHealthUrl: string
  localModel: string
  localCredentialRef: string
  localStartMode: LocalStartMode
  localRunner: LocalRunner
  localScriptPath: string
  localScriptArguments: string[]
  localWorkingDirectory: string
  localStartupTimeoutMilliseconds: number
  remoteUrl: string
  remoteModel: string
  remoteCredentialRef: string
  quickModelA?: QuickModelRef
  quickModelB?: QuickModelRef
}

export interface VoiceSettingsDocument {
  revision: number
  settings: VoiceSettings
}

export interface CredentialStatus {
  configured: boolean
  source?: string
  writable: boolean
}

export interface VoiceSettingsResponse {
  document: VoiceSettingsDocument
  credentials: Record<string, CredentialStatus>
  runtime: LocalRuntimeStatus
  recommendations?: VoiceEnvironmentRecommendations
}

export interface VoiceEnvironmentRecommendations {
  localLauncherPath: string
  localWorkingDirectory: string
  localStreamUrl: string
  localHealthUrl: string
}

export type LocalRuntimePhase =
  | 'not-configured'
  | 'stopped'
  | 'starting'
  | 'ready'
  | 'error'

export interface LocalRuntimeStatus {
  phase: LocalRuntimePhase
  message: string
  errorCode?: string
  processId?: number
  startedAt?: string
  elapsedMilliseconds?: number
  logTail?: string
}

export const DEFAULT_VOICE_SETTINGS: Readonly<VoiceSettings> = Object.freeze({
  provider: 'local-qwen',
  // Empty means automatic language detection / the provider default. A
  // language is fixed only when the user explicitly enters a BCP-47 tag.
  language: '',
  autoSubmit: false,
  setupCompleted: false,
  setupVersion: VOICE_SETUP_VERSION,
  localStreamUrl: 'ws://127.0.0.1:8765/v1/stream',
  localHealthUrl: 'http://127.0.0.1:8765/health',
  localModel: 'Qwen/Qwen3-ASR-0.6B',
  localCredentialRef: 'DSH_QWEN_ASR_API_KEY',
  localStartMode: 'on-demand',
  localRunner: 'powershell',
  localScriptPath: '',
  localScriptArguments: [],
  localWorkingDirectory: '',
  localStartupTimeoutMilliseconds: 300_000,
  remoteUrl: '',
  remoteModel: '',
  remoteCredentialRef: 'DSH_ASR_API_KEY',
})

export type HostVoiceFrame =
  | { type: 'ready'; provider: VoiceProvider }
  | { type: 'partial'; text: string }
  | { type: 'final'; text: string }
  | { type: 'done' }
  | { type: 'error'; message: string }
