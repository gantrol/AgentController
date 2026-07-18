# Codex Micro 桌面模拟器

这是一个与 AgentController 完全独立的 Windows Codex Micro 模拟器：

- `CodexMicroVhfUm.dll` 是 UMDF2 HID source driver，直接调用微软正式 VHF
  接口并由系统内置 `Vhf.sys` 枚举 HID，暴露
  `VID 303A / PID 8360 / UsagePage FF00 / Usage 01 / ReportId 06`；
- 同一个 UMDF2 source 另创建一个仅服务于 Full access 二次确认框的受限 VHF
  键盘子设备（`VID 303A / PID 8361`）；控制协议只允许 Tab、Shift+Tab 和 Enter，
  不能输入文字或任意按键；
- `CodexMicroSimulator.exe` 直接连接该 HID，向 Codex 发送按键、旋钮和摇杆
  报告，并接收 Agent 灯光输出；
- 透明 WPF 窗口只显示键盘本体，支持等比缩放、拖动、任务栏、置顶和系统托盘；
- 窗口默认以 `590 × 610` 设计画布的 75% 打开，可从约 60% 起继续缩放；
- 命名单实例锁确保系统中只有一套全局鼠标钩子和驱动输出读取器，避免旋钮事件
  重复，以及多个进程拆分 Agent 灯光快照；
- 应用与托盘使用自制的“圆角方块底座 + 左上旋钮”图标，不复用 OpenAI
  或 Codex 应用图标；
- 右击机身可以重新连接、切换置顶状态和退出；托盘提供同样入口并可恢复窗口；
- 模拟器接收鼠标但不激活自身，白色旋钮可用滚轮或上下拖动选择 Codex
  菜单项，短按打开或确认，且不会关闭当前 Codex 弹出菜单；白色旋钮右键
  不执行操作，Micro 设置只由左下黑色旋钮打开；
- 白色旋钮旁会实时显示“序号 / 总数 · 当前项”，覆盖模型菜单、推理强度、
  速度、权限菜单和 Full access 确认按钮；可访问性树只读，动作仍全部由 HID 交付；
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

## 便携应用

[`v0.1.0` 预发布版](../../../releases/tag/codex-micro-v0.1.0) 提供 self-contained
Windows x64 便携包，不需要另外安装 .NET Runtime，也不会导入自签名根证书。
便携包只包含桌面应用；运行 `CodexMicroSimulator.exe` 前，仍需按下文在本机编译
并安装 VHF 驱动。

## 本地构建

本仓库只提交驱动源码，不提供预编译的 `.dll`、`.sys`、`.cat`、`.cer` 或安装器
二进制；这些生成物均已忽略，克隆后必须自行编译，也不应重新提交到仓库。

准备环境：

- Windows 10/11 x64；
- 用于桌面端和测试的 .NET SDK 9；
- Visual Studio 2022 或更新版本，并安装 **使用 C++ 的桌面开发**、Windows SDK
  `10.0.26100.0`（或兼容的更新版本）和 MSBuild；
- 首次还原固定版本 `Microsoft.Windows.WDK.x64` NuGet 包时需要联网；
- 只有安装驱动步骤需要管理员 PowerShell。

在仓库根目录先构建桌面端：

```powershell
dotnet restore .\virtual-micro\src\CodexMicro.Desktop\CodexMicro.Desktop.csproj
dotnet build .\virtual-micro\src\CodexMicro.Desktop\CodexMicro.Desktop.csproj -c Release --no-restore
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

- 桌面端：`src/CodexMicro.Desktop/bin/Release/net9.0-windows10.0.19041.0/`
- Microsoft UMDF2 VHF 驱动包：`driver/CodexMicroVhfUm/x64/Release/`
- 原生安装器：`tools/CodexMicro.DriverInstaller.Native/bin/Release/`

## 安装虚拟 HID

在管理员 PowerShell 中运行：

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

安装脚本生成本机代码签名证书 `CN=Codex Micro Simulator Driver`，将其加入
LocalMachine 的 `Root` 与 `TrustedPublisher`，签名 VHF source driver 和
catalog；报告描述符变化时只刷新已有模拟器设备，然后创建并验证
`Root\CodexMicroHidUm`；安装记录写入
`virtual-micro/driver-install.log`。

脚本生成的证书和签名后文件只是本机开发产物。不要公开私钥，也不要把该测试
证书当作生产签名身份复用。

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
