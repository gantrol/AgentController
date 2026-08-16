/** Native-slot microphone, first-use gate, and streaming voice settings. */

import { useEffect, useMemo, useState, useSyncExternalStore, type ReactNode } from 'react'
import type { QuickModelRef, VoiceProvider, VoiceSettings } from '../voice-contract.ts'
import { ModelController } from './model-controller.ts'
import { VoiceController } from './voice-controller.ts'
import { VoiceSettingsClient } from './settings-client.ts'

export const VOICE_LOCALE_NAMESPACE = 'settings.agentcontrollerMicroVoice'

export const zh = {
  nav: '语音输入', title: '语音输入', intro: '配置 DeepSeek Harness 与 Micro 共用的流式语音识别。',
  firstUse: '首次使用语音前，需要选择识别服务并完成流式握手测试。',
  provider: '识别方式', local: '本地流式 Qwen', localDesc: '音频留在本机；服务持续返回 partial / final。',
  system: '系统 / 浏览器', systemDesc: '兼容模式；效果和可用性取决于浏览器与系统。',
  remote: '远程流式 API', remoteDesc: '通过固定 WebSocket 协议实时发送 PCM。',
  language: '语言（可选）', autoSubmit: '识别结束后自动发送', autoSubmitDesc: '关闭时只写入输入框，便于检查。',
  streamUrl: '本地流式地址', healthUrl: '健康检查地址', localModel: '模型',
  localKeyRef: '本地 API Key 引用', startMode: '启动策略', onDemand: '首次按麦克风时启动',
  withHarness: '随 Harness 启动', manual: '由用户手动启动', runner: '脚本类型',
  powershell: 'PowerShell 脚本', executable: '可执行程序', scriptPath: '启动脚本 / 程序',
  scriptArgs: '参数（每行一个）', workingDirectory: '工作目录', startupTimeout: '模型启动超时（秒）',
  runtime: '本地 ASR 状态', start: '启动', stop: '停止', starting: '正在启动并加载模型…',
  remoteUrl: '远程 WebSocket 地址', remoteModel: '远程模型（可选）', remoteKeyRef: '远程 API Key 引用',
  bundledLauncher: '内置 Qwen 启动器', bundledLauncherDesc: '插件自带 WSL + vLLM 流式适配器；需要先在 WSL 安装 qwen-asr[vllm]、aiohttp 与 numpy。', useBundledLauncher: '采用',
  key: 'API Key', keyPlaceholder: '留空则保留现有密钥', configured: '已配置', notConfigured: '未配置',
  from: '来源', clear: '清除', configure: '配置', back: '返回语音设置', providerDetail: '识别服务、启动方式与流式协议',
  saveTest: '保存并验证', testing: '正在验证流式服务…', verified: '流式服务验证通过，语音输入已启用。', retry: '重试', close: '关闭',
  streamNote: '协议 dsh-stream-v1：start JSON → 16 kHz 单声道 PCM16 → stop；服务必须返回 ready / partial / final / done / error。只有收到 ready 后才会开启麦克风。',
  systemNote: 'Windows/Linux 的系统识别质量通常更依赖浏览器。验证时会请求一次麦克风权限；插件不会静默改用远程服务。',
  remoteNote: '公网地址必须使用 wss://；API Key 只由插件 Host 作为 Bearer header 发送。',
  privacy: '隐私边界', privacyLocal: '本地流式：音频只发送到 loopback 地址。', privacySystem: '系统识别：处理位置由浏览器/操作系统决定。', privacyRemote: '远程 API：音频会发送到配置的服务。',
  quickModelA: '快捷模型 A', quickModelB: '快捷模型 B', quickModelsDesc: 'Micro 点按旋钮或左下额度键时，只在这两个模型间切换。', quickAuto: '自动选择', quickUnavailable: '请先打开一个可用会话',
  micStart: '开始语音输入', micStop: '停止语音输入', micRequesting: '正在启动识别服务', micListening: '正在聆听', micProcessing: '正在完成转写',
} as const

export const en: Record<keyof typeof zh, string> = {
  nav: 'Voice input', title: 'Voice input', intro: 'Configure streaming speech recognition shared by DeepSeek Harness and Micro.',
  firstUse: 'Choose a provider and pass its streaming handshake before first use.',
  provider: 'Recognition provider', local: 'Local streaming Qwen', localDesc: 'Audio stays local; the service continuously returns partial / final frames.',
  system: 'System / browser', systemDesc: 'Compatibility mode; quality and availability depend on the browser and OS.',
  remote: 'Remote streaming API', remoteDesc: 'Streams PCM over the fixed WebSocket protocol.',
  language: 'Language (optional)', autoSubmit: 'Submit when recognition ends', autoSubmitDesc: 'When off, recognized text stays in the composer for review.',
  streamUrl: 'Local streaming URL', healthUrl: 'Health-check URL', localModel: 'Model',
  localKeyRef: 'Local API key reference', startMode: 'Startup policy', onDemand: 'Start on first microphone press',
  withHarness: 'Start with Harness', manual: 'Start manually', runner: 'Script type', powershell: 'PowerShell script',
  executable: 'Executable', scriptPath: 'Startup script / executable', scriptArgs: 'Arguments (one per line)',
  workingDirectory: 'Working directory', startupTimeout: 'Model startup timeout (seconds)', runtime: 'Local ASR status',
  start: 'Start', stop: 'Stop', starting: 'Starting and loading the model…',
  remoteUrl: 'Remote WebSocket URL', remoteModel: 'Remote model (optional)', remoteKeyRef: 'Remote API key reference',
  bundledLauncher: 'Bundled Qwen launcher', bundledLauncherDesc: 'Uses the bundled WSL + vLLM streaming adapter. Install qwen-asr[vllm], aiohttp, and numpy in WSL first.', useBundledLauncher: 'Use',
  key: 'API key', keyPlaceholder: 'Leave blank to keep the existing secret', configured: 'Configured', notConfigured: 'Not configured',
  from: 'source', clear: 'Clear', configure: 'Configure', back: 'Back to voice settings', providerDetail: 'Recognition service, startup, and streaming protocol',
  saveTest: 'Save and verify', testing: 'Verifying the streaming service…', verified: 'Streaming service verified; voice input is enabled.', retry: 'Retry', close: 'Close',
  streamNote: 'dsh-stream-v1 sends start JSON, mono 16 kHz PCM16 frames, then stop. The service must return ready / partial / final / done / error. Microphone capture begins only after ready.',
  systemNote: 'System recognition on Windows/Linux depends heavily on the browser. Verification requests microphone permission once; the plugin never silently switches to a remote service.',
  remoteNote: 'Public endpoints must use wss://. The Host sends API keys as Bearer headers without exposing them to browser JavaScript.',
  privacy: 'Privacy boundary', privacyLocal: 'Local streaming: audio is sent only to a loopback endpoint.', privacySystem: 'System recognition: processing location is controlled by the browser/OS.', privacyRemote: 'Remote API: audio is sent to the configured service.',
  quickModelA: 'Quick model A', quickModelB: 'Quick model B', quickModelsDesc: 'A Micro knob press or lower-left quota-button click toggles only between these two models.', quickAuto: 'Choose automatically', quickUnavailable: 'Open an available session first',
  micStart: 'Start voice input', micStop: 'Stop voice input', micRequesting: 'Starting recognition service', micListening: 'Listening', micProcessing: 'Finishing transcription',
}

export type VoiceLocaleKey = keyof typeof zh
export type VoiceTranslate = (key: VoiceLocaleKey) => string

const STYLE_ID = 'agentcontroller-dsh-micro-voice'

export function ensureVoiceStyles(): void {
  if (document.querySelector(`style[data-plugin="${STYLE_ID}"]`) !== null) return
  const style = document.createElement('style')
  style.dataset.plugin = STYLE_ID
  style.textContent = `
.acmv-mic{width:32px;height:32px;border:0;border-radius:9px;background:transparent;color:var(--dsw-alias-content-secondary,#666);display:grid;place-items:center;cursor:pointer;position:relative;transition:background .15s,color .15s,transform .15s}.acmv-mic:hover{background:var(--dsw-alias-background-hover,rgba(0,0,0,.06));color:var(--dsw-alias-content-primary,#222)}.acmv-mic:active{transform:scale(.94)}.acmv-mic:disabled{cursor:wait;opacity:.68}.acmv-mic[data-active=true]{background:rgba(69,120,255,.12);color:#3979ee}.acmv-mic[data-error=true]{color:#c4514c}.acmv-mic-dot{position:absolute;right:4px;top:4px;width:6px;height:6px;border-radius:50%;background:#4a82ef;box-shadow:0 0 0 3px rgba(74,130,239,.12)}.acmv-mic[data-phase=requesting] .acmv-mic-dot,.acmv-mic[data-phase=processing] .acmv-mic-dot{animation:acmv-pulse 1s infinite alternate}@keyframes acmv-pulse{from{opacity:.35;transform:scale(.8)}to{opacity:1;transform:scale(1.1)}}
.acmv-overlay{position:fixed;inset:0;z-index:2147483000;background:rgba(20,24,32,.34);backdrop-filter:blur(3px);display:grid;place-items:center;padding:24px}.acmv-dialog{width:min(920px,calc(100vw - 48px));height:min(760px,calc(100vh - 48px));background:var(--dsw-alias-background-primary,#fff);border:1px solid var(--dsw-alias-border-subtle,#ddd);border-radius:20px;box-shadow:0 24px 80px rgba(0,0,0,.22);overflow:hidden;position:relative}.acmv-dialog-close{position:absolute;right:18px;top:16px;z-index:2;width:34px;height:34px;border:0;border-radius:10px;background:transparent;color:inherit;font-size:22px;cursor:pointer}.acmv-dialog-close:hover{background:rgba(0,0,0,.06)}
.acmv-page{height:100%;overflow:auto;padding:26px 30px 46px;color:var(--dsw-alias-content-primary,#252525);box-sizing:border-box;font-family:Inter,"Segoe UI","Microsoft YaHei",system-ui,sans-serif}.acmv-head h2{font-size:22px;font-weight:600;margin:0 0 6px}.acmv-head p{font-size:13px;color:var(--dsw-alias-content-secondary,#737373);margin:0 0 20px;line-height:1.55}.acmv-setup-banner{padding:11px 13px;margin:0 0 16px;border:1px solid rgba(81,129,232,.25);border-radius:11px;background:rgba(81,129,232,.08);font-size:12px;color:#3a5f99}.acmv-group{border:1px solid var(--dsw-alias-border-subtle,#e4e4e4);border-radius:14px;background:var(--dsw-alias-background-primary,#fff);padding:18px;margin:0 0 16px}.acmv-section-title{font-size:13px;font-weight:600;margin:26px 0 10px}.acmv-options{border:1px solid var(--dsw-alias-border-subtle,#e4e4e4);border-radius:14px;background:var(--dsw-alias-background-primary,#fff);overflow:hidden;margin:0 0 16px}.acmv-row{min-height:72px;display:flex;align-items:center;justify-content:space-between;gap:22px;padding:0 18px;border-bottom:1px solid var(--dsw-alias-border-subtle,#ececec)}.acmv-row:last-child{border-bottom:0}.acmv-row-copy strong{display:block;font-size:13px}.acmv-row-copy small{display:block;font-size:11px;color:var(--dsw-alias-content-secondary,#777);margin-top:4px;line-height:1.45}.acmv-row-action{display:flex;align-items:center;gap:8px;min-width:180px;justify-content:flex-end}.acmv-back{border:0;background:transparent;color:var(--dsw-alias-content-secondary,#666);padding:0;margin:0 0 16px;cursor:pointer;font:inherit;font-size:12px}.acmv-back:hover{color:inherit}.acmv-label{display:block;font-size:13px;font-weight:600;margin:0 0 10px}.acmv-providers{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}.acmv-provider{min-height:88px;border:1px solid var(--dsw-alias-border-subtle,#dedede);border-radius:12px;background:transparent;color:inherit;padding:13px;text-align:left;cursor:pointer;font:inherit}.acmv-provider:hover{background:var(--dsw-alias-background-hover,#f7f7f7)}.acmv-provider[data-selected=true]{border-color:#78a7ff;background:rgba(82,137,239,.09);box-shadow:0 0 0 1px rgba(82,137,239,.18)}.acmv-provider strong{display:block;font-size:13px;margin-bottom:5px}.acmv-provider span{display:block;font-size:11px;color:var(--dsw-alias-content-secondary,#777);line-height:1.45}
.acmv-grid{display:grid;grid-template-columns:1fr 1fr;gap:14px}.acmv-field{display:grid;gap:6px;min-width:0}.acmv-field>span{font-size:12px;color:var(--dsw-alias-content-secondary,#666)}.acmv-input,.acmv-select,.acmv-textarea{width:100%;box-sizing:border-box;border:1px solid var(--dsw-alias-border-subtle,#dcdcdc);border-radius:9px;background:var(--dsw-alias-background-primary,#fff);color:inherit;padding:0 10px;font:inherit;font-size:12px;outline:none}.acmv-input,.acmv-select{height:38px}.acmv-textarea{min-height:76px;padding:9px 10px;resize:vertical}.acmv-input:focus,.acmv-select:focus,.acmv-textarea:focus{border-color:#76a4f5;box-shadow:0 0 0 2px rgba(70,130,240,.12)}.acmv-keyline{display:flex;gap:7px;align-items:center}.acmv-keyline .acmv-input{flex:1}.acmv-status{font-size:10px;padding:3px 6px;border-radius:6px;background:var(--dsw-alias-background-secondary,#f1f1f1);white-space:nowrap}.acmv-status[data-ok=true]{color:#337455;background:rgba(53,143,94,.1)}.acmv-runtime{display:flex;align-items:center;gap:10px;padding:11px 12px;margin-top:14px;border-radius:10px;background:var(--dsw-alias-background-secondary,#f6f6f6)}.acmv-runtime-dot{width:8px;height:8px;border-radius:50%;background:#aaa}.acmv-runtime-dot[data-phase=ready]{background:#51a877}.acmv-runtime-dot[data-phase=starting]{background:#e0a93d;animation:acmv-pulse .8s infinite alternate}.acmv-runtime-dot[data-phase=error],.acmv-runtime-dot[data-phase=not-configured]{background:#c95656}.acmv-runtime span{font-size:11px;flex:1}.acmv-log{max-height:90px;overflow:auto;white-space:pre-wrap;font:10px/1.45 Consolas,monospace;color:var(--dsw-alias-content-secondary,#777);margin:10px 0 0}.acmv-note{font-size:11px;line-height:1.55;color:var(--dsw-alias-content-secondary,#747474);margin:13px 0 0;padding:10px 12px;border-radius:9px;background:var(--dsw-alias-background-secondary,#f7f7f7)}.acmv-actions{display:flex;align-items:center;justify-content:flex-end;gap:10px;margin-top:20px}.acmv-button{height:36px;border:1px solid var(--dsw-alias-border-subtle,#d8d8d8);border-radius:9px;background:var(--dsw-alias-background-primary,#fff);color:inherit;padding:0 14px;font:inherit;font-size:12px;cursor:pointer}.acmv-button[data-primary=true]{background:#202124;color:#fff;border-color:#202124}.acmv-button:disabled{opacity:.55;cursor:wait}.acmv-feedback{font-size:11px;color:var(--dsw-alias-content-secondary,#777)}.acmv-feedback[data-error=true]{color:#bd4b48}.acmv-privacy strong{font-size:12px}.acmv-privacy p{font-size:11px;color:var(--dsw-alias-content-secondary,#777);margin:6px 0 0}@media(max-width:760px){.acmv-page{padding:20px 16px 38px}.acmv-providers,.acmv-grid{grid-template-columns:1fr}.acmv-dialog{width:calc(100vw - 20px);height:calc(100vh - 20px)}.acmv-overlay{padding:10px}}
`
  document.head.append(style)
}

function MicIcon(): ReactNode {
  return <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><rect x="9" y="3" width="6" height="11" rx="3"/><path d="M5.5 11.5a6.5 6.5 0 0 0 13 0M12 18v3M9 21h6"/></svg>
}

export interface VoiceButtonProps {
  session: { sessionId: string }
  voice: VoiceController
  settingsClient: VoiceSettingsClient
  modelController: ModelController
  t: VoiceTranslate
}

export function VoiceButton({ session, voice, settingsClient, modelController, t }: VoiceButtonProps): ReactNode {
  const state = useSyncExternalStore(voice.subscribe, voice.getSnapshot, voice.getSnapshot)
  const settings = useSyncExternalStore(settingsClient.subscribe, settingsClient.getSnapshot, settingsClient.getSnapshot)
  const addressed = state.sessionId === session.sessionId
  const active = addressed && state.phase !== 'idle' && state.phase !== 'error' && state.phase !== 'setup-required' && state.phase !== 'configuring'
  const busy = addressed && state.phase === 'processing'
  const setupRequired = addressed && state.phase === 'setup-required'
  const configurationOpen = addressed && (setupRequired || state.phase === 'configuring')
  const label = !active ? t('micStart')
    : state.phase === 'requesting' ? t('micRequesting')
      : state.phase === 'listening' ? t('micListening') : t('micProcessing')
  useEffect(() => {
    if (setupRequired && settings.document?.settings.setupCompleted === true) voice.dismissConfiguration()
  }, [settings.document?.revision, setupRequired, voice])
  return <>
    <button type="button" className="acmv-mic" data-active={active} data-error={addressed && state.phase === 'error'} data-phase={addressed ? state.phase : 'idle'} disabled={busy} aria-label={active ? t('micStop') : t('micStart')} title={state.partial !== '' ? state.partial : addressed && state.error !== undefined ? state.error : label} onClick={() => { if (active) void voice.stop(); else void voice.start(session.sessionId).catch(() => {}) }}>
      <MicIcon/>{active ? <span className="acmv-mic-dot"/> : null}
    </button>
    {configurationOpen ? <div className="acmv-overlay" role="presentation"><div className="acmv-dialog" role="dialog" aria-modal="true" aria-label={t('title')}><button type="button" className="acmv-dialog-close" aria-label={t('close')} onClick={() => { voice.dismissConfiguration() }}>×</button><VoiceSettingsSection settingsClient={settingsClient} modelController={modelController} t={t} setupMode={setupRequired}/></div></div> : null}
  </>
}

interface SettingsSectionProps {
  settingsClient: VoiceSettingsClient
  modelController: ModelController
  t: VoiceTranslate
  setupMode?: boolean
}

function ProviderCard({ id, selected, title, description, onSelect }: { id: VoiceProvider; selected: boolean; title: string; description: string; onSelect: (id: VoiceProvider) => void }): ReactNode {
  return <button type="button" className="acmv-provider" data-selected={selected} onClick={() => { onSelect(id) }}><strong>{title}</strong><span>{description}</span></button>
}

function CredentialField({ label, reference, draft, setDraft, configured, source, clear, t }: { label: string; reference: string; draft: string; setDraft: (value: string) => void; configured: boolean; source?: string; clear: () => void; t: VoiceTranslate }): ReactNode {
  return <label className="acmv-field"><span>{label}</span><div className="acmv-keyline"><input className="acmv-input" type="password" autoComplete="off" value={draft} placeholder={t('keyPlaceholder')} onChange={event => { setDraft(event.target.value) }}/><span className="acmv-status" data-ok={configured}>{configured ? `${t('configured')}${source === undefined ? '' : ` · ${t('from')} ${source}`}` : t('notConfigured')}</span>{configured ? <button type="button" className="acmv-button" onClick={clear}>{t('clear')}</button> : null}</div><span style={{ fontSize: 10, opacity: .7 }}>{reference}</span></label>
}

function modelKey(value: QuickModelRef | undefined): string {
  return value === undefined ? '' : JSON.stringify([value.provider, value.model])
}

async function verifySystemProvider(): Promise<void> {
  const speech = window as Window & { SpeechRecognition?: unknown; webkitSpeechRecognition?: unknown }
  if (speech.SpeechRecognition === undefined && speech.webkitSpeechRecognition === undefined) {
    throw new Error('System speech recognition is unavailable in this browser.')
  }
  if (navigator.mediaDevices?.getUserMedia === undefined) throw new Error('Microphone capture is unavailable in this browser.')
  const stream = await navigator.mediaDevices.getUserMedia({ audio: true })
  for (const track of stream.getTracks()) track.stop()
}

export function VoiceSettingsSection({ settingsClient, modelController, t, setupMode = false }: SettingsSectionProps): ReactNode {
  const state = useSyncExternalStore(settingsClient.subscribe, settingsClient.getSnapshot, settingsClient.getSnapshot)
  const models = useSyncExternalStore(modelController.subscribe, modelController.getSnapshot, modelController.getSnapshot)
  const [draft, setDraft] = useState<VoiceSettings>()
  const [localSecret, setLocalSecret] = useState('')
  const [remoteSecret, setRemoteSecret] = useState('')
  const [feedback, setFeedback] = useState('')
  const [failure, setFailure] = useState('')
  const [page, setPage] = useState<'overview' | 'provider'>(setupMode ? 'provider' : 'overview')

  useEffect(() => { if (state.status === 'idle') void settingsClient.load() }, [settingsClient, state.status])
  useEffect(() => { if (models.status === 'idle') void modelController.refresh().catch(() => {}) }, [modelController, models.status])
  useEffect(() => { if (state.document !== undefined) setDraft(structuredClone(state.document.settings)) }, [state.document?.revision])

  const providerCopy = useMemo(() => ({
    'local-qwen': [t('local'), t('localDesc')] as const,
    system: [t('system'), t('systemDesc')] as const,
    'remote-websocket': [t('remote'), t('remoteDesc')] as const,
  }), [t])
  if (draft === undefined) return <div className="acmv-page"><div className="acmv-head"><h2>{t('title')}</h2><p>{state.error ?? t('intro')}</p>{state.status === 'error' ? <button type="button" className="acmv-button" onClick={() => { void settingsClient.load() }}>{t('retry')}</button> : null}</div></div>

  const credential = (ref: string) => state.credentials[ref] ?? { configured: false, writable: true }
  const update = <K extends keyof VoiceSettings>(key: K, value: VoiceSettings[K]): void => { setDraft(current => current === undefined ? current : { ...current, [key]: value, setupCompleted: false }) }
  const chooseModel = (key: 'quickModelA' | 'quickModelB', value: string): void => { update(key, models.choices.find(choice => modelKey(choice.ref) === value)?.ref) }
  const useBundledLauncher = (): void => {
    const recommendation = state.recommendations
    if (recommendation === undefined) return
    setDraft(current => current === undefined ? current : {
      ...current,
      localStartMode: 'on-demand',
      localRunner: 'powershell',
      localScriptPath: recommendation.localLauncherPath,
      localWorkingDirectory: recommendation.localWorkingDirectory,
      localStreamUrl: recommendation.localStreamUrl,
      localHealthUrl: recommendation.localHealthUrl,
      setupCompleted: false,
    })
  }
  const secrets = (): Readonly<Record<string, string>> => ({
    ...(draft.localCredentialRef === '' ? {} : { [draft.localCredentialRef]: localSecret }),
    ...(draft.remoteCredentialRef === '' ? {} : { [draft.remoteCredentialRef]: remoteSecret }),
  })
  const verify = async (): Promise<void> => {
    setFeedback(''); setFailure('')
    try {
      if (draft.provider === 'system') await verifySystemProvider()
      await settingsClient.configureAndTest(draft, secrets())
      setLocalSecret(''); setRemoteSecret(''); setFeedback(t('verified'))
    } catch (error) { setFailure(error instanceof Error ? error.message : String(error)) }
  }
  const startRuntime = async (): Promise<void> => {
    setFeedback(''); setFailure('')
    try {
      await settingsClient.save({ ...draft, setupCompleted: false }, secrets())
      await settingsClient.startRuntime()
      setFeedback(t('verified'))
    } catch (error) { setFailure(error instanceof Error ? error.message : String(error)) }
  }
  const privacy = draft.provider === 'local-qwen' ? t('privacyLocal') : draft.provider === 'system' ? t('privacySystem') : t('privacyRemote')
  const busy = state.status === 'saving' || state.status === 'testing'
  const saveActions = <div className="acmv-actions"><span className="acmv-feedback" data-error={failure !== ''}>{failure || feedback || state.error}</span><button type="button" className="acmv-button" data-primary disabled={busy} onClick={() => { void verify() }}>{state.status === 'testing' ? t('testing') : t('saveTest')}</button></div>
  if (page === 'overview') return <div className="acmv-page"><header className="acmv-head"><h2>{t('title')}</h2><p>{t('intro')}</p></header>{!draft.setupCompleted ? <div className="acmv-setup-banner">{t('firstUse')}</div> : null}<div className="acmv-section-title">Options</div><section className="acmv-options"><label className="acmv-row"><span className="acmv-row-copy"><strong>{t('quickModelA')}</strong><small>{t('quickModelsDesc')}</small></span><span className="acmv-row-action"><select className="acmv-select" value={modelKey(draft.quickModelA)} onChange={event => { chooseModel('quickModelA', event.target.value) }}><option value="">{models.choices.length === 0 ? t('quickUnavailable') : t('quickAuto')}</option>{models.choices.map(choice => <option key={modelKey(choice.ref)} value={modelKey(choice.ref)}>{choice.label}</option>)}</select></span></label><label className="acmv-row"><span className="acmv-row-copy"><strong>{t('quickModelB')}</strong><small>{t('quickModelsDesc')}</small></span><span className="acmv-row-action"><select className="acmv-select" value={modelKey(draft.quickModelB)} onChange={event => { chooseModel('quickModelB', event.target.value) }}><option value="">{models.choices.length === 0 ? t('quickUnavailable') : t('quickAuto')}</option>{models.choices.map(choice => <option key={modelKey(choice.ref)} value={modelKey(choice.ref)}>{choice.label}</option>)}</select></span></label><div className="acmv-row"><span className="acmv-row-copy"><strong>{t('provider')}</strong><small>{t('providerDetail')}</small></span><span className="acmv-row-action"><strong>{providerCopy[draft.provider][0]}</strong><button type="button" className="acmv-button" onClick={() => { setPage('provider') }}>{t('configure')} ›</button></span></div><label className="acmv-row"><span className="acmv-row-copy"><strong>{t('language')}</strong><small>BCP 47 · zh-CN / en-US</small></span><span className="acmv-row-action"><input className="acmv-input" value={draft.language} onChange={event => { update('language', event.target.value) }}/></span></label><label className="acmv-row"><span className="acmv-row-copy"><strong>{t('autoSubmit')}</strong><small>{t('autoSubmitDesc')}</small></span><span className="acmv-row-action"><input type="checkbox" checked={draft.autoSubmit} onChange={event => { update('autoSubmit', event.target.checked) }}/></span></label><div className="acmv-row acmv-privacy"><span className="acmv-row-copy"><strong>{t('privacy')}</strong><small>{privacy}</small></span></div></section>{saveActions}</div>

  const runtime = state.runtime
  return <div className="acmv-page">{setupMode ? null : <button type="button" className="acmv-back" onClick={() => { setPage('overview') }}>‹ {t('back')}</button>}<header className="acmv-head"><h2>{t('provider')}</h2><p>{t('providerDetail')}</p></header>{setupMode || !draft.setupCompleted ? <div className="acmv-setup-banner">{t('firstUse')}</div> : null}<section className="acmv-group"><div className="acmv-providers">{(['local-qwen', 'system', 'remote-websocket'] as const).map(id => <ProviderCard key={id} id={id} selected={draft.provider === id} title={providerCopy[id][0]} description={providerCopy[id][1]} onSelect={value => { update('provider', value) }}/>)}</div></section>
    {draft.provider === 'local-qwen' ? <section className="acmv-group">
      {state.recommendations === undefined ? null : <div className="acmv-runtime" style={{ marginTop: 0, marginBottom: 14 }}>
        <span><strong>{t('bundledLauncher')}</strong><br/>{t('bundledLauncherDesc')}<br/><small style={{ opacity: .72, wordBreak: 'break-all' }}>{state.recommendations.localLauncherPath}</small></span>
        <button type="button" className="acmv-button" onClick={useBundledLauncher}>{t('useBundledLauncher')}</button>
      </div>}
      <div className="acmv-grid">
        <label className="acmv-field"><span>{t('streamUrl')}</span><input className="acmv-input" value={draft.localStreamUrl} onChange={event => { update('localStreamUrl', event.target.value) }}/></label>
        <label className="acmv-field"><span>{t('healthUrl')}</span><input className="acmv-input" value={draft.localHealthUrl} onChange={event => { update('localHealthUrl', event.target.value) }}/></label>
        <label className="acmv-field"><span>{t('localModel')}</span><input className="acmv-input" value={draft.localModel} onChange={event => { update('localModel', event.target.value) }}/></label>
        <label className="acmv-field"><span>{t('startMode')}</span><select className="acmv-select" value={draft.localStartMode} onChange={event => { update('localStartMode', event.target.value as VoiceSettings['localStartMode']) }}><option value="on-demand">{t('onDemand')}</option><option value="with-harness">{t('withHarness')}</option><option value="manual">{t('manual')}</option></select></label>
        {draft.localStartMode === 'manual' ? null : <>
          <label className="acmv-field"><span>{t('runner')}</span><select className="acmv-select" value={draft.localRunner} onChange={event => { update('localRunner', event.target.value as VoiceSettings['localRunner']) }}><option value="powershell">{t('powershell')}</option><option value="executable">{t('executable')}</option></select></label>
          <label className="acmv-field"><span>{t('scriptPath')}</span><input className="acmv-input" value={draft.localScriptPath} onChange={event => { update('localScriptPath', event.target.value) }}/></label>
          <label className="acmv-field"><span>{t('workingDirectory')}</span><input className="acmv-input" value={draft.localWorkingDirectory} onChange={event => { update('localWorkingDirectory', event.target.value) }}/></label>
          <label className="acmv-field"><span>{t('startupTimeout')}</span><input className="acmv-input" type="number" min="5" max="600" value={Math.round(draft.localStartupTimeoutMilliseconds / 1000)} onChange={event => { update('localStartupTimeoutMilliseconds', Number(event.target.value) * 1000) }}/></label>
          <label className="acmv-field" style={{ gridColumn: '1 / -1' }}><span>{t('scriptArgs')}</span><textarea className="acmv-textarea" value={draft.localScriptArguments.join('\n')} onChange={event => { update('localScriptArguments', event.target.value.split(/\r?\n/u).map(argument => argument.trim()).filter(argument => argument !== '')) }}/></label>
        </>}
      </div>
      {draft.localCredentialRef === '' ? null : <div style={{ marginTop: 14 }}><CredentialField label={t('key')} reference={draft.localCredentialRef} draft={localSecret} setDraft={setLocalSecret} {...credential(draft.localCredentialRef)} clear={() => { void settingsClient.clearCredential(draft.localCredentialRef) }} t={t}/></div>}
      <label className="acmv-field" style={{ marginTop: 14 }}><span>{t('localKeyRef')}</span><input className="acmv-input" value={draft.localCredentialRef} onChange={event => { update('localCredentialRef', event.target.value) }}/></label>
      <div className="acmv-runtime"><i className="acmv-runtime-dot" data-phase={runtime?.phase ?? 'stopped'}/><span><strong>{t('runtime')}</strong><br/>{runtime?.message ?? t('notConfigured')}</span><button type="button" className="acmv-button" disabled={runtime?.phase === 'starting'} onClick={() => { void startRuntime() }}>{runtime?.phase === 'starting' ? t('starting') : t('start')}</button><button type="button" className="acmv-button" disabled={runtime?.phase !== 'ready' && runtime?.phase !== 'error'} onClick={() => { void settingsClient.stopRuntime() }}>{t('stop')}</button></div>
      {runtime?.logTail === undefined ? null : <pre className="acmv-log">{runtime.logTail}</pre>}
      <p className="acmv-note">{t('streamNote')}</p>
    </section> : null}
    {draft.provider === 'system' ? <section className="acmv-group"><p className="acmv-note" style={{ margin: 0 }}>{t('systemNote')}</p></section> : null}
    {draft.provider === 'remote-websocket' ? <section className="acmv-group"><div className="acmv-grid"><label className="acmv-field"><span>{t('remoteUrl')}</span><input className="acmv-input" value={draft.remoteUrl} placeholder="wss://asr.example.com/v1/stream" onChange={event => { update('remoteUrl', event.target.value) }}/></label><label className="acmv-field"><span>{t('remoteModel')}</span><input className="acmv-input" value={draft.remoteModel} onChange={event => { update('remoteModel', event.target.value) }}/></label><label className="acmv-field"><span>{t('remoteKeyRef')}</span><input className="acmv-input" value={draft.remoteCredentialRef} onChange={event => { update('remoteCredentialRef', event.target.value) }}/></label></div>{draft.remoteCredentialRef === '' ? null : <div style={{ marginTop: 14 }}><CredentialField label={t('key')} reference={draft.remoteCredentialRef} draft={remoteSecret} setDraft={setRemoteSecret} {...credential(draft.remoteCredentialRef)} clear={() => { void settingsClient.clearCredential(draft.remoteCredentialRef) }} t={t}/></div>}<p className="acmv-note">{t('remoteNote')}</p></section> : null}{saveActions}</div>
}
