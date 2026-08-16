/** Dynamic, composer-scoped rotary navigation for controls contributed by any plugin. */

const COMPOSER_SELECTOR = '[data-composer-card]'
const INTERACTION_LAYER_SELECTOR = [
  '[role="dialog"]',
  '[role="menu"]',
  '[role="listbox"]',
  '[aria-modal="true"]',
].join(',')
const CANDIDATE_SELECTOR = [
  'select',
  'button',
  'input[type="button"]',
  'input[type="submit"]',
  'input[type="checkbox"]',
  'input[type="radio"]',
  'input[type="range"]',
  '[role="button"]',
  '[role="menuitem"]',
  '[role="menuitemradio"]',
  '[role="menuitemcheckbox"]',
  '[role="option"]',
  '[tabindex]:not(textarea):not(input):not([contenteditable="true"])',
].join(',')

const HIGHLIGHT_ATTRIBUTE = 'data-codex-micro-selected'
const STYLE_SELECTOR = 'style[data-plugin="agentcontroller-dsh-micro-navigation"]'
const SETTLE_DELAY_MS = 55

interface SavedSelection {
  index: number
  label: string
}

function hiddenByStyle(element: Element, boundary?: Element): boolean {
  let current: Element | null = element
  while (current !== null) {
    const style = window.getComputedStyle(current)
    if (style.display === 'none' || style.visibility === 'hidden') return true
    if (current === boundary) break
    current = current.parentElement
  }
  return false
}

function available(element: HTMLElement, boundary: Element): boolean {
  if (element.hidden || element.closest('[hidden],[aria-hidden="true"]') !== null) return false
  if (hiddenByStyle(element, boundary)) return false
  if (element.getAttribute('aria-disabled') === 'true') return false
  if ('disabled' in element && (element as HTMLButtonElement).disabled) return false
  return true
}

function activeComposer(): HTMLElement | undefined {
  const composers = [...document.querySelectorAll<HTMLElement>(COMPOSER_SELECTOR)]
    .filter(composer => !composer.hidden
      && composer.closest('[hidden],[aria-hidden="true"]') === null
      && !hiddenByStyle(composer))
  const active = document.activeElement
  return composers.find(composer => active !== null && composer.contains(active))
    ?? composers.at(-1)
}

function activeInteractionLayers(composer: HTMLElement): HTMLElement[] {
  return [...composer.querySelectorAll<HTMLElement>(INTERACTION_LAYER_SELECTOR)]
    .filter(layer => available(layer, composer))
}

function topInteractionLayer(composer: HTMLElement): HTMLElement | undefined {
  const layers = activeInteractionLayers(composer)
  const active = document.activeElement
  const focused = layers.filter(layer => active !== null && layer.contains(active)).at(-1)
  if (focused !== undefined) return focused

  // A nested layer is always more specific than its ancestor. When peers are
  // visible, the later DOM node is the one most recently painted by React.
  return layers
    .filter(layer => !layers.some(other => other !== layer && layer.contains(other)))
    .at(-1)
}

function candidates(boundary: HTMLElement): HTMLElement[] {
  const seen = new Set<HTMLElement>()
  const values: HTMLElement[] = []
  for (const element of boundary.querySelectorAll<HTMLElement>(CANDIDATE_SELECTOR)) {
    if (seen.has(element) || !available(element, boundary)) continue
    // A parent button and a semantic child can both match the selector. Keep
    // only the actual interactive owner so one visual row is never selected twice.
    const interactiveParent = element.parentElement?.closest<HTMLElement>(CANDIDATE_SELECTOR)
    if (interactiveParent !== null && interactiveParent !== undefined && boundary.contains(interactiveParent)) {
      continue
    }
    seen.add(element)
    values.push(element)
  }
  return values
}

function compact(value: string): string {
  return value.replace(/\s+/gu, ' ').trim().slice(0, 96)
}

export function composerControlLabel(element: HTMLElement): string {
  const aria = element.getAttribute('aria-label')
  if (aria !== null && aria.trim() !== '') return compact(aria)
  const title = element.getAttribute('title')
  if (title !== null && title.trim() !== '') return compact(title)
  if (element instanceof HTMLSelectElement) {
    const selected = element.selectedOptions.item(0)?.textContent
    if (selected !== undefined && selected !== null && selected.trim() !== '') return compact(selected)
  }
  const text = element.textContent
  if (text !== null && text.trim() !== '') return compact(text)
  if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) {
    if (element.placeholder.trim() !== '') return compact(element.placeholder)
  }
  return element.tagName.toLowerCase()
}

function controlledLayerTrigger(
  composer: HTMLElement,
  layer: HTMLElement,
): HTMLElement | undefined {
  // ModelSelect publishes the strongest association: the trigger explicitly
  // controls the menu id.
  if (layer.id !== '') {
    const controlled = [...composer.querySelectorAll<HTMLElement>('[aria-controls]')]
      .find(element => !layer.contains(element)
        && available(element, composer)
        && element.getAttribute('aria-controls')?.split(/\s+/u).includes(layer.id))
    if (controlled !== undefined) return controlled
  }

  // InputBar's command launcher has no aria-controls, but does expose the
  // expanded popup type. Match the popup role so an unrelated expanded
  // control can never be used as a dismiss target.
  const layerRole = layer.getAttribute('role')
  if (layerRole !== null) {
    const expanded = [...composer.querySelectorAll<HTMLElement>('[aria-expanded="true"][aria-haspopup]')]
      .find(element => {
        if (layer.contains(element) || !available(element, composer)) return false
        const popup = element.getAttribute('aria-haspopup')
        return popup === layerRole || (popup === 'true' && layerRole === 'menu')
      })
    if (expanded !== undefined) return expanded
  }

  // PermissionSelect's primitive Menu keeps the anchor and popup as siblings
  // in a private wrapper and injects no ARIA relationship into the anchor.
  // Only accept the single outside button in the nearest containing wrapper;
  // never widen this search to the whole composer.
  let container = layer.parentElement
  while (container !== null && container !== composer) {
    const boundary = container
    const anchors = [...boundary.querySelectorAll<HTMLElement>('button,[role="button"]')]
      .filter(element => !layer.contains(element) && available(element, boundary))
    if (anchors.length === 1) return anchors[0]
    container = boundary.parentElement
  }

  return undefined
}

function paneDepth(layer: HTMLElement): number {
  // DSH's model selector keeps its root and drilled panes in the same menu.
  // Its drilled panes expose radio menuitems *and* the menu is explicitly
  // associated with a trigger. Permission menus also contain radio items but
  // intentionally have no aria-controls; they remain a single level.
  if (layer.querySelector('[role="menuitemradio"],[role="menuitemcheckbox"]') === null
    || layer.id === '') return 1
  const composer = layer.closest<HTMLElement>(COMPOSER_SELECTOR)
  if (composer === null) return 1
  const explicitlyControlled = [...composer.querySelectorAll<HTMLElement>('[aria-controls]')]
    .some(element => element.getAttribute('aria-controls')?.split(/\s+/u).includes(layer.id))
  return explicitlyControlled ? 2 : 1
}

function waitForUiCommit(): Promise<void> {
  return new Promise(resolve => window.setTimeout(resolve, SETTLE_DELAY_MS))
}

export function ensureComposerNavigationStyles(): void {
  if (document.querySelector(STYLE_SELECTOR) !== null) return
  const style = document.createElement('style')
  style.dataset.plugin = 'agentcontroller-dsh-micro-navigation'
  style.textContent = `
[${HIGHLIGHT_ATTRIBUTE}="true"]{
  outline:2px solid #76a5ff!important;
  outline-offset:2px!important;
  box-shadow:0 0 0 4px rgba(72,132,244,.14),0 0 12px rgba(72,132,244,.22)!important;
  border-radius:9px!important;
  position:relative;
  z-index:3;
}
`
  document.head.append(style)
}

/**
 * Keeps exactly one live control selected. Once a menu/dialog opens, that
 * topmost interaction layer exclusively owns rotary navigation until it is
 * committed or backed out.
 */
export class ComposerNavigator {
  private selected: HTMLElement | undefined
  private selectedBoundary: HTMLElement | undefined
  private highlightTimer: number | undefined
  private mutationTimer: number | undefined
  private observedDepth = 0
  private rootSelection: SavedSelection | undefined
  private readonly listeners = new Set<() => void>()
  private readonly observer: MutationObserver

  constructor() {
    this.observer = new MutationObserver(() => { this.scheduleDepthCheck() })
    this.observer.observe(document.body, {
      subtree: true,
      childList: true,
      attributes: true,
      attributeFilter: ['aria-expanded', 'aria-hidden', 'hidden', 'style'],
    })
    this.observedDepth = this.computeDepth()
  }

  get navigationDepth(): number {
    const depth = this.computeDepth()
    this.observedDepth = depth
    return depth
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => { this.listeners.delete(listener) }
  }

  step(delta: -1 | 1): string {
    const composer = activeComposer()
    if (composer === undefined) throw new Error('No visible DeepSeek Harness composer is available.')
    const boundary = topInteractionLayer(composer) ?? composer
    const controls = candidates(boundary)
    if (controls.length === 0) throw new Error('The current interaction layer has no available controls.')
    const current = this.selected !== undefined && this.selectedBoundary === boundary
      ? controls.indexOf(this.selected)
      : -1
    const origin = current < 0 ? (delta > 0 ? -1 : 0) : current
    const next = (origin + delta + controls.length) % controls.length
    this.select(controls[next] as HTMLElement, boundary)
    return composerControlLabel(controls[next] as HTMLElement)
  }

  activate(): string {
    const composer = activeComposer()
    if (composer === undefined) throw new Error('No visible DeepSeek Harness composer is available.')
    const layer = topInteractionLayer(composer)
    const boundary = layer ?? composer
    const controls = candidates(boundary)
    const target = this.selected !== undefined
      && this.selectedBoundary === boundary
      && controls.includes(this.selected)
      ? this.selected
      : controls[0]
    if (target === undefined) throw new Error('The current interaction layer has no available controls.')
    this.select(target, boundary)
    const label = composerControlLabel(target)

    if (layer !== undefined && paneDepth(layer) === 1) {
      this.rootSelection = {
        index: Math.max(0, controls.indexOf(target)),
        label,
      }
    }

    if (target instanceof HTMLTextAreaElement
      || (target instanceof HTMLInputElement
        && !['button', 'checkbox', 'radio', 'submit'].includes(target.type))) {
      target.focus({ preventScroll: true })
      const length = target.value.length
      target.setSelectionRange?.(length, length)
      this.hideHighlight()
      return label
    }
    if (target instanceof HTMLSelectElement) {
      target.focus({ preventScroll: true })
      const picker = (target as HTMLSelectElement & { showPicker?: () => void }).showPicker
      if (picker !== undefined) picker.call(target)
      else target.click()
      this.hideHighlight()
      return label
    }
    target.click()
    this.hideHighlight()
    return label
  }

  async settle(): Promise<void> {
    await waitForUiCommit()
    this.checkDepth()
  }

  async back(): Promise<string> {
    const composer = activeComposer()
    if (composer === undefined) throw new Error('No visible DeepSeek Harness composer is available.')
    const layer = topInteractionLayer(composer)
    if (layer === undefined) throw new Error('No open composer menu is available to go back from.')
    const depth = this.computeDepth()
    const trigger = controlledLayerTrigger(composer, layer)
    if (trigger === undefined) {
      throw new Error('The current composer menu does not expose a safe parent trigger.')
    }

    this.clearSelection()
    trigger.click()
    await waitForUiCommit()

    if (depth > 1 && topInteractionLayer(composer) === undefined) {
      // DSH's drilled model/effort pane has no public back button. Closing and
      // reopening its own semantic trigger deterministically restores the root
      // pane while staying entirely within plugin-owned DOM actions.
      trigger.click()
      await waitForUiCommit()
      const root = topInteractionLayer(composer)
      if (root !== undefined) this.restoreRootSelection(root)
    } else {
      this.rootSelection = undefined
    }

    this.checkDepth()
    return depth > 1 ? 'Previous composer menu opened.' : 'Composer menu closed.'
  }

  dispose(): void {
    this.observer.disconnect()
    if (this.mutationTimer !== undefined) window.clearTimeout(this.mutationTimer)
    this.mutationTimer = undefined
    this.listeners.clear()
    this.clearSelection()
  }

  private computeDepth(): number {
    const composer = activeComposer()
    if (composer === undefined) return 0
    const layers = activeInteractionLayers(composer)
    if (layers.length === 0) return 0
    const top = topInteractionLayer(composer)
    if (top === undefined) return 0
    const ancestorDepth = layers.filter(layer => layer === top || layer.contains(top)).length
    return Math.max(ancestorDepth, paneDepth(top))
  }

  private scheduleDepthCheck(): void {
    if (this.mutationTimer !== undefined) return
    this.mutationTimer = window.setTimeout(() => {
      this.mutationTimer = undefined
      this.checkDepth()
    }, 0)
  }

  private checkDepth(): void {
    const next = this.computeDepth()
    if (next === this.observedDepth) return
    this.observedDepth = next
    if (next === 0) {
      this.rootSelection = undefined
      this.clearSelection()
    }
    for (const listener of this.listeners) listener()
  }

  private restoreRootSelection(root: HTMLElement): void {
    const controls = candidates(root)
    if (controls.length === 0) return
    const saved = this.rootSelection
    const target = saved === undefined
      ? controls[0]
      : controls.find(control => composerControlLabel(control) === saved.label)
        ?? controls[Math.min(saved.index, controls.length - 1)]
    if (target !== undefined) this.select(target, root)
  }

  private select(target: HTMLElement, boundary: HTMLElement): void {
    if (this.selected !== target || this.selectedBoundary !== boundary) this.clearSelection()
    this.selected = target
    this.selectedBoundary = boundary
    target.setAttribute(HIGHLIGHT_ATTRIBUTE, 'true')
    target.scrollIntoView?.({ block: 'nearest', inline: 'nearest' })
    if (this.highlightTimer !== undefined) window.clearTimeout(this.highlightTimer)
    this.highlightTimer = window.setTimeout(() => {
      this.highlightTimer = undefined
      this.hideHighlight()
    }, 1_800)
  }

  private hideHighlight(): void {
    this.selected?.removeAttribute(HIGHLIGHT_ATTRIBUTE)
  }

  private clearSelection(): void {
    if (this.highlightTimer !== undefined) window.clearTimeout(this.highlightTimer)
    this.highlightTimer = undefined
    this.hideHighlight()
    this.selected = undefined
    this.selectedBoundary = undefined
  }
}
