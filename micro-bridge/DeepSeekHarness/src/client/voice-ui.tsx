/** DeepSeek's single voice button. Capture, ASR, and settings live in Micro. */

import { useSyncExternalStore, type ReactNode } from 'react'
import { MICRO_VOICE_BUTTON_ENDPOINT } from '../protocol.ts'
import type { MicroVoiceStatusFrame } from '../protocol.ts'

export const VOICE_LOCALE_NAMESPACE = 'agentcontrollerMicroVoice'

export const zh = {
  micStart: '使用小键盘开始语音输入',
  micStop: '停止小键盘语音输入',
  micRequesting: '正在连接 Codex Micro 小键盘',
  micListening: '小键盘正在聆听',
  micStopping: '小键盘正在完成转写',
  micUnavailable: '请打开 Codex Micro 小键盘并在小键盘端配置语音',
} as const

export const en: Record<keyof typeof zh, string> = {
  micStart: 'Start voice input on the Micro keypad',
  micStop: 'Stop voice input on the Micro keypad',
  micRequesting: 'Connecting to the Codex Micro keypad',
  micListening: 'The Micro keypad is listening',
  micStopping: 'The Micro keypad is finishing transcription',
  micUnavailable: 'Open Codex Micro and configure voice on the keypad',
}

export type VoiceLocaleKey = keyof typeof zh
export type VoiceTranslate = (key: VoiceLocaleKey) => string

export interface KeypadVoiceSnapshot {
  active: boolean
  phase: 'idle' | 'starting' | 'listening' | 'stopping' | 'error'
  sessionId?: string
  message: string
}

interface VoiceButtonResponse {
  success: boolean
  active: boolean
  message: string
}

function responseBody(value: unknown): VoiceButtonResponse {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('The Micro bridge returned an invalid voice-button response.')
  }
  const fields = value as Record<string, unknown>
  if (typeof fields.success !== 'boolean'
      || typeof fields.active !== 'boolean'
      || typeof fields.message !== 'string') {
    throw new Error('The Micro bridge returned an incomplete voice-button response.')
  }
  return {
    success: fields.success,
    active: fields.active,
    message: fields.message,
  }
}

/** Browser-side projection of the keypad-owned voice session. */
export class KeypadVoiceController {
  private snapshot: KeypadVoiceSnapshot = {
    active: false,
    phase: 'idle',
    message: '',
  }
  private readonly listeners = new Set<() => void>()
  private pending = false

  getSnapshot = (): KeypadVoiceSnapshot => this.snapshot

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  dispose(): void {
    this.listeners.clear()
  }

  applyStatus(frame: MicroVoiceStatusFrame): void {
    this.publish({
      active: frame.active,
      phase: frame.phase,
      message: frame.message,
      ...(frame.sessionId === undefined
        ? this.snapshot.sessionId === undefined ? {} : { sessionId: this.snapshot.sessionId }
        : { sessionId: frame.sessionId }),
    })
  }

  async toggle(sessionId: string): Promise<void> {
    if (this.pending) return
    this.pending = true
    this.publish({
      ...this.snapshot,
      phase: this.snapshot.active ? 'stopping' : 'starting',
      sessionId,
      message: '',
    })
    try {
      const response = await fetch(MICRO_VOICE_BUTTON_ENDPOINT, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ sessionId }),
      })
      const result = responseBody(await response.json() as unknown)
      if (!response.ok || !result.success) throw new Error(result.message)
      this.publish({
        active: result.active,
        phase: result.active ? 'listening' : 'idle',
        sessionId,
        message: result.message,
      })
    } catch (error) {
      this.publish({
        active: this.snapshot.active,
        phase: 'error',
        sessionId,
        message: error instanceof Error ? error.message : String(error),
      })
      throw error
    } finally {
      this.pending = false
    }
  }

  private publish(next: KeypadVoiceSnapshot): void {
    this.snapshot = next
    for (const listener of [...this.listeners]) listener()
  }
}

const STYLE_ID = 'agentcontroller-dsh-micro-voice'

export function ensureVoiceStyles(): void {
  if (document.querySelector(`style[data-plugin="${STYLE_ID}"]`) !== null) return
  const style = document.createElement('style')
  style.dataset.plugin = STYLE_ID
  style.textContent = `
.acmv-mic{width:32px;height:32px;border:0;border-radius:9px;background:transparent;color:var(--dsw-alias-content-secondary,#666);display:grid;place-items:center;cursor:pointer;position:relative;transition:background .15s,color .15s,transform .15s}.acmv-mic:hover{background:var(--dsw-alias-background-hover,rgba(0,0,0,.06));color:var(--dsw-alias-content-primary,#222)}.acmv-mic:active{transform:scale(.94)}.acmv-mic:disabled{cursor:wait;opacity:.68}.acmv-mic[data-active=true]{background:rgba(69,120,255,.12);color:#3979ee}.acmv-mic[data-error=true]{color:#c4514c}.acmv-mic-dot{position:absolute;right:4px;top:4px;width:6px;height:6px;border-radius:50%;background:#4a82ef;box-shadow:0 0 0 3px rgba(74,130,239,.12)}.acmv-mic[data-phase=starting] .acmv-mic-dot,.acmv-mic[data-phase=stopping] .acmv-mic-dot{animation:acmv-pulse 1s infinite alternate}@keyframes acmv-pulse{from{opacity:.35;transform:scale(.8)}to{opacity:1;transform:scale(1.1)}}
`
  document.head.append(style)
}

function MicIcon(): ReactNode {
  return <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5.5 11.5a6.5 6.5 0 0 0 13 0M12 18v3M9 21h6"/></svg>
}

export interface VoiceButtonProps {
  session: { sessionId: string }
  voice: KeypadVoiceController
  t: VoiceTranslate
}

export function VoiceButton({ session, voice, t }: VoiceButtonProps): ReactNode {
  const state = useSyncExternalStore(voice.subscribe, voice.getSnapshot, voice.getSnapshot)
  const addressed = state.sessionId === session.sessionId
  const active = addressed && state.active
  const busy = addressed && (state.phase === 'starting' || state.phase === 'stopping')
  const label = state.phase === 'starting' ? t('micRequesting')
    : state.phase === 'stopping' ? t('micStopping')
      : active ? t('micListening') : t('micStart')
  const title = addressed && state.message !== '' ? state.message : label
  return <button
    type="button"
    className="acmv-mic"
    data-active={active}
    data-error={addressed && state.phase === 'error'}
    data-phase={addressed ? state.phase : 'idle'}
    disabled={busy}
    aria-label={active ? t('micStop') : t('micStart')}
    title={title}
    onClick={() => { void voice.toggle(session.sessionId).catch(() => {}) }}
  >
    <MicIcon/>{active || busy ? <span className="acmv-mic-dot"/> : null}
  </button>
}
