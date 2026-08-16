/** Same-origin HTTP surface for the external plugin's non-secret settings and write-only keys. */

import type { IncomingMessage, ServerResponse } from 'node:http'
import { dirname } from 'node:path'
import { fileURLToPath } from 'node:url'
import { LocalAsrRuntimeError } from './local-asr-runtime.ts'
import { SettingsRevisionConflict, VoiceSettingsStore } from './settings.ts'
import type { VoiceGateway } from './voice-server.ts'

const MAX_BODY_BYTES = 128 * 1024
const CREDENTIAL_REF = /^[A-Za-z_][A-Za-z0-9_]*$/u
const BUNDLED_LOCAL_LAUNCHER = fileURLToPath(
  new URL('../scripts/start-qwen3-asr-stream.ps1', import.meta.url),
)

interface CredentialsLike {
  describe(ref: string): Promise<{ configured: boolean; source?: string; writable: boolean }>
  set(ref: string, value: string): Promise<void>
  unset(ref: string): Promise<void>
}

class HttpError extends Error {
  constructor(readonly status: number, message: string) {
    super(message)
  }
}

function sendJson(response: ServerResponse, status: number, body: unknown): void {
  response.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'cache-control': 'no-store',
    'x-content-type-options': 'nosniff',
  })
  response.end(JSON.stringify(body))
}

function assertSameOrigin(request: IncomingMessage): void {
  const origin = request.headers.origin
  if (origin === undefined) return
  try {
    if (new URL(origin).host !== request.headers.host) throw new Error('host mismatch')
  } catch {
    throw new HttpError(403, 'Cross-origin settings writes are not allowed.')
  }
}

async function readBody(request: IncomingMessage): Promise<unknown> {
  let size = 0
  const chunks: Buffer[] = []
  for await (const raw of request) {
    const chunk = Buffer.isBuffer(raw) ? raw : Buffer.from(raw)
    size += chunk.length
    if (size > MAX_BODY_BYTES) {
      request.resume()
      throw new HttpError(413, 'Settings request is too large.')
    }
    chunks.push(chunk)
  }
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8')) as unknown
  } catch {
    throw new HttpError(400, 'Settings request must be valid JSON.')
  }
}

function object(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new HttpError(400, 'Settings request must be an object.')
  }
  return value as Record<string, unknown>
}

function refFrom(value: unknown): string {
  const ref = object(value).ref
  if (typeof ref !== 'string' || !CREDENTIAL_REF.test(ref)) {
    throw new HttpError(400, 'Credential reference is invalid.')
  }
  return ref
}

async function assertOwnedRef(store: VoiceSettingsStore, ref: string): Promise<void> {
  const settings = (await store.get()).settings
  if (ref !== settings.localCredentialRef && ref !== settings.remoteCredentialRef) {
    throw new HttpError(400, 'Credential reference is not used by the current voice settings.')
  }
}

/** Prefix route handler for settings plus write-only credential operations. */
export class SettingsApi {
  constructor(
    private readonly store: VoiceSettingsStore,
    private readonly credentials: CredentialsLike,
    private readonly voice: VoiceGateway,
  ) {}

  async handle(request: IncomingMessage, response: ServerResponse, prefix: string): Promise<void> {
    try {
      const pathname = new URL(request.url ?? prefix, 'http://127.0.0.1').pathname
      if (pathname === prefix) await this.settings(request, response)
      else if (pathname === `${prefix}/credential`) await this.credential(request, response)
      else if (pathname === `${prefix}/runtime`) await this.runtime(request, response)
      else if (pathname === `${prefix}/test`) await this.test(request, response)
      else throw new HttpError(404, 'Settings endpoint was not found.')
    } catch (error) {
      if (response.headersSent) {
        response.destroy()
        return
      }
      if (error instanceof SettingsRevisionConflict) {
        sendJson(response, 409, { error: error.message, document: error.current })
        return
      }
      if (error instanceof HttpError) {
        sendJson(response, error.status, { error: error.message })
        return
      }
      if (error instanceof LocalAsrRuntimeError) {
        sendJson(response, 422, { error: error.message, code: error.code })
        return
      }
      sendJson(response, 500, { error: error instanceof Error ? error.message : String(error) })
    }
  }

  private async settings(request: IncomingMessage, response: ServerResponse): Promise<void> {
    if (request.method === 'GET') {
      const document = await this.store.get()
      const refs = [...new Set([
        document.settings.localCredentialRef,
        document.settings.remoteCredentialRef,
      ].filter(value => value !== ''))]
      const credentials = Object.fromEntries(await Promise.all(refs.map(async ref => [
        ref,
        await this.credentials.describe(ref),
      ])))
      sendJson(response, 200, {
        document,
        credentials,
        runtime: await this.voice.runtimeStatus(),
        recommendations: {
          localLauncherPath: BUNDLED_LOCAL_LAUNCHER,
          localWorkingDirectory: dirname(BUNDLED_LOCAL_LAUNCHER),
          localStreamUrl: 'ws://127.0.0.1:8765/v1/stream',
          localHealthUrl: 'http://127.0.0.1:8765/health',
        },
      })
      return
    }
    if (request.method === 'PUT') {
      assertSameOrigin(request)
      const body = object(await readBody(request))
      if (!Number.isInteger(body.expectedRevision) || typeof body.expectedRevision !== 'number') {
        throw new HttpError(400, 'expectedRevision must be an integer.')
      }
      const document = await this.store.save(body.expectedRevision, body.settings)
      sendJson(response, 200, { document })
      return
    }
    response.setHeader('allow', 'GET, PUT')
    throw new HttpError(405, 'Method not allowed.')
  }

  private async credential(request: IncomingMessage, response: ServerResponse): Promise<void> {
    if (request.method !== 'PUT' && request.method !== 'DELETE') {
      response.setHeader('allow', 'PUT, DELETE')
      throw new HttpError(405, 'Method not allowed.')
    }
    assertSameOrigin(request)
    const body = await readBody(request)
    const ref = refFrom(body)
    await assertOwnedRef(this.store, ref)
    if (request.method === 'PUT') {
      const value = object(body).value
      if (typeof value !== 'string' || value.trim() === '' || value.length > 32_768) {
        throw new HttpError(400, 'Credential value must be a non-empty string no longer than 32768 characters.')
      }
      await this.credentials.set(ref, value.trim())
    } else {
      await this.credentials.unset(ref)
    }
    sendJson(response, 200, { credential: await this.credentials.describe(ref) })
  }

  private async runtime(request: IncomingMessage, response: ServerResponse): Promise<void> {
    if (request.method === 'GET') {
      sendJson(response, 200, { runtime: await this.voice.runtimeStatus() })
      return
    }
    if (request.method !== 'POST' && request.method !== 'DELETE') {
      response.setHeader('allow', 'GET, POST, DELETE')
      throw new HttpError(405, 'Method not allowed.')
    }
    assertSameOrigin(request)
    sendJson(response, 200, {
      runtime: request.method === 'POST'
        ? await this.voice.startRuntime()
        : await this.voice.stopRuntime(),
    })
  }

  private async test(request: IncomingMessage, response: ServerResponse): Promise<void> {
    if (request.method !== 'POST') {
      response.setHeader('allow', 'POST')
      throw new HttpError(405, 'Method not allowed.')
    }
    assertSameOrigin(request)
    await this.voice.testConfiguredProvider()
    sendJson(response, 200, {
      success: true,
      message: 'Streaming ASR accepted the dsh-stream-v1 handshake.',
      runtime: await this.voice.runtimeStatus(),
    })
  }
}
