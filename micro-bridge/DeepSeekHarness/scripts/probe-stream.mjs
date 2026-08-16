import WebSocket from 'ws'

const arguments_ = process.argv.slice(2).filter(value => value !== '--')
const url = arguments_[0] ?? 'ws://127.0.0.1:8765/v1/stream'
const timeoutMilliseconds = Number(arguments_[1] ?? 120_000)
const frames = []

await new Promise((resolve, reject) => {
  const socket = new WebSocket(url, { maxPayload: 64 * 1024, perMessageDeflate: false })
  const timer = setTimeout(() => {
    socket.terminate()
    reject(new Error('Streaming probe timed out.'))
  }, timeoutMilliseconds)

  const finish = error => {
    clearTimeout(timer)
    if (error === undefined) resolve()
    else reject(error)
  }

  socket.once('open', () => {
    socket.send(JSON.stringify({
      type: 'start',
      protocol: 'dsh-stream-v1',
      encoding: 'pcm_s16le',
      sampleRate: 16_000,
      channels: 1,
      language: 'zh-CN',
      model: 'Qwen/Qwen3-ASR-0.6B',
    }))
  })
  socket.on('message', raw => {
    const frame = JSON.parse(raw.toString())
    frames.push(frame)
    if (frame.type === 'ready') {
      socket.send(Buffer.alloc(16_000 * 2 * 2))
      socket.send(JSON.stringify({ type: 'stop' }))
    }
    if (frame.type === 'error') finish(new Error(String(frame.message ?? 'ASR error')))
    if (frame.type === 'done') {
      const hallucinated = frames.some(candidate =>
        (candidate.type === 'partial' || candidate.type === 'final')
          && typeof candidate.text === 'string'
          && candidate.text.trim() !== '')
      if (hallucinated) {
        socket.close(1008, 'silence hallucination')
        finish(new Error('Silence produced a non-empty transcript.'))
        return
      }
      console.log(JSON.stringify({ success: true, frames }, null, 2))
      socket.close(1000, 'probe complete')
      finish()
    }
  })
  socket.once('error', finish)
  socket.once('close', (code, reason) => {
    if (!frames.some(frame => frame.type === 'done')) {
      finish(new Error(`Streaming probe closed early (${String(code)}: ${reason.toString()}).`))
    }
  })
})
