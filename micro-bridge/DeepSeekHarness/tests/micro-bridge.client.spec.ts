// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apply } from '../src/client/index.tsx'
import {
  MICRO_EVENTS_ENDPOINT,
  MICRO_REPORT_ENDPOINT,
  MICRO_SETTINGS_ENDPOINT,
} from '../src/protocol.ts'
import { DEFAULT_VOICE_SETTINGS } from '../src/voice-contract.ts'

class FakeEventSource {
  static instances: FakeEventSource[] = []
  readonly listeners = new Map<string, EventListener[]>()
  closed = false

  constructor(readonly url: string) {
    FakeEventSource.instances.push(this)
  }

  addEventListener(type: string, listener: EventListener): void {
    const listeners = this.listeners.get(type) ?? []
    listeners.push(listener)
    this.listeners.set(type, listeners)
  }

  emit(frame: unknown): void {
    const event = new MessageEvent('message', {
      data: typeof frame === 'string' ? frame : JSON.stringify(frame),
    })
    for (const listener of this.listeners.get('message') ?? []) listener(event)
  }

  close(): void {
    this.closed = true
  }
}

class FakeSessionList {
  current: string | undefined = 'session-1'
  readonly listeners = new Set<() => void>()

  getSnapshot(): { current?: string } {
    return this.current === undefined ? {} : { current: this.current }
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }
}

interface SlotEntry {
  name: string
  id: string
  inject?: () => Record<string, unknown>
  component: unknown
}

class FakeContext {
  readonly effects: Array<() => void> = []
  readonly entries: SlotEntry[] = []
  readonly logger = { warn: vi.fn() }
  readonly dictionaries = new Map<string, Record<string, string>>()
  readonly conversationHeaderEntry = { store: {} }
  readonly viewStore = {
    current: null as string | null,
    setView: vi.fn((view: string) => { this.viewStore.current = view }),
  }

  constructor(private readonly services: Readonly<Record<string, unknown>>) {}

  readonly slots = {
    inject: (_name: string, factory: () => unknown): void => { factory() },
    register: (options: Omit<SlotEntry, 'component'>, component: unknown): unknown => {
      this.entries.push({ ...options, component })
      return () => {}
    },
    entries: (name: string): readonly unknown[] =>
      name === 'conversation.session.header'
        ? [this.conversationHeaderEntry]
        : [],
    hostFace: () => ({
      storeOf: () => ({
        getSnapshot: () => ({ view: this.viewStore.current }),
        actions: { setView: this.viewStore.setView },
      }),
    }),
  }

  readonly locale = {
    register: (namespace: string, dictionaries: { zh: Record<string, string> }): (() => void) => {
      this.dictionaries.set(namespace, dictionaries.zh)
      return () => { this.dictionaries.delete(namespace) }
    },
    bind: (namespace: string) => (key: string): string =>
      this.dictionaries.get(namespace)?.[key] ?? key,
  }

  get(name: string): unknown {
    return this.services[name]
  }

  effect(setup: () => void | (() => void)): void {
    const dispose = setup()
    if (typeof dispose === 'function') this.effects.push(dispose)
  }

  dispose(): void {
    for (const dispose of this.effects.reverse()) dispose()
    this.effects.length = 0
  }
}

let context: FakeContext | undefined
let sessionList: FakeSessionList
let openSession: ReturnType<typeof vi.fn>
let forkSession: ReturnType<typeof vi.fn>
let cancelTurn: ReturnType<typeof vi.fn>
let startSession: ReturnType<typeof vi.fn>
let selectModel: ReturnType<typeof vi.fn>
let currentModel: {
  provider: string
  model: string
  reasoningEffort?: string
}
let reports: Array<Record<string, unknown>>
let voiceSettings: typeof DEFAULT_VOICE_SETTINGS

beforeEach(() => {
  FakeEventSource.instances = []
  sessionList = new FakeSessionList()
  openSession = vi.fn((id: string) => { sessionList.current = id })
  forkSession = vi.fn(async () => 'fork-child')
  cancelTurn = vi.fn(async () => ({ ok: true }))
  startSession = vi.fn()
  currentModel = { provider: 'deepseek', model: 'model-a', reasoningEffort: 'medium' }
  selectModel = vi.fn(async (selection: typeof currentModel) => {
    currentModel = { ...selection }
  })
  reports = []
  voiceSettings = structuredClone(DEFAULT_VOICE_SETTINGS)
  vi.stubGlobal('EventSource', FakeEventSource)
  vi.stubGlobal('fetch', vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input)
    if (url === MICRO_SETTINGS_ENDPOINT) {
      return new Response(JSON.stringify({
        document: { revision: 0, settings: voiceSettings },
        credentials: {},
      }), { status: 200, headers: { 'content-type': 'application/json' } })
    }
    expect(url).toBe(MICRO_REPORT_ENDPOINT)
    if (typeof init?.body !== 'string') throw new TypeError('expected JSON report body')
    reports.push(JSON.parse(init.body) as Record<string, unknown>)
    return new Response(null, { status: 204 })
  }))
  vi.spyOn(document, 'hasFocus').mockReturnValue(true)
  vi.spyOn(window, 'focus').mockImplementation(() => {})
})

afterEach(() => {
  context?.dispose()
  context = undefined
  window.sessionStorage.clear()
  document.querySelectorAll('style[data-plugin="agentcontroller-dsh-micro-voice"]')
    .forEach(element => { element.remove() })
  document.querySelectorAll('style[data-plugin="agentcontroller-dsh-micro-navigation"]')
    .forEach(element => { element.remove() })
  document.body.replaceChildren()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  delete (window as Window & { webkitSpeechRecognition?: unknown }).webkitSpeechRecognition
})

function mount(): FakeEventSource {
  const draft = { value: '' }
  const input = {
    setDraft: vi.fn((value: string) => { draft.value = value }),
    submit: vi.fn(),
    notify: vi.fn(),
    state: {
      getSnapshot: () => ({ draft: draft.value }),
      subscribe: () => () => {},
    },
  }
  context = new FakeContext({
    sessions: {
      list: sessionList,
      open: openSession,
      fork: forkSession,
      scope: (id: string) => ({ id }),
      binding: () => ({
        session: {
          cancel: cancelTurn,
          loadOlder: vi.fn(async () => {}),
          getSnapshot: () => ({ pending: [] }),
        },
      }),
    },
    workspaces: { startSession, archiveSession: vi.fn(async () => {}) },
    layout: { toggleSidebar: vi.fn(), openDetails: vi.fn(), closeDetails: vi.fn() },
    conversation: { input: { for: () => input } },
    modelDirectories: {
      directoryFor: () => ({
        store: {
          getSnapshot: () => ({
            current: currentModel,
            groups: [{
              id: 'deepseek',
              name: 'DeepSeek',
              models: [
                {
                  id: 'model-a',
                  name: 'Model A',
                  reasoning: {
                    defaultEffort: 'medium',
                    efforts: [
                      { id: 'low', name: 'Low' },
                      { id: 'medium', name: 'Medium' },
                      { id: 'high', name: 'High' },
                    ],
                  },
                },
                { id: 'model-b', name: 'Model B' },
              ],
            }],
            status: 'ready',
            error: null,
          }),
          subscribe: () => () => {},
        },
        load: vi.fn(async () => {}),
        select: selectModel,
      }),
    },
  })
  apply(context as unknown as Parameters<typeof apply>[0])
  return FakeEventSource.instances[0] as FakeEventSource
}

describe('external DeepSeek Harness browser bundle', () => {
  it('registers a native settings page and composer voice control', () => {
    mount()
    expect(context?.entries.map(entry => `${entry.name}:${entry.id}`)).toEqual([
      'conversation.input.right:agentcontroller-micro-voice',
      'settings.section:agentcontroller-micro-voice',
      'conversation.session.header.actions:agentcontroller-micro-view-bridge',
    ])
    expect(document.querySelector('style[data-plugin="agentcontroller-dsh-micro-voice"]')).not.toBeNull()
    expect(document.querySelector('style[data-plugin="agentcontroller-dsh-micro-navigation"]')).not.toBeNull()
  })

  it('adjusts reasoning independently and toggles between two available models', async () => {
    vi.spyOn(window, 'focus').mockImplementation(() => {})
    const source = mount()
    source.emit({
      version: 1,
      type: 'action/execute',
      requestId: 'reasoning-up',
      actionId: 'reasoning/increase',
      sessionId: 'session-1',
    })
    await vi.waitFor(() => {
      expect(selectModel).toHaveBeenCalledWith({
        provider: 'deepseek',
        model: 'model-a',
        reasoningEffort: 'high',
      })
    })

    source.emit({
      version: 1,
      type: 'action/execute',
      requestId: 'model-toggle',
      actionId: 'model/toggle-quick',
      sessionId: 'session-1',
    })
    await vi.waitFor(() => {
      expect(selectModel).toHaveBeenLastCalledWith({
        provider: 'deepseek',
        model: 'model-b',
      })
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'model-toggle',
        success: true,
      }))
    })
  })

  it('focuses and opens exact sessions through Harness services', async () => {
    const focus = vi.spyOn(window, 'focus').mockImplementation(() => {})
    const source = mount()
    expect(source.url).toMatch(new RegExp(`^${MICRO_EVENTS_ENDPOINT}\\?browserId=`))

    source.emit({ version: 1, type: 'activate', requestId: 'activate-1' })
    source.emit({
      version: 1,
      type: 'session/activate',
      requestId: 'session-1-open',
      sessionId: 'session-2',
    })

    await vi.waitFor(() => {
      expect(focus).toHaveBeenCalledTimes(2)
      expect(openSession).toHaveBeenCalledWith('session-2')
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'session-1-open',
        currentSessionId: 'session-2',
        success: true,
      }))
    })

    source.emit({
      version: 1,
      type: 'session/activate',
      requestId: 'session-2-already-current',
      sessionId: 'session-2',
    })
    await vi.waitFor(() => {
      expect(focus).toHaveBeenCalledTimes(3)
      expect(openSession).toHaveBeenCalledTimes(1)
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'session-2-already-current',
        currentSessionId: 'session-2',
        success: true,
        message: expect.stringContaining('already active'),
      }))
    })
  })

  it('uses domain actions and acknowledges voice edges without DOM simulation', async () => {
    vi.spyOn(window, 'focus').mockImplementation(() => {})
    const source = mount()
    const bridge = context?.entries.find(entry =>
      entry.id === 'agentcontroller-micro-view-bridge')
    const bindings = bridge?.inject?.().bindings as Map<string, {
      view: string | null
      setView(view: string): void
    }>
    bindings.set('session-1', {
      view: null,
      setView: context!.viewStore.setView,
    })
    source.emit({ version: 1, type: 'action/execute', requestId: 'new', actionId: 'session/new' })
    source.emit({
      version: 1,
      type: 'action/execute',
      requestId: 'fork',
      actionId: 'session/fork',
      sessionId: 'session-1',
    })
    source.emit({
      version: 1,
      type: 'action/execute',
      requestId: 'cancel',
      actionId: 'turn/cancel',
      sessionId: 'session-1',
    })
    source.emit({ version: 1, type: 'voice/stop', requestId: 'voice-stop' })
    source.emit({
      version: 1,
      type: 'action/execute',
      requestId: 'view-trajectory',
      actionId: 'view/toggle-chat-trajectory',
      sessionId: 'session-1',
    })

    await vi.waitFor(() => {
      expect(startSession).toHaveBeenCalledOnce()
      expect(forkSession).toHaveBeenCalledWith({ sessionId: 'session-1', increaseTitle: true })
      expect(openSession).toHaveBeenCalledWith('fork-child')
      expect(cancelTurn).toHaveBeenCalledOnce()
      expect(context?.viewStore.setView).toHaveBeenCalledWith('trajectory')
      expect(reports).toContainEqual(expect.objectContaining({ requestId: 'voice-stop', success: true }))
      expect(reports).toContainEqual(expect.objectContaining({ requestId: 'view-trajectory', success: true }))
    })
  })

  it('opens plugin-owned voice configuration when hardware requests setup', async () => {
    vi.spyOn(window, 'focus').mockImplementation(() => {})
    const source = mount()
    const voice = context?.entries.find(entry =>
      entry.id === 'agentcontroller-micro-voice')?.inject?.().voice as {
        getSnapshot(): { phase: string }
      }

    source.emit({ version: 1, type: 'voice/configure', requestId: 'voice-configure' })

    await vi.waitFor(() => {
      expect(voice.getSnapshot().phase).toBe('setup-required')
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'voice-configure',
        success: true,
        message: 'Micro Bridge voice settings opened.',
      }))
    })
  })

  it('reports voice success only after system recognition starts listening', async () => {
    class FakeSpeechRecognition {
      continuous = false
      interimResults = false
      lang = ''
      onresult = null
      onerror = null
      onend = null
      readonly start = vi.fn()
      readonly stop = vi.fn()
      readonly abort = vi.fn()
    }
    voiceSettings = {
      ...structuredClone(DEFAULT_VOICE_SETTINGS),
      provider: 'system',
      setupCompleted: true,
    }
    Object.defineProperty(window, 'webkitSpeechRecognition', {
      configurable: true,
      value: FakeSpeechRecognition,
    })
    const source = mount()

    source.emit({ version: 1, type: 'voice/start', requestId: 'voice-start' })

    await vi.waitFor(() => {
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'voice-start',
        success: true,
        message: 'Micro Bridge streaming voice input is listening.',
      }))
    })
  })

  it('ignores stale frames and releases its event source', () => {
    const source = mount()
    source.emit({ version: 2, type: 'activate', requestId: 'stale' })
    expect(reports.some(report => report.requestId === 'stale')).toBe(false)
    context?.dispose()
    context = undefined
    expect(source.closed).toBe(true)
    expect(sessionList.listeners.size).toBe(0)
  })
})
