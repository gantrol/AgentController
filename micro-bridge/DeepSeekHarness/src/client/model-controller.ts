/** Direct model/reasoning control over DeepSeek Harness's shared model directory. */

interface QuickModelRef {
  provider: string
  model: string
}

interface SnapshotFace<T> {
  getSnapshot(): T
  subscribe(listener: () => void): () => void
}

interface ReasoningEffort {
  id: string
  name: string
}

interface CatalogModel {
  id: string
  name: string
  reasoning?: {
    efforts: readonly ReasoningEffort[]
    defaultEffort?: string
  }
}

interface ProviderGroup {
  id: string
  name: string
  models: readonly CatalogModel[]
}

interface ModelSelection extends QuickModelRef {
  reasoningEffort?: string
}

interface ModelDirectoryState {
  current: ModelSelection | null
  groups: readonly ProviderGroup[]
  status: 'idle' | 'loading' | 'ready' | 'selecting' | 'error'
  error: string | null
}

interface ModelDirectory {
  readonly store: SnapshotFace<ModelDirectoryState>
  load(): Promise<unknown>
  select(selection: ModelSelection): Promise<void>
}

export interface ModelDirectoriesFace {
  directoryFor(sessionId: string): ModelDirectory
}

export interface QuickModelChoice {
  ref: QuickModelRef
  label: string
}

export interface ModelControlSnapshot {
  status: 'idle' | 'loading' | 'ready' | 'error'
  choices: readonly QuickModelChoice[]
  current?: ModelSelection
  currentLabel?: string
  error?: string
}

interface ResolvedChoice extends QuickModelChoice {
  model: CatalogModel
}

type Listener = () => void

function same(left: QuickModelRef | undefined, right: QuickModelRef | undefined): boolean {
  return left !== undefined && right !== undefined
    && left.provider === right.provider && left.model === right.model
}

function flatten(state: ModelDirectoryState): ResolvedChoice[] {
  return state.groups.flatMap(group => group.models.map(model => ({
    ref: { provider: group.id, model: model.id },
    label: `${model.name} · ${group.name}`,
    model,
  })))
}

function selection(choice: ResolvedChoice): ModelSelection {
  const effort = choice.model.reasoning?.defaultEffort
  return {
    ...choice.ref,
    ...(effort === undefined ? {} : { reasoningEffort: effort }),
  }
}

/** One controller is shared by hardware actions and the plugin settings page. */
export class ModelController {
  private snapshot: ModelControlSnapshot = { status: 'idle', choices: [] }
  private readonly listeners = new Set<Listener>()
  private activeDirectory: ModelDirectory | undefined
  private unsubscribeDirectory: (() => void) | undefined

  constructor(
    private readonly directories: ModelDirectoriesFace,
    private readonly currentSessionId: () => string | undefined,
  ) {}

  getSnapshot = (): ModelControlSnapshot => this.snapshot

  subscribe = (listener: Listener): (() => void) => {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  dispose(): void {
    this.unsubscribeDirectory?.()
    this.unsubscribeDirectory = undefined
    this.activeDirectory = undefined
  }

  async refresh(): Promise<void> {
    this.publish({ ...this.snapshot, status: 'loading' })
    try {
      const { state } = await this.load()
      this.publishState(state)
    } catch (error) {
      this.publish({
        ...this.snapshot,
        status: 'error',
        error: error instanceof Error ? error.message : String(error),
      })
      throw error
    }
  }

  async stepReasoning(delta: -1 | 1, explicitSessionId?: string): Promise<string> {
    const { directory, state } = await this.load(explicitSessionId)
    const current = state.current
    if (current === null) throw new Error('The current session has no selected model.')
    const choice = flatten(state).find(item => same(item.ref, current))
    if (choice === undefined) throw new Error('The selected model is absent from the current model directory.')
    const reasoning = choice.model.reasoning
    if (reasoning === undefined || reasoning.efforts.length === 0) {
      throw new Error('The selected model does not expose reasoning effort levels.')
    }
    const levels: Array<{ id?: string; name: string }> = [
      ...(reasoning.defaultEffort === undefined
        ? [{ name: 'Provider default' }]
        : []),
      ...reasoning.efforts,
    ]
    const effective = current.reasoningEffort ?? reasoning.defaultEffort
    const at = Math.max(0, levels.findIndex(level => level.id === effective))
    const next = Math.max(0, Math.min(levels.length - 1, at + delta))
    const target = levels[next]
    if (target === undefined) throw new Error('No reasoning effort is available.')
    if (next !== at) {
      await directory.select({
        provider: current.provider,
        model: current.model,
        ...(target.id === undefined ? {} : { reasoningEffort: target.id }),
      })
    }
    this.publishState(directory.store.getSnapshot())
    return `Reasoning effort: ${target.name}.`
  }

  async toggleQuickModel(explicitSessionId?: string): Promise<string> {
    const { directory, state } = await this.load(explicitSessionId)
    const choices = flatten(state)
    if (choices.length < 2) throw new Error('At least two models are required for quick switching.')
    const currentChoice = choices.find(item => same(item.ref, state.current ?? undefined))
    const first = currentChoice ?? choices[0]
    if (first === undefined) throw new Error('No first quick model is available.')
    const firstIndex = choices.findIndex(item => same(item.ref, first.ref))
    const second = choices[(firstIndex + 1) % choices.length]
    if (second === undefined) throw new Error('No second quick model is available.')
    const target = same(state.current ?? undefined, first.ref) ? second : first
    await directory.select(selection(target))
    this.publishState(directory.store.getSnapshot())
    return `Quick model switched to ${target.label}.`
  }

  private async load(explicitSessionId?: string): Promise<{
    directory: ModelDirectory
    state: ModelDirectoryState
  }> {
    const sessionId = explicitSessionId ?? this.currentSessionId()
    if (sessionId === undefined || sessionId.trim() === '') {
      throw new Error('No current session is available for model control.')
    }
    const directory = this.directories.directoryFor(sessionId)
    this.bind(directory)
    await directory.load()
    return { directory, state: directory.store.getSnapshot() }
  }

  private bind(directory: ModelDirectory): void {
    if (this.activeDirectory === directory) return
    this.unsubscribeDirectory?.()
    this.activeDirectory = directory
    this.unsubscribeDirectory = directory.store.subscribe(() => {
      if (this.activeDirectory === directory) {
        this.publishState(directory.store.getSnapshot())
      }
    })
  }

  private publishState(state: ModelDirectoryState): void {
    const resolved = flatten(state)
    const choices = resolved.map(({ ref, label }) => ({ ref, label }))
    const currentChoice = state.current === null
      ? undefined
      : resolved.find(item => same(item.ref, state.current ?? undefined))
    this.publish({
      status: 'ready',
      choices,
      ...(state.current === null ? {} : { current: state.current }),
      ...(currentChoice === undefined ? {} : { currentLabel: currentChoice.model.name }),
      ...(state.error === null ? {} : { error: state.error }),
    })
  }

  private publish(snapshot: ModelControlSnapshot): void {
    this.snapshot = snapshot
    for (const listener of [...this.listeners]) listener()
  }
}
