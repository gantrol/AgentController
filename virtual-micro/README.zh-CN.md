# 独立 Codex Micro 小键盘与虚拟 HID

[English](./README.md)

`CodexMicro.exe` 是独立于 Agent Controller 的 Windows 小键盘。发行窗口直接复用
原来的 WPF `MainWindow.xaml`、资源字典、控件模板和动画，不再用另一套绘图技术
近似重画，因此圆角、透明、阴影、字体和 DPI 栅格化都与原界面走同一条渲染路径。

Agent Controller 不引用或承载小键盘 UI。标题栏和托盘入口只启动磁盘上的独立
`CodexMicro.exe`；找不到时打开 Releases 下载页。两个程序只共享当前用户的 Micro
Broker 协议和 HID 驱动，可以单独启动，也可以同时运行。

DeepSeek 特调包首次点击会先复用已有 Harness；未发现可用桥接时，可选择“使用我
已有的 DSH”，或“在专用 WSL 中安装 DSH（推荐）”。流程不假定盘符、源码目录或
用户已有发行版名称，也不执行 `git clone`。完整的官方来源、端口说明和逐步排错见
[Windows 首次使用 DeepSeek：默认配置准备](./DEEPSEEK-WINDOWS-SETUP.zh-CN.md)。

发行包采用 framework-dependent 单文件发布。实测 `CodexMicro.exe` 约 `24.9 MiB`，
zip 约 `6.4 MiB`，封包脚本把 zip 上限固定为 `15 MiB`；超过即构建失败。这样保留
原 WPF 像素效果，同时避免约 `75 MiB` 的自包含 WPF 便携包。独立运行前需安装
[Microsoft .NET 10 Desktop Runtime x64 官方安装程序](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-10.0.10-windows-x64-installer)。

## 窗口与托盘

- 以 `590 × 610` 设计面的 75%（`442.5 × 457.5`）固定大小打开；
- 窗口客户区透明，机身外不会出现不透明矩形背景；
- 拖动机身空白处移动；移动由应用直接更新坐标，不进入 Windows move/size loop；
- 窗口没有系统缩放边框、最大化按钮或系统标题栏，也不允许调整大小，因此拖到
  屏幕边缘不会触发 Snap、贴靠布局或 Windows 自动改写窗口尺寸；
- 右击机身空白处可切换置顶或收起面板；
- 关闭窗口或按 Alt+F4 只收起到 Windows 通知区域，不退出后台进程；
- 通知区域图标双击可显示/收起；右键菜单提供“显示/收起小键盘”和“退出”；
- 托盘菜单的“开机自启动”可随时启用或关闭；启用后登录 Windows 时会启动并立即
  显示小键盘；
- Codex 模式下，软件设置页提供“反转旋钮方向”；设置按当前小键盘保存，
  不会影响外部 Harness；
- 鼠标输入不会激活小键盘，因此 Codex 的输入焦点保持不变。

## 界面语言

托盘菜单的“语言”子菜单提供“自动（跟随 Agent Controller / Windows）”、
“简体中文”和“English”。切换后托盘、机身菜单、悬停说明、状态及错误文本会立即
刷新；选择保存在 `%LOCALAPPDATA%\CodexMicro\settings.json`。自动模式优先读取
Agent Controller 的界面语言；其设置仍为自动或文件不存在时跟随 Windows UI 语言。

## 语音归属与本地 Qwen 自启动

语音能力完全属于小键盘：小键盘持有麦克风、识别配置、服务进程和远程 API Key。
Bridge 只在 DeepSeek 与小键盘之间转发“切换语音”请求以及增量 / 最终文字；DeepSeek 插件只
增加一个语音按钮，不采集音频、不连接 Qwen，也没有语音设置页。

在“小键盘软件设置 → 语音输入”中选择“本地流式 Qwen”，再点“使用本机示例”。
该按钮默认选择“随小键盘启动”，可按需改回首次使用时启动或仅检测。示例配置不含
盘符或用户目录：

- 流式地址：`ws://127.0.0.1:8765/v1/stream`；
- 健康检查：`http://127.0.0.1:8765/health`；
- 启动脚本：`{AppDir}\voice\start-qwen3-asr-stream.ps1`；
- 工作目录：`{AppDir}\voice`；
- 模型：`Qwen/Qwen3-ASR-0.6B`；
- 默认使用 1 秒识别分块以降低首个增量结果的等待时间；可在单独运行启动脚本时通过
  `-ChunkSeconds` 调整；
- WSL 发行版：默认 `Ubuntu`，可改成 `wsl -l -q` 显示的名称；
- WSL Python：默认留空，由脚本依次查找小键盘专用或已有本地 Qwen 虚拟环境，最后
  才使用 WSL 的 `python3`。

路径支持 `{AppDir}`、`{LocalAppData}`、环境变量和相对路径。解析发生在运行时，配置
文件不会保存本机安装目录的绝对路径。

启动方式有三种：

- “首次使用时启动”：按麦克风键、点 DeepSeek 的语音按钮或执行“保存并验证”时启动；
- “随小键盘启动”：小键盘打开后后台预热；
- “仅检测”：服务由用户自己启动，小键盘只检查健康状态。

若要登录 Windows 后自动启动 Qwen，同时启用托盘菜单的“小键盘开机自启动”，并把
这里设为“随小键盘启动”；登录后会先启动小键盘，再由小键盘预热语音服务。

小键盘先检查 `/health` 返回 `ready: true` 和 `dsh-stream-v1`，未就绪时才启动脚本；
模型加载完成后再连接 WebSocket。退出时只停止由当前小键盘启动的进程，绝不会接管
或终止原本就在运行的服务。“退出小键盘时停止”也可关闭。

Qwen3-ASR 的流式接口使用 vLLM 后端。以 WSL 中的当前用户为例，一次性准备环境：

```powershell
wsl -d Ubuntu -- bash -lc 'python3.12 -m venv "$HOME/.local/share/codex-micro/qwen-asr/venv" && source "$HOME/.local/share/codex-micro/qwen-asr/venv/bin/activate" && python -m pip install -U "qwen-asr[vllm]" aiohttp numpy'
```

如果发行版名或 Python 位置不同，在小键盘设置中修改即可，不需要改脚本。首次使用
模型 ID 时会由 Qwen/Hugging Face 缓存下载模型，因而就绪时间可能较长；默认等待
600 秒，可在设置中调整。依赖、模型与硬件要求见
[Qwen3-ASR 官方安装指南](https://github.com/QwenLM/Qwen3-ASR#quickstart)，流式
协议实现参考
[Qwen 官方流式推理示例](https://github.com/QwenLM/Qwen3-ASR/blob/main/examples/example_qwen3_asr_vllm_streaming.py)。

只检查本机环境而不加载模型或启动服务，可在解压目录运行：

```powershell
& ".\voice\start-qwen3-asr-stream.ps1" -Distribution Ubuntu -CheckOnly
```

## 控件

- 6 个 Agent 键发送 `AG00`–`AG05`，并显示 Codex 返回的槽位灯光；
- 4 个命令键发送 `ACT06`–`ACT09`，Codex 键发送 `ACT12`；
- Codex 模式下，语音键按下发送 `ACT10 down`，松开后发送 `ACT10 up`；外部 Harness
  模式下，该键直接控制小键盘自己的语音采集与识别；
- Codex 模式下，白色旋钮可设为常规输入区导航或“仅推理强度”；后者旋转时只改变
  思考强度，短按在快捷模型 A / B 间切换；
- Codex 软件设置中的“反转旋钮方向”只作用于当前 Codex 小键盘；外部 Harness 忽略该设置；
- 外部 Harness 独立保存白色旋钮模式。默认只动态发现当前输入框内可见、可用的
  控件（包括其他插件后来加入的按钮），用蓝色轮廓高亮，短按执行；也可改为
  “仅推理强度”或“最近会话”；
- 摇杆支持连续拖动并在松开时回中；外部 Harness 默认上/下切换侧边栏会话，
  左侧折叠/展开左栏，右侧打开详情抽屉；
- 左下旋钮左键切换当前 Agent 配置的快捷模型，右键直达该 Agent 的独立设置；
  Codex 模式长按仍打开官方 Micro 设置。

## 虚拟 HID

`CodexMicroVhfUm.dll` 是基于微软 Virtual HID Framework 的 UMDF2 source driver。
系统 `Vhf.sys` 枚举：

- 主设备：`VID 303A / PID 8360 / UsagePage FF00 / Usage 01 / ReportId 06`；
- 受限确认键盘：`VID 303A / PID 8361`，只允许 Tab、Shift+Tab 与 Enter。

小键盘本身不直接调用私有驱动 IOCTL。当前用户唯一的 Broker 子进程拥有驱动句柄、
输入 sequence、held-input 租约和输出流；如果 Agent Controller 已启动，小键盘会连接
现有 Broker，否则由 `CodexMicro.exe --micro-broker` 启动同一实现。

当前只提供未签名开发者驱动流程。不要关闭 Windows 驱动签名强制，也不要安装来源
不明的证书。构建、签名和安装细节见
[`UNSIGNED-DRIVER.zh-CN.md`](./UNSIGNED-DRIVER.zh-CN.md)。

## 构建与封包

准备 Windows 10/11 x64 和 .NET SDK 10。在仓库根目录运行：

```powershell
dotnet build .\virtual-micro\src\CodexMicro.DesktopHost\CodexMicro.DesktopHost.csproj -c Release
.\scripts\package-micro.ps1 -Version 0.2.4
# 准备带桥接插件和在线自动配置入口的 DeepSeek 特调包
.\scripts\package-micro.ps1 -Version 0.2.4 -Preset deepseek
```

产物：

- 单文件可执行程序：`.artifacts/micro-release/0.2.4/publish/CodexMicro.exe`；
- 独立压缩包：`dist/CodexMicro-Keypad-0.2.4-win-x64.zip`；
- SHA-256：同名 `.sha256` 文件。
- DeepSeek 特调包：`dist/Deepseek-Harness-Keypad-v0.2.4-win-x64.zip`；
- 可单独安装到已有 DSH 的 Bridge：
  `dist/Deepseek-Harness-Keypad-Bridge-v0.2.4.zip`（也带独立 SHA-256）。

封包脚本收录 `CodexMicro.exe`、README、首次配置文档、许可证和小键盘端 `voice`
启动适配器，不复制模型、Python 环境、Desktop Runtime、调试符号或驱动。
`deepseek` 预设会先构建精简的 DeepSeek Harness Bridge 运行包，并同时产出独立插件
zip；主包内保留同一载荷供专用 WSL 安装。两份都不包含源码、测试或开发依赖。
预设还包含无固定路径的托管安装脚本和显式首次选择；默认目标为 DeepSeek，
默认语音为系统识别。本地 Qwen、远程流式识别和未来的离线 WSL payload 保留为
可选能力。驱动仍作为单独的安全边界安装。

## 安装驱动

先退出 Codex、Agent Controller 和 CodexMicro，再从普通 PowerShell 运行：

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

脚本自行弹出 UAC，完成本机签名、安装/更新与健康检查。末尾出现 `Ready` 才表示新版
驱动已经加载；退出码 `3010` 表示必须先重启 Windows。简明步骤见
[安装教程](../docs/CodexMicroSimulator-安装教程.zh-CN.md)。

## 验证

```powershell
dotnet test .\virtual-micro\tests\CodexMicro.Protocol.Tests\CodexMicro.Protocol.Tests.csproj -c Release
dotnet test .\tests\AgentController.MicroBroker.Tests\AgentController.MicroBroker.Tests.csproj -c Release
dotnet test .\virtual-micro\tests\CodexMicro.Desktop.Tests\CodexMicro.Desktop.Tests.csproj -c Release
```

桌面测试锁定原 XAML 的透明窗口、固定尺寸、NoResize、无抢焦点行为和关键视觉几何；
Broker 与协议测试锁定共享租约、输入批次和 RPC 合同。
