/** Shell-free native process helper kept inside the external bundle. */

import { spawn } from 'node:child_process'

export type NativeCommandRunner = (
  executable: string,
  args: readonly string[],
  signal: AbortSignal,
) => Promise<void>

export const runNativeCommand: NativeCommandRunner = async (executable, args, signal) => {
  if (signal.aborted) throw new Error(`${executable} launch was cancelled`)
  await new Promise<void>((resolve, reject) => {
    const child = spawn(executable, [...args], {
      shell: false,
      windowsHide: true,
      stdio: 'ignore',
    })
    let settled = false
    const finish = (error?: Error): void => {
      if (settled) return
      settled = true
      signal.removeEventListener('abort', onAbort)
      child.removeAllListeners()
      if (error === undefined) resolve()
      else reject(error)
    }
    const onAbort = (): void => {
      child.kill()
      finish(new Error(`${executable} launch was cancelled`))
    }
    signal.addEventListener('abort', onAbort, { once: true })
    child.once('error', finish)
    child.once('exit', (code, exitSignal) => {
      if (code === 0) finish()
      else finish(new Error(
        `${executable} exited before completing (${code === null ? exitSignal ?? 'unknown' : String(code)})`,
      ))
    })
  })
}
