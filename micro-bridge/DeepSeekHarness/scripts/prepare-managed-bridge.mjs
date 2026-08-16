import { readFile, writeFile } from 'node:fs/promises'
import { resolve } from 'node:path'

const root = resolve(process.argv[2] ?? '')
if (root.length === 0) {
  throw new Error('prepare-managed-bridge: package root is required')
}

const manifestPath = resolve(root, 'package.json')
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'))
if (manifest.name !== '@agentcontroller/dsh-micro-bridge-deepseek-harness') {
  throw new Error(`prepare-managed-bridge: unexpected package ${String(manifest.name)}`)
}

const releaseVersion = (process.argv[3] ?? '').trim()
if (releaseVersion !== '') {
  if (!/^[0-9A-Za-z][0-9A-Za-z.+-]*$/u.test(releaseVersion)) {
    throw new Error(`prepare-managed-bridge: invalid version ${releaseVersion}`)
  }
  manifest.version = releaseVersion
}

// The release bundle already contains lib/index.js and lib/client.js. A local
// file dependency would otherwise run prepare again inside the end user's WSL
// profile and require the bridge's development toolchain.
delete manifest.scripts
delete manifest.devDependencies
await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, 'utf8')
