import { randomUUID } from 'node:crypto'
import { mkdtemp, rm } from 'node:fs/promises'
import { createServer as createHttpServer, type Server as HttpServer } from 'node:http'
import type { IncomingMessage, ServerResponse } from 'node:http'
import { connect } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import * as Bridge from '../src/index.ts'

const originalInternals = { ...Bridge.internals }
let root: string | undefined
let dispose: (() => void | Promise<void>) | undefined
let httpServer: HttpServer | undefined

afterEach(async () => {
  await new Promise<void>((resolve) => {
    if (httpServer === undefined) {
      resolve()
      return
    }
    httpServer.close(() => { resolve() })
  })
  httpServer = undefined
  await dispose?.()
  dispose = undefined
  Object.assign(Bridge.internals, originalInternals)
  if (root !== undefined) await rm(root, { recursive: true, force: true })
  root = undefined
  vi.restoreAllMocks()
})

function testEndpoint(directory: string): string {
  return process.platform === 'win32'
    ? String.raw`\\.\pipe\agentcontroller-dsh-test-${String(process.pid)}-${randomUUID()}`
    : join(directory, 'micro.sock')
}

async function request(endpoint: string, payload: unknown): Promise<Bridge.MicroResponse> {
  return await new Promise<Bridge.MicroResponse>((resolve, reject) => {
    const socket = connect(endpoint)
    const chunks: Buffer[] = []
    socket.once('connect', () => { socket.end(`${JSON.stringify(payload)}\n`) })
    socket.on('data', (chunk: Buffer) => { chunks.push(chunk) })
    socket.once('end', () => {
      try {
        resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')) as Bridge.MicroResponse)
      } catch (error) {
        reject(error)
      }
    })
    socket.once('error', reject)
  })
}

async function mount(rows: Array<Record<string, unknown>> = []): Promise<{
  endpoint: string
  openWebUi: ReturnType<typeof vi.fn>
  eventHandler: (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
  reportHandler: (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
  controlHandler: (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
  voiceButtonHandler: (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
}> {
  root = await mkdtemp(join(tmpdir(), 'agentcontroller-dsh-plugin-'))
  const endpoint = testEndpoint(root)
  const openWebUi = vi.fn(async () => 41_944)
  Bridge.internals.pipeEndpoint = () => endpoint
  Bridge.internals.openWebUi = openWebUi

  const routeDisposers: Array<() => void> = []
  const routeHandlers = new Map<
    string,
    (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
  >()
  const context = {
    logger: { warn: vi.fn(), error: vi.fn() },
    webServer: {
      port: 30_880,
      register: (route: {
        path: string
        handler: (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
      }) => {
        routeHandlers.set(route.path, route.handler)
        const release = vi.fn()
        routeDisposers.push(release)
        return release
      },
    },
    get: (name: string) => name === 'apiProxy' ? {
      sessions: {
        list: vi.fn(async (call: { rpcId: string }) => ({
          rpcId: call.rpcId,
          result: { ok: true, value: { items: rows } },
        })),
      },
    } : undefined,
    effect: async (factory: () => Promise<() => void | Promise<void>>): Promise<void> => {
      dispose = await factory()
    },
  }
  await Bridge.apply(context as unknown as Parameters<typeof Bridge.apply>[0])
  const eventHandler = routeHandlers.get(Bridge.MICRO_EVENTS_ENDPOINT)
  const reportHandler = routeHandlers.get(Bridge.MICRO_REPORT_ENDPOINT)
  const controlHandler = routeHandlers.get(Bridge.MICRO_CONTROL_ENDPOINT)
  const voiceButtonHandler = routeHandlers.get(Bridge.MICRO_VOICE_BUTTON_ENDPOINT)
  if (eventHandler === undefined) throw new Error('event route was not registered')
  if (reportHandler === undefined) throw new Error('report route was not registered')
  if (controlHandler === undefined) throw new Error('control route was not registered')
  if (voiceButtonHandler === undefined) throw new Error('voice-button route was not registered')
  return {
    endpoint,
    openWebUi,
    eventHandler,
    reportHandler,
    controlHandler,
    voiceButtonHandler,
  }
}

const baseRequest = { version: Bridge.MICRO_PROTOCOL_VERSION, source: 'codex-micro' } as const

describe('external DeepSeek Harness host bundle', () => {
  it('opens a dedicated Windows app surface from WSL without a command shell', async () => {
    const runNativeCommand = vi.fn(async () => {})
    const launchWindowsAppBrowser = vi.fn(async () => 27_001)
    Bridge.internals.platform = 'linux'
    Bridge.internals.wslWindowsAppBrowser = () =>
      '/mnt/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'
    Bridge.internals.wslWindowsUrlHandler = () =>
      '/mnt/c/Windows/System32/rundll32.exe'
    Bridge.internals.runNativeCommand = runNativeCommand
    Bridge.internals.launchWindowsAppBrowser = launchWindowsAppBrowser

    await Bridge.internals.openWebUi(
      'http://127.0.0.1:3080/?codexMicroSurface=1',
      new AbortController().signal,
    )

    expect(launchWindowsAppBrowser).toHaveBeenCalledWith(
      '/mnt/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
      'http://127.0.0.1:3080/?codexMicroSurface=1',
    )
    expect(runNativeCommand).not.toHaveBeenCalled()
  })

  it('falls back to the Windows URL handler from WSL when app mode is unavailable', async () => {
    const runNativeCommand = vi.fn(async () => {})
    Bridge.internals.platform = 'linux'
    Bridge.internals.wslWindowsAppBrowser = () => undefined
    Bridge.internals.wslWindowsUrlHandler = () =>
      '/mnt/c/Windows/System32/rundll32.exe'
    Bridge.internals.runNativeCommand = runNativeCommand

    await Bridge.internals.openWebUi(
      'http://127.0.0.1:3080/?codexMicroSurface=1',
      new AbortController().signal,
    )

    expect(runNativeCommand).toHaveBeenCalledWith(
      '/mnt/c/Windows/System32/rundll32.exe',
      [
        'url.dll,FileProtocolHandler',
        'http://127.0.0.1:3080/?codexMicroSurface=1',
      ],
      expect.any(AbortSignal),
    )
  })

  it('accepts the versioned request contract on loopback HTTP for WSL hosts', async () => {
    const { controlHandler } = await mount([
      { sessionId: 'wsl-session', updatedAt: 12, running: false, blank: false },
    ])
    httpServer = createHttpServer((req, res) => { void controlHandler(req, res) })
    await new Promise<void>((resolve, reject) => {
      httpServer?.once('error', reject)
      httpServer?.listen(0, '127.0.0.1', () => { resolve() })
    })
    const address = httpServer.address()
    if (address === null || typeof address === 'string') throw new Error('missing test port')

    const result = await fetch(
      `http://127.0.0.1:${String(address.port)}${Bridge.MICRO_CONTROL_ENDPOINT}`,
      {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ ...baseRequest, action: 'state/read' }),
      },
    )

    expect(result.status).toBe(200)
    expect(result.headers.get('cache-control')).toBe('no-store')
    await expect(result.json()).resolves.toMatchObject({
      success: true,
      state: { sessions: [{ id: 'wsl-session' }] },
    })
  })

  it('opens at most one dedicated web surface while startup is pending', async () => {
    const { endpoint, openWebUi } = await mount()
    const first = await request(endpoint, { ...baseRequest, action: 'activate' })
    const second = await request(endpoint, { ...baseRequest, action: 'activate' })

    expect(first).toMatchObject({ success: true, status: 'opening', windowProcessId: 41_944 })
    expect(second).toMatchObject({ success: true, status: 'opening', windowProcessId: 41_944 })
    expect(openWebUi).toHaveBeenCalledOnce()
    expect(openWebUi.mock.calls[0]?.[0]).toContain('codexMicroSurface=1')
  })

  it('retries a native open that never produces a connected surface', async () => {
    let now = 10_000
    Bridge.internals.now = () => now
    const { endpoint, openWebUi } = await mount()

    await request(endpoint, { ...baseRequest, action: 'activate' })
    now += 11_999
    await request(endpoint, { ...baseRequest, action: 'activate' })
    expect(openWebUi).toHaveBeenCalledOnce()

    now += 1
    const retry = await request(endpoint, { ...baseRequest, action: 'activate' })

    expect(retry).toMatchObject({ success: true, status: 'opening' })
    expect(openWebUi).toHaveBeenCalledTimes(2)
  })

  it('projects recent top-level sessions and advertises native capabilities', async () => {
    const { endpoint } = await mount([
      { sessionId: 'old', updatedAt: 1, running: false, blank: false, cwd: 'D:\\work\\old' },
      { sessionId: 'child', updatedAt: 9, running: true, blank: false, parentSessionId: 'old' },
      { sessionId: 'new', updatedAt: 10, running: true, blank: false,
        projections: { values: { title: 'Current task' } } },
      { sessionId: 'blank', updatedAt: 11, running: false, blank: true },
    ])
    const result = await request(endpoint, { ...baseRequest, action: 'state/read' })

    expect(result.success).toBe(true)
    expect(result.state?.sessions.map(row => row.id)).toEqual(['new', 'old'])
    expect(result.state?.sessions.map(row => row.status)).toEqual(['running', 'idle'])
    expect(result.state?.sessions[0]?.displayTitle).toBe('Current task')
    expect(result.state?.capabilities.voiceInput).toBe(true)
    expect(result.state?.capabilities.knobSettings).toBe(true)
    expect(result.state?.capabilities.actions).toContain('interaction/approve')
    expect(result.state?.capabilities.actions).toContain('view/toggle-chat-trajectory')
    expect(result.state?.capabilities.actions).toContain('composer/submit')
    expect(result.state?.components).toMatchObject({
      adapter: 'ready',
      browser: 'disconnected',
    })
  })

  it('merges browser-owned completion, waiting, and error states into state reads', async () => {
    const {
      endpoint,
      eventHandler,
      reportHandler,
    } = await mount([
      { sessionId: 'running', updatedAt: 50, running: true, blank: false },
      { sessionId: 'completed', updatedAt: 40, running: false, blank: false },
      { sessionId: 'waiting', updatedAt: 30, running: true, blank: false },
      { sessionId: 'error', updatedAt: 20, running: false, blank: false },
      { sessionId: 'idle', updatedAt: 10, running: false, blank: false },
    ])
    httpServer = createHttpServer((req, res) => {
      if (req.url?.startsWith(Bridge.MICRO_EVENTS_ENDPOINT) === true) {
        void eventHandler(req, res)
      } else if (req.url === Bridge.MICRO_REPORT_ENDPOINT) {
        void reportHandler(req, res)
      } else {
        res.writeHead(404)
        res.end()
      }
    })
    await new Promise<void>((resolve, reject) => {
      httpServer?.once('error', reject)
      httpServer?.listen(0, '127.0.0.1', () => { resolve() })
    })
    const address = httpServer.address()
    if (address === null || typeof address === 'string') throw new Error('missing test port')
    const origin = `http://127.0.0.1:${String(address.port)}`
    const streamAbort = new AbortController()
    const stream = await fetch(
      `${origin}${Bridge.MICRO_EVENTS_ENDPOINT}?browserId=status-browser`,
      { signal: streamAbort.signal },
    )
    const reported = await fetch(`${origin}${Bridge.MICRO_REPORT_ENDPOINT}`, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({
        version: Bridge.MICRO_PROTOCOL_VERSION,
        browserId: 'status-browser',
        currentSessionId: 'running',
        visible: true,
        focused: true,
        surface: 'dedicated',
        navigationDepth: 0,
        sessionStates: [
          { id: 'running', status: 'running' },
          { id: 'completed', status: 'completed' },
          { id: 'waiting', status: 'waiting' },
          { id: 'error', status: 'error' },
          { id: 'idle', status: 'idle' },
        ],
      }),
    })
    expect(reported.status).toBe(204)

    const result = await request(endpoint, { ...baseRequest, action: 'state/read' })
    expect(result.state?.sessions.map(row => [row.id, row.status])).toEqual([
      ['running', 'running'],
      ['completed', 'completed'],
      ['waiting', 'waiting'],
      ['error', 'error'],
      ['idle', 'idle'],
    ])

    streamAbort.abort()
    await stream.body?.cancel().catch(() => {})
  })

  it('rejects unsupported bridge commands', async () => {
    const { endpoint, openWebUi } = await mount()
    const invalid = await request(endpoint, { ...baseRequest, action: 'mouse/click' })

    expect(invalid.success).toBe(false)
    expect(openWebUi).not.toHaveBeenCalled()
  })

  it('relays the one DeepSeek voice button to the keypad and returns its result', async () => {
    const { endpoint, voiceButtonHandler } = await mount()
    httpServer = createHttpServer((req, res) => { void voiceButtonHandler(req, res) })
    await new Promise<void>((resolve, reject) => {
      httpServer?.once('error', reject)
      httpServer?.listen(0, '127.0.0.1', () => { resolve() })
    })
    const address = httpServer.address()
    if (address === null || typeof address === 'string') throw new Error('missing test port')

    const button = fetch(
      `http://127.0.0.1:${String(address.port)}${Bridge.MICRO_VOICE_BUTTON_ENDPOINT}`,
      {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ sessionId: 'session-voice' }),
      },
    )
    const polled = await request(endpoint, { ...baseRequest, action: 'voice/request' })
    const requestId = polled.voiceRequest?.requestId

    expect(polled.voiceRequest).toMatchObject({
      command: 'toggle',
      sessionId: 'session-voice',
    })
    expect(requestId).toEqual(expect.any(String))

    const completed = await request(endpoint, {
      ...baseRequest,
      action: 'voice/result',
      requestId,
      success: true,
      active: true,
      message: 'The keypad microphone is listening.',
    })
    const buttonResponse = await button

    expect(completed).toMatchObject({ success: true, status: 'completed' })
    expect(buttonResponse.status).toBe(200)
    await expect(buttonResponse.json()).resolves.toMatchObject({
      success: true,
      active: true,
      message: 'The keypad microphone is listening.',
    })
  })
})
