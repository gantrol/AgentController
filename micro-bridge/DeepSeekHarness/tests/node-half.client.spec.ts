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

async function readSseFrame(
  reader: ReadableStreamDefaultReader<Uint8Array>,
  timeoutMs = 1_500,
): Promise<Bridge.MicroBrowserFrame> {
  const decoder = new TextDecoder()
  const reading = (async (): Promise<Bridge.MicroBrowserFrame> => {
    let buffered = ''
    while (true) {
      const { done, value } = await reader.read()
      if (done) throw new Error('SSE stream ended before a frame arrived')
      buffered += decoder.decode(value, { stream: true })
      let boundary = buffered.indexOf('\n\n')
      while (boundary !== -1) {
        const block = buffered.slice(0, boundary)
        buffered = buffered.slice(boundary + 2)
        const data = block
          .split(/\r?\n/u)
          .filter(line => line.startsWith('data:'))
          .map(line => line.slice(5).trimStart())
          .join('\n')
        if (data !== '') return JSON.parse(data) as Bridge.MicroBrowserFrame
        boundary = buffered.indexOf('\n\n')
      }
    }
  })()
  let timeout: ReturnType<typeof setTimeout> | undefined
  try {
    return await Promise.race([
      reading,
      new Promise<never>((_resolve, reject) => {
        timeout = setTimeout(() => {
          reject(new Error(`Timed out waiting ${String(timeoutMs)}ms for an SSE frame`))
        }, timeoutMs)
      }),
    ])
  } finally {
    if (timeout !== undefined) clearTimeout(timeout)
  }
}

interface ConnectedTestBrowser {
  abort: AbortController
  reader: ReadableStreamDefaultReader<Uint8Array>
}

async function connectTestBrowser(
  origin: string,
  browserId: string,
  surface: Bridge.MicroBrowserReport['surface'],
  focused: boolean,
): Promise<ConnectedTestBrowser> {
  const abort = new AbortController()
  const stream = await fetch(
    `${origin}${Bridge.MICRO_EVENTS_ENDPOINT}?browserId=${encodeURIComponent(browserId)}`,
    { signal: abort.signal },
  )
  if (stream.body === null) throw new Error(`missing ${browserId} SSE body`)
  const reader = stream.body.getReader()
  const reported = await fetch(`${origin}${Bridge.MICRO_REPORT_ENDPOINT}`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      version: Bridge.MICRO_PROTOCOL_VERSION,
      browserId,
      currentSessionId: null,
      visible: true,
      focused,
      surface,
      navigationDepth: 0,
    }),
  })
  if (reported.status !== 204) {
    abort.abort()
    await reader.cancel().catch(() => {})
    throw new Error(`${browserId} report failed with ${String(reported.status)}`)
  }
  return { abort, reader }
}

async function disconnectTestBrowser(browser: ConnectedTestBrowser): Promise<void> {
  browser.abort.abort()
  await browser.reader.cancel().catch(() => {})
}

describe('external DeepSeek Harness host bundle', () => {
  it('defers a WSL app surface to the Windows keypad host', async () => {
    const runNativeCommand = vi.fn(async () => {})
    const launchWindowsAppBrowser = vi.fn(async () => 27_001)
    Bridge.internals.platform = 'linux'
    Bridge.internals.isWsl = () => true
    Bridge.internals.runNativeCommand = runNativeCommand
    Bridge.internals.launchWindowsAppBrowser = launchWindowsAppBrowser

    const processId = await Bridge.internals.openWebUi(
      'http://127.0.0.1:3080/?codexMicroSurface=1',
      new AbortController().signal,
    )

    expect(processId).toBeUndefined()
    expect(launchWindowsAppBrowser).not.toHaveBeenCalled()
    expect(runNativeCommand).not.toHaveBeenCalled()
  })

  it('uses the native Linux URL handler outside WSL', async () => {
    const runNativeCommand = vi.fn(async () => {})
    Bridge.internals.platform = 'linux'
    Bridge.internals.isWsl = () => false
    Bridge.internals.runNativeCommand = runNativeCommand

    await Bridge.internals.openWebUi(
      'http://127.0.0.1:3080/?codexMicroSurface=1',
      new AbortController().signal,
    )

    expect(runNativeCommand).toHaveBeenCalledWith(
      'xdg-open',
      ['http://127.0.0.1:3080/?codexMicroSurface=1'],
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

  it('e2e: a focused normal Chrome tab cannot intercept the DeepSeek key', async () => {
    const {
      eventHandler,
      reportHandler,
      controlHandler,
      openWebUi,
    } = await mount()
    httpServer = createHttpServer((req, res) => {
      if (req.url?.startsWith(Bridge.MICRO_EVENTS_ENDPOINT) === true) {
        void eventHandler(req, res)
      } else if (req.url === Bridge.MICRO_REPORT_ENDPOINT) {
        void reportHandler(req, res)
      } else if (req.url === Bridge.MICRO_CONTROL_ENDPOINT) {
        void controlHandler(req, res)
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
    const tabAbort = new AbortController()
    let tabStream: Response | undefined
    try {
      tabStream = await fetch(
        `${origin}${Bridge.MICRO_EVENTS_ENDPOINT}?browserId=focused-chrome-tab`,
        { signal: tabAbort.signal },
      )
      const reported = await fetch(`${origin}${Bridge.MICRO_REPORT_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          version: Bridge.MICRO_PROTOCOL_VERSION,
          browserId: 'focused-chrome-tab',
          currentSessionId: null,
          visible: true,
          focused: true,
          surface: 'tab',
          navigationDepth: 0,
        }),
      })
      expect(reported.status).toBe(204)

      const activated = await fetch(`${origin}${Bridge.MICRO_CONTROL_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ ...baseRequest, action: 'activate' }),
      })

      expect(activated.status).toBe(200)
      await expect(activated.json()).resolves.toMatchObject({
        success: true,
        status: 'opening',
        windowProcessId: 41_944,
      })
      expect(openWebUi).toHaveBeenCalledOnce()
      expect(openWebUi.mock.calls[0]?.[0]).toContain('codexMicroSurface=1')
    } finally {
      tabAbort.abort()
      await tabStream?.body?.cancel().catch(() => {})
    }
  })

  it('e2e: the DeepSeek key beats a focused tab and targets the dedicated surface', async () => {
    const {
      eventHandler,
      reportHandler,
      controlHandler,
      openWebUi,
    } = await mount([
      { sessionId: 'agent-light-session', updatedAt: 50, running: false, blank: false },
    ])
    httpServer = createHttpServer((req, res) => {
      if (req.url?.startsWith(Bridge.MICRO_EVENTS_ENDPOINT) === true) {
        void eventHandler(req, res)
      } else if (req.url === Bridge.MICRO_REPORT_ENDPOINT) {
        void reportHandler(req, res)
      } else if (req.url === Bridge.MICRO_CONTROL_ENDPOINT) {
        void controlHandler(req, res)
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
    const tabAbort = new AbortController()
    const dedicatedAbort = new AbortController()
    let tabStream: Response | undefined
    let dedicatedReader: ReadableStreamDefaultReader<Uint8Array> | undefined
    try {
      tabStream = await fetch(
        `${origin}${Bridge.MICRO_EVENTS_ENDPOINT}?browserId=focused-chrome-tab`,
        { signal: tabAbort.signal },
      )
      const tabReported = await fetch(`${origin}${Bridge.MICRO_REPORT_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          version: Bridge.MICRO_PROTOCOL_VERSION,
          browserId: 'focused-chrome-tab',
          currentSessionId: 'agent-light-session',
          visible: true,
          focused: true,
          surface: 'tab',
          navigationDepth: 0,
          sessionStates: [{ id: 'agent-light-session', status: 'idle' }],
        }),
      })
      expect(tabReported.status).toBe(204)

      const dedicatedStream = await fetch(
        `${origin}${Bridge.MICRO_EVENTS_ENDPOINT}?browserId=deepseek-app`,
        { signal: dedicatedAbort.signal },
      )
      if (dedicatedStream.body === null) throw new Error('missing dedicated SSE body')
      dedicatedReader = dedicatedStream.body.getReader()
      const dedicatedReported = await fetch(`${origin}${Bridge.MICRO_REPORT_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          version: Bridge.MICRO_PROTOCOL_VERSION,
          browserId: 'deepseek-app',
          currentSessionId: 'agent-light-session',
          visible: true,
          focused: false,
          surface: 'dedicated',
          navigationDepth: 0,
          sessionStates: [{ id: 'agent-light-session', status: 'running' }],
        }),
      })
      expect(dedicatedReported.status).toBe(204)

      const stateRead = await fetch(`${origin}${Bridge.MICRO_CONTROL_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ ...baseRequest, action: 'state/read' }),
      })
      await expect(stateRead.json()).resolves.toMatchObject({
        success: true,
        state: {
          currentSessionId: 'agent-light-session',
          sessions: [{ id: 'agent-light-session', status: 'running', running: true }],
        },
      })

      const activation = fetch(`${origin}${Bridge.MICRO_CONTROL_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ ...baseRequest, action: 'activate' }),
      })
      const frame = await readSseFrame(dedicatedReader)
      expect(frame).toMatchObject({
        version: Bridge.MICRO_PROTOCOL_VERSION,
        type: 'activate',
        requestId: expect.any(String),
      })
      if (!('requestId' in frame)) throw new Error('activation frame is missing requestId')
      const acknowledged = await fetch(`${origin}${Bridge.MICRO_REPORT_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          version: Bridge.MICRO_PROTOCOL_VERSION,
          browserId: 'deepseek-app',
          currentSessionId: 'agent-light-session',
          visible: true,
          focused: true,
          surface: 'dedicated',
          navigationDepth: 0,
          sessionStates: [{ id: 'agent-light-session', status: 'running' }],
          requestId: frame.requestId,
          success: true,
          message: 'Dedicated DeepSeek surface focused.',
        }),
      })
      expect(acknowledged.status).toBe(204)
      await expect((await activation).json()).resolves.toMatchObject({
        success: true,
        status: 'background',
      })
      expect(openWebUi).not.toHaveBeenCalled()
    } finally {
      tabAbort.abort()
      dedicatedAbort.abort()
      await Promise.all([
        tabStream?.body?.cancel().catch(() => {}),
        dedicatedReader?.cancel().catch(() => {}),
      ])
    }
  })

  it('e2e: a reconnecting tab cannot steal an action queued for the dedicated surface', async () => {
    const {
      eventHandler,
      reportHandler,
      controlHandler,
      openWebUi,
    } = await mount()
    httpServer = createHttpServer((req, res) => {
      if (req.url?.startsWith(Bridge.MICRO_EVENTS_ENDPOINT) === true) {
        void eventHandler(req, res)
      } else if (req.url === Bridge.MICRO_REPORT_ENDPOINT) {
        void reportHandler(req, res)
      } else if (req.url === Bridge.MICRO_CONTROL_ENDPOINT) {
        void controlHandler(req, res)
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
    const browsers: ConnectedTestBrowser[] = []
    try {
      browsers.push(await connectTestBrowser(
        origin,
        'initial-chrome-tab',
        'tab',
        true,
      ))
      const activation = await fetch(`${origin}${Bridge.MICRO_CONTROL_ENDPOINT}`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          ...baseRequest,
          action: 'session/activate',
          sessionId: 'queued-session',
        }),
      })
      await expect(activation.json()).resolves.toMatchObject({
        success: true,
        status: 'opening',
      })
      expect(openWebUi).toHaveBeenCalledOnce()

      // This connection reproduces the startup race that used to drain every
      // pending physical-key frame before its surface report was known.
      browsers.push(await connectTestBrowser(
        origin,
        'late-chrome-tab',
        'tab',
        false,
      ))
      const dedicated = await connectTestBrowser(
        origin,
        'deepseek-app',
        'dedicated',
        false,
      )
      browsers.push(dedicated)

      await expect(readSseFrame(dedicated.reader)).resolves.toMatchObject({
        version: Bridge.MICRO_PROTOCOL_VERSION,
        type: 'session/activate',
        requestId: expect.any(String),
        sessionId: 'queued-session',
      })
    } finally {
      await Promise.all(browsers.map(disconnectTestBrowser))
    }
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
