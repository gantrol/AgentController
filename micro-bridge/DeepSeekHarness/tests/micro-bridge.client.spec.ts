// @vitest-environment jsdom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { apply } from '../src/client/index.tsx'
import {
  MICRO_EVENTS_ENDPOINT,
  MICRO_REPORT_ENDPOINT,
  MICRO_VOICE_BUTTON_ENDPOINT,
} from '../src/protocol.ts'

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

interface FakeSessionSummary {
  sessionId: string
  running: boolean
  parentSessionId?: string
  pendingInteraction?: 'approval' | 'plan-review' | 'question'
  completed: boolean
}

class FakeSessionList {
  current: string | undefined = 'session-1'
  items: FakeSessionSummary[] = [
    { sessionId: 'session-1', running: false, completed: false },
  ]
  readonly listeners = new Set<() => void>()

  getSnapshot(): {
    current?: string
    items: FakeSessionSummary[]
  } {
    return {
      ...(this.current === undefined ? {} : { current: this.current }),
      items: this.items,
    }
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  publish(): void {
    for (const listener of this.listeners) listener()
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
let submitComposer: ReturnType<typeof vi.fn>
let selectModel: ReturnType<typeof vi.fn>
let currentModel: {
  provider: string
  model: string
  reasoningEffort?: string
}
let reports: Array<Record<string, unknown>>
let voiceButtonRequests: Array<Record<string, unknown>>
let voiceButtonResult: { success: boolean; active: boolean; message: string }
let composerDraft: { value: string }
let sessionRuntimeStates: Record<string, {
  pending: readonly []
  running?: boolean
  lastAgentError?: string | null
  promptError?: unknown | null
}>

beforeEach(() => {
  FakeEventSource.instances = []
  sessionList = new FakeSessionList()
  openSession = vi.fn((id: string) => { sessionList.current = id })
  forkSession = vi.fn(async () => 'fork-child')
  cancelTurn = vi.fn(async () => ({ ok: true }))
  startSession = vi.fn()
  submitComposer = vi.fn()
  currentModel = { provider: 'deepseek', model: 'model-a', reasoningEffort: 'medium' }
  selectModel = vi.fn(async (selection: typeof currentModel) => {
    currentModel = { ...selection }
  })
  reports = []
  voiceButtonRequests = []
  voiceButtonResult = {
    success: true,
    active: true,
    message: 'The keypad microphone is listening.',
  }
  sessionRuntimeStates = {}
  vi.stubGlobal('EventSource', FakeEventSource)
  vi.stubGlobal('fetch', vi.fn(async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input)
    if (url === MICRO_VOICE_BUTTON_ENDPOINT) {
      if (typeof init?.body !== 'string') throw new TypeError('expected voice-button JSON body')
      voiceButtonRequests.push(JSON.parse(init.body) as Record<string, unknown>)
      return new Response(JSON.stringify(voiceButtonResult), {
        status: voiceButtonResult.success ? 200 : 409,
        headers: { 'content-type': 'application/json' },
      })
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
})

function mount(initialDraft = ''): FakeEventSource {
  composerDraft = { value: initialDraft }
  const input = {
    setDraft: vi.fn((value: string) => { composerDraft.value = value }),
    submit: submitComposer,
    notify: vi.fn(),
    state: {
      getSnapshot: () => ({ draft: composerDraft.value }),
      subscribe: () => () => {},
    },
  }
  context = new FakeContext({
    sessions: {
      list: sessionList,
      open: openSession,
      fork: forkSession,
      scope: (id: string) => ({ id }),
      binding: (id: string) => ({
        session: {
          cancel: cancelTurn,
          loadOlder: vi.fn(async () => {}),
          getSnapshot: () => sessionRuntimeStates[id] ?? { pending: [] },
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
  it('reports running, completed, waiting, error, and idle session states', async () => {
    sessionList.current = 'running'
    sessionList.items = [
      { sessionId: 'running', running: true, completed: false },
      { sessionId: 'completed', running: false, completed: true },
      {
        sessionId: 'waiting',
        running: true,
        pendingInteraction: 'approval',
        completed: false,
      },
      { sessionId: 'error', running: false, completed: false },
      { sessionId: 'idle', running: false, completed: false },
      {
        sessionId: 'child',
        parentSessionId: 'running',
        running: true,
        completed: false,
      },
    ]
    sessionRuntimeStates.error = {
      pending: [],
      lastAgentError: 'model failed',
    }

    mount()

    await vi.waitFor(() => {
      expect(reports).toContainEqual(expect.objectContaining({
        sessionStates: [
          { id: 'running', status: 'running' },
          { id: 'completed', status: 'completed' },
          { id: 'waiting', status: 'waiting' },
          { id: 'error', status: 'error' },
          { id: 'idle', status: 'idle' },
        ],
      }))
    })

    sessionList.items = sessionList.items.map(item => item.sessionId === 'running'
      ? { ...item, running: false, completed: false }
      : item)
    sessionList.publish()
    await vi.waitFor(() => {
      expect(reports.at(-1)?.sessionStates).toEqual(expect.arrayContaining([
        { id: 'running', status: 'completed' },
      ]))
    })

    sessionList.items = sessionList.items.map(item => item.sessionId === 'running'
      ? { ...item, running: true }
      : item)
    sessionList.publish()
    await vi.waitFor(() => {
      expect(reports.at(-1)?.sessionStates).toEqual(expect.arrayContaining([
        { id: 'running', status: 'running' },
      ]))
    })

    sessionList.items = sessionList.items.map(item => item.sessionId === 'running'
      ? { ...item, running: false }
      : item)
    sessionList.publish()
    await vi.waitFor(() => {
      expect(reports.at(-1)?.sessionStates).toEqual(expect.arrayContaining([
        { id: 'running', status: 'completed' },
      ]))
    })
  })

  it('registers exactly one composer voice control and no plugin settings page', () => {
    mount()
    expect(context?.entries.map(entry => `${entry.name}:${entry.id}`)).toEqual([
      'conversation.input.right:agentcontroller-micro-voice',
      'conversation.session.header.actions:agentcontroller-micro-view-bridge',
    ])
    expect(document.querySelector('style[data-plugin="agentcontroller-dsh-micro-voice"]')).not.toBeNull()
    expect(document.querySelector('style[data-plugin="agentcontroller-dsh-micro-navigation"]')).not.toBeNull()
  })

  it('adjusts reasoning independently and toggles between two available models', async () => {
    vi.spyOn(window, 'focus').mockImplementation(() => {})
    const source = mount()
    await vi.waitFor(() => {
      expect(reports).toContainEqual(expect.objectContaining({
        currentModel: 'Model A',
      }))
    })
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
        currentModel: 'Model B',
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

  it('submits a non-empty current composer through the native input service', async () => {
    const source = mount('Explain this change')

    source.emit({
      version: 1,
      type: 'action/execute',
      requestId: 'composer-submit',
      actionId: 'composer/submit',
      sessionId: 'session-1',
    })

    await vi.waitFor(() => {
      expect(submitComposer).toHaveBeenCalledOnce()
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'composer-submit',
        success: true,
        message: 'DeepSeek Harness composer submitted.',
      }))
    })
  })

  it('does not submit an empty composer', async () => {
    const source = mount('   ')

    source.emit({
      version: 1,
      type: 'action/execute',
      requestId: 'composer-submit-empty',
      actionId: 'composer/submit',
      sessionId: 'session-1',
    })

    await vi.waitFor(() => {
      expect(submitComposer).not.toHaveBeenCalled()
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'composer-submit-empty',
        success: false,
        message: expect.stringContaining('empty'),
      }))
    })
  })

  it('uses domain actions without DOM simulation', async () => {
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
      expect(reports).toContainEqual(expect.objectContaining({ requestId: 'view-trajectory', success: true }))
    })
  })

  it('projects keypad voice status onto the single DeepSeek button', async () => {
    const source = mount()
    const voice = context?.entries.find(entry =>
      entry.id === 'agentcontroller-micro-voice')?.inject?.().voice as {
        getSnapshot(): { active: boolean; phase: string; sessionId?: string; message: string }
      }

    source.emit({
      version: 1,
      type: 'voice/status',
      active: true,
      phase: 'listening',
      sessionId: 'session-1',
      message: 'The keypad microphone is listening.',
    })

    await vi.waitFor(() => {
      expect(voice.getSnapshot()).toMatchObject({
        active: true,
        phase: 'listening',
        sessionId: 'session-1',
        message: 'The keypad microphone is listening.',
      })
    })

    source.emit({
      version: 1,
      type: 'voice/status',
      active: true,
      phase: 'restarting',
      sessionId: 'session-1',
      message: 'Restarting the keypad-owned voice service.',
    })
    await vi.waitFor(() => {
      expect(voice.getSnapshot()).toMatchObject({
        active: true,
        phase: 'restarting',
        sessionId: 'session-1',
        message: 'Restarting the keypad-owned voice service.',
      })
    })
  })

  it('sends the DeepSeek voice button only to the keypad bridge endpoint', async () => {
    const source = mount()
    expect(source).toBeDefined()
    const voice = context?.entries.find(entry =>
      entry.id === 'agentcontroller-micro-voice')?.inject?.().voice as {
        toggle(sessionId: string): Promise<void>
        getSnapshot(): { active: boolean; phase: string }
      }

    await voice.toggle('session-1')

    expect(voiceButtonRequests).toEqual([{ sessionId: 'session-1' }])
    expect(voice.getSnapshot()).toMatchObject({ active: true, phase: 'listening' })
  })

  it('writes keypad-recognized text into the exact composer and can submit it', async () => {
    const source = mount('你好')
    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'dictation-1',
      sessionId: 'session-1',
      text: '世界',
      language: 'zh-CN',
      autoSubmit: true,
    })

    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('你好世界')
      expect(submitComposer).toHaveBeenCalledOnce()
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'dictation-1',
        success: true,
        message: 'Keypad dictation was written and submitted.',
      }))
    })
  })

  it('replaces live keypad partials and commits only the final transcript', async () => {
    const source = mount('已有内容：')
    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'dictation-partial-1',
      sessionId: 'session-1',
      text: '你好',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'stream-1',
      dictationPhase: 'partial',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('已有内容：你好')
    })

    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'dictation-partial-2',
      sessionId: 'session-1',
      text: '你好世界',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'stream-1',
      dictationPhase: 'partial',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('已有内容：你好世界')
    })

    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'dictation-final',
      sessionId: 'session-1',
      text: '你好，世界！',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'stream-1',
      dictationPhase: 'final',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('已有内容：你好，世界！')
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'dictation-final',
        success: true,
      }))
    })
  })

  it('e2e: preserves a user edit while later live dictation frames arrive', async () => {
    const source = mount('已有内容：')
    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'manual-edit-partial-1',
      sessionId: 'session-1',
      text: '你好',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'manual-edit-stream',
      dictationPhase: 'partial',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('已有内容：你好')
    })

    composerDraft.value = '用户手改：您好'
    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'manual-edit-partial-2',
      sessionId: 'session-1',
      text: '你好世界',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'manual-edit-stream',
      dictationPhase: 'partial',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('用户手改：您好世界')
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'manual-edit-partial-2',
        success: true,
      }))
    })

    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'manual-edit-revision',
      sessionId: 'session-1',
      text: '您好，世界',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'manual-edit-stream',
      dictationPhase: 'partial',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('用户手改：您好世界')
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'manual-edit-revision',
        success: true,
      }))
    })

    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'manual-edit-final',
      sessionId: 'session-1',
      text: '您好，世界！',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'manual-edit-stream',
      dictationPhase: 'final',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('用户手改：您好世界！')
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'manual-edit-final',
        success: true,
      }))
    })
  })

  it('restores the original draft when a live keypad preview is cancelled', async () => {
    const source = mount('原稿')
    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'dictation-partial',
      sessionId: 'session-1',
      text: '临时识别',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'stream-cancel',
      dictationPhase: 'partial',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('原稿临时识别')
    })

    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'dictation-cancel',
      sessionId: 'session-1',
      text: '',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'stream-cancel',
      dictationPhase: 'cancel',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('原稿')
    })
  })

  it('keeps a manual edit when live keypad dictation is cancelled', async () => {
    const source = mount('原稿')
    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'manual-cancel-partial',
      sessionId: 'session-1',
      text: '临时识别',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'manual-cancel-stream',
      dictationPhase: 'partial',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('原稿临时识别')
    })

    composerDraft.value = '用户保留稿'
    source.emit({
      version: 1,
      type: 'composer/dictate',
      requestId: 'manual-cancel',
      sessionId: 'session-1',
      text: '',
      language: 'zh-CN',
      autoSubmit: false,
      dictationId: 'manual-cancel-stream',
      dictationPhase: 'cancel',
    })
    await vi.waitFor(() => {
      expect(composerDraft.value).toBe('用户保留稿')
      expect(reports).toContainEqual(expect.objectContaining({
        requestId: 'manual-cancel',
        success: true,
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
