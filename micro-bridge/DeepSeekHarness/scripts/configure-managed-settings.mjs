import { constants } from 'node:fs'
import {
  access,
  chmod,
  lstat,
  mkdir,
  readFile,
  rename,
  rm,
  writeFile,
} from 'node:fs/promises'
import { createRequire } from 'node:module'
import { dirname, isAbsolute, resolve } from 'node:path'
import { pathToFileURL } from 'node:url'

export const VISION_MODEL_ID = 'deepseek-v4-flash-vision-exp'

const DEFAULT_CONTEXT_WINDOW = 1_000_000
const DEFAULT_MODELS = [
  {
    id: 'deepseek-v4-flash',
    name: 'DeepSeek-V4-Flash',
    contextWindow: DEFAULT_CONTEXT_WINDOW,
    inputModalities: ['text'],
  },
  {
    id: 'deepseek-v4-pro',
    name: 'DeepSeek-V4-Pro',
    contextWindow: DEFAULT_CONTEXT_WINDOW,
    inputModalities: ['text'],
  },
  {
    id: VISION_MODEL_ID,
    name: 'DeepSeek-V4-Flash-Vision-Exp',
    contextWindow: DEFAULT_CONTEXT_WINDOW,
    inputModalities: ['text', 'image'],
  },
]

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

export function ensureVisionModel(settings) {
  if (!isRecord(settings)) {
    throw new TypeError('DeepSeek settings must be a YAML mapping.')
  }

  let namespace = settings['llm-deepseek']
  if (namespace === undefined) {
    namespace = {}
    settings['llm-deepseek'] = namespace
  }
  if (!isRecord(namespace)) {
    throw new TypeError('The llm-deepseek settings section must be a mapping.')
  }

  let models = namespace.models
  if (models === undefined) {
    namespace.models = structuredClone(DEFAULT_MODELS)
    return 'added'
  }
  if (!Array.isArray(models)) {
    throw new TypeError('The llm-deepseek.models setting must be a sequence.')
  }

  const matches = models.filter(
    (model) => isRecord(model) && model.id === VISION_MODEL_ID,
  )
  if (matches.length > 1) {
    throw new TypeError(`Duplicate ${VISION_MODEL_ID} model entries are not safe to merge.`)
  }
  if (matches.length === 0) {
    models.push(structuredClone(DEFAULT_MODELS[2]))
    return 'added'
  }

  const model = matches[0]
  let changed = false
  if (typeof model.name !== 'string' || model.name.length === 0) {
    model.name = 'DeepSeek-V4-Flash-Vision-Exp'
    changed = true
  }
  if (!Number.isSafeInteger(model.contextWindow) || model.contextWindow <= 0) {
    model.contextWindow = DEFAULT_CONTEXT_WINDOW
    changed = true
  }

  const currentModalities = Array.isArray(model.inputModalities)
    ? model.inputModalities.filter(
        (value) => value === 'text' || value === 'image',
      )
    : []
  const modalities = [...new Set(currentModalities)]
  if (!modalities.includes('text')) modalities.unshift('text')
  if (!modalities.includes('image')) modalities.push('image')
  if (
    !Array.isArray(model.inputModalities) ||
    model.inputModalities.length !== modalities.length ||
    model.inputModalities.some((value, index) => value !== modalities[index])
  ) {
    model.inputModalities = modalities
    changed = true
  }

  return changed ? 'updated' : 'current'
}

async function loadSettings(settingsPath, yaml) {
  try {
    const info = await lstat(settingsPath)
    if (info.isSymbolicLink() || !info.isFile()) {
      throw new TypeError('Refusing to update a non-regular settings file.')
    }
  } catch (error) {
    if (error?.code === 'ENOENT') return { document: new yaml.Document({}), fresh: true }
    throw error
  }

  const source = await readFile(settingsPath, 'utf8')
  const document = yaml.parseDocument(source, { prettyErrors: true })
  if (document.errors.length > 0) {
    throw new TypeError(`Could not parse DeepSeek settings: ${document.errors[0].message}`)
  }
  return { document, fresh: false }
}

export async function configureManagedSettings(settingsPath, dshManifestPath) {
  if (!isAbsolute(settingsPath) || !isAbsolute(dshManifestPath)) {
    throw new TypeError('Managed settings and DSH manifest paths must be absolute.')
  }
  await access(dshManifestPath, constants.R_OK)

  const requireFromDsh = createRequire(dshManifestPath)
  const yaml = requireFromDsh('yaml')
  const { document } = await loadSettings(settingsPath, yaml)
  const settings = document.toJS() ?? {}
  const result = ensureVisionModel(settings)

  // Replacing only the managed namespace value keeps unrelated top-level YAML
  // nodes and comments intact while making the catalog update deterministic.
  document.set('llm-deepseek', settings['llm-deepseek'])
  const output = document.toString({ lineWidth: 0 })
  const parent = dirname(settingsPath)
  const temporaryPath = resolve(
    parent,
    `.settings.yaml.installing-${process.pid}-${Date.now()}`,
  )
  await mkdir(parent, { recursive: true, mode: 0o700 })
  try {
    await writeFile(temporaryPath, output, {
      encoding: 'utf8',
      flag: 'wx',
      mode: 0o600,
    })
    await rename(temporaryPath, settingsPath)
    await chmod(settingsPath, 0o600)
  } finally {
    await rm(temporaryPath, { force: true })
  }
  return result
}

async function main() {
  const settingsPath = process.argv[2] ?? ''
  const dshManifestPath = process.argv[3] ?? ''
  const result = await configureManagedSettings(settingsPath, dshManifestPath)
  process.stdout.write(`managed-vision-model=${result}\n`)
  process.stdout.write(`managed-vision-model-id=${VISION_MODEL_ID}\n`)
}

if (import.meta.url === pathToFileURL(resolve(process.argv[1] ?? '')).href) {
  await main()
}
