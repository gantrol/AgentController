/** Host-side streaming audio gateway for local Qwen or remote WebSocket ASR. */

import type { IncomingMessage } from 'node:http'
import type { Duplex } from 'node:stream'
import WebSocket, { WebSocketServer } from 'ws'
import { LocalAsrRuntime } from './local-asr-runtime.ts'
import { VoiceSettingsStore } from './settings.ts'
import type {
  HostVoiceFrame as BrowserVoiceFrame,
  LocalRuntimeStatus,
  VoiceProvider,
  VoiceSettings,
} from './voice-contract.ts'

const SAMPLE_RATE = 16_000
const CHANNELS = 1
const BYTES_PER_SAMPLE = 2
const MAX_CLIENT_FRAME_BYTES = 256 * 1024
const MAX_PROVIDER_MESSAGE_BYTES = 64 * 1024
const CONNECT_TIMEOUT_MS = 12_000
const READY_TIMEOUT_MS = 30_000
const STOP_TIMEOUT_MS = 30_000

interface CredentialsLike {
  resolve(ref: string): Promise<{ value: string; source?: string } | undefined>
}

interface LoggerLike {
  warn(message: string | Error): void
}

interface ProviderSession {
  push(pcm: Buffer): void
  stop(): Promise<void>
  cancel(): void
}

function send(socket: WebSocket, frame: BrowserVoiceFrame): void {
  if (socket.readyState === WebSocket.OPEN) socket.send(JSON.stringify(frame))
}

function message(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function isLoopbackAddress(value: string | undefined): boolean {
  if (value === undefined) return false
  const normalized = value.toLowerCase().split('%', 1)[0]
  return normalized === '127.0.0.1' || normalized === '::1' || normalized === '::ffff:127.0.0.1'
}

function trustedUpgrade(request: IncomingMessage): boolean {
  if (!isLoopbackAddress(request.socket.remoteAddress)) return false
  const origin = request.headers.origin
  if (origin === undefined) return true
  try {
    const parsed = new URL(origin)
    return parsed.host === request.headers.host && (parsed.protocol === 'http:' || parsed.protocol === 'https:')
  } catch {
    return false
  }
}

function rejectUpgrade(socket: Duplex): void {
  socket.end([
    'HTTP/1.1 403 Forbidden',
    'Connection: close',
    'Content-Type: text/plain; charset=utf-8',
    'Content-Length: 9',
    '',
    'forbidden',
  ].join('\r\n'))
}

type ProviderFrame = BrowserVoiceFrame | { type: 'ready' }

function parseProviderFrame(data: WebSocket.RawData): ProviderFrame {
  const bytes = Buffer.isBuffer(data) ? data : Buffer.from(data as ArrayBuffer)
  if (bytes.length > MAX_PROVIDER_MESSAGE_BYTES) throw new Error('ASR frame is too large.')
  let value: unknown
  try {
    value = JSON.parse(bytes.toString('utf8')) as unknown
  } catch {
    throw new Error('ASR returned invalid JSON.')
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('ASR returned an invalid frame.')
  }
  const fields = value as Record<string, unknown>
  switch (fields.type) {
    case 'ready':
      return { type: 'ready' }
    case 'partial':
    case 'final':
      if (typeof fields.text !== 'string' || fields.text.length > MAX_PROVIDER_MESSAGE_BYTES) {
        throw new Error('ASR returned invalid text.')
      }
      return { type: fields.type, text: fields.text }
    case 'done':
      return { type: 'done' }
    case 'error':
      return {
        type: 'error',
        message: typeof fields.message === 'string' && fields.message.length <= 1_024
          ? fields.message
          : 'ASR reported an error.',
      }
    default:
      throw new Error('ASR returned an unsupported frame.')
  }
}

class StreamingProviderSession implements ProviderSession {
  private socket: WebSocket | undefined
  private stopped = false
  private completed = false
  private finishStop: (() => void) | undefined

  private constructor(
    private readonly provider: Exclude<VoiceProvider, 'system'>,
    private readonly emit: (frame: BrowserVoiceFrame) => void,
  ) {}

  static async open(
    settings: VoiceSettings,
    credentials: CredentialsLike,
    runtime: LocalAsrRuntime,
    emit: (frame: BrowserVoiceFrame) => void,
  ): Promise<StreamingProviderSession> {
    const provider = settings.provider
    if (provider === 'system') throw new Error('System speech recognition runs in the browser.')
    const credentialRef = provider === 'local-qwen'
      ? settings.localCredentialRef
      : settings.remoteCredentialRef
    const resolved = credentialRef === '' ? undefined : await credentials.resolve(credentialRef)
    if (provider === 'local-qwen') await runtime.ensureReady(settings, resolved?.value)
    const instance = new StreamingProviderSession(provider, emit)
    await instance.connect(
      provider === 'local-qwen' ? settings.localStreamUrl : settings.remoteUrl,
      provider === 'local-qwen' ? settings.localModel : settings.remoteModel,
      settings.language,
      resolved?.value,
    )
    return instance
  }

  push(pcm: Buffer): void {
    if (!this.stopped && this.socket?.readyState === WebSocket.OPEN) this.socket.send(pcm)
  }

  async stop(): Promise<void> {
    if (this.stopped) return
    this.stopped = true
    const socket = this.socket
    if (socket === undefined || socket.readyState !== WebSocket.OPEN) return
    const completion = new Promise<void>(resolve => { this.finishStop = resolve })
    socket.send(JSON.stringify({ type: 'stop' }))
    const timer = setTimeout(() => { this.finish() }, STOP_TIMEOUT_MS)
    timer.unref()
    await completion
    clearTimeout(timer)
    if (socket.readyState === WebSocket.OPEN) socket.close(1000, 'complete')
  }

  cancel(): void {
    this.stopped = true
    const socket = this.socket
    if (socket?.readyState === WebSocket.OPEN) socket.send(JSON.stringify({ type: 'cancel' }))
    socket?.close(1000, 'cancelled')
    this.finish()
  }

  private async connect(url: string, model: string, language: string, apiKey?: string): Promise<void> {
    const socket = new WebSocket(url, {
      headers: apiKey === undefined ? undefined : { authorization: `Bearer ${apiKey}` },
      maxPayload: MAX_PROVIDER_MESSAGE_BYTES,
      perMessageDeflate: false,
    })
    this.socket = socket
    let opened = false
    let ready = false
    let finishOpen: ((error?: Error) => void) | undefined
    let finishReady: ((error?: Error) => void) | undefined
    const openPromise = new Promise<void>((resolve, reject) => {
      let settled = false
      finishOpen = error => {
        if (settled) return
        settled = true
        if (error === undefined) resolve()
        else reject(error)
      }
    })
    const readyPromise = new Promise<void>((resolve, reject) => {
      let settled = false
      finishReady = error => {
        if (settled) return
        settled = true
        if (error === undefined) resolve()
        else reject(error)
      }
    })

    socket.on('open', () => {
      opened = true
      finishOpen?.()
      socket.send(JSON.stringify({
        type: 'start',
        protocol: 'dsh-stream-v1',
        encoding: 'pcm_s16le',
        sampleRate: SAMPLE_RATE,
        channels: CHANNELS,
        ...(language === '' ? {} : { language }),
        model,
      }))
    })
    socket.on('message', data => {
      try {
        const frame = parseProviderFrame(data)
        if (frame.type === 'ready') {
          ready = true
          finishReady?.()
          return
        }
        if (!ready) throw new Error('ASR sent recognition output before its ready frame.')
        this.emit(frame)
        if (frame.type === 'done' || frame.type === 'error') this.finish()
      } catch (error) {
        const normalized = error instanceof Error ? error : new Error(message(error))
        finishReady?.(normalized)
        this.emit({ type: 'error', message: normalized.message })
        this.cancel()
      }
    })
    socket.once('error', error => {
      const normalized = new Error(`${this.provider === 'local-qwen' ? 'Local' : 'Remote'} streaming ASR connection failed: ${error.message}`)
      if (!opened) finishOpen?.(normalized)
      if (!ready) finishReady?.(normalized)
      else if (!this.completed) this.emit({ type: 'error', message: normalized.message })
      this.finish()
    })
    socket.once('close', () => {
      if (!opened) finishOpen?.(new Error('ASR connection closed before opening.'))
      if (!ready) finishReady?.(new Error('ASR connection closed before the ready frame.'))
      this.finish()
    })

    const connectTimer = setTimeout(() => {
      finishOpen?.(new Error('ASR connection timed out.'))
      socket.terminate()
    }, CONNECT_TIMEOUT_MS)
    connectTimer.unref()
    try {
      await openPromise
    } finally {
      clearTimeout(connectTimer)
    }
    const readyTimer = setTimeout(() => {
      finishReady?.(new Error('ASR protocol ready frame timed out.'))
      socket.terminate()
    }, READY_TIMEOUT_MS)
    readyTimer.unref()
    try {
      await readyPromise
    } finally {
      clearTimeout(readyTimer)
    }
  }

  private finish(): void {
    if (this.completed) return
    this.completed = true
    this.finishStop?.()
  }
}

class VoiceConnection {
  private provider: ProviderSession | undefined
  private started = false
  private stopping = false

  constructor(
    private readonly socket: WebSocket,
    private readonly settings: VoiceSettingsStore,
    private readonly credentials: CredentialsLike,
    private readonly runtime: LocalAsrRuntime,
    private readonly logger: LoggerLike,
  ) {}

  async text(raw: WebSocket.RawData): Promise<void> {
    const bytes = Buffer.isBuffer(raw) ? raw : Buffer.from(raw as ArrayBuffer)
    if (bytes.length > MAX_CLIENT_FRAME_BYTES) throw new Error('Voice control frame is too large.')
    let value: unknown
    try {
      value = JSON.parse(bytes.toString('utf8')) as unknown
    } catch {
      throw new Error('Voice control frame is invalid JSON.')
    }
    if (typeof value !== 'object' || value === null || Array.isArray(value)) {
      throw new Error('Voice control frame is invalid.')
    }
    const type = (value as Record<string, unknown>).type
    if (type === 'start') await this.start()
    else if (type === 'stop') await this.stop()
    else if (type === 'cancel') this.cancel()
    else throw new Error('Voice control frame type is unsupported.')
  }

  binary(data: WebSocket.RawData): void {
    if (!this.started || this.stopping || this.provider === undefined) return
    const bytes = Buffer.isBuffer(data) ? data : Buffer.from(data as ArrayBuffer)
    if (bytes.length > MAX_CLIENT_FRAME_BYTES || bytes.length % BYTES_PER_SAMPLE !== 0) {
      throw new Error('Voice PCM frame is invalid.')
    }
    this.provider.push(bytes)
  }

  cancel(): void {
    this.stopping = true
    this.provider?.cancel()
  }

  fail(error: unknown): void {
    this.logger.warn(error instanceof Error ? error : new Error(String(error)))
    send(this.socket, { type: 'error', message: message(error) })
    this.cancel()
  }

  private async start(): Promise<void> {
    if (this.started) throw new Error('Voice input is already active.')
    const config = (await this.settings.get()).settings
    if (!config.setupCompleted) throw new Error('VOICE_SETUP_REQUIRED: Configure and test DeepSeek Harness voice input first.')
    if (config.provider === 'system') throw new Error('System speech recognition runs directly in the browser.')
    const emit = (frame: BrowserVoiceFrame): void => { send(this.socket, frame) }
    this.provider = await StreamingProviderSession.open(config, this.credentials, this.runtime, emit)
    this.started = true
    send(this.socket, { type: 'ready', provider: config.provider })
  }

  private async stop(): Promise<void> {
    if (!this.started || this.stopping || this.provider === undefined) return
    this.stopping = true
    await this.provider.stop()
  }
}

/** Owns the same-origin WebSocket acceptor and all bridge audio sessions. */
export class VoiceGateway {
  private readonly server = new WebSocketServer({ noServer: true, maxPayload: MAX_CLIENT_FRAME_BYTES })

  constructor(
    private readonly settings: VoiceSettingsStore,
    private readonly credentials: CredentialsLike,
    private readonly runtime: LocalAsrRuntime,
    private readonly logger: LoggerLike,
  ) {
    this.server.on('connection', socket => {
      const connection = new VoiceConnection(socket, this.settings, this.credentials, this.runtime, this.logger)
      socket.on('message', (data, isBinary) => {
        try {
          if (isBinary) connection.binary(data)
          else void connection.text(data).catch(error => { connection.fail(error) })
        } catch (error) {
          connection.fail(error)
        }
      })
      socket.once('close', () => { connection.cancel() })
      socket.once('error', () => { connection.cancel() })
    })
  }

  async runtimeStatus(): Promise<LocalRuntimeStatus> {
    const config = (await this.settings.get()).settings
    const resolved = config.localCredentialRef === ''
      ? undefined
      : await this.credentials.resolve(config.localCredentialRef)
    return await this.runtime.inspect(config, resolved?.value)
  }

  async startRuntime(): Promise<LocalRuntimeStatus> {
    const config = (await this.settings.get()).settings
    const resolved = config.localCredentialRef === ''
      ? undefined
      : await this.credentials.resolve(config.localCredentialRef)
    return await this.runtime.start(config, resolved?.value)
  }

  async stopRuntime(): Promise<LocalRuntimeStatus> {
    await this.runtime.stop()
    return this.runtime.status()
  }

  async warmup(): Promise<void> {
    const config = (await this.settings.get()).settings
    if (!config.setupCompleted || config.provider !== 'local-qwen' || config.localStartMode !== 'with-harness') return
    const resolved = config.localCredentialRef === ''
      ? undefined
      : await this.credentials.resolve(config.localCredentialRef)
    await this.runtime.ensureReady(config, resolved?.value)
  }

  async testConfiguredProvider(settings?: VoiceSettings): Promise<void> {
    const config = settings ?? (await this.settings.get()).settings
    if (config.provider === 'system') return
    const session = await StreamingProviderSession.open(config, this.credentials, this.runtime, () => {})
    session.cancel()
  }

  handleUpgrade(request: IncomingMessage, socket: Duplex, head: Buffer): void {
    if (!trustedUpgrade(request)) {
      rejectUpgrade(socket)
      return
    }
    this.server.handleUpgrade(request, socket, head, websocket => {
      this.server.emit('connection', websocket, request)
    })
  }

  async close(): Promise<void> {
    for (const client of this.server.clients) client.terminate()
    await new Promise<void>((resolve, reject) => {
      this.server.close(error => { if (error === undefined) resolve(); else reject(error) })
    })
  }
}
