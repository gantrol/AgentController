# DeepSeek Keypad · GPUI

DeepSeek 专用的原生控制面。它不是 SaaS 页面，也不是脱离后端自演状态的 UI：按键通过现有 DeepSeek Bridge 控制真实会话；网页仍是长文本、文件与登录状态的权威界面。

## 运行

```powershell
cd D:\AgentController\virtual-micro\src\DeepSeekKeypad.Gpui
cargo run --release
```

控制端点按以下顺序解析：

1. `--endpoint http://127.0.0.1:PORT/__agentcontroller/micro/request`
2. `DEEPSEEK_KEYPAD_CONTROL_URI`
3. `%LOCALAPPDATA%\CodexMicro\harness-settings.json` 中的 `deepseek-harness`
4. 默认端点 `http://127.0.0.1:3080/__agentcontroller/micro/request`

端点只允许 HTTP loopback 和固定协议路径，避免控制请求离开本机。

## 已实现

- GPUI 透明窗口、置顶、固定尺寸；顶部拖动区使用 Windows 非客户区命中测试，不调用 Windows 上无实现的 `start_window_move`
- 4 × 4 等宽网格，标准键严格为 `82 × 82`；语音键是明确的双列变体
- 无可见描述性文字；会话状态、连接状态和操作反馈只使用图形与信号灯
- 中性浅色硬件面板、深青色强调色；无渐变、无蓝紫夜间主题、无大圆角卡片堆叠
- 真实 Bridge 状态轮询、最多六个会话及 capability gating
- 新建会话、会话视图、停止、分叉、模型切换、会话切换、侧栏/详情、选择器旋钮均发送真实协议动作
- DeepSeek 键先请求现有表面；Bridge 返回 `opening` 时，用 Edge/Chrome app mode 打开独立网页表面

语音键目前是禁用态。仓库中没有 GPUI 语音后端时，不制造“正在聆听”的假交互。

## 代码边界

| 模块 | 职责 |
| --- | --- |
| `main.rs` | 最小进程入口 |
| `app.rs` | GPUI 启动、资源和窗口创建 |
| `bridge.rs` | 版本化 loopback 协议、配置发现和反序列化 |
| `browser.rs` | 独立 DeepSeek 网页表面的进程启动 |
| `controls.rs` | 可复用的 keycap、旋钮、信号灯、方向键和窗口控件 |
| `keypad.rs` | 控制面状态机、动作映射和布局 |
| `platform.rs` | Windows 置顶与启动错误处理 |
| `theme.rs` | 共享尺寸、颜色、阴影和反馈时长令牌 |

这套边界遵循 Zed 的维护取向：正确性和清晰度优先；新文件只承载可命名的逻辑组件；不使用 `mod.rs`；注释只解释不明显的原因；失败传回界面状态，不静默丢弃。

## Web 与 Agent 的结合方案

当前阶段采用两个顶层窗口、一个状态源：

```text
GPUI keypad ── versioned loopback commands ── DeepSeek Bridge/Cordis plugin
                                                  │
                                                  └── official DeepSeek Web surface
```

- 小键盘负责高频、低信息密度动作；网页负责输入、长输出、文件、授权和恢复。
- Bridge 维护唯一的当前会话 ID 和 capability 集合；小键盘不读取 DOM，也不猜网页状态。
- DeepSeek 键在网页已存在时只置前，在不存在时打开 `/?codexMicroSurface=1`；20 秒启动保护避免重复窗口。
- 第一阶段继续使用 Edge/Chrome app mode，复用成熟浏览器登录与站点兼容性。
- 第二阶段可将网页替换为独立、不透明的 WebView2 顶层 sidecar。它与透明 GPUI 窗口保持分离，以免透明合成、焦点、输入法和 WebView 子窗口互相牵制。
- sidecar 只增加宿主能力：持久化 profile、外部链接转系统浏览器、下载/权限策略、崩溃重建和 `Hidden → Opening → Ready → Foreground` 生命周期；业务动作继续走同一 Bridge 协议。

这样 web/agent 的切换不是两套状态互相同步，而是两种视图共同订阅一个 Agent 状态源。

## 验证

```powershell
cargo fmt --check
cargo clippy --all-targets -- -D warnings
cargo test
cargo build --release
```

设计与工程参考：

- [Zed repository rules](https://github.com/zed-industries/zed/blob/main/.rules)
- [Zed GPUI README](https://github.com/zed-industries/zed/blob/main/crates/gpui/README.md)
- [shadcn/ui components](https://ui.shadcn.com/docs/components)
- [DeepSeek Harness Web guide](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/user/guide/index.zh.md)
- [Microsoft WebView2 overview](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- [Elgato Stream Deck Neo](https://www.elgato.com/us/en/explorer/products/neo/elgato-stream-deck-neo-quick-start-guide/)
- [Teenage Engineering EP-133 K.O. II](https://teenage.engineering/products/ep-133-k-o-ii)
