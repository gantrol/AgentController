/**
 * Codex Micro bridge, host half: accepts versioned local requests, projects
 * recent Harness sessions from the Host API, and delivers focus/session
 * activation to the browser through same-origin SSE.
 */

import { randomUUID } from 'node:crypto'
import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import type { IncomingMessage, ServerResponse } from 'node:http'
import { createServer, type Server, type Socket } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import type { Duplex } from 'node:stream'
import { runNativeCommand, type NativeCommandRunner } from './native-command.ts'
import { LocalAsrRuntime } from './local-asr-runtime.ts'
import { SettingsApi } from './settings-api.ts'
import { VoiceSettingsStore } from './settings.ts'
import { VoiceGateway } from './voice-server.ts'
import type {
  MicroActionExecutionFrame,
  MicroActionId,
  MicroBrowserReport,
  MicroBrowserFrame,
  MicroComponentSnapshot,
  MicroRequest,
  MicroResponse,
  MicroSessionActivationFrame,
  MicroSessionSummary,
  MicroStateSnapshot,
  MicroVoiceFrame,
} from './protocol.ts'
import {
  MICRO_EVENTS_ENDPOINT,
  MICRO_CONTROL_ENDPOINT,
  MICRO_PIPE_NAME,
  MICRO_PROTOCOL_VERSION,
  MICRO_REPORT_ENDPOINT,
  MICRO_SETTINGS_ENDPOINT,
  MICRO_VOICE_ENDPOINT,
} from './protocol.ts'

export type {
  MicroActivationFrame,
  MicroActivationRequest,
  MicroActionExecutionFrame,
  MicroActionExecutionRequest,
  MicroActionId,
  MicroBrowserReport,
  MicroBrowserFrame,
  MicroCapabilities,
  MicroComponentSnapshot,
  MicroRequest,
  MicroResponse,
  MicroSessionActivationFrame,
  MicroSessionActivationRequest,
  MicroSessionSummary,
  MicroStateRequest,
  MicroStateSnapshot,
  MicroVoiceFrame,
  MicroVoiceRequest,
} from './protocol.ts'
export {
  MICRO_EVENTS_ENDPOINT,
  MICRO_CONTROL_ENDPOINT,
  MICRO_PIPE_NAME,
  MICRO_PROTOCOL_VERSION,
  MICRO_REPORT_ENDPOINT,
  MICRO_SETTINGS_ENDPOINT,
  MICRO_VOICE_ENDPOINT,
} from './protocol.ts'

/** Cordis plugin name. */
export const name = 'agentcontroller-deepseek-harness-micro-bridge'

/** Required host services: API projection, route registry, and effective web port. */
export const inject = ['apiProxy', 'webServer', 'credentials']

const MAX_REQUEST_BYTES = 4_096
const REQUEST_TIMEOUT_MS = 3_000
const FOCUS_ACK_TIMEOUT_MS = 650
const ACTION_ACK_TIMEOUT_MS = 2_500
const VOICE_ACK_TIMEOUT_MS = 12_000
const NATIVE_OPEN_COOLDOWN_MS = 5_000
const MAX_VISIBLE_SESSIONS = 6
const ACTION_IDS = new Set<MicroActionId>([
  'session/new',
  'session/fork',
  'session/archive',
  'turn/cancel',
  'view/toggle-chat-trajectory',
  'interaction/approve',
  'interaction/reject',
  'history/load-older',
  'layout/toggle-sidebar',
  'layout/open-details',
  'layout/close-details',
  'composer/select-previous',
  'composer/select-next',
  'composer/activate-selection',
  'composer/back',
  'reasoning/decrease',
  'reasoning/increase',
  'model/toggle-quick',
  'goal/open',
])

interface SessionListRow {
  sessionId: string
  updatedAt: number
  running: boolean
  blank: boolean
  parentSessionId?: string
  cwd?: string
  projections?: { values: Record<string, unknown> }
}

interface ApiProxyFace {
  sessions: {
    list(request: { rpcId: string; payload: Record<string, never> }): Promise<{
      result:
        | { ok: true; value: { items: SessionListRow[] } }
        | { ok: false; error: { message: string } }
    }>
  }
}

interface CredentialsFace {
  resolve(ref: string): Promise<{ value: string; source?: string } | undefined>
  describe(ref: string): Promise<{ configured: boolean; source?: string; writable: boolean }>
  set(ref: string, value: string): Promise<void>
  unset(ref: string): Promise<void>
}

interface HostContext {
  logger: {
    warn(value: unknown): void
    error(value: unknown): void
  }
  webServer: {
    port: number
    register(route: {
      kind: 'exact' | 'prefix'
      path: string
      handler: (request: IncomingMessage, response: ServerResponse) => void | Promise<void>
    }): () => void
    registerUpgrade(route: {
      path: string
      handler: (request: IncomingMessage, socket: Duplex, head: Buffer) => void | Promise<void>
    }): () => void
  }
  credentials: CredentialsFace
  get(name: string): unknown
  effect(
    factory: () => Promise<() => void | Promise<void>>,
    label?: string,
  ): Promise<void>
}

function defaultPipeEndpoint(platform: NodeJS.Platform, webPort: number): string {
  const pipeName = webPort === 3_080 ? MICRO_PIPE_NAME : `${MICRO_PIPE_NAME}-${String(webPort)}`
  if (platform === 'win32') return `\\\\.\\pipe\\${pipeName}`
  return join(tmpdir(), `${pipeName}-${String(process.pid)}.sock`)
}

/** Mutable process/OS seams used only by deterministic adapter tests. */
export const internals: {
  pipeEndpoint: (webPort: number) => string
  platform: NodeJS.Platform
  runNativeCommand: NativeCommandRunner
  windowsAppBrowser: () => string | undefined
  wslWindowsAppBrowser: () => string | undefined
  wslWindowsUrlHandler: () => string | undefined
  launchWindowsAppBrowser: (executable: string, url: string) => Promise<number>
  openWebUi: (url: string, signal: AbortSignal) => Promise<number | undefined>
} = {
  pipeEndpoint: webPort => defaultPipeEndpoint(process.platform, webPort),
  platform: process.platform,
  runNativeCommand,
  windowsAppBrowser() {
    const roots = [process.env['ProgramFiles(x86)'], process.env.ProgramFiles]
      .filter((value): value is string => value !== undefined && value.trim() !== '')
    const candidates = roots.flatMap(root => [
      join(root, 'Microsoft', 'Edge', 'Application', 'msedge.exe'),
      join(root, 'Google', 'Chrome', 'Application', 'chrome.exe'),
    ])
    return candidates.find(candidate => existsSync(candidate))
  },
  wslWindowsAppBrowser() {
    if (process.env.WSL_INTEROP === undefined &&
        process.env.WSL_DISTRO_NAME === undefined) return undefined
    const candidates = [
      '/mnt/c/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
      '/mnt/c/Program Files/Microsoft/Edge/Application/msedge.exe',
      '/mnt/c/Program Files/Google/Chrome/Application/chrome.exe',
      '/mnt/c/Program Files (x86)/Google/Chrome/Application/chrome.exe',
    ]
    return candidates.find(candidate => existsSync(candidate))
  },
  wslWindowsUrlHandler() {
    if (process.env.WSL_INTEROP === undefined &&
        process.env.WSL_DISTRO_NAME === undefined) return undefined
    const candidates = [
      '/mnt/c/Windows/System32/rundll32.exe',
      '/mnt/c/WINDOWS/System32/rundll32.exe',
    ]
    return candidates.find(candidate => existsSync(candidate))
  },
  launchWindowsAppBrowser(executable, url) {
    return new Promise<number>((resolve, reject) => {
      const child = spawn(executable, [`--app=${url}`], {
        detached: true,
        stdio: 'ignore',
        windowsHide: true,
      })
      const onError = (error: Error): void => { reject(error) }
      child.once('error', onError)
      child.once('spawn', () => {
        child.off('error', onError)
        if (child.pid === undefined) {
          reject(new Error('browser app process started without a process id'))
          return
        }
        child.unref()
        resolve(child.pid)
      })
    })
  },
  async openWebUi(url, signal) {
    switch (internals.platform) {
      case 'win32': {
        const browser = internals.windowsAppBrowser()
        if (browser !== undefined) {
          return await internals.launchWindowsAppBrowser(browser, url)
        }
        await internals.runNativeCommand(
          'rundll32.exe',
          ['url.dll,FileProtocolHandler', url],
          signal,
        )
        return undefined
      }
      case 'darwin':
        await internals.runNativeCommand('open', [url], signal)
        return undefined
      case 'linux':
        {
          const windowsAppBrowser = internals.wslWindowsAppBrowser()
          if (windowsAppBrowser !== undefined) {
            // The Harness host lives in WSL, but its UI remains a native
            // Windows app window.  Launch app mode directly so repeated
            // Micro activation can be matched and raised by HWND instead of
            // spraying ordinary browser tabs through the URL handler.
            await internals.launchWindowsAppBrowser(windowsAppBrowser, url)
            // The pid returned by a WSL interop spawn belongs to the Linux
            // relay, not the native Windows browser.  Let Micro locate the
            // guarded Harness window by title/process instead.
            return undefined
          }
          const windowsUrlHandler = internals.wslWindowsUrlHandler()
          if (windowsUrlHandler !== undefined) {
            await internals.runNativeCommand(
              windowsUrlHandler,
              ['url.dll,FileProtocolHandler', url],
              signal,
            )
            return undefined
          }
        }
        await internals.runNativeCommand('xdg-open', [url], signal)
        return undefined
      default:
        throw new Error(`opening the Harness UI is unsupported on ${internals.platform}`)
    }
  },
}

function response(
  success: boolean,
  message: string,
  status?: MicroResponse['status'],
  state?: MicroStateSnapshot,
  windowProcessId?: number,
): MicroResponse {
  return {
    success,
    message,
    ...(status === undefined ? {} : { status }),
    ...(windowProcessId === undefined ? {} : { windowProcessId }),
    ...(state === undefined ? {} : { state }),
  }
}

function parseRequest(line: Buffer): MicroRequest | undefined {
  let value: unknown
  try {
    value = JSON.parse(line.toString('utf8'))
  } catch {
    return undefined
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return undefined
  const request = value as Record<string, unknown>
  if (request.version !== MICRO_PROTOCOL_VERSION || request.source !== 'codex-micro') return undefined
  switch (request.action) {
    case 'activate':
    case 'state/read':
    case 'voice/configure':
    case 'voice/start':
    case 'voice/stop':
      return request as unknown as MicroRequest
    case 'session/activate':
      return typeof request.sessionId === 'string' && request.sessionId.trim() !== ''
        ? request as unknown as MicroRequest
        : undefined
    case 'action/execute':
      return typeof request.actionId === 'string'
        && ACTION_IDS.has(request.actionId as MicroActionId)
        && (request.sessionId === undefined
          || (typeof request.sessionId === 'string' && request.sessionId.trim() !== ''))
        ? request as unknown as MicroRequest
        : undefined
    default:
      return undefined
  }
}

function sseFrame(frame: MicroBrowserFrame): string {
  return `data: ${JSON.stringify(frame)}\n\n`
}

function displayTitle(row: SessionListRow): string {
  if (row.blank) return 'New Session'
  const projected = row.projections?.values.title
  if (typeof projected === 'string' && projected.trim() !== '') return projected
  const cwd = row.cwd?.replace(/[/\\]+$/u, '')
  const base = cwd?.split(/[/\\]/u).pop()
  return base === undefined || base === '' ? row.sessionId : base
}

class MicroBridge {
  private readonly server: Server
  private readonly pipeSockets = new Set<Socket>()
  private readonly browserConnections = new Map<string, ServerResponse>()
  private readonly browserReports = new Map<string, MicroBrowserReport & { reportedAt: number }>()
  private readonly dedicatedBrowsers = new Set<string>()
  private readonly reportWaiters = new Map<string, (report?: MicroBrowserReport) => void>()
  private readonly nativeTasks = new Set<Promise<unknown>>()
  private readonly abort = new AbortController()
  private readonly pendingFrames: MicroBrowserFrame[] = []
  private currentSessionId: string | undefined
  private lastNativeOpenAt = Number.NEGATIVE_INFINITY
  private dedicatedOpenPending = false
  private dedicatedProcessId: number | undefined
  private listening = false

  constructor(
    private readonly ctx: HostContext,
    private readonly apiProxy: ApiProxyFace,
    private readonly webUrl: string,
    private readonly pipeEndpoint: string,
    private readonly componentSnapshot: (browserConnected: boolean) => Promise<MicroComponentSnapshot>,
  ) {
    this.server = createServer({ allowHalfOpen: true }, (socket) => { this.acceptPipe(socket) })
  }

  /** Bind the local endpoint and reject activation if another owner has it. */
  async listen(): Promise<void> {
    await new Promise<void>((resolve, reject) => {
      const onError = (error: Error): void => { reject(error) }
      this.server.once('error', onError)
      this.server.listen(this.pipeEndpoint, () => {
        this.server.off('error', onError)
        this.server.on('error', (error) => { this.ctx.logger.error(error) })
        this.listening = true
        resolve()
      })
    })
  }

  /** Own the same-origin SSE endpoint registered by the Cordis host half. */
  handleEvents(req: IncomingMessage, res: ServerResponse): void {
    if (req.method !== 'GET' && req.method !== 'HEAD') {
      res.writeHead(405, { allow: 'GET, HEAD' })
      res.end()
      return
    }
    res.writeHead(200, {
      'content-type': 'text/event-stream',
      'cache-control': 'no-cache',
      'connection': 'keep-alive',
    })
    if (req.method === 'HEAD') {
      res.end()
      return
    }
    const browserId = this.readBrowserId(req) ?? randomUUID()
    res.write(': connected\n\n')
    this.browserConnections.set(browserId, res)
    res.once('close', () => {
      if (this.browserConnections.get(browserId) === res) {
        this.browserConnections.delete(browserId)
        this.browserReports.delete(browserId)
        this.dedicatedBrowsers.delete(browserId)
      }
    })
    if (this.pendingFrames.length !== 0) {
      for (const frame of this.pendingFrames.splice(0)) {
        res.write(sseFrame(frame))
      }
    }
  }

  /** Accept browser presence and exact frame acknowledgements. */
  async handleReport(req: IncomingMessage, res: ServerResponse): Promise<void> {
    if (req.method !== 'POST') {
      res.writeHead(405, { allow: 'POST' })
      res.end()
      return
    }
    try {
      const body = await this.readBody(req)
      const value = JSON.parse(body) as unknown
      if (!this.isBrowserReport(value)) {
        res.writeHead(400)
        res.end()
        return
      }
      const report = value
      this.browserReports.set(report.browserId, { ...report, reportedAt: Date.now() })
      if (report.surface === 'dedicated') {
        this.dedicatedBrowsers.add(report.browserId)
        this.dedicatedOpenPending = false
      } else {
        this.dedicatedBrowsers.delete(report.browserId)
      }
      this.currentSessionId = report.currentSessionId ?? undefined
      if (report.requestId !== undefined) {
        this.reportWaiters.get(report.requestId)?.(report)
      }
      res.writeHead(204)
      res.end()
    } catch (error) {
      if (!this.abort.signal.aborted) {
        this.ctx.logger.warn('client-micro-bridge: invalid browser report')
        this.ctx.logger.warn(error)
      }
      if (!res.headersSent) res.writeHead(400)
      res.end()
    }
  }

  /**
   * Accept the same versioned request contract over loopback HTTP.  This is
   * the cross-OS transport used when the Harness runs in WSL while Codex
   * Micro remains a Windows process.  Remote/LAN callers are rejected before
   * parsing a request; the named-pipe transport remains available for a
   * native Windows Harness.
   */
  async handleControl(req: IncomingMessage, res: ServerResponse): Promise<void> {
    if (!this.isLoopbackRequest(req)) {
      res.writeHead(403, { 'cache-control': 'no-store' })
      res.end()
      return
    }
    if (req.method !== 'POST') {
      res.writeHead(405, { allow: 'POST', 'cache-control': 'no-store' })
      res.end()
      return
    }
    try {
      const body = await this.readBody(req)
      if (Buffer.byteLength(body, 'utf8') > MAX_REQUEST_BYTES) {
        res.writeHead(413, { 'cache-control': 'no-store' })
        res.end()
        return
      }
      const request = parseRequest(Buffer.from(body, 'utf8'))
      if (request === undefined) {
        this.writeControlResponse(
          res,
          400,
          response(false, 'Unsupported DeepSeek Harness request.'),
        )
        return
      }
      const result = await this.handleRequest(request)
      this.writeControlResponse(res, 200, result)
    } catch (error) {
      if (!this.abort.signal.aborted) {
        this.ctx.logger.warn('client-micro-bridge: loopback control request failed')
        this.ctx.logger.warn(error)
      }
      if (!res.headersSent) {
        this.writeControlResponse(
          res,
          500,
          response(false, 'DeepSeek Harness request failed.'),
        )
      } else {
        res.end()
      }
    }
  }

  /** Stop accepting requests and await every bridge-owned resource. */
  async dispose(): Promise<void> {
    this.abort.abort()
    for (const socket of this.pipeSockets) socket.destroy()
    this.pipeSockets.clear()
    for (const res of this.browserConnections.values()) res.destroy()
    this.browserConnections.clear()
    this.browserReports.clear()
    this.dedicatedBrowsers.clear()
    for (const finish of this.reportWaiters.values()) finish()
    this.reportWaiters.clear()

    const closed = this.listening
      ? new Promise<void>((resolve) => {
        this.server.close(() => {
          this.listening = false
          resolve()
        })
      })
      : Promise.resolve()
    await Promise.allSettled([closed, ...this.nativeTasks])
  }

  private acceptPipe(socket: Socket): void {
    this.pipeSockets.add(socket)
    socket.once('close', () => { this.pipeSockets.delete(socket) })
    // A client disconnect is not a process error; close owns cleanup.
    socket.on('error', () => {})

    let received = Buffer.alloc(0)
    let settled = false
    const timer = setTimeout(() => {
      finish(response(false, 'DeepSeek Harness request timed out.'))
    }, REQUEST_TIMEOUT_MS)
    timer.unref()

    const finish = (result: MicroResponse): void => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      socket.end(`${JSON.stringify(result)}\n`)
    }

    const consume = (line: Buffer): void => {
      if (line.length === 0) {
        finish(response(false, 'Unsupported DeepSeek Harness request.'))
        return
      }
      const request = parseRequest(line)
      if (request === undefined) {
        finish(response(false, 'Unsupported DeepSeek Harness request.'))
        return
      }
      void this.handleRequest(request).then(finish, (error: unknown) => {
        this.ctx.logger.warn('client-micro-bridge: request failed')
        this.ctx.logger.warn(error)
        finish(response(false, 'DeepSeek Harness request failed.'))
      })
    }

    socket.on('data', (chunk: Buffer) => {
      if (settled) return
      received = Buffer.concat([received, chunk])
      const newline = received.indexOf(0x0a)
      const lineBytes = newline === -1 ? received.length : newline
      if (lineBytes > MAX_REQUEST_BYTES) {
        finish(response(false, 'DeepSeek Harness request is too large.'))
        return
      }
      if (newline !== -1) {
        const line = received.subarray(0, newline)
        const withoutCr = line.at(-1) === 0x0d ? line.subarray(0, -1) : line
        consume(withoutCr)
      }
    })
    socket.once('end', () => {
      if (settled) return
      if (received.length > MAX_REQUEST_BYTES) {
        finish(response(false, 'DeepSeek Harness request is too large.'))
        return
      }
      const withoutCr = received.at(-1) === 0x0d ? received.subarray(0, -1) : received
      consume(withoutCr)
    })
  }

  private isLoopbackRequest(req: IncomingMessage): boolean {
    const address = req.socket.remoteAddress?.toLowerCase()
    return address === '127.0.0.1'
      || address === '::1'
      || address === '::ffff:127.0.0.1'
  }

  private writeControlResponse(
    res: ServerResponse,
    statusCode: number,
    result: MicroResponse,
  ): void {
    const body = JSON.stringify(result)
    res.writeHead(statusCode, {
      'content-type': 'application/json; charset=utf-8',
      'content-length': String(Buffer.byteLength(body)),
      'cache-control': 'no-store',
    })
    res.end(body)
  }

  private async handleRequest(request: MicroRequest): Promise<MicroResponse> {
    switch (request.action) {
      case 'activate': {
        const requestId = randomUUID()
        const frame = { version: MICRO_PROTOCOL_VERSION, type: 'activate', requestId } as const
        const delivery = await this.deliver(frame, FOCUS_ACK_TIMEOUT_MS)
        if (!delivery.delivered) {
          const processId = await this.scheduleOpenOnce()
          return response(
            true,
            'DeepSeek Harness is opening.',
            'opening',
            undefined,
            processId,
          )
        }
        if (delivery.report === undefined) {
          return response(
            true,
            'DeepSeek Harness activation was sent to the connected browser, but foreground was not confirmed.',
            'background',
            undefined,
            this.dedicatedProcessId,
          )
        }
        if (!delivery.report.success) {
          return response(false, delivery.report.message ?? 'DeepSeek Harness rejected activation.')
        }
        // document.hasFocus() confirms only Chromium's document focus. On
        // Windows it can remain true while another top-level application is
        // in front, so let Codex Micro perform and verify the native HWND
        // activation instead of returning a false "foreground" success.
        if (!delivery.report.visible && delivery.report.surface !== 'dedicated') {
          const processId = await this.scheduleOpenOnce()
          return response(
            true,
            'DeepSeek Harness was in a background tab; a dedicated window is opening.',
            'opening',
            undefined,
            processId,
          )
        }
        return response(
          true,
          'DeepSeek Harness acknowledged the page activation; native foreground confirmation is required.',
          'background',
          undefined,
          this.dedicatedProcessId,
        )
      }
      case 'state/read':
        return await this.readState()
      case 'session/activate': {
        this.currentSessionId = request.sessionId
        const frame: MicroSessionActivationFrame = {
          version: MICRO_PROTOCOL_VERSION,
          type: 'session/activate',
          requestId: randomUUID(),
          sessionId: request.sessionId,
        }
        const delivery = await this.deliver(frame, FOCUS_ACK_TIMEOUT_MS)
        if (!delivery.delivered) {
          this.pendingFrames.push(frame)
          const processId = await this.scheduleOpenOnce()
          return response(
            true,
            'DeepSeek Harness is opening the selected session.',
            'opening',
            undefined,
            processId,
          )
        }
        if (delivery.report === undefined) {
          // Session activation is idempotent in the browser client.  The
          // frame was delivered, so a lost focus ACK must not be presented as
          // a failed session open (or trigger an unsafe duplicate launch).
          return response(
            true,
            'DeepSeek Harness received the session activation; foreground confirmation is pending.',
            'background',
            undefined,
            this.dedicatedProcessId,
          )
        }
        if (!delivery.report.success) {
          return response(false, delivery.report.message ?? 'DeepSeek Harness rejected the session.')
        }
        if (!delivery.report.visible && delivery.report.surface !== 'dedicated') {
          const processId = await this.scheduleOpenOnce()
          return response(
            true,
            'The selected DeepSeek Harness session opened in a background tab; a dedicated window is opening.',
            'opening',
            undefined,
            processId,
          )
        }
        return response(
          true,
          'DeepSeek Harness session activated.',
          'background',
          undefined,
          this.dedicatedProcessId,
        )
      }
      case 'action/execute': {
        const frame: MicroActionExecutionFrame = {
          version: MICRO_PROTOCOL_VERSION,
          type: 'action/execute',
          requestId: randomUUID(),
          actionId: request.actionId,
          ...(request.sessionId === undefined ? {} : { sessionId: request.sessionId }),
        }
        const delivery = await this.deliver(frame, ACTION_ACK_TIMEOUT_MS)
        if (!delivery.delivered) {
          this.pendingFrames.push(frame)
          const processId = await this.scheduleOpenOnce()
          return response(
            true,
            'DeepSeek Harness is opening to execute the action.',
            'opening',
            undefined,
            processId,
          )
        }
        if (delivery.report === undefined) {
          return response(false, 'DeepSeek Harness action delivery was not confirmed; it was not retried.')
        }
        if (delivery.report.success !== true) {
          return response(false, delivery.report.message ?? 'DeepSeek Harness rejected the action.')
        }
        return response(
          true,
          delivery.report.message ?? 'DeepSeek Harness action completed.',
          'completed',
        )
      }
      case 'voice/start':
      case 'voice/stop': {
        const frame: MicroVoiceFrame = {
          version: MICRO_PROTOCOL_VERSION,
          type: request.action,
          requestId: randomUUID(),
        }
        const delivery = await this.deliver(
          frame,
          request.action === 'voice/start' ? VOICE_ACK_TIMEOUT_MS : ACTION_ACK_TIMEOUT_MS,
        )
        if (!delivery.delivered) {
          if (request.action === 'voice/start') {
            const processId = await this.scheduleOpenOnce()
            return response(
              false,
              'DeepSeek Harness is opening. Press the microphone again after its page is ready.',
              'opening',
              undefined,
              processId,
            )
          }
          return response(true, 'DeepSeek Harness voice input is already inactive.', 'completed')
        }
        if (delivery.report?.success === false) {
          return response(false, delivery.report.message ?? 'DeepSeek Harness rejected voice input.')
        }
        if (delivery.report === undefined) {
          return response(false, 'Micro Bridge did not confirm that voice input reached the listening state.')
        }
        return response(
          true,
          delivery.report?.message ?? (request.action === 'voice/start'
            ? 'DeepSeek Harness accepted voice input.'
            : 'DeepSeek Harness stopped voice input.'),
          'completed',
        )
      }
      case 'voice/configure': {
        const frame: MicroVoiceFrame = {
          version: MICRO_PROTOCOL_VERSION,
          type: request.action,
          requestId: randomUUID(),
        }
        const delivery = await this.deliver(frame, ACTION_ACK_TIMEOUT_MS)
        if (!delivery.delivered) {
          this.pendingFrames.push(frame)
          const processId = await this.scheduleOpenOnce()
          return response(
            true,
            'DeepSeek Harness is opening the Micro Bridge voice settings.',
            'opening',
            undefined,
            processId,
          )
        }
        if (delivery.report?.success !== true) {
          return response(false, delivery.report?.message ?? 'Micro Bridge voice settings could not be opened.')
        }
        return response(
          true,
          delivery.report.message ?? 'Micro Bridge voice settings opened.',
          'background',
          undefined,
          this.dedicatedProcessId,
        )
      }
    }
  }

  private async readState(): Promise<MicroResponse> {
    const listed = await this.apiProxy.sessions.list({ rpcId: randomUUID(), payload: {} })
    if (!listed.result.ok) return response(false, listed.result.error.message)
    const browserState = this.preferredBrowser()?.state
    const browserCurrent = browserState?.currentSessionId
    const currentSessionId = browserCurrent === null
      ? undefined
      : browserCurrent ?? this.currentSessionId
    const sessions: MicroSessionSummary[] = listed.result.value.items
      .filter(row => (!row.blank || row.sessionId === currentSessionId)
        && row.parentSessionId === undefined)
      .sort((left, right) => right.updatedAt - left.updatedAt)
      .slice(0, MAX_VISIBLE_SESSIONS)
      .map(row => ({
        id: row.sessionId,
        displayTitle: displayTitle(row),
        running: row.running,
        updatedAt: row.updatedAt,
      }))
    const state: MicroStateSnapshot = {
      capabilities: {
        sessionList: true,
        sessionActivation: true,
        knobSettings: true,
        voiceInput: true,
        actions: [...ACTION_IDS],
      },
      sessions,
      ...(currentSessionId === undefined ? {} : { currentSessionId }),
      navigationDepth: browserState?.navigationDepth ?? 0,
      components: await this.componentSnapshot(browserState !== undefined),
    }
    return response(true, 'DeepSeek Harness state read.', 'completed', state)
  }

  private async deliver(
    frame: MicroBrowserFrame & { requestId: string },
    timeoutMs: number,
  ): Promise<{ delivered: boolean; report?: MicroBrowserReport }> {
    const target = this.preferredBrowser()
    if (target === undefined) return { delivered: false }
    const report = new Promise<MicroBrowserReport | undefined>((resolve) => {
      let settled = false
      const finish = (value?: MicroBrowserReport): void => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        this.reportWaiters.delete(frame.requestId)
        resolve(value)
      }
      const timer = setTimeout(() => { finish() }, timeoutMs)
      timer.unref()
      this.reportWaiters.set(frame.requestId, finish)
    })
    target.response.write(sseFrame(frame))
    const acknowledged = await report
    return acknowledged === undefined
      ? { delivered: true }
      : { delivered: true, report: acknowledged }
  }

  private preferredBrowser(): {
    response: ServerResponse
    state?: MicroBrowserReport & { reportedAt: number }
  } | undefined {
    let preferred: {
      response: ServerResponse
      state?: MicroBrowserReport & { reportedAt: number }
      score: number
      reportedAt: number
    } | undefined
    for (const [browserId, response] of this.browserConnections) {
      if (response.destroyed || response.writableEnded) {
        this.browserConnections.delete(browserId)
        continue
      }
      const state = this.browserReports.get(browserId)
      const score = (state?.focused === true ? 4 : 0)
        + (state?.visible === true ? 2 : 0)
        + (state?.surface === 'dedicated' ? 1 : 0)
      const candidate = {
        response,
        score,
        reportedAt: state?.reportedAt ?? 0,
        ...(state === undefined ? {} : { state }),
      }
      if (preferred === undefined
        || candidate.score > preferred.score
        || (candidate.score === preferred.score && candidate.reportedAt > preferred.reportedAt)) {
        preferred = candidate
      }
    }
    if (preferred === undefined) return undefined
    return {
      response: preferred.response,
      ...(preferred.state === undefined ? {} : { state: preferred.state }),
    }
  }

  private readBrowserId(req: IncomingMessage): string | undefined {
    try {
      const url = new URL(req.url ?? MICRO_EVENTS_ENDPOINT, this.webUrl)
      const value = url.searchParams.get('browserId')?.trim()
      return value === undefined || value === '' ? undefined : value
    } catch {
      return undefined
    }
  }

  private async readBody(req: IncomingMessage): Promise<string> {
    return await new Promise<string>((resolve, reject) => {
      let size = 0
      const chunks: Buffer[] = []
      req.on('data', (chunk: Buffer | string) => {
        const value = typeof chunk === 'string' ? Buffer.from(chunk) : chunk
        size += value.length
        if (size > MAX_REQUEST_BYTES) {
          reject(new Error('browser report is too large'))
          req.destroy()
          return
        }
        chunks.push(value)
      })
      req.once('end', () => { resolve(Buffer.concat(chunks).toString('utf8')) })
      req.once('error', reject)
    })
  }

  private isBrowserReport(value: unknown): value is MicroBrowserReport {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) return false
    const report = value as Record<string, unknown>
    return report.version === MICRO_PROTOCOL_VERSION
      && typeof report.browserId === 'string'
      && report.browserId.trim() !== ''
      && (report.currentSessionId === null || typeof report.currentSessionId === 'string')
      && typeof report.visible === 'boolean'
      && typeof report.focused === 'boolean'
      && (report.surface === undefined || report.surface === 'tab' || report.surface === 'dedicated')
      && (report.navigationDepth === undefined
        || (Number.isInteger(report.navigationDepth) && (report.navigationDepth as number) >= 0))
      && (report.requestId === undefined || typeof report.requestId === 'string')
      && (report.success === undefined || typeof report.success === 'boolean')
      && (report.message === undefined || typeof report.message === 'string')
  }

  /**
   * Open one dedicated browser surface at a time. A successful native launch
   * stays pending until that surface reports through SSE; repeated key presses
   * therefore cannot fan out into tabs while the browser is still starting.
   */
  private scheduleOpenOnce(): Promise<number | undefined> {
    const hasDedicatedBrowser = [...this.dedicatedBrowsers]
      .some(browserId => this.browserConnections.has(browserId))
    if (this.dedicatedOpenPending || hasDedicatedBrowser) {
      return Promise.resolve(this.dedicatedProcessId)
    }
    const now = Date.now()
    if (now - this.lastNativeOpenAt < NATIVE_OPEN_COOLDOWN_MS) {
      return Promise.resolve(this.dedicatedProcessId)
    }
    this.lastNativeOpenAt = now
    this.dedicatedOpenPending = true
    const url = new URL(this.webUrl)
    url.searchParams.set('codexMicroSurface', '1')
    const task = internals.openWebUi(url.toString(), this.abort.signal)
      .then((processId) => {
        this.dedicatedProcessId = processId
        return processId
      })
      .catch((error: unknown) => {
        this.dedicatedOpenPending = false
        if (this.abort.signal.aborted) return undefined
        this.ctx.logger.warn('client-micro-bridge: failed to open the Harness web surface')
        this.ctx.logger.warn(error)
        return undefined
      })
      .finally(() => { this.nativeTasks.delete(task) })
    this.nativeTasks.add(task)
    return task
  }
}

/** Mount one atomic external bundle: pipe, browser bridge, settings API, and voice gateway. */
export async function apply(ctx: HostContext): Promise<void> {
  const apiProxy = ctx.get('apiProxy') as ApiProxyFace | undefined
  if (apiProxy === undefined) throw new Error('DeepSeek Harness Micro bridge requires apiProxy')
  const webPort = ctx.webServer.port
  const settings = new VoiceSettingsStore()
  const runtime = new LocalAsrRuntime()
  const voice = new VoiceGateway(settings, ctx.credentials, runtime, ctx.logger)
  const settingsApi = new SettingsApi(settings, ctx.credentials, voice)
  const bridge = new MicroBridge(
    ctx,
    apiProxy,
    `http://127.0.0.1:${String(webPort)}`,
    internals.pipeEndpoint(webPort),
    async browserConnected => {
      const document = await settings.get()
      // The Micro status LED is polled independently of the settings page.
      // Inspect here so an externally started service, or a service that died,
      // is reflected instead of leaving the last cached runtime phase forever.
      const runtimeStatus = await voice.runtimeStatus()
      return {
        adapter: 'ready',
        browser: browserConnected ? 'connected' : 'disconnected',
        voiceSetup: document.settings.setupCompleted ? 'ready' : 'required',
        voiceRuntime: runtimeStatus.phase,
        voiceMessage: runtimeStatus.message,
      }
    },
  )
  await ctx.effect(async () => {
    const disposeRoute = ctx.webServer.register({
      kind: 'exact',
      path: MICRO_EVENTS_ENDPOINT,
      handler: (req, res) => { bridge.handleEvents(req, res) },
    })
    const disposeReportRoute = ctx.webServer.register({
      kind: 'exact',
      path: MICRO_REPORT_ENDPOINT,
      handler: (req, res) => { void bridge.handleReport(req, res) },
    })
    const disposeControlRoute = ctx.webServer.register({
      kind: 'exact',
      path: MICRO_CONTROL_ENDPOINT,
      handler: (req, res) => { void bridge.handleControl(req, res) },
    })
    const disposeSettingsRoute = ctx.webServer.register({
      kind: 'prefix',
      path: MICRO_SETTINGS_ENDPOINT,
      handler: (req, res) => settingsApi.handle(req, res, MICRO_SETTINGS_ENDPOINT),
    })
    const disposeVoiceRoute = ctx.webServer.registerUpgrade({
      path: MICRO_VOICE_ENDPOINT,
      handler: (req, socket, head) => { voice.handleUpgrade(req, socket, head) },
    })
    try {
      await bridge.listen()
      void voice.warmup().catch(error => {
        ctx.logger.warn('client-micro-bridge: local streaming ASR warmup failed')
        ctx.logger.warn(error)
      })
    } catch (error) {
      disposeRoute()
      disposeReportRoute()
      disposeControlRoute()
      disposeSettingsRoute()
      disposeVoiceRoute()
      await bridge.dispose()
      await Promise.allSettled([voice.close(), runtime.dispose(), settings.dispose()])
      throw error
    }
    return async () => {
      disposeRoute()
      disposeReportRoute()
      disposeControlRoute()
      disposeSettingsRoute()
      disposeVoiceRoute()
      await Promise.allSettled([bridge.dispose(), voice.close(), runtime.dispose(), settings.dispose()])
    }
  }, 'agentcontroller-deepseek-harness: external Micro bridge')
}
