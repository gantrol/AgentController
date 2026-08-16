import { readFile, readdir, writeFile } from 'node:fs/promises'
import { join } from 'node:path'

const [dshHome] = process.argv.slice(2)
if (dshHome === undefined || dshHome.trim() === '') {
  throw new Error('usage: migrate-dsh-home-wsl.mjs <wsl-dsh-home>')
}

function windowsPathToWsl(value) {
  const linkPrefix = value.startsWith('link:') ? 'link:' : ''
  const candidate = linkPrefix === '' ? value : value.slice(linkPrefix.length)
  const match = /^([A-Za-z]):[\\/]+(.*)$/u.exec(candidate)
  if (match === null) return value
  const drive = match[1].toLowerCase()
  const tail = match[2].replaceAll('\\', '/').replaceAll(/\/{2,}/gu, '/')
  return `${linkPrefix}/mnt/${drive}/${tail}`
}

function migrate(value) {
  if (typeof value === 'string') return windowsPathToWsl(value)
  if (Array.isArray(value)) return value.map(migrate)
  if (value !== null && typeof value === 'object') {
    return Object.fromEntries(
      Object.entries(value).map(([key, child]) => [key, migrate(child)]),
    )
  }
  return value
}

const candidates = [
  join(dshHome, 'profiles', 'web', 'package.json'),
]
const storageDirectory = join(dshHome, 'storages')
try {
  for (const entry of await readdir(storageDirectory, { withFileTypes: true })) {
    if (entry.isFile() && entry.name.endsWith('.json')) {
      candidates.push(join(storageDirectory, entry.name))
    }
  }
} catch (error) {
  if (error?.code !== 'ENOENT') throw error
}

for (const path of candidates) {
  let source
  try {
    source = await readFile(path, 'utf8')
  } catch (error) {
    if (error?.code === 'ENOENT') continue
    throw error
  }
  const original = JSON.parse(source)
  const migrated = migrate(original)
  const output = `${JSON.stringify(migrated, null, 2)}\n`
  if (output !== source) {
    await writeFile(path, output, { encoding: 'utf8', mode: 0o600 })
    process.stdout.write(`migrated ${path}\n`)
  }
}
