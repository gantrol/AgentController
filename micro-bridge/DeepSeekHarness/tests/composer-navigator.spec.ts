// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  ComposerNavigator,
  ensureComposerNavigationStyles,
} from '../src/client/composer-navigator.ts'

afterEach(() => {
  vi.useRealTimers()
  document.head.replaceChildren()
  document.body.replaceChildren()
  vi.restoreAllMocks()
})

describe('composer-scoped rotary navigation', () => {
  it('ignores page-global and unavailable controls while highlighting dynamic plugin controls', () => {
    const outside = document.createElement('button')
    outside.textContent = 'Global action'
    document.body.append(outside)

    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const input = document.createElement('textarea')
    input.placeholder = 'Message'
    const disabled = document.createElement('button')
    disabled.disabled = true
    disabled.textContent = 'Disabled'
    const pluginButton = document.createElement('button')
    pluginButton.ariaLabel = 'Plugin tool'
    composer.append(input, disabled, pluginButton)
    document.body.append(composer)

    ensureComposerNavigationStyles()
    const navigator = new ComposerNavigator()
    expect(navigator.step(1)).toBe('Plugin tool')
    expect(input.hasAttribute('data-codex-micro-selected')).toBe(false)
    expect(pluginButton.dataset.codexMicroSelected).toBe('true')
    expect(outside.hasAttribute('data-codex-micro-selected')).toBe(false)
    expect(disabled.hasAttribute('data-codex-micro-selected')).toBe(false)

    const later = document.createElement('button')
    later.textContent = 'Added later'
    composer.append(later)
    expect(navigator.step(1)).toBe('Added later')
    expect(later.dataset.codexMicroSelected).toBe('true')
    navigator.dispose()
    expect(later.hasAttribute('data-codex-micro-selected')).toBe(false)
  })

  it('focuses text inputs and clicks the highlighted button', () => {
    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const input = document.createElement('textarea')
    input.value = 'draft'
    const action = document.createElement('button')
    action.textContent = 'Attach'
    const clicked = vi.fn()
    action.addEventListener('click', clicked)
    composer.append(input, action)
    document.body.append(composer)

    const navigator = new ComposerNavigator()
    expect(navigator.step(1)).toBe('Attach')
    expect(input.hasAttribute('data-codex-micro-selected')).toBe(false)
    expect(navigator.activate()).toBe('Attach')
    expect(clicked).toHaveBeenCalledOnce()
    expect(action.hasAttribute('data-codex-micro-selected')).toBe(false)
    navigator.dispose()
  })

  it('keeps an idle highlight until selection explicitly changes', () => {
    vi.useFakeTimers()
    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const first = document.createElement('button')
    first.textContent = 'First'
    const second = document.createElement('button')
    second.textContent = 'Second'
    composer.append(first, second)
    document.body.append(composer)

    const navigator = new ComposerNavigator()
    expect(navigator.step(1)).toBe('First')
    expect(first.dataset.codexMicroSelected).toBe('true')
    vi.advanceTimersByTime(60_000)
    expect(first.dataset.codexMicroSelected).toBe('true')
    expect(navigator.step(1)).toBe('Second')
    expect(first.hasAttribute('data-codex-micro-selected')).toBe(false)
    expect(second.dataset.codexMicroSelected).toBe('true')
    expect(navigator.activate()).toBe('Second')
    expect(second.hasAttribute('data-codex-micro-selected')).toBe(false)
    navigator.dispose()
  })

  it('selects the leading Commands plus control before later composer controls', () => {
    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const input = document.createElement('textarea')
    const commands = document.createElement('button')
    commands.ariaLabel = 'Commands'
    const permission = document.createElement('button')
    permission.ariaLabel = 'Workspace Write'
    composer.append(input, commands, permission)
    document.body.append(composer)

    const navigator = new ComposerNavigator()
    expect(navigator.step(1)).toBe('Commands')
    expect(commands.dataset.codexMicroSelected).toBe('true')
    expect(navigator.step(1)).toBe('Workspace Write')
    expect(commands.hasAttribute('data-codex-micro-selected')).toBe(false)
    expect(permission.dataset.codexMicroSelected).toBe('true')
    navigator.dispose()
  })

  it('clears stale highlights when a control becomes unavailable or a menu takes ownership', async () => {
    vi.useFakeTimers()
    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const first = document.createElement('button')
    first.textContent = 'First'
    const second = document.createElement('button')
    second.textContent = 'Second'
    composer.append(first, second)
    document.body.append(composer)

    const navigator = new ComposerNavigator()
    expect(navigator.step(1)).toBe('First')
    first.disabled = true
    await Promise.resolve()
    vi.advanceTimersByTime(0)
    expect(first.hasAttribute('data-codex-micro-selected')).toBe(false)

    expect(navigator.step(1)).toBe('Second')
    const menu = document.createElement('div')
    menu.setAttribute('role', 'menu')
    const option = document.createElement('button')
    option.setAttribute('role', 'menuitem')
    option.textContent = 'Menu option'
    menu.append(option)
    composer.append(menu)
    await Promise.resolve()
    vi.advanceTimersByTime(0)
    expect(second.hasAttribute('data-codex-micro-selected')).toBe(false)

    expect(navigator.step(1)).toBe('Menu option')
    menu.remove()
    await Promise.resolve()
    vi.advanceTimersByTime(0)
    expect(option.hasAttribute('data-codex-micro-selected')).toBe(false)
    navigator.dispose()
  })

  it('clears the old highlight when a new composer becomes active', async () => {
    vi.useFakeTimers()
    const oldComposer = document.createElement('section')
    oldComposer.dataset.composerCard = 'true'
    const oldControl = document.createElement('button')
    oldControl.textContent = 'Old session control'
    oldComposer.append(oldControl)
    document.body.append(oldComposer)

    const navigator = new ComposerNavigator()
    expect(navigator.step(1)).toBe('Old session control')

    const newComposer = document.createElement('section')
    newComposer.dataset.composerCard = 'true'
    const newControl = document.createElement('button')
    newControl.textContent = 'New session control'
    newComposer.append(newControl)
    document.body.append(newComposer)
    await Promise.resolve()
    vi.advanceTimersByTime(0)

    expect(oldControl.hasAttribute('data-codex-micro-selected')).toBe(false)
    expect(navigator.step(1)).toBe('New session control')
    expect(newControl.dataset.codexMicroSelected).toBe('true')
    navigator.dispose()
  })

  it('locks navigation to the open menu and backs out one level at a time', async () => {
    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const trigger = document.createElement('button')
    trigger.textContent = 'DeepSeek-V4-Pro High'
    trigger.setAttribute('aria-haspopup', 'menu')
    trigger.setAttribute('aria-controls', 'model-menu')
    const microphone = document.createElement('button')
    microphone.textContent = 'Microphone'
    const send = document.createElement('button')
    send.textContent = 'Send'
    composer.append(trigger, microphone, send)
    document.body.append(composer)

    const renderRoot = (): void => {
      const menu = document.createElement('div')
      menu.id = 'model-menu'
      menu.setAttribute('role', 'menu')
      const model = document.createElement('button')
      model.setAttribute('role', 'menuitem')
      model.textContent = 'Model'
      const effort = document.createElement('button')
      effort.setAttribute('role', 'menuitem')
      effort.textContent = 'Reasoning effort'
      model.addEventListener('click', () => {
        const pro = document.createElement('button')
        pro.setAttribute('role', 'menuitemradio')
        pro.textContent = 'DeepSeek-V4-Pro'
        const flash = document.createElement('button')
        flash.setAttribute('role', 'menuitemradio')
        flash.textContent = 'DeepSeek-V4-Flash'
        menu.replaceChildren(pro, flash)
      })
      menu.append(model, effort)
      composer.append(menu)
      trigger.setAttribute('aria-expanded', 'true')
    }
    trigger.addEventListener('click', () => {
      const menu = composer.querySelector('#model-menu')
      if (menu !== null) {
        menu.remove()
        trigger.setAttribute('aria-expanded', 'false')
      } else {
        renderRoot()
      }
    })

    const navigator = new ComposerNavigator()
    expect(navigator.step(1)).toBe('DeepSeek-V4-Pro High')
    navigator.activate()
    await navigator.settle()
    expect(navigator.navigationDepth).toBe(1)

    expect(navigator.step(1)).toBe('Model')
    expect(microphone.hasAttribute('data-codex-micro-selected')).toBe(false)
    navigator.activate()
    await navigator.settle()
    expect(navigator.navigationDepth).toBe(2)
    expect(navigator.step(1)).toBe('DeepSeek-V4-Pro')
    expect(send.hasAttribute('data-codex-micro-selected')).toBe(false)

    await navigator.back()
    expect(navigator.navigationDepth).toBe(1)
    expect(composer.querySelector('[data-codex-micro-selected]')?.textContent).toBe('Model')

    await navigator.back()
    expect(navigator.navigationDepth).toBe(0)
    expect(composer.querySelector('[role="menu"]')).toBeNull()
    navigator.dispose()
  })

  it('closes the command listbox through its expanded launcher without aria-controls', async () => {
    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const launcher = document.createElement('button')
    launcher.ariaLabel = 'Commands'
    launcher.setAttribute('aria-haspopup', 'listbox')
    launcher.setAttribute('aria-expanded', 'true')
    const listbox = document.createElement('div')
    listbox.setAttribute('role', 'listbox')
    const goal = document.createElement('button')
    goal.setAttribute('role', 'option')
    goal.textContent = 'goal'
    listbox.append(goal)
    composer.append(launcher, listbox)
    document.body.append(composer)

    launcher.addEventListener('click', () => {
      listbox.remove()
      launcher.setAttribute('aria-expanded', 'false')
    })

    const navigator = new ComposerNavigator()
    expect(navigator.navigationDepth).toBe(1)
    await expect(navigator.back()).resolves.toBe('Composer menu closed.')
    expect(navigator.navigationDepth).toBe(0)
    expect(composer.querySelector('[role="listbox"]')).toBeNull()
    navigator.dispose()
  })

  it('closes the permission menu through its sibling anchor without popup aria', async () => {
    const composer = document.createElement('section')
    composer.dataset.composerCard = 'true'
    const wrapper = document.createElement('span')
    const launcher = document.createElement('button')
    launcher.ariaLabel = 'Workspace Write'
    const menu = document.createElement('div')
    menu.setAttribute('role', 'menu')
    const readOnly = document.createElement('button')
    readOnly.setAttribute('role', 'menuitemradio')
    readOnly.textContent = 'Read Only'
    const workspaceWrite = document.createElement('button')
    workspaceWrite.setAttribute('role', 'menuitemradio')
    workspaceWrite.textContent = 'Workspace Write'
    menu.append(readOnly, workspaceWrite)
    wrapper.append(launcher, menu)
    composer.append(wrapper)
    document.body.append(composer)

    launcher.addEventListener('click', () => { menu.remove() })

    const navigator = new ComposerNavigator()
    expect(navigator.navigationDepth).toBe(1)
    await expect(navigator.back()).resolves.toBe('Composer menu closed.')
    expect(navigator.navigationDepth).toBe(0)
    expect(composer.querySelector('[role="menu"]')).toBeNull()
    navigator.dispose()
  })
})
