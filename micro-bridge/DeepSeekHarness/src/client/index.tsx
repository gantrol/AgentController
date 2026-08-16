/** Browser half of the AgentController Micro bridge and voice plugin. */

import { useEffect, type ComponentType } from 'react'
import type {
  MicroActionId,
  MicroBrowserReport,
} from '../protocol.ts'
import {
  MICRO_EVENTS_ENDPOINT,
  MICRO_PROTOCOL_VERSION,
  MICRO_REPORT_ENDPOINT,
} from '../protocol.ts'
import {
  ComposerNavigator,
  ensureComposerNavigationStyles,
} from './composer-navigator.ts'
import {
  ModelController,
  type ModelDirectoriesFace,
} from './model-controller.ts'
import { VoiceSettingsClient } from './settings-client.ts'
import { VoiceController } from './voice-controller.ts'
import {
  VOICE_LOCALE_NAMESPACE,
  VoiceButton,
  VoiceSettingsSection,
  en,
  ensureVoiceStyles,
  zh,
  type VoiceTranslate,
} from './voice-ui.tsx'

interface SnapshotFace<T> {
  getSnapshot(): T
  subscribe(listener: () => void): () => void
}

interface SessionCancelResult {
  ok: boolean
  error?: { message?: string }
}

interface PendingReceipt {
  accepted: boolean
  reason?: string
}

interface ApprovalWait {
  kind: 'approval'
  sessionId: string
  payload: { approvalId: string }
  respond(result: unknown): Promise<PendingReceipt>
}

interface QuestionWait {
  kind: 'question'
  sessionId: string
  payload: {
    questions: Array<{
      id: string
      options?: Array<{ label: string }>
      intent?: { kind?: string; approve?: string }
    }>
  }
  respond(result: unknown): Promise<PendingReceipt>
}

type PendingWait = ApprovalWait | QuestionWait

interface SessionFace {
  cancel(): Promise<SessionCancelResult>
  loadOlder(): Promise<void>
  getSnapshot(): {
    pending: readonly PendingWait[]
  }
}

interface SessionsFace {
  readonly list: SnapshotFace<{ current?: string }>
  open(sessionId: string): void
  fork(options: { sessionId: string; increaseTitle?: boolean }): Promise<string>
  scope(sessionId: string): unknown | undefined
  binding(sessionId: string): {
    session: SessionFace
  } | undefined
}

interface WorkspacesFace {
  startSession(): void
  archiveSession(sessionId: string): Promise<void>
}

interface LayoutFace {
  toggleSidebar(): void
  openDetails(): void
  closeDetails(): void
}

interface InputFace {
  setDraft(text: string): void
  submit(): void
  notify(level: 'info' | 'error', text: string): void
  readonly state: SnapshotFace<{ draft: string }>
}

interface ConversationFace {
  readonly input: {
    for(scope: unknown): InputFace
  }
}

interface SlotsFace {
  inject(name: string, factory: () => unknown): void
  register<Props>(
    options: {
      name: string
      id: string
      order?: number
      label?: () => string
      locale?: string
      inject?: () => Record<string, unknown>
      store?: unknown
    },
    component: ComponentType<Props>,
  ): unknown
  /** Read-only slot ledger exposed by the Harness runtime. */
  entries?(name: string): readonly ConversationSlotEntry[]
}

interface ConversationSlotEntry {
  readonly store?: unknown
}

interface ConversationViewBinding {
  view: string | null
  setView(view: string): void
}

interface ConversationViewBridgeProps {
  sessionId: string
  useStore<T>(selector: (state: { view: string | null }) => T): T
  actions: { setView(view: string): void }
  bindings: Map<string, ConversationViewBinding>
}

interface LocaleFace {
  register(
    namespace: string,
    dictionaries: { zh: Readonly<Record<string, string>>; en: Readonly<Record<string, string>> },
  ): () => void
  bind(namespace: string): (key: string) => string
}

interface ClientContext {
  get(name: string): unknown
  effect(setup: () => void | (() => void), label: string): void
  readonly slots: SlotsFace
  readonly locale: LocaleFace
  readonly logger: {
    warn(message: unknown): void
  }
}

/** Cordis plugin name. */
export const name = 'client-agentcontroller-deepseek-harness'

/** Required browser services and native extension seats. */
export const inject = [
  'sessions',
  'workspaces',
  'layout',
  'conversation',
  'modelDirectories',
  'slots',
  'locale',
]

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

function mintBrowserId(): string {
  try {
    const existing = window.sessionStorage.getItem('dsh.codexMicro.browserId')
    if (existing !== null && existing !== '') return existing
    const value = globalThis.crypto.randomUUID()
    window.sessionStorage.setItem('dsh.codexMicro.browserId', value)
    return value
  } catch {
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`
  }
}

/** Invisible public-slot bridge over ui-conversation's registered view store. */
function ConversationViewBridge({
  sessionId,
  useStore,
  actions,
  bindings,
}: ConversationViewBridgeProps): null {
  const view = useStore(state => state.view)
  useEffect(() => {
    const binding: ConversationViewBinding = {
      view,
      setView: value => { actions.setView(value) },
    }
    bindings.set(sessionId, binding)
    return () => {
      if (bindings.get(sessionId) === binding) bindings.delete(sessionId)
    }
  }, [actions, bindings, sessionId, view])
  return null
}

function toggleConversationView(
  bindings: Map<string, ConversationViewBinding>,
  sessionId: string,
): 'chat' | 'trajectory' {
  const binding = bindings.get(sessionId)
  if (binding === undefined) {
    throw new Error('The current DeepSeek Harness session has no mounted conversation view.')
  }
  const next = binding.view === 'trajectory' ? 'chat' : 'trajectory'
  // Advance the bridge snapshot before dispatch so two rapid hardware presses
  // toggle deterministically even before React commits the store update.
  binding.view = next
  binding.setView(next)
  return next
}

/** Subscribe to host frames and report authoritative browser state. */
export function apply(ctx: ClientContext): void {
  const sessions = ctx.get('sessions') as SessionsFace | undefined
  const workspaces = ctx.get('workspaces') as WorkspacesFace | undefined
  const layout = ctx.get('layout') as LayoutFace | undefined
  const conversation = ctx.get('conversation') as ConversationFace | undefined
  const modelDirectories = ctx.get('modelDirectories') as ModelDirectoriesFace | undefined
  if (sessions === undefined) throw new Error('client-micro-bridge requires sessions')
  if (workspaces === undefined) throw new Error('client-micro-bridge requires workspaces')
  if (layout === undefined) throw new Error('client-micro-bridge requires layout')
  if (conversation === undefined) throw new Error('client-micro-bridge requires conversation')
  if (modelDirectories === undefined) throw new Error('client-micro-bridge requires modelDirectories')

  ensureVoiceStyles()
  ensureComposerNavigationStyles()
  ctx.effect(
    () => ctx.locale.register(VOICE_LOCALE_NAMESPACE, { zh, en }),
    'client-micro-bridge: voice dictionaries',
  )
  const t = ctx.locale.bind(VOICE_LOCALE_NAMESPACE) as VoiceTranslate
  const settingsClient = new VoiceSettingsClient()
  const voice = new VoiceController(sessions, conversation, settingsClient)
  const composerNavigator = new ComposerNavigator()
  const modelController = new ModelController(
    modelDirectories,
    () => sessions.list.getSnapshot().current,
    settingsClient,
  )
  const viewBindings = new Map<string, ConversationViewBinding>()
  ctx.effect(() => () => { void voice.dispose() }, 'client-micro-bridge: voice controller')
  ctx.effect(() => () => { composerNavigator.dispose() }, 'client-micro-bridge: composer navigator')

  ctx.slots.inject('conversation.input.right', () => ctx.slots.register({
    name: 'conversation.input.right',
    id: 'agentcontroller-micro-voice',
    order: 90,
    locale: VOICE_LOCALE_NAMESPACE,
    inject: () => ({ voice, settingsClient, modelController, t }),
  }, VoiceButton))
  ctx.slots.inject('settings.section', () => ctx.slots.register({
    name: 'settings.section',
    id: 'agentcontroller-micro-voice',
    order: 45,
    label: () => t('nav'),
    locale: VOICE_LOCALE_NAMESPACE,
    inject: () => ({ settingsClient, modelController, t }),
  }, VoiceSettingsSection))
  ctx.slots.inject('conversation.session.header.actions', () => {
    const sharedStore = (ctx.slots.entries?.(
      'conversation.session.header') ?? [])
      .find(entry => entry.store !== undefined)?.store
    if (sharedStore === undefined) {
      throw new Error('client-micro-bridge requires the conversation header view store')
    }
    return ctx.slots.register({
      name: 'conversation.session.header.actions',
      id: 'agentcontroller-micro-view-bridge',
      order: 999,
      store: sharedStore,
      inject: () => ({ bindings: viewBindings }),
    }, ConversationViewBridge)
  })

  ctx.effect(() => {
    const browserId = mintBrowserId()
    const source = new EventSource(
      `${MICRO_EVENTS_ENDPOINT}?browserId=${encodeURIComponent(browserId)}`,
    )

    const report = async (
      requestId?: string,
      success?: boolean,
      message?: string,
    ): Promise<void> => {
      const state = sessions.list.getSnapshot()
      const body: MicroBrowserReport = {
        version: MICRO_PROTOCOL_VERSION,
        browserId,
        currentSessionId: state.current ?? null,
        visible: document.visibilityState === 'visible',
        focused: document.hasFocus(),
        surface: new URL(window.location.href).searchParams.get('codexMicroSurface') === '1'
          ? 'dedicated'
          : 'tab',
        navigationDepth: composerNavigator.navigationDepth,
        ...(requestId === undefined ? {} : { requestId }),
        ...(success === undefined ? {} : { success }),
        ...(message === undefined ? {} : { message }),
      }
      try {
        await fetch(MICRO_REPORT_ENDPOINT, {
          method: 'POST',
          headers: { 'content-type': 'application/json' },
          body: JSON.stringify(body),
          keepalive: true,
        })
      } catch (error) {
        ctx.logger.warn('client-micro-bridge: browser report failed')
        ctx.logger.warn(error)
      }
    }

    const focusAndReport = async (
      requestId: string,
      success: boolean,
      message: string,
    ): Promise<void> => {
      window.focus()
      // Foreground ownership changes asynchronously on desktop browsers.
      await new Promise<void>((resolve) => { window.setTimeout(resolve, 50) })
      await report(requestId, success, message)
    }

    const currentSession = (explicit?: unknown): string | undefined =>
      typeof explicit === 'string' && explicit.trim() !== ''
        ? explicit
        : sessions.list.getSnapshot().current

    const requireSession = (explicit: string | undefined, operation: string): {
      id: string
      face: SessionFace
    } => {
      const id = currentSession(explicit)
      if (id === undefined) throw new Error(`No current session is available to ${operation}.`)
      const face = sessions.binding(id)?.session
      if (face === undefined) throw new Error('The current session is not locally addressable.')
      return { id, face }
    }

    const answerPending = async (
      sessionId: string | undefined,
      outcome: 'allowed-once' | 'rejected',
    ): Promise<string> => {
      const { face } = requireSession(sessionId, 'answer')
      const snapshot = face.getSnapshot()
      const approval = snapshot.pending.find((item): item is ApprovalWait => item.kind === 'approval')
      if (approval !== undefined) {
        const receipt = await approval.respond({
          ok: true,
          value: { sessionId: approval.sessionId, approvalId: approval.payload.approvalId, outcome },
        })
        if (!receipt.accepted) throw new Error(receipt.reason ?? 'The approval response was rejected.')
        return outcome === 'allowed-once'
          ? 'DeepSeek Harness approval allowed once.'
          : 'DeepSeek Harness approval rejected.'
      }

      const question = snapshot.pending.find((item): item is QuestionWait => item.kind === 'question')
      const review = question?.payload.questions.find(item => item.intent?.kind === 'plan-review')
      const approveLabel = review?.intent?.approve
      const selected = outcome === 'allowed-once'
        ? approveLabel
        : review?.options?.find(option => option.label !== approveLabel)?.label
      if (question === undefined || review === undefined || selected === undefined) {
        throw new Error('No pending approval or binary plan review is available.')
      }
      const receipt = await question.respond({
        ok: true,
        value: {
          sessionId: question.sessionId,
          answer: { answers: [{ id: review.id, selected: [selected] }] },
        },
      })
      if (!receipt.accepted) throw new Error(receipt.reason ?? 'The plan review response was rejected.')
      return outcome === 'allowed-once'
        ? 'DeepSeek Harness plan approved.'
        : 'DeepSeek Harness plan declined.'
    }

    const execute = async (
      actionId: MicroActionId,
      sessionId: string | undefined,
    ): Promise<string> => {
      switch (actionId) {
        case 'session/new':
          workspaces.startSession()
          return 'New DeepSeek Harness session requested.'
        case 'session/fork': {
          const sourceId = currentSession(sessionId)
          if (sourceId === undefined) throw new Error('No current session is available to fork.')
          const childId = await sessions.fork({ sessionId: sourceId, increaseTitle: true })
          sessions.open(childId)
          return 'DeepSeek Harness session forked.'
        }
        case 'session/archive': {
          const targetId = currentSession(sessionId)
          if (targetId === undefined) throw new Error('No current session is available to archive.')
          await workspaces.archiveSession(targetId)
          return 'DeepSeek Harness session archived.'
        }
        case 'turn/cancel': {
          const { face } = requireSession(sessionId, 'stop')
          const result = await face.cancel()
          if (!result.ok) throw new Error(result.error?.message ?? 'The running turn rejected cancellation.')
          return 'DeepSeek Harness turn cancelled.'
        }
        case 'view/toggle-chat-trajectory': {
          const targetId = currentSession(sessionId)
          if (targetId === undefined) {
            throw new Error('No current session is available to switch views.')
          }
          const next = toggleConversationView(viewBindings, targetId)
          return next === 'trajectory'
            ? 'DeepSeek Harness trajectory opened.'
            : 'DeepSeek Harness conversation opened.'
        }
        case 'interaction/approve':
          return await answerPending(sessionId, 'allowed-once')
        case 'interaction/reject':
          return await answerPending(sessionId, 'rejected')
        case 'history/load-older': {
          const { face } = requireSession(sessionId, 'load older history')
          await face.loadOlder()
          return 'Older DeepSeek Harness history loaded.'
        }
        case 'layout/toggle-sidebar':
          layout.toggleSidebar()
          return 'DeepSeek Harness sidebar toggled.'
        case 'layout/open-details':
          layout.openDetails()
          return 'DeepSeek Harness details opened.'
        case 'layout/close-details':
          layout.closeDetails()
          return 'DeepSeek Harness details closed.'
        case 'composer/select-previous':
          return `Composer control selected: ${composerNavigator.step(-1)}.`
        case 'composer/select-next':
          return `Composer control selected: ${composerNavigator.step(1)}.`
        case 'composer/activate-selection':
        {
          const label = composerNavigator.activate()
          await composerNavigator.settle()
          return `Composer control activated: ${label}.`
        }
        case 'composer/back':
          return await composerNavigator.back()
        case 'reasoning/decrease':
          return await modelController.stepReasoning(-1, currentSession(sessionId))
        case 'reasoning/increase':
          return await modelController.stepReasoning(1, currentSession(sessionId))
        case 'model/toggle-quick':
          return await modelController.toggleQuickModel(currentSession(sessionId))
        case 'goal/open': {
          const targetId = currentSession(sessionId)
          if (targetId === undefined) throw new Error('No current session is available for Goal.')
          const scope = sessions.scope(targetId)
          if (scope === undefined) throw new Error('The current session has no composer scope.')
          const input = conversation.input.for(scope)
          const draft = input.state.getSnapshot().draft
          if (draft.trim() !== '' && !draft.trimStart().startsWith('/goal')) {
            throw new Error('The composer already contains a draft; Goal did not overwrite it.')
          }
          input.setDraft('/goal ')
          const textarea = [...document.querySelectorAll<HTMLTextAreaElement>(
            '[data-composer-card] textarea',
          )].at(-1)
          textarea?.focus({ preventScroll: true })
          textarea?.setSelectionRange(6, 6)
          return 'Goal command opened in the DeepSeek Harness composer.'
        }
      }
    }

    const handleFrame = async (fields: Record<string, unknown>): Promise<void> => {
      const requestId = typeof fields.requestId === 'string' ? fields.requestId : undefined
      if (requestId === undefined || requestId.trim() === '') return
      try {
        switch (fields.type) {
          case 'activate':
            await focusAndReport(requestId, true, 'DeepSeek Harness focused.')
            return
          case 'session/activate':
            if (typeof fields.sessionId !== 'string' || fields.sessionId.trim() === '') return
            if (sessions.list.getSnapshot().current !== fields.sessionId) {
              sessions.open(fields.sessionId)
              await focusAndReport(requestId, true, 'DeepSeek Harness session activated.')
            } else {
              // sessions.open() is not guaranteed to be idempotent in every
              // Harness build. Re-selecting the already active Agent key is a
              // focus gesture, not a second navigation request.
              await focusAndReport(requestId, true, 'DeepSeek Harness session was already active and was focused.')
            }
            return
          case 'action/execute': {
            const actionId = fields.actionId
            if (typeof actionId !== 'string' || !ACTION_IDS.has(actionId as MicroActionId)) return
            const message = await execute(
              actionId as MicroActionId,
              currentSession(fields.sessionId),
            )
            await focusAndReport(requestId, true, message)
            return
          }
          case 'voice/start':
            await voice.start(currentSession(fields.sessionId))
            await focusAndReport(requestId, true, 'Micro Bridge streaming voice input is listening.')
            return
          case 'voice/stop':
            await report(requestId, true, 'DeepSeek Harness voice input is finishing.')
            void voice.stop()
            return
          case 'voice/configure':
            await voice.showConfiguration(currentSession(fields.sessionId))
            await focusAndReport(requestId, true, 'Micro Bridge voice settings opened.')
            return
          default:
            return
        }
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error)
        ctx.logger.warn('client-micro-bridge: browser action was rejected')
        ctx.logger.warn(error)
        await focusAndReport(requestId, false, message)
      }
    }

    source.addEventListener('open', () => { void report() })
    source.addEventListener('message', (event: MessageEvent<string>) => {
      let frame: unknown
      try {
        frame = JSON.parse(event.data) as unknown
      } catch {
        ctx.logger.warn(`client-micro-bridge: unparseable event frame: ${event.data}`)
        return
      }
      if (typeof frame !== 'object' || frame === null || Array.isArray(frame)) return
      const fields = frame as Record<string, unknown>
      if (fields.version !== MICRO_PROTOCOL_VERSION) return
      void handleFrame(fields)
    })
    const reportState = (): void => { void report() }
    const unsubscribe = sessions.list.subscribe(reportState)
    const unsubscribeNavigation = composerNavigator.subscribe(reportState)
    document.addEventListener('visibilitychange', reportState)
    window.addEventListener('focus', reportState)
    window.addEventListener('blur', reportState)
    void report()
    return () => {
      source.close()
      unsubscribe()
      unsubscribeNavigation()
      document.removeEventListener('visibilitychange', reportState)
      window.removeEventListener('focus', reportState)
      window.removeEventListener('blur', reportState)
    }
  }, 'client-micro-bridge: event source')
}
