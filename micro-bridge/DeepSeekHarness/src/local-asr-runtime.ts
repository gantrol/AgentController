/** Managed lifecycle for an optional loopback streaming ASR service. */

import { spawn, type ChildProcessWithoutNullStreams } from 'node:child_process'
import { existsSync } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import type { LocalRuntimeStatus, VoiceSettings } from './voice-contract.ts'

const HEALTH_TIMEOUT_MS = 2_500
const HEALTH_RETRY_MS = 400
const LOG_LIMIT_BYTES = 64 * 1024
const STOP_TIMEOUT_MS = 5_000

const ANSI_CONTROL_SEQUENCE = /\u001b\[[0-?]*[ -/]*[@-~]/gu

/** Keep child diagnostics readable when Windows tools mix UTF-8 and UTF-16LE output. */
export function sanitizeRuntimeLog(raw: Buffer): string {
  return raw
    .toString('utf8')
    .replaceAll('\u0000', '')
    .replace(ANSI_CONTROL_SEQUENCE, '')
    .replaceAll('\uFFFD', '')
    .replace(/\r\n?/gu, '\n')
    .replace(/\n{3,}/gu, '\n\n')
    .trim()
}

export class LocalAsrRuntimeError extends Error {
  constructor(readonly code: string, message: string, cause?: unknown) {
    super(message, { cause })
    this.name = 'LocalAsrRuntimeError'
  }
}

function runtimeError(code: string, message: string, cause?: unknown): LocalAsrRuntimeError {
  return new LocalAsrRuntimeError(code, message, cause)
}

function detail(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

interface ManagedProcess {
  child: ChildProcessWithoutNullStreams
  exit: Promise<{ code: number | null; signal: NodeJS.Signals | null; error?: Error }>
  settled: boolean
}

/**
 * Owns only processes it starts. A manually started service is detected by its
 * health endpoint and is never terminated by the plugin.
 */
export class LocalAsrRuntime {
  private managed: ManagedProcess | undefined
  private startPromise: Promise<LocalRuntimeStatus> | undefined
  private stopPromise: Promise<void> | undefined
  private statusValue: LocalRuntimeStatus = {
    phase: 'stopped',
    message: 'Local streaming ASR is stopped.',
  }
  private startedAt = 0
  private logChunks: Buffer[] = []
  private logBytes = 0
  private disposed = false

  status(): LocalRuntimeStatus {
    const elapsedMilliseconds = this.startedAt === 0 ? undefined : Date.now() - this.startedAt
    return {
      ...this.statusValue,
      ...(elapsedMilliseconds === undefined ? {} : { elapsedMilliseconds }),
      ...(this.logBytes === 0 ? {} : { logTail: sanitizeRuntimeLog(Buffer.concat(this.logChunks)) }),
    }
  }

  async inspect(settings: VoiceSettings, apiKey?: string): Promise<LocalRuntimeStatus> {
    if (settings.localScriptPath === '' && settings.localStartMode !== 'manual') {
      return this.publish('not-configured', 'Choose a local ASR startup script.', 'ASR_SCRIPT_NOT_CONFIGURED')
    }
    if (this.managed?.settled === true && this.statusValue.phase === 'ready') {
      this.managed = undefined
      this.publish('error', 'The managed local ASR process exited.', 'ASR_PROCESS_EXITED')
    }
    if (await this.healthy(settings, apiKey)) {
      return this.publish(
        'ready',
        this.managed === undefined
          ? 'Local streaming ASR is ready (externally managed).'
          : 'Local streaming ASR is ready.',
      )
    }
    if (this.statusValue.phase === 'ready') {
      return this.publish('error', 'The local ASR health check failed.', 'ASR_HEALTH_CHECK_FAILED')
    }
    return this.status()
  }

  async ensureReady(settings: VoiceSettings, apiKey?: string): Promise<LocalRuntimeStatus> {
    if (this.disposed) throw runtimeError('ASR_RUNTIME_DISPOSED', 'Local ASR runtime is shutting down.')
    const current = await this.inspect(settings, apiKey)
    if (current.phase === 'ready') return current
    if (settings.localStartMode === 'manual') {
      throw runtimeError(
        'ASR_MANUAL_START_REQUIRED',
        'Start the configured local streaming ASR service, then retry.',
      )
    }
    if (settings.localScriptPath === '') {
      throw runtimeError('ASR_SCRIPT_NOT_CONFIGURED', 'Choose a local ASR startup script first.')
    }
    if (this.startPromise !== undefined) return await this.startPromise
    const operation = this.startInternal(settings, apiKey).finally(() => {
      if (this.startPromise === operation) this.startPromise = undefined
    })
    this.startPromise = operation
    return await operation
  }

  async start(settings: VoiceSettings, apiKey?: string): Promise<LocalRuntimeStatus> {
    return await this.ensureReady(settings, apiKey)
  }

  async stop(): Promise<void> {
    if (this.stopPromise !== undefined) return await this.stopPromise
    const operation = this.stopInternal().finally(() => {
      if (this.stopPromise === operation) this.stopPromise = undefined
    })
    this.stopPromise = operation
    await operation
  }

  async dispose(): Promise<void> {
    this.disposed = true
    await this.stop()
  }

  private async startInternal(settings: VoiceSettings, apiKey?: string): Promise<LocalRuntimeStatus> {
    this.validateLaunch(settings)
    this.logChunks = []
    this.logBytes = 0
    this.startedAt = Date.now()
    this.publish('starting', 'Starting the local streaming ASR service.')
    const { file, args } = this.command(settings)
    let child: ChildProcessWithoutNullStreams
    try {
      child = spawn(file, args, {
        cwd: settings.localWorkingDirectory === '' ? undefined : settings.localWorkingDirectory,
        windowsHide: true,
        shell: false,
        stdio: ['pipe', 'pipe', 'pipe'],
      })
      child.stdin.end()
    } catch (error) {
      throw this.fail('ASR_START_FAILED', 'The local ASR process could not be started.', error)
    }
    const managed: ManagedProcess = {
      child,
      settled: false,
      exit: Promise.resolve({ code: null, signal: null }),
    }
    this.managed = managed
    this.capture(child.stdout)
    this.capture(child.stderr)
    managed.exit = new Promise(resolve => {
      let settled = false
      const finish = (value: { code: number | null; signal: NodeJS.Signals | null; error?: Error }): void => {
        if (settled) return
        settled = true
        managed.settled = true
        resolve(value)
      }
      child.once('error', error => { finish({ code: null, signal: null, error }) })
      child.once('close', (code, signal) => { finish({ code, signal }) })
    })

    const deadline = Date.now() + settings.localStartupTimeoutMilliseconds
    while (Date.now() < deadline) {
      if (managed.settled) {
        const exit = await managed.exit
        throw this.fail(
          'ASR_PROCESS_EXITED',
          `The local ASR process exited during startup${exit.code === null ? '' : ` (code ${String(exit.code)})`}.`,
          exit.error,
        )
      }
      if (await this.healthy(settings, apiKey)) {
        return this.publish('ready', 'Local streaming ASR is ready.', undefined, child.pid)
      }
      await delay(HEALTH_RETRY_MS, undefined, { ref: false })
    }
    await this.terminate(managed)
    this.managed = undefined
    throw this.fail(
      'ASR_MODEL_LOADING_TIMEOUT',
      'The local ASR process started, but its health endpoint did not become ready in time.',
    )
  }

  private validateLaunch(settings: VoiceSettings): void {
    if (!isAbsolute(settings.localScriptPath) || !existsSync(settings.localScriptPath)) {
      throw this.fail('ASR_SCRIPT_NOT_FOUND', 'The configured local ASR startup script does not exist.')
    }
    if (settings.localWorkingDirectory !== ''
      && (!isAbsolute(settings.localWorkingDirectory) || !existsSync(settings.localWorkingDirectory))) {
      throw this.fail('ASR_WORKDIR_NOT_FOUND', 'The configured local ASR working directory does not exist.')
    }
    if (settings.localRunner === 'powershell'
      && !settings.localScriptPath.toLowerCase().endsWith('.ps1')) {
      throw this.fail('ASR_SCRIPT_TYPE_MISMATCH', 'PowerShell startup requires a .ps1 script.')
    }
  }

  private command(settings: VoiceSettings): { file: string; args: string[] } {
    if (settings.localRunner === 'executable') {
      return { file: settings.localScriptPath, args: settings.localScriptArguments }
    }
    if (process.platform !== 'win32') {
      return { file: 'pwsh', args: ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', settings.localScriptPath, ...settings.localScriptArguments] }
    }
    const systemRoot = process.env.SystemRoot?.trim() || 'C:\\Windows'
    return {
      file: join(systemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe'),
      args: ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', settings.localScriptPath, ...settings.localScriptArguments],
    }
  }

  private async healthy(settings: VoiceSettings, apiKey?: string): Promise<boolean> {
    const controller = new AbortController()
    const timer = setTimeout(() => { controller.abort() }, HEALTH_TIMEOUT_MS)
    timer.unref()
    try {
      const response = await fetch(settings.localHealthUrl, {
        method: 'GET',
        headers: {
          accept: 'application/json, text/plain;q=0.8',
          ...(apiKey === undefined ? {} : { authorization: `Bearer ${apiKey}` }),
        },
        redirect: 'error',
        signal: controller.signal,
      })
      await response.body?.cancel()
      return response.ok
    } catch {
      return false
    } finally {
      clearTimeout(timer)
    }
  }

  private capture(stream: NodeJS.ReadableStream): void {
    stream.on('data', (raw: unknown) => {
      const chunk = Buffer.isBuffer(raw) ? raw : Buffer.from(String(raw))
      this.logChunks.push(chunk)
      this.logBytes += chunk.length
      while (this.logBytes > LOG_LIMIT_BYTES && this.logChunks.length > 0) {
        const removed = this.logChunks.shift()
        this.logBytes -= removed?.length ?? 0
      }
    })
  }

  private async stopInternal(): Promise<void> {
    const managed = this.managed
    this.managed = undefined
    if (managed !== undefined) await this.terminate(managed)
    this.startedAt = 0
    if (!this.disposed) this.publish('stopped', 'Local streaming ASR is stopped.')
  }

  private async terminate(managed: ManagedProcess): Promise<void> {
    if (managed.settled) return
    if (process.platform === 'win32' && managed.child.pid !== undefined) {
      await this.killWindowsTree(managed.child.pid)
      const treeExited = await Promise.race([
        managed.exit.then(() => true),
        delay(STOP_TIMEOUT_MS, false, { ref: false }),
      ])
      if (treeExited) return
    }
    managed.child.kill()
    const exited = await Promise.race([
      managed.exit.then(() => true),
      delay(STOP_TIMEOUT_MS, false, { ref: false }),
    ])
    if (exited) return
    managed.child.kill('SIGKILL')
    await Promise.race([
      managed.exit,
      delay(STOP_TIMEOUT_MS, undefined, { ref: false }),
    ])
  }

  /** PowerShell launchers can own WSL/model workers; do not orphan that tree. */
  private async killWindowsTree(processId: number): Promise<void> {
    const systemRoot = process.env.SystemRoot?.trim() || 'C:\\Windows'
    const taskkill = spawn(
      join(systemRoot, 'System32', 'taskkill.exe'),
      ['/PID', String(processId), '/T', '/F'],
      { windowsHide: true, shell: false, stdio: 'ignore' },
    )
    await Promise.race([
      new Promise<void>(resolve => {
        taskkill.once('error', () => { resolve() })
        taskkill.once('close', () => { resolve() })
      }),
      delay(STOP_TIMEOUT_MS, undefined, { ref: false }),
    ])
    if (taskkill.exitCode === null) taskkill.kill()
  }

  private publish(
    phase: LocalRuntimeStatus['phase'],
    message: string,
    errorCode?: string,
    processId?: number,
  ): LocalRuntimeStatus {
    this.statusValue = {
      phase,
      message,
      ...(errorCode === undefined ? {} : { errorCode }),
      ...(processId === undefined ? {} : { processId }),
      ...(this.startedAt === 0 ? {} : { startedAt: new Date(this.startedAt).toISOString() }),
    }
    return this.status()
  }

  private fail(code: string, message: string, cause?: unknown): LocalAsrRuntimeError {
    this.publish('error', cause === undefined ? message : `${message} ${detail(cause)}`, code)
    return runtimeError(code, message, cause)
  }
}
