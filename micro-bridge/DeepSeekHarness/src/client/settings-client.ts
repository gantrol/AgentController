/** Reactive browser client for the external plugin's settings and write-only credentials. */

import { MICRO_SETTINGS_ENDPOINT } from '../protocol.ts'
import type {
  CredentialStatus,
  LocalRuntimeStatus,
  VoiceEnvironmentRecommendations,
  VoiceSettings,
  VoiceSettingsDocument,
  VoiceSettingsResponse,
} from '../voice-contract.ts'

export interface VoiceSettingsSnapshot {
  status: 'idle' | 'loading' | 'ready' | 'saving' | 'testing' | 'error'
  document?: VoiceSettingsDocument
  credentials: Record<string, CredentialStatus>
  runtime?: LocalRuntimeStatus
  recommendations?: VoiceEnvironmentRecommendations
  error?: string
}

type Listener = () => void

function errorMessage(value: unknown): string {
  return value instanceof Error ? value.message : String(value)
}

async function json<T>(response: Response): Promise<T> {
  const value = await response.json() as unknown
  if (!response.ok) {
    const detail = typeof value === 'object' && value !== null
      ? (value as Record<string, unknown>).error
      : undefined
    const code = typeof value === 'object' && value !== null
      ? (value as Record<string, unknown>).code
      : undefined
    throw new Error(typeof detail === 'string'
      ? `${typeof code === 'string' ? `${code}: ` : ''}${detail}`
      : `Request failed (HTTP ${String(response.status)}).`)
  }
  return value as T
}

export class VoiceSettingsClient {
  private snapshot: VoiceSettingsSnapshot = { status: 'idle', credentials: {} }
  private readonly listeners = new Set<Listener>()
  private loading: Promise<void> | undefined

  getSnapshot = (): VoiceSettingsSnapshot => this.snapshot

  subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  async load(): Promise<void> {
    if (this.loading !== undefined) return await this.loading
    this.publish({
      status: 'loading',
      credentials: this.snapshot.credentials,
      ...(this.snapshot.document === undefined ? {} : { document: this.snapshot.document }),
      ...(this.snapshot.recommendations === undefined ? {} : { recommendations: this.snapshot.recommendations }),
    })
    const task = (async () => {
      try {
        const response = await fetch(MICRO_SETTINGS_ENDPOINT, { cache: 'no-store' })
        const payload = await json<VoiceSettingsResponse>(response)
        this.publish({
          status: 'ready',
          document: payload.document,
          credentials: payload.credentials,
          runtime: payload.runtime,
          ...(payload.recommendations === undefined ? {} : { recommendations: payload.recommendations }),
        })
      } catch (error) {
        this.publish({ ...this.snapshot, status: 'error', error: errorMessage(error) })
        throw error
      } finally {
        this.loading = undefined
      }
    })()
    this.loading = task
    return await task
  }

  async settings(): Promise<VoiceSettings> {
    if (this.snapshot.document === undefined) await this.load()
    const document = this.snapshot.document
    if (document === undefined) throw new Error(this.snapshot.error ?? 'Voice settings are unavailable.')
    return structuredClone(document.settings)
  }

  async save(
    settings: VoiceSettings,
    secrets: Readonly<Record<string, string>> = {},
  ): Promise<void> {
    if (this.snapshot.document === undefined) await this.load()
    const current = this.snapshot.document
    if (current === undefined) throw new Error('Voice settings are unavailable.')
    this.publish({
      status: 'saving',
      credentials: this.snapshot.credentials,
      ...(this.snapshot.document === undefined ? {} : { document: this.snapshot.document }),
      ...(this.snapshot.recommendations === undefined ? {} : { recommendations: this.snapshot.recommendations }),
    })
    try {
      const saved = await json<{ document: VoiceSettingsDocument }>(await fetch(MICRO_SETTINGS_ENDPOINT, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ expectedRevision: current.revision, settings }),
      }))
      this.publish({ ...this.snapshot, status: 'saving', document: saved.document })
      for (const [ref, value] of Object.entries(secrets)) {
        if (value.trim() === '') continue
        await json(await fetch(`${MICRO_SETTINGS_ENDPOINT}/credential`, {
          method: 'PUT',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify({ ref, value }),
        }))
      }
      await this.reloadAfterWrite()
    } catch (error) {
      this.publish({ ...this.snapshot, status: 'error', error: errorMessage(error) })
      throw error
    }
  }

  async clearCredential(ref: string): Promise<void> {
    await json(await fetch(`${MICRO_SETTINGS_ENDPOINT}/credential`, {
      method: 'DELETE',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ ref }),
    }))
    await this.reloadAfterWrite()
  }

  async startRuntime(): Promise<LocalRuntimeStatus> {
    this.publish({
      ...this.snapshot,
      runtime: {
        phase: 'starting',
        message: 'Starting local streaming ASR and loading the model…',
      },
    })
    try {
      const payload = await json<{ runtime: LocalRuntimeStatus }>(await fetch(
        `${MICRO_SETTINGS_ENDPOINT}/runtime`,
        { method: 'POST' },
      ))
      this.publish({ ...this.snapshot, runtime: payload.runtime })
      return payload.runtime
    } catch (error) {
      this.publish({
        ...this.snapshot,
        runtime: { phase: 'error', message: errorMessage(error), errorCode: 'ASR_START_FAILED' },
      })
      throw error
    }
  }

  async stopRuntime(): Promise<LocalRuntimeStatus> {
    const payload = await json<{ runtime: LocalRuntimeStatus }>(await fetch(
      `${MICRO_SETTINGS_ENDPOINT}/runtime`,
      { method: 'DELETE' },
    ))
    this.publish({ ...this.snapshot, runtime: payload.runtime })
    return payload.runtime
  }

  /** Save an incomplete draft, prove the provider handshake, then mark setup complete. */
  async configureAndTest(
    settings: VoiceSettings,
    secrets: Readonly<Record<string, string>> = {},
  ): Promise<void> {
    await this.save({ ...settings, setupCompleted: false }, secrets)
    const { error: _previousError, ...testingSnapshot } = this.snapshot
    this.publish({
      ...testingSnapshot,
      status: 'testing',
      ...(settings.provider !== 'local-qwen'
        ? {}
        : { runtime: { phase: 'starting', message: 'Starting local streaming ASR and loading the model…' } }),
    })
    try {
      const tested = await json<{
        success: boolean
        message: string
        runtime: LocalRuntimeStatus
      }>(await fetch(`${MICRO_SETTINGS_ENDPOINT}/test`, { method: 'POST' }))
      if (!tested.success) throw new Error(tested.message)
      const current = await this.settings()
      await this.save({ ...current, setupCompleted: true })
      this.publish({ ...this.snapshot, runtime: tested.runtime })
    } catch (error) {
      this.publish({ ...this.snapshot, status: 'error', error: errorMessage(error) })
      throw error
    }
  }

  private async reloadAfterWrite(): Promise<void> {
    const response = await fetch(MICRO_SETTINGS_ENDPOINT, { cache: 'no-store' })
    const payload = await json<VoiceSettingsResponse>(response)
    this.publish({
      status: 'ready',
      document: payload.document,
      credentials: payload.credentials,
      runtime: payload.runtime,
      ...(payload.recommendations === undefined ? {} : { recommendations: payload.recommendations }),
    })
  }

  private publish(next: VoiceSettingsSnapshot): void {
    this.snapshot = next
    for (const listener of [...this.listeners]) listener()
  }
}
