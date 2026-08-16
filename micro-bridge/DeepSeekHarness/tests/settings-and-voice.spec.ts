import { mkdtemp, readFile, rm } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'
import { WebSocketServer } from 'ws'
import { appendTranscript } from '../src/client/voice-controller.ts'
import { LocalAsrRuntime, LocalAsrRuntimeError, sanitizeRuntimeLog } from '../src/local-asr-runtime.ts'
import {
  parseVoiceSettings,
  SettingsRevisionConflict,
  VoiceSettingsStore,
  normalizeLocalBaseUrl,
  normalizeLocalHealthUrl,
  normalizeLocalStreamUrl,
  normalizeRemoteUrl,
} from '../src/settings.ts'
import { DEFAULT_VOICE_SETTINGS } from '../src/voice-contract.ts'
import { VoiceGateway } from '../src/voice-server.ts'

let root: string | undefined

afterEach(async () => {
  if (root !== undefined) await rm(root, { recursive: true, force: true })
  root = undefined
})

describe('voice configuration safety', () => {
  it('allows secrets only toward loopback local ASR and secure remote streaming', () => {
    expect(normalizeLocalBaseUrl('http://127.0.0.1:8000/v1/')).toBe('http://127.0.0.1:8000/v1')
    expect(() => normalizeLocalBaseUrl('https://asr.example.com/v1')).toThrow(/loopback/u)
    expect(normalizeLocalStreamUrl('ws://127.0.0.1:8000/v1/stream')).toBe('ws://127.0.0.1:8000/v1/stream')
    expect(() => normalizeLocalStreamUrl('wss://asr.example.com/v1/stream')).toThrow(/loopback/u)
    expect(normalizeLocalHealthUrl('http://localhost:8000/health')).toBe('http://localhost:8000/health')
    expect(normalizeRemoteUrl('wss://asr.example.com/v1/stream')).toBe('wss://asr.example.com/v1/stream')
    expect(normalizeRemoteUrl('ws://localhost:9000/stream')).toBe('ws://localhost:9000/stream')
    expect(() => normalizeRemoteUrl('ws://asr.example.com/stream')).toThrow(/wss/u)
  })

  it('uses the streaming defaults when loading a pre-streaming settings document', () => {
    const parsed = parseVoiceSettings({
      ...DEFAULT_VOICE_SETTINGS,
      localStreamUrl: undefined,
      localHealthUrl: undefined,
    })
    expect(parsed.localStreamUrl).toBe(DEFAULT_VOICE_SETTINGS.localStreamUrl)
    expect(parsed.localHealthUrl).toBe(DEFAULT_VOICE_SETTINGS.localHealthUrl)
  })

  it('serializes writes and refuses stale settings pages', async () => {
    root = await mkdtemp(join(tmpdir(), 'agentcontroller-dsh-settings-'))
    const path = join(root, 'settings.json')
    const store = new VoiceSettingsStore(path)
    const initial = await store.get()
    const saved = await store.save(initial.revision, {
      ...DEFAULT_VOICE_SETTINGS,
      autoSubmit: true,
      quickModelA: { provider: 'deepseek', model: 'model-a' },
      quickModelB: { provider: 'openai', model: 'model-b' },
    })

    await expect(store.save(initial.revision, DEFAULT_VOICE_SETTINGS))
      .rejects.toBeInstanceOf(SettingsRevisionConflict)
    expect(saved.revision).toBe(1)
    expect(JSON.parse(await readFile(path, 'utf8'))).toMatchObject({
      revision: 1,
      settings: {
        autoSubmit: true,
        quickModelA: { provider: 'deepseek', model: 'model-a' },
        quickModelB: { provider: 'openai', model: 'model-b' },
      },
    })
  })
})

describe('streaming ASR lifecycle', () => {
  it('cleans mixed Windows child-process diagnostics before displaying them', () => {
    const mixed = Buffer.concat([
      Buffer.from('[dsh-qwen-asr] Loading\r\n', 'utf8'),
      Buffer.from('wsl: proxy warning\r\n', 'utf16le'),
      Buffer.from('\u001b[0;36mready\u001b[0m\rprogress\r\n', 'utf8'),
    ])
    expect(sanitizeRuntimeLog(mixed)).toBe('[dsh-qwen-asr] Loading\nwsl: proxy warning\nready\nprogress')
  })

  it('reports a missing local startup script with a stable setup error', async () => {
    const runtime = new LocalAsrRuntime()
    const status = await runtime.inspect({ ...DEFAULT_VOICE_SETTINGS })
    expect(status).toMatchObject({
      phase: 'not-configured',
      errorCode: 'ASR_SCRIPT_NOT_CONFIGURED',
    })
    await expect(runtime.ensureReady({ ...DEFAULT_VOICE_SETTINGS }))
      .rejects.toMatchObject({ code: 'ASR_SCRIPT_NOT_CONFIGURED' } satisfies Partial<LocalAsrRuntimeError>)
  })

  it('performs the dsh-stream-v1 ready handshake before accepting a provider', async () => {
    const provider = new WebSocketServer({ host: '127.0.0.1', port: 0 })
    await new Promise<void>((resolve, reject) => {
      provider.once('listening', resolve)
      provider.once('error', reject)
    })
    const address = provider.address()
    if (address === null || typeof address === 'string') throw new Error('Test provider did not bind to TCP.')
    let startFrame: Record<string, unknown> | undefined
    provider.on('connection', socket => {
      socket.once('message', raw => {
        startFrame = JSON.parse(raw.toString()) as Record<string, unknown>
        setTimeout(() => { socket.send(JSON.stringify({ type: 'ready' })) }, 20)
      })
    })

    root = await mkdtemp(join(tmpdir(), 'agentcontroller-dsh-stream-'))
    const store = new VoiceSettingsStore(join(root, 'settings.json'))
    const runtime = new LocalAsrRuntime()
    const gateway = new VoiceGateway(
      store,
      { resolve: async () => undefined },
      runtime,
      { warn: () => {} },
    )
    await gateway.testConfiguredProvider({
      ...DEFAULT_VOICE_SETTINGS,
      provider: 'remote-websocket',
      remoteUrl: `ws://127.0.0.1:${String(address.port)}/stream`,
      remoteModel: 'test-stream-model',
    })
    expect(startFrame).toMatchObject({
      type: 'start',
      protocol: 'dsh-stream-v1',
      encoding: 'pcm_s16le',
      sampleRate: 16_000,
      channels: 1,
      model: 'test-stream-model',
    })
    await new Promise<void>((resolve, reject) => {
      provider.close(error => { if (error === undefined) resolve(); else reject(error) })
    })
  })
})

describe('dictation draft composition', () => {
  it('does not insert Western spaces into CJK text', () => {
    expect(appendTranscript('你好', '世界', 'zh-CN')).toBe('你好世界')
    expect(appendTranscript('你好', '。', 'zh-CN')).toBe('你好。')
  })

  it('separates English segments while preserving existing whitespace', () => {
    expect(appendTranscript('hello', 'world', 'en-US')).toBe('hello world')
    expect(appendTranscript('hello ', 'world', 'en-US')).toBe('hello world')
  })

  it('auto-detects transcript spacing without a language override', () => {
    expect(appendTranscript('你好', '世界', '')).toBe('你好世界')
    expect(appendTranscript('hello', 'world', '')).toBe('hello world')
  })
})
