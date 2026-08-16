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
  controlHandler: (req: IncomingMessage, res: ServerResponse) => void | Promise<void>
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
      registerUpgrade: () => {
        const release = vi.fn()
        routeDisposers.push(release)
        return release
      },
    },
    credentials: {
      resolve: vi.fn(async () => undefined),
      describe: vi.fn(async () => ({ configured: false, writable: true })),
      set: vi.fn(async () => {}),
      unset: vi.fn(async () => {}),
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
  const controlHandler = routeHandlers.get(Bridge.MICRO_CONTROL_ENDPOINT)
  if (controlHandler === undefined) throw new Error('control route was not registered')
  return { endpoint, openWebUi, controlHandler }
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
    expect(result.state?.sessions[0]?.displayTitle).toBe('Current task')
    expect(result.state?.capabilities.voiceInput).toBe(true)
    expect(result.state?.capabilities.knobSettings).toBe(true)
    expect(result.state?.capabilities.actions).toContain('interaction/approve')
    expect(result.state?.capabilities.actions).toContain('view/toggle-chat-trajectory')
    expect(result.state?.components).toMatchObject({
      adapter: 'ready',
      browser: 'disconnected',
    })
  })

  it('accepts push-to-talk edges and rejects unsupported requests', async () => {
    const { endpoint, openWebUi } = await mount()
    const start = await request(endpoint, { ...baseRequest, action: 'voice/start' })
    const stop = await request(endpoint, { ...baseRequest, action: 'voice/stop' })
    const invalid = await request(endpoint, { ...baseRequest, action: 'mouse/click' })

    expect(start).toMatchObject({
      success: false,
      status: 'opening',
      message: expect.stringMatching(/press the microphone again/iu),
    })
    expect(stop).toMatchObject({ success: true, status: 'completed' })
    expect(openWebUi).toHaveBeenCalledOnce()
    expect(invalid.success).toBe(false)
  })

  it('queues plugin-owned voice settings while the Harness surface opens', async () => {
    const { endpoint, openWebUi } = await mount()

    const configure = await request(endpoint, {
      ...baseRequest,
      action: 'voice/configure',
    })

    expect(configure).toMatchObject({
      success: true,
      status: 'opening',
      message: expect.stringMatching(/Micro Bridge voice settings/iu),
    })
    expect(openWebUi).toHaveBeenCalledOnce()
  })
})
