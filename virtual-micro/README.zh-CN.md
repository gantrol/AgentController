# Codex Micro 面板与虚拟 HID

这里包含 Agent Controller 使用的 Windows 设备支持和可承载 Micro 面板：

- `CodexMicroVhfUm.dll` 是 UMDF2 HID source driver，直接调用微软正式 VHF
  接口并由系统内置 `Vhf.sys` 枚举 HID，暴露
  `VID 303A / PID 8360 / UsagePage FF00 / Usage 01 / ReportId 06`；
- 同一个 UMDF2 source 另创建一个仅服务于 Full access 二次确认框的受限 VHF
  键盘子设备（`VID 303A / PID 8361`）；控制协议只允许 Tab、Shift+Tab 和 Enter，
  不能输入文字或任意按键；
- `AgentController.exe` 直接承载透明 WPF 面板。面板与实体手柄各自使用独立的
  Broker 逻辑客户端 ID 和 held-input 租约，但由同一个进程级 Broker 子进程独占
  HID 和输出流；
- 面板只显示键盘本体，支持等比缩放、拖动、置顶和不激活输入；通过 Agent
  Controller 标题栏或托盘打开，关闭只会隐藏；
- 窗口默认以 `590 × 610` 设计画布的 75% 打开，可从约 60% 起继续缩放；
- Agent Controller 现有的单实例生命周期确保只有一个鼠标钩子所有者和 Broker
  输出消费者；不再存在独立 Micro 可执行文件、互斥锁或第二个托盘图标；
- 右击机身可以重新连接、切换置顶状态或隐藏面板；
- 模拟器接收鼠标但不激活自身，白色旋钮可用滚轮或上下拖动选择 Codex
  菜单项，短按打开或确认，且不会关闭当前 Codex 弹出菜单；白色旋钮右键
  不执行操作，Micro 设置只由左下黑色旋钮打开；
- 白色旋钮旁会实时显示“序号 / 总数 · 当前项”，覆盖模型菜单、推理强度、
  速度、权限菜单和 Full access 确认按钮；可访问性树只读，动作仍全部由 HID 交付；
- 旋钮输入不再等待可访问性反馈：滚轮与拖动事件只保留最多三个待处理净刻度，
  反向操作会抵消队列，按下确认前会丢弃旧刻度；可访问性观察只异步显示状态，
  不会阻塞或补发输入；超过 180 ms 的输入，以及一次卡顿发送期间产生的积压，
  均直接丢弃；
- Agent 键悬停时，在能与 Codex 本地最近任务索引匹配的槽位上显示
  “所属项目 › 会话标题”；当前仅在官方默认 `recent` 来源可本地证明时合并，
  其他来源继续显示通用槽位名。该 UI 观察器只读
  `~/.codex/session_index.jsonl` 与 `.codex-global-state.json`，槽位状态仍独立
  以 VHF 灯光输出为准，也不会写入上述文件；
- 所有控件悬停时显示名称、当前映射、操作方式或连接状态；
- 六颗动作键只读观察 `~/.codex/config.toml` 中的
  `desktop.codex-micro-layout`，Codex 改键后自动更新图标和动作提示；
- Codex 图形采用当前 Codex 设置资源中的矢量轮廓，OpenAI 图形从当前 Codex
  MSIX 安装包读取，不额外复制品牌位图；
- 设置旋钮发送真实的 650 ms `ENC` 长按，由 Codex 自身桥接到
  `/settings/codex-micro`，随后把真实 Codex 主窗口切到前台；
- Codex 键会在动作交付后选择并激活面积最大的非工具 Codex 主窗口，避开同一
  进程中的小型工具窗。

完整界面尺寸、驱动身份、交互和验收条件见
[`DESIGN.zh-CN.md`](./DESIGN.zh-CN.md)。

## 历史独立版本

历史 [`v1.0.0` 正式版](../../../releases/tag/codex-micro-v1.0.0) 曾提供独立便携
模拟器。当前源码不再构建或分发该可执行文件，面板已经并入 Agent Controller。
单独的未签名开发者驱动包仍可使用，但安装前必须在本机签名；完整顺序见
[`UNSIGNED-DRIVER.zh-CN.md`](./UNSIGNED-DRIVER.zh-CN.md)。

## 未签名开发者驱动包

`CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip` 包含预编译的 UMDF2 DLL、
INF、未签名 catalog、原生 PnP 安装器、用于审计的相关源码，以及本机签名/安装脚本。
包内没有证书或私钥，下载后不能把它当作正式生产驱动直接安装。

如果希望审计并二次签名完全相同的二进制、但不想搭 C/C++ 编译环境，请使用该包并
参阅[未签名包说明](./UNSIGNED-DRIVER.zh-CN.md)。

## 本地构建

Git 仓库仍然只提交驱动源码；生成的 `.dll`、`.sys`、`.cat`、`.cer` 和安装器
二进制均保持忽略，也不应重新提交。Release 中单独附加的开发者驱动包是唯一的
预编译驱动分发物。

准备环境：

- 已验证的构建主机是 Windows 11 x64；
- 用于 Agent Controller、内嵌面板和测试的 .NET SDK 10；
- 可复现的驱动构建路线是 Visual Studio/Build Tools 2022，并安装 **使用 C++ 的
  桌面开发**、MSVC v143 x64/x86 工具、x64/x86 Spectre 缓解库、Windows SDK
  `10.0.26100.0` 和 MSBuild；
- 首次还原固定的 `Microsoft.Windows.WDK.x64` `10.0.26100.6584` NuGet 包时需要联网；
- 只有安装驱动步骤需要管理员 PowerShell。

Visual Studio 2026 也可以调度构建，但还需要安装 v143 工具集和匹配的 26100 SDK
组件；项目有意固定 26100 工具链，不会静默切换到更新的 WDK。

在仓库根目录先构建 Agent Controller 和内嵌面板：

```powershell
dotnet restore .\AgentController.sln
dotnet build .\app\AgentController.csproj -c Release --no-restore
```

再定位已安装的 64 位 MSBuild，编译 UMDF2 source driver 和原生安装器：

```powershell
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild `
  -find 'MSBuild\**\Bin\amd64\MSBuild.exe' | Select-Object -First 1

if (-not $msbuild) { throw '未找到 Visual Studio MSBuild。' }

& $msbuild '.\virtual-micro\driver\CodexMicroVhfUm\CodexMicroVhfUm.vcxproj' `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 /m

& $msbuild '.\virtual-micro\tools\CodexMicro.DriverInstaller.Native\CodexMicro.DriverInstaller.Native.vcxproj' `
  /restore /t:Build /p:Configuration=Release /p:Platform=x64 /m
```

主要输出：

- 内嵌面板：`src/AgentController.MicroSurface.Wpf/bin/Release/net10.0-windows10.0.19041.0/`
- Microsoft UMDF2 VHF 驱动包：`driver/CodexMicroVhfUm/x64/Release/`
- 原生安装器：`tools/CodexMicro.DriverInstaller.Native/bin/Release/`

## 安装虚拟 HID

先退出 AgentController，再用普通 PowerShell 运行：

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

脚本会自行弹出 UAC，并完成本机签名、安装/更新和健康检查。驱动不检查 Codex 或
AgentController 版本；INF 版本只供 Windows 更新驱动包。简明步骤与验证命令见
[本地安装说明](../docs/CodexMicroSimulator-安装教程.zh-CN.md)，构建和证书细节见
[`UNSIGNED-DRIVER.zh-CN.md`](./UNSIGNED-DRIVER.zh-CN.md)。

## 验证

```powershell
dotnet test .\virtual-micro\tests\CodexMicro.Protocol.Tests\CodexMicro.Protocol.Tests.csproj -c Release
dotnet test .\virtual-micro\tests\CodexMicro.Desktop.Tests\CodexMicro.Desktop.Tests.csproj -c Release
```

桌面端测试覆盖 Codex TOML 布局、未知键帽回退、全部键帽离屏渲染、正方形
键帽、丝印安全区、无边框八方向缩放、摇杆角度/视觉行程、旋钮手势以及
连续旋钮动画、菜单位置文字和受限 VHF 键盘线协议。

如需生成离屏视觉预览，可在运行桌面测试前将
`CODEX_MICRO_PREVIEW_PATH` 设置为目标 PNG 的绝对路径。
