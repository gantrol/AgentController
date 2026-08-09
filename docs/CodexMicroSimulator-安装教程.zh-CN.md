# Agent Controller 本地安装（含 Micro 驱动）

[English](./CodexMicroSimulator-installation.md)

适用于 Windows 10/11 x64。驱动只提供手柄与虚拟 Micro 共用的 HID 通道，**不会读取或检查 Codex、Agent Controller 的版本**。INF 版本只是 Windows 用来识别驱动包的新旧，不是应用兼容白名单。

## 最快安装

1. 解压 Agent Controller 应用包和 Micro 驱动包，同时退出 Codex 和所有 Agent Controller 进程；否则 Codex 可能让 Windows 继续加载旧 UMDF 驱动二进制。
2. 在普通 PowerShell 中，从仓库根目录运行：

   ```powershell
   .\virtual-micro\Install-CodexMicroDriver.ps1
   ```

   如果当前目录就是解压后的 `virtual-micro`，运行：

   ```powershell
   .\Install-CodexMicroDriver.ps1
   ```

3. 同意脚本自动弹出的 Windows UAC。末尾出现 `Ready` 才表示新版驱动已经实际加载。如果脚本提示需要重启（退出码 `3010`），请先重启 Windows，再打开 Codex 或 Agent Controller。
4. 解压 `CodexMicro-Keypad-*-win-x64.zip`，以普通用户身份启动
   `CodexMicro.exe`。也可以从 Agent Controller 标题栏的小键盘图标启动已放在同目录、
   `CodexMicro` 子目录或 `%LOCALAPPDATA%\CodexMicro` 中的独立程序。Micro 默认置顶，
   右击机身空白处可切换置顶。窗口固定大小，拖动机身由应用自行移动，不会进入
   Windows 的系统移动/缩放循环或触发 Snap。关闭只收起到通知区域；托盘图标双击
   显示/收起，右键菜单提供“显示/收起小键盘”和“退出”。
5. 如需改语言，在托盘菜单中打开“语言”，选择自动、简体中文或 English。自动模式
   优先跟随 Agent Controller，没有明确设置时跟随 Windows；切换立即生效。
6. 如需登录 Windows 后自动运行，在托盘菜单勾选“开机自启动”。程序会静默进入
   通知区域；取消勾选即可移除当前用户的启动项，不需要管理员权限。

不要用管理员身份长期运行 Agent Controller 或 CodexMicro，也不要关闭 Windows 驱动
签名强制。

## 升级

- 普通应用或 Codex 更新：退出 Agent Controller，替换应用文件后重新启动。
- **不需要每次更新都重装驱动。** 只有驱动包本身更新、设备消失，或健康检查失败时，才重新运行安装脚本。
- 驱动版本不需要与 Codex 或 Agent Controller 版本一致。

## 验证与排错

只有新版二进制已经生效时，安装脚本才会显示 `Ready`。如果显示“已安装但需要重启”，即使设备管理器已经显示新版 INF 版本，Windows 当前仍可能运行旧二进制。也可检查设备：

```powershell
Get-PnpDevice -FriendlyName 'Codex Micro Simulator UMDF2 Virtual HID' |
  Select-Object Status, FriendlyName, InstanceId
```

正常结果只有一个设备，且 `Status` 为 `OK`。失败时先看 `virtual-micro/driver-install.log`；构建、重新签名和证书细节见 [`UNSIGNED-DRIVER.zh-CN.md`](../virtual-micro/UNSIGNED-DRIVER.zh-CN.md)。

## 附录：前置安装

### 只运行发布包

- Windows 10/11 x64；
- 已安装并可登录的 Codex 桌面版；
- 使用实体手柄时需要 Windows 可识别的 XInput 手柄；使用语音键时需要可用麦克风；
- Agent Controller Windows 发布包为自包含版本；单独下载的精简 Micro 小键盘包需要
  Microsoft .NET 10 Desktop Runtime x64。

### 本机签名并安装预编译驱动

- Windows SDK `10.0.26100.0`（提供 SignTool）；
- 固定的 `Microsoft.Windows.WDK.x64` `10.0.26100.6584` NuGet 包（提供 Inf2Cat）；
- 如果该 NuGet 包尚未缓存，需要 Visual Studio/Build Tools 的 MSBuild 联网还原一次。

这条路线不需要 C++ 编译环境、Visual C++ Redistributable 或 .NET Runtime。

### 从源码重新构建

另需 Visual Studio Build Tools 2022 的 **使用 C++ 的桌面开发**、MSVC v143 x64/x86、x64/x86 Spectre 缓解库和 MSBuild。构建 Agent Controller 还需要 .NET SDK 10。

只有驱动安装阶段需要 UAC；不要导入来源不明的证书，也不要分发脚本在本机生成的测试证书或私钥。
