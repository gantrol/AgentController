/** Browser-owned push-to-talk state machine and microphone capture. */

import { MICRO_VOICE_ENDPOINT } from '../protocol.ts'
import type { HostVoiceFrame, VoiceProvider, VoiceSettings } from '../voice-contract.ts'
import { VoiceSettingsClient } from './settings-client.ts'

const TARGET_SAMPLE_RATE = 16_000

interface SnapshotFace<T> {
  getSnapshot(): T
  subscribe(listener: () => void): () => void
}

interface InputFace {
  setDraft(text: string): void
  submit(): void
  notify(level: 'info' | 'error', text: string): void
  state: SnapshotFace<{ draft: string }>
}

interface SessionsFace {
  list: SnapshotFace<{ current?: string }>
  scope(sessionId: string): unknown
}

interface ConversationFace {
  input: { for(scope: unknown): InputFace }
}

export interface VoiceSnapshot {
  phase: 'idle' | 'configuring' | 'setup-required' | 'requesting' | 'listening' | 'processing' | 'error'
  provider?: VoiceProvider
  sessionId?: string
  partial: string
  error?: string
}

interface SpeechRecognitionResultLike {
  isFinal: boolean
  length: number
  [index: number]: { transcript: string }
}

interface SpeechRecognitionEventLike extends Event {
  resultIndex: number
  results: ArrayLike<SpeechRecognitionResultLike>
}

interface SpeechRecognitionErrorEventLike extends Event {
  error: string
  message?: string
}

interface SpeechRecognitionLike {
  continuous: boolean
  interimResults: boolean
  lang: string
  onresult: ((event: SpeechRecognitionEventLike) => void) | null
  onerror: ((event: SpeechRecognitionErrorEventLike) => void) | null
  onend: (() => void) | null
  start(): void
  stop(): void
  abort(): void
}

type SpeechRecognitionConstructor = new () => SpeechRecognitionLike

interface SpeechWindow extends Window {
  SpeechRecognition?: SpeechRecognitionConstructor
  webkitSpeechRecognition?: SpeechRecognitionConstructor
}

function detail(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}

function punctuationStart(value: string): boolean {
  return /^[,.;:!?，。；：！？、\])}）】》]/u.test(value)
}

/** Append one recognized segment without inserting Western spaces into CJK dictation. */
export function appendTranscript(draft: string, text: string, language: string): string {
  const next = text.trim()
  if (next === '') return draft
  if (draft === '' || /\s$/u.test(draft) || punctuationStart(next)) return `${draft}${next}`
  const cjk = /^(?:zh|ja|ko)(?:-|$)/iu.test(language)
    || (language === '' && /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Hangul}]/u.test(next))
  return `${draft}${cjk ? '' : ' '}${next}`
}

function websocketUrl(path: string): string {
  const url = new URL(path, window.location.href)
  url.protocol = url.protocol === 'https:' ? 'wss:' : 'ws:'
  return url.toString()
}

function parseHostFrame(raw: unknown): HostVoiceFrame {
  if (typeof raw !== 'string' || raw.length > 64 * 1024) throw new Error('Voice server returned an invalid frame.')
  let value: unknown
  try {
    value = JSON.parse(raw) as unknown
  } catch {
    throw new Error('Voice server returned invalid JSON.')
  }
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error('Voice server returned an invalid frame.')
  }
  const row = value as Record<string, unknown>
  if (row.type === 'ready' && (row.provider === 'local-qwen' || row.provider === 'remote-websocket')) {
    return { type: 'ready', provider: row.provider }
  }
  if ((row.type === 'partial' || row.type === 'final') && typeof row.text === 'string') {
    return { type: row.type, text: row.text }
  }
  if (row.type === 'done') return { type: 'done' }
  if (row.type === 'error' && typeof row.message === 'string') return { type: 'error', message: row.message }
  throw new Error('Voice server returned an unsupported frame.')
}

function pcm16(samples: Float32Array): ArrayBuffer {
  const output = new Int16Array(samples.length)
  for (let index = 0; index < samples.length; index += 1) {
    const sample = Math.max(-1, Math.min(1, samples[index] ?? 0))
    output[index] = sample < 0 ? sample * 0x8000 : sample * 0x7fff
  }
  return output.buffer
}

function downsample(input: Float32Array, sourceRate: number): Float32Array {
  if (sourceRate === TARGET_SAMPLE_RATE) return input
  const ratio = sourceRate / TARGET_SAMPLE_RATE
  const length = Math.max(1, Math.floor(input.length / ratio))
  const output = new Float32Array(length)
  for (let index = 0; index < length; index += 1) {
    const position = index * ratio
    const left = Math.floor(position)
    const fraction = position - left
    output[index] = (input[left] ?? 0) * (1 - fraction) + (input[left + 1] ?? input[left] ?? 0) * fraction
  }
  return output
}

const WORKLET_SOURCE = `
class DshPcm16Processor extends AudioWorkletProcessor {
  constructor(options) {
    super();
    this.targetRate = options.processorOptions.targetRate;
    this.carry = new Float32Array(0);
    this.position = 0;
  }
  process(inputs) {
    const input = inputs[0] && inputs[0][0];
    if (!input || input.length === 0) return true;
    const combined = new Float32Array(this.carry.length + input.length);
    combined.set(this.carry); combined.set(input, this.carry.length);
    const ratio = sampleRate / this.targetRate;
    const values = [];
    while (this.position + 1 < combined.length) {
      const left = Math.floor(this.position);
      const fraction = this.position - left;
      const sample = combined[left] * (1 - fraction) + combined[left + 1] * fraction;
      values.push(Math.max(-1, Math.min(1, sample)));
      this.position += ratio;
    }
    const consumed = Math.floor(this.position);
    this.carry = combined.slice(consumed);
    this.position -= consumed;
    if (values.length > 0) {
      const pcm = new Int16Array(values.length);
      for (let i = 0; i < values.length; i += 1) pcm[i] = values[i] < 0 ? values[i] * 32768 : values[i] * 32767;
      this.port.postMessage(pcm.buffer, [pcm.buffer]);
    }
    return true;
  }
}
registerProcessor('dsh-pcm16', DshPcm16Processor);
`

export class VoiceController {
  private snapshot: VoiceSnapshot = { phase: 'idle', partial: '' }
  private readonly listeners = new Set<() => void>()
  private generation = 0
  private desired = false
  private socket: WebSocket | undefined
  private stream: MediaStream | undefined
  private audioContext: AudioContext | undefined
  private audioNodes: AudioNode[] = []
  private recognition: SpeechRecognitionLike | undefined
  private settingsAtStart: VoiceSettings | undefined
  private committed = false
  private disposed = false

  constructor(
    private readonly sessions: SessionsFace,
    private readonly conversation: ConversationFace,
    private readonly settings: VoiceSettingsClient,
  ) {}

  getSnapshot = (): VoiceSnapshot => this.snapshot

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  async requireConfigured(sessionId?: string): Promise<VoiceSettings> {
    const target = sessionId ?? this.sessions.list.getSnapshot().current
    if (target === undefined) throw new Error('Open a DeepSeek Harness session before using voice input.')
    const settings = await this.settings.settings()
    if (!settings.setupCompleted) {
      this.desired = false
      this.publish({
        phase: 'setup-required',
        sessionId: target,
        provider: settings.provider,
        partial: '',
        error: 'Configure and test the Micro Bridge voice plugin before first use.',
      })
      throw new Error('VOICE_SETUP_REQUIRED: Open Micro Bridge voice settings and complete a provider test.')
    }
    return settings
  }

  async start(sessionId?: string): Promise<void> {
    if (this.disposed) throw new Error('The Micro Bridge voice plugin has been disposed.')
    if (this.desired) {
      if (this.snapshot.phase === 'listening') return
      throw new Error('Voice input is already starting.')
    }
    const target = sessionId ?? this.sessions.list.getSnapshot().current
    if (target === undefined) {
      this.fail('Open a DeepSeek Harness session before using voice input.')
      return
    }
    this.desired = true
    this.committed = false
    const generation = ++this.generation
    this.publish({ phase: 'requesting', sessionId: target, partial: '' })
    try {
      const settings = await this.requireConfigured(target)
      if (!this.current(generation)) return
      this.settingsAtStart = settings
      this.publish({ phase: 'requesting', sessionId: target, provider: settings.provider, partial: '' })
      if (settings.provider === 'system') {
        await this.startSystem(generation, target, settings)
      } else {
        await this.startGateway(generation, target, settings)
        await this.waitForListening(generation)
      }
    } catch (error) {
      if (this.activeGeneration(generation) && this.snapshot.phase === 'setup-required') throw error
      if (this.current(generation)) {
        const message = detail(error)
        if (this.snapshot.phase !== 'error') this.fail(message)
        throw new Error(message)
      }
    }
  }

  async stop(): Promise<void> {
    if (!this.desired && this.snapshot.phase === 'idle') return
    this.desired = false
    const recognition = this.recognition
    if (recognition !== undefined) {
      this.publish({ ...this.snapshot, phase: 'processing', partial: '' })
      recognition.stop()
      return
    }
    this.stopCapture()
    const socket = this.socket
    if (socket?.readyState === WebSocket.OPEN) {
      this.publish({ ...this.snapshot, phase: 'processing', partial: '' })
      socket.send(JSON.stringify({ type: 'stop' }))
    } else {
      await this.finish(false)
    }
  }

  async showConfiguration(sessionId?: string): Promise<void> {
    const target = sessionId ?? this.sessions.list.getSnapshot().current
    if (target === undefined) {
      throw new Error('Open a DeepSeek Harness session before configuring the Micro Bridge voice plugin.')
    }
    if (this.desired || this.snapshot.phase === 'processing') {
      throw new Error('Stop voice input before changing Micro Bridge voice settings.')
    }
    const settings = await this.settings.settings()
    this.publish({
      phase: settings.setupCompleted ? 'configuring' : 'setup-required',
      sessionId: target,
      provider: settings.provider,
      partial: '',
      ...(settings.setupCompleted
        ? {}
        : { error: 'Configure and test the Micro Bridge voice plugin before first use.' }),
    })
  }

  dismissConfiguration(): void {
    if (this.snapshot.phase === 'setup-required' || this.snapshot.phase === 'configuring') {
      this.publish({ phase: 'idle', partial: '' })
    }
  }

  async dispose(): Promise<void> {
    this.disposed = true
    this.desired = false
    this.generation += 1
    this.recognition?.abort()
    this.recognition = undefined
    if (this.socket?.readyState === WebSocket.OPEN) this.socket.send(JSON.stringify({ type: 'cancel' }))
    this.socket?.close(1000, 'disposed')
    this.socket = undefined
    this.stopCapture()
    this.listeners.clear()
  }

  private async startSystem(generation: number, sessionId: string, settings: VoiceSettings): Promise<void> {
    const view = window as SpeechWindow
    const Constructor = view.SpeechRecognition ?? view.webkitSpeechRecognition
    if (Constructor === undefined) throw new Error('System speech recognition is unavailable in this browser.')
    const recognition = new Constructor()
    recognition.continuous = true
    recognition.interimResults = true
    // Leave lang untouched for automatic browser/OS language selection.
    if (settings.language !== '') recognition.lang = settings.language
    recognition.onresult = (event) => {
      if (!this.activeGeneration(generation)) return
      let partial = ''
      for (let index = event.resultIndex; index < event.results.length; index += 1) {
        const result = event.results[index]
        const text = result?.[0]?.transcript ?? ''
        if (result?.isFinal === true) this.commit(sessionId, text, settings.language)
        else partial += text
      }
      this.publish({ ...this.snapshot, phase: 'listening', partial })
    }
    recognition.onerror = (event) => {
      if (this.activeGeneration(generation)) this.fail(event.message ?? `System recognition failed: ${event.error}`)
    }
    recognition.onend = () => {
      if (!this.activeGeneration(generation)) return
      this.recognition = undefined
      if (this.desired) this.fail('System speech recognition ended unexpectedly; press the mic to retry.')
      else this.finishRecognition()
    }
    this.recognition = recognition
    recognition.start()
    this.publish({ phase: 'listening', provider: 'system', sessionId, partial: '' })
  }

  private async startGateway(generation: number, sessionId: string, settings: VoiceSettings): Promise<void> {
    const socket = new WebSocket(websocketUrl(MICRO_VOICE_ENDPOINT))
    socket.binaryType = 'arraybuffer'
    this.socket = socket
    socket.onmessage = (event) => {
      try {
        const frame = parseHostFrame(event.data)
        void this.handleHostFrame(generation, sessionId, settings, frame).catch(error => {
          if (this.current(generation)) this.fail(detail(error))
        })
      } catch (error) {
        if (this.current(generation)) this.fail(detail(error))
      }
    }
    socket.onerror = () => { if (this.current(generation)) this.fail('Voice WebSocket connection failed.') }
    socket.onclose = () => {
      if (!this.current(generation)) return
      if (this.snapshot.phase === 'processing') void this.finish(false)
      else if (this.desired) this.fail('Voice WebSocket closed unexpectedly.')
    }
    await new Promise<void>((resolve, reject) => {
      socket.onopen = () => { resolve() }
      const previous = socket.onerror
      socket.onerror = (event) => { previous?.call(socket, event); reject(new Error('Voice WebSocket connection failed.')) }
    })
    if (!this.current(generation)) {
      socket.close(1000, 'cancelled')
      return
    }
    socket.send(JSON.stringify({ type: 'start' }))
  }

  private async waitForListening(generation: number): Promise<void> {
    if (this.snapshot.phase === 'listening') return
    await new Promise<void>((resolve, reject) => {
      let settled = false
      const finish = (error?: Error): void => {
        if (settled) return
        settled = true
        window.clearTimeout(timeout)
        unsubscribe()
        if (error === undefined) resolve()
        else reject(error)
      }
      const inspect = (): void => {
        if (!this.activeGeneration(generation)) {
          finish(new Error('Voice startup was cancelled.'))
          return
        }
        if (this.snapshot.phase === 'listening') {
          finish()
          return
        }
        if (this.snapshot.phase === 'error') {
          finish(new Error(this.snapshot.error ?? 'Voice input failed to start.'))
        }
      }
      const unsubscribe = this.subscribe(inspect)
      const timeout = window.setTimeout(() => {
        finish(new Error('Timed out waiting for microphone capture to start. Check browser microphone permission.'))
      }, 10_000)
      inspect()
    })
  }

  private async handleHostFrame(
    generation: number,
    sessionId: string,
    settings: VoiceSettings,
    frame: HostVoiceFrame,
  ): Promise<void> {
    if (!this.activeGeneration(generation)) return
    switch (frame.type) {
      case 'ready':
        if (!this.desired) {
          this.socket?.send(JSON.stringify({ type: 'cancel' }))
          return
        }
        await this.startCapture(generation)
        if (this.current(generation)) {
          this.publish({ phase: 'listening', provider: frame.provider, sessionId, partial: '' })
        }
        return
      case 'partial':
        this.publish({ ...this.snapshot, partial: frame.text })
        return
      case 'final':
        this.commit(sessionId, frame.text, settings.language)
        this.publish({ ...this.snapshot, partial: '' })
        return
      case 'done':
        await this.finish(true)
        return
      case 'error':
        this.fail(frame.message)
        return
    }
  }

  private async startCapture(generation: number): Promise<void> {
    const stream = await navigator.mediaDevices.getUserMedia({
      audio: {
        channelCount: 1,
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true,
      },
    })
    if (!this.current(generation)) {
      for (const track of stream.getTracks()) track.stop()
      return
    }
    this.stream = stream
    const context = new AudioContext({ latencyHint: 'interactive' })
    this.audioContext = context
    const source = context.createMediaStreamSource(stream)
    const sink = context.createGain()
    sink.gain.value = 0
    this.audioNodes = [source, sink]
    if (context.audioWorklet !== undefined && typeof AudioWorkletNode !== 'undefined') {
      const moduleUrl = URL.createObjectURL(new Blob([WORKLET_SOURCE], { type: 'text/javascript' }))
      try {
        await context.audioWorklet.addModule(moduleUrl)
      } finally {
        URL.revokeObjectURL(moduleUrl)
      }
      if (!this.current(generation)) return
      const worklet = new AudioWorkletNode(context, 'dsh-pcm16', {
        numberOfInputs: 1,
        numberOfOutputs: 1,
        outputChannelCount: [1],
        processorOptions: { targetRate: TARGET_SAMPLE_RATE },
      })
      worklet.port.onmessage = (event: MessageEvent<ArrayBuffer>) => { this.sendPcm(event.data) }
      source.connect(worklet).connect(sink).connect(context.destination)
      this.audioNodes.push(worklet)
    } else {
      const processor = context.createScriptProcessor(4_096, 1, 1)
      processor.onaudioprocess = (event) => {
        const samples = downsample(event.inputBuffer.getChannelData(0), context.sampleRate)
        this.sendPcm(pcm16(samples))
      }
      source.connect(processor).connect(sink).connect(context.destination)
      this.audioNodes.push(processor)
    }
    await context.resume()
  }

  private sendPcm(bytes: ArrayBuffer): void {
    if (this.desired && this.socket?.readyState === WebSocket.OPEN) this.socket.send(bytes)
  }

  private commit(sessionId: string, text: string, language: string): void {
    const scope = this.sessions.scope(sessionId)
    if (scope === undefined) return
    const input = this.conversation.input.for(scope)
    const current = input.state.getSnapshot().draft
    const next = appendTranscript(current, text, language)
    if (next === current) return
    input.setDraft(next)
    this.committed = true
  }

  private input(): InputFace | undefined {
    const sessionId = this.snapshot.sessionId
    if (sessionId === undefined) return undefined
    const scope = this.sessions.scope(sessionId)
    return scope === undefined ? undefined : this.conversation.input.for(scope)
  }

  private finishRecognition(): void {
    const submit = this.committed && this.settingsAtStart?.autoSubmit === true
    if (submit) this.input()?.submit()
    void this.finish(false)
  }

  private async finish(fromServer: boolean): Promise<void> {
    const submit = fromServer && this.committed && this.settingsAtStart?.autoSubmit === true
    this.desired = false
    this.socket?.close(1000, 'complete')
    this.socket = undefined
    this.stopCapture()
    if (submit) this.input()?.submit()
    this.settingsAtStart = undefined
    this.committed = false
    this.publish({ phase: 'idle', partial: '' })
  }

  private stopCapture(): void {
    for (const node of this.audioNodes) node.disconnect()
    this.audioNodes = []
    for (const track of this.stream?.getTracks() ?? []) track.stop()
    this.stream = undefined
    const context = this.audioContext
    this.audioContext = undefined
    if (context !== undefined && context.state !== 'closed') void context.close()
  }

  private fail(error: string): void {
    this.desired = false
    this.recognition?.abort()
    this.recognition = undefined
    this.socket?.close(1011, 'voice error')
    this.socket = undefined
    this.stopCapture()
    this.input()?.notify('error', error)
    this.publish({ ...this.snapshot, phase: 'error', partial: '', error })
  }

  private current(generation: number): boolean {
    return this.activeGeneration(generation) && this.desired
  }

  private activeGeneration(generation: number): boolean {
    return !this.disposed && generation === this.generation
  }

  private publish(next: VoiceSnapshot): void {
    this.snapshot = next
    for (const listener of [...this.listeners]) listener()
  }
}
