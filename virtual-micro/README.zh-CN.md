# 独立 Codex Micro 小键盘与虚拟 HID

[English](./README.md)

`CodexMicro.exe` 是独立于 Agent Controller 的 Windows 小键盘。发行窗口直接复用
原来的 WPF `MainWindow.xaml`、资源字典、控件模板和动画，不再用另一套绘图技术
近似重画，因此圆角、透明、阴影、字体和 DPI 栅格化都与原界面走同一条渲染路径。

Agent Controller 不引用或承载小键盘 UI。标题栏和托盘入口只启动磁盘上的独立
`CodexMicro.exe`；找不到时打开 Releases 下载页。两个程序只共享当前用户的 Micro
Broker 协议和 HID 驱动，可以单独启动，也可以同时运行。

发行包采用 framework-dependent 单文件发布。实测 `CodexMicro.exe` 约 `24.9 MiB`，
zip 约 `6.4 MiB`，封包脚本把 zip 上限固定为 `15 MiB`；超过即构建失败。这样保留
原 WPF 像素效果，同时避免约 `75 MiB` 的自包含 WPF 便携包。独立运行前需安装
Microsoft .NET 10 Desktop Runtime x64。

## 窗口与托盘

- 以 `590 × 610` 设计面的 75%（`442.5 × 457.5`）固定大小打开；
- 窗口客户区透明，机身外不会出现不透明矩形背景；
- 拖动机身空白处移动；移动由应用直接更新坐标，不进入 Windows move/size loop；
- 窗口没有系统缩放边框、最大化按钮或系统标题栏，也不允许调整大小，因此拖到
  屏幕边缘不会触发 Snap、贴靠布局或 Windows 自动改写窗口尺寸；
- 右击机身空白处可切换置顶或收起面板；
- 关闭窗口或按 Alt+F4 只收起到 Windows 通知区域，不退出后台进程；
- 通知区域图标双击可显示/收起；右键菜单提供“显示/收起小键盘”和“退出”；
- 托盘菜单的“开机自启动”可随时启用或关闭；启用后登录 Windows 时静默启动到
  通知区域，不主动弹出小键盘；
- 鼠标输入不会激活小键盘，因此 Codex 的输入焦点保持不变。

## 界面语言

托盘菜单的“语言”子菜单提供“自动（跟随 Agent Controller / Windows）”、
“简体中文”和“English”。切换后托盘、机身菜单、悬停说明、状态及错误文本会立即
刷新；选择保存在 `%LOCALAPPDATA%\CodexMicro\settings.json`。自动模式优先读取
Agent Controller 的界面语言；其设置仍为自动或文件不存在时跟随 Windows UI 语言。

## 控件

- 6 个 Agent 键发送 `AG00`–`AG05`，并显示 Codex 返回的槽位灯光；
- 4 个命令键发送 `ACT06`–`ACT09`，Codex 键发送 `ACT12`；
- 语音键按下发送 `ACT10 down`，松开后发送 `ACT10 up`；
- 白色旋钮支持滚轮、拖动和短按，发送 `ENC_CW`、`ENC_CC` 与 `ENC`；
- 摇杆支持连续拖动并在松开时回中；
- 左下设置旋钮发送真实的 650 ms `ENC` 长按，交由 Codex 打开 Micro 设置。

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
.\scripts\package-micro.ps1 -Version 1.2.0
```

产物：

- 单文件可执行程序：`.artifacts/micro-release/1.2.0/publish/CodexMicro.exe`；
- 独立压缩包：`dist/CodexMicro-Keypad-1.2.0-win-x64.zip`；
- SHA-256：同名 `.sha256` 文件。

封包脚本只收录 `CodexMicro.exe`、README 和许可证，不复制 Desktop Runtime、调试符号
或驱动。驱动仍作为单独的安全边界安装。

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
