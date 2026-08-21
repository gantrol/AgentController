import { describe, expect, it } from 'vitest'
// @ts-expect-error The installer helper is shipped as executable ESM rather than compiled TypeScript.
import * as managedSettings from '../scripts/configure-managed-settings.mjs'

const { ensureVisionModel, VISION_MODEL_ID } = managedSettings

describe('managed DeepSeek settings', () => {
  it('creates the complete default catalog with native image input', () => {
    const settings: Record<string, unknown> = {}

    expect(ensureVisionModel(settings)).toBe('added')
    expect(settings).toMatchObject({
      'llm-deepseek': {
        models: [
          { id: 'deepseek-v4-flash', inputModalities: ['text'] },
          { id: 'deepseek-v4-pro', inputModalities: ['text'] },
          {
            id: VISION_MODEL_ID,
            inputModalities: ['text', 'image'],
          },
        ],
      },
    })
  })

  it('appends the vision model without replacing custom models or other settings', () => {
    const settings = {
      locale: { preference: 'zh' },
      'llm-deepseek': {
        baseURL: 'https://example.invalid/v1',
        models: [{ id: 'custom-model', inputModalities: ['text'] }],
      },
    }

    expect(ensureVisionModel(settings)).toBe('added')
    expect(settings.locale.preference).toBe('zh')
    expect(settings['llm-deepseek'].baseURL).toBe('https://example.invalid/v1')
    expect(settings['llm-deepseek'].models).toEqual([
      { id: 'custom-model', inputModalities: ['text'] },
      {
        id: VISION_MODEL_ID,
        name: 'DeepSeek-V4-Flash-Vision-Exp',
        contextWindow: 1_000_000,
        inputModalities: ['text', 'image'],
      },
    ])
  })

  it('repairs only the managed vision model and is idempotent', () => {
    const model = {
      id: VISION_MODEL_ID,
      inputModalities: ['image', 'image', 'unsupported'],
    }
    const settings = { 'llm-deepseek': { models: [model] } }

    expect(ensureVisionModel(settings)).toBe('updated')
    expect(model).toEqual({
      id: VISION_MODEL_ID,
      name: 'DeepSeek-V4-Flash-Vision-Exp',
      contextWindow: 1_000_000,
      inputModalities: ['text', 'image'],
    })
    expect(ensureVisionModel(settings)).toBe('current')
  })

  it('refuses ambiguous or malformed model catalogs', () => {
    expect(() =>
      ensureVisionModel({ 'llm-deepseek': { models: 'not-a-sequence' } }),
    ).toThrow('must be a sequence')
    expect(() =>
      ensureVisionModel({
        'llm-deepseek': {
          models: [{ id: VISION_MODEL_ID }, { id: VISION_MODEL_ID }],
        },
      }),
    ).toThrow('Duplicate')
  })
})
