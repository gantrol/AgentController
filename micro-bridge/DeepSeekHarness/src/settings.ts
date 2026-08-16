/** Durable, non-secret configuration owned by the external bridge bundle. */

import { randomUUID } from 'node:crypto'
import { mkdir, readFile, rename, rm, writeFile } from 'node:fs/promises'
import { homedir } from 'node:os'
import { dirname, join } from 'node:path'
import {
  DEFAULT_VOICE_SETTINGS,
  LOCAL_RUNNERS,
  LOCAL_START_MODES,
  VOICE_SETUP_VERSION,
  VOICE_PROVIDERS,
  type LocalRunner,
  type LocalStartMode,
  type VoiceProvider,
  type QuickModelRef,
  type VoiceSettings,
  type VoiceSettingsDocument,
} from './voice-contract.ts'

export {
  DEFAULT_VOICE_SETTINGS,
  VOICE_PROVIDERS,
  type VoiceProvider,
  type QuickModelRef,
  type VoiceSettings,
  type VoiceSettingsDocument,
} from './voice-contract.ts'

const CREDENTIAL_REF = /^[A-Za-z_][A-Za-z0-9_]*$/u
const LANGUAGE_TAG = /^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8}){0,3}$/u

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function requiredString(value: unknown, field: string, max = 200): string {
  if (typeof value !== 'string' || value.trim() === '' || value.length > max) {
    throw new TypeError(`${field} must be a non-empty string no longer than ${String(max)} characters`)
  }
  return value.trim()
}

function optionalString(value: unknown, field: string, max = 2_048): string {
  if (typeof value !== 'string' || value.length > max) {
    throw new TypeError(`${field} must be a string no longer than ${String(max)} characters`)
  }
  return value.trim()
}

function stringArray(value: unknown, field: string, maxItems = 64): string[] {
  if (!Array.isArray(value) || value.length > maxItems) {
    throw new TypeError(`${field} must be an array with no more than ${String(maxItems)} entries`)
  }
  return value.map((item, index) => optionalString(item, `${field}[${String(index)}]`, 2_048))
}

function credentialRef(value: unknown, field: string): string {
  const ref = optionalString(value, field, 128)
  if (ref !== '' && !CREDENTIAL_REF.test(ref)) {
    throw new TypeError(`${field} must be an environment-variable style credential reference`)
  }
  return ref
}

function quickModelRef(value: unknown, field: string): QuickModelRef | undefined {
  if (value === undefined || value === null) return undefined
  if (!isPlainObject(value)) throw new TypeError(`${field} must be a model reference`)
  return {
    provider: requiredString(value.provider, `${field}.provider`),
    model: requiredString(value.model, `${field}.model`),
  }
}

function loopbackHost(hostname: string): boolean {
  const host = hostname.toLowerCase()
  return host === 'localhost' || host === '127.0.0.1' || host === '[::1]' || host === '::1'
}

/** Validate a local OpenAI-compatible base URL without creating an SSRF-with-secret primitive. */
export function normalizeLocalBaseUrl(value: unknown): string {
  const text = requiredString(value, 'localBaseUrl', 2_048).replace(/\/+$/u, '')
  let url: URL
  try {
    url = new URL(text)
  } catch {
    throw new TypeError('localBaseUrl must be a valid URL')
  }
  if (url.protocol !== 'http:' || !loopbackHost(url.hostname) || url.username !== '' || url.password !== '') {
    throw new TypeError('localBaseUrl must be an unauthenticated http:// loopback URL')
  }
  return url.toString().replace(/\/+$/u, '')
}

/** Validate the stream endpoint used by a locally managed or manual ASR service. */
export function normalizeLocalStreamUrl(value: unknown): string {
  const text = requiredString(value, 'localStreamUrl', 2_048)
  let url: URL
  try {
    url = new URL(text)
  } catch {
    throw new TypeError('localStreamUrl must be a valid URL')
  }
  if (url.protocol !== 'ws:' || !loopbackHost(url.hostname) || url.username !== '' || url.password !== '') {
    throw new TypeError('localStreamUrl must be an unauthenticated ws:// loopback URL')
  }
  return url.toString()
}

/** Validate the readiness endpoint without allowing the managed process to probe the network. */
export function normalizeLocalHealthUrl(value: unknown): string {
  const text = requiredString(value, 'localHealthUrl', 2_048)
  let url: URL
  try {
    url = new URL(text)
  } catch {
    throw new TypeError('localHealthUrl must be a valid URL')
  }
  if (url.protocol !== 'http:' || !loopbackHost(url.hostname) || url.username !== '' || url.password !== '') {
    throw new TypeError('localHealthUrl must be an unauthenticated http:// loopback URL')
  }
  return url.toString()
}

function migratedStreamUrl(value: Record<string, unknown>): string {
  if (value.localStreamUrl !== undefined) return normalizeLocalStreamUrl(value.localStreamUrl)
  if (value.localBaseUrl === undefined) return DEFAULT_VOICE_SETTINGS.localStreamUrl
  const legacy = normalizeLocalBaseUrl(value.localBaseUrl)
  const url = new URL(legacy)
  url.protocol = 'ws:'
  url.pathname = `${url.pathname.replace(/\/+$/u, '')}/stream`
  return normalizeLocalStreamUrl(url.toString())
}

function migratedHealthUrl(value: Record<string, unknown>): string {
  if (value.localHealthUrl !== undefined) return normalizeLocalHealthUrl(value.localHealthUrl)
  if (value.localBaseUrl === undefined) return DEFAULT_VOICE_SETTINGS.localHealthUrl
  const legacy = new URL(normalizeLocalBaseUrl(value.localBaseUrl))
  legacy.pathname = '/health'
  legacy.search = ''
  legacy.hash = ''
  return normalizeLocalHealthUrl(legacy.toString())
}

/** Validate the remote streaming endpoint; plaintext ws is allowed only on loopback. */
export function normalizeRemoteUrl(value: unknown): string {
  const text = optionalString(value, 'remoteUrl', 2_048)
  if (text === '') return ''
  let url: URL
  try {
    url = new URL(text)
  } catch {
    throw new TypeError('remoteUrl must be a valid WebSocket URL')
  }
  const secure = url.protocol === 'wss:'
  const localPlaintext = url.protocol === 'ws:' && loopbackHost(url.hostname)
  if ((!secure && !localPlaintext) || url.username !== '' || url.password !== '') {
    throw new TypeError('remoteUrl must use wss://, or ws:// on loopback')
  }
  return url.toString()
}

/** Strictly decode settings received from disk or the same-origin settings UI. */
export function parseVoiceSettings(value: unknown): VoiceSettings {
  if (!isPlainObject(value)) throw new TypeError('settings must be an object')
  const provider = value.provider
  if (typeof provider !== 'string' || !VOICE_PROVIDERS.includes(provider as VoiceProvider)) {
    throw new TypeError('provider is not supported')
  }
  const language = optionalString(value.language ?? '', 'language', 35)
  if (language !== '' && !LANGUAGE_TAG.test(language)) {
    throw new TypeError('language must be empty (automatic) or a BCP-47 language tag')
  }
  if (typeof value.autoSubmit !== 'boolean') throw new TypeError('autoSubmit must be boolean')
  const localStartModeValue = value.localStartMode ?? 'manual'
  if (typeof localStartModeValue !== 'string'
    || !LOCAL_START_MODES.includes(localStartModeValue as LocalStartMode)) {
    throw new TypeError('localStartMode is not supported')
  }
  const localRunnerValue = value.localRunner ?? DEFAULT_VOICE_SETTINGS.localRunner
  if (typeof localRunnerValue !== 'string'
    || !LOCAL_RUNNERS.includes(localRunnerValue as LocalRunner)) {
    throw new TypeError('localRunner is not supported')
  }
  const localScriptPath = optionalString(
    value.localScriptPath ?? DEFAULT_VOICE_SETTINGS.localScriptPath,
    'localScriptPath',
  )
  const localStartupTimeoutMilliseconds = value.localStartupTimeoutMilliseconds
    ?? DEFAULT_VOICE_SETTINGS.localStartupTimeoutMilliseconds
  if (!Number.isInteger(localStartupTimeoutMilliseconds)
    || typeof localStartupTimeoutMilliseconds !== 'number'
    || localStartupTimeoutMilliseconds < 5_000
    || localStartupTimeoutMilliseconds > 600_000) {
    throw new TypeError('localStartupTimeoutMilliseconds must be an integer from 5000 to 600000')
  }
  const remoteUrl = normalizeRemoteUrl(value.remoteUrl)
  if (provider === 'remote-websocket' && remoteUrl === '') {
    throw new TypeError('remoteUrl is required for the remote WebSocket provider')
  }
  const quickModelA = quickModelRef(value.quickModelA, 'quickModelA')
  const quickModelB = quickModelRef(value.quickModelB, 'quickModelB')
  return {
    provider: provider as VoiceProvider,
    language,
    autoSubmit: value.autoSubmit,
    setupCompleted: value.setupCompleted === true && value.setupVersion === VOICE_SETUP_VERSION,
    setupVersion: VOICE_SETUP_VERSION,
    localStreamUrl: migratedStreamUrl(value),
    localHealthUrl: migratedHealthUrl(value),
    localModel: requiredString(value.localModel, 'localModel'),
    localCredentialRef: credentialRef(value.localCredentialRef, 'localCredentialRef'),
    localStartMode: localStartModeValue as LocalStartMode,
    localRunner: localRunnerValue as LocalRunner,
    localScriptPath,
    localScriptArguments: stringArray(
      value.localScriptArguments ?? DEFAULT_VOICE_SETTINGS.localScriptArguments,
      'localScriptArguments',
    ),
    localWorkingDirectory: optionalString(
      value.localWorkingDirectory ?? DEFAULT_VOICE_SETTINGS.localWorkingDirectory,
      'localWorkingDirectory',
    ),
    localStartupTimeoutMilliseconds,
    remoteUrl,
    remoteModel: optionalString(value.remoteModel, 'remoteModel'),
    remoteCredentialRef: credentialRef(value.remoteCredentialRef, 'remoteCredentialRef'),
    ...(quickModelA === undefined ? {} : { quickModelA }),
    ...(quickModelB === undefined ? {} : { quickModelB }),
  }
}

export function defaultSettingsPath(): string {
  const explicit = process.env.DSH_MICRO_BRIDGE_SETTINGS_FILE
  if (explicit !== undefined && explicit.trim() !== '') return explicit
  const home = process.env.DSH_HOME ?? join(homedir(), '.dsh')
  return join(home, 'storages', 'dsh-micro-bridge-deepseek-harness.json')
}

/** Serialized compare-and-set store, so two open settings pages cannot silently overwrite each other. */
export class VoiceSettingsStore {
  private document: VoiceSettingsDocument | undefined
  private tail: Promise<void> = Promise.resolve()

  constructor(private readonly path = defaultSettingsPath()) {}

  async get(): Promise<VoiceSettingsDocument> {
    await this.tail
    if (this.document !== undefined) return structuredClone(this.document)
    this.document = await this.read()
    return structuredClone(this.document)
  }

  async save(expectedRevision: number, value: unknown): Promise<VoiceSettingsDocument> {
    const settings = parseVoiceSettings(value)
    let result: VoiceSettingsDocument | undefined
    const operation = this.tail.then(async () => {
      const current = this.document ?? await this.read()
      if (current.revision !== expectedRevision) {
        throw new SettingsRevisionConflict(current)
      }
      const next = { revision: current.revision + 1, settings }
      await this.write(next)
      this.document = next
      result = structuredClone(next)
    })
    this.tail = operation.catch(() => {})
    await operation
    return result as VoiceSettingsDocument
  }

  async dispose(): Promise<void> {
    await this.tail
  }

  private async read(): Promise<VoiceSettingsDocument> {
    try {
      const parsed = JSON.parse(await readFile(this.path, 'utf8')) as unknown
      if (!isPlainObject(parsed) || !Number.isInteger(parsed.revision)
        || typeof parsed.revision !== 'number' || parsed.revision < 0) {
        throw new TypeError('settings document has an invalid revision')
      }
      return { revision: parsed.revision, settings: parseVoiceSettings(parsed.settings) }
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === 'ENOENT') {
        return { revision: 0, settings: structuredClone(DEFAULT_VOICE_SETTINGS) }
      }
      throw error
    }
  }

  private async write(document: VoiceSettingsDocument): Promise<void> {
    await mkdir(dirname(this.path), { recursive: true })
    const temporary = `${this.path}.${process.pid.toString()}.${randomUUID()}.tmp`
    try {
      await writeFile(temporary, `${JSON.stringify(document, null, 2)}\n`, {
        encoding: 'utf8',
        flag: 'wx',
        mode: 0o600,
      })
      await rename(temporary, this.path)
    } finally {
      await rm(temporary, { force: true })
    }
  }
}

export class SettingsRevisionConflict extends Error {
  constructor(readonly current: VoiceSettingsDocument) {
    super('Voice settings changed in another page; reload and try again.')
    this.name = 'SettingsRevisionConflict'
  }
}
