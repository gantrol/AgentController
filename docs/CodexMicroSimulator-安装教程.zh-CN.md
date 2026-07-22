# Agent Controller 本地安装（含 Micro 驱动）

[English](./CodexMicroSimulator-installation.md)

适用于 Windows 10/11 x64。驱动只提供手柄与虚拟 Micro 共用的 HID 通道，**不会读取或检查 Codex、Agent Controller 的版本**。INF 版本只是 Windows 用来识别驱动包的新旧，不是应用兼容白名单。

## 最快安装

1. 解压 Agent Controller 应用包和 Micro 驱动包，退出旧版 Agent Controller。
2. 在普通 PowerShell 中，从仓库根目录运行：

   ```powershell
   .\virtual-micro\Install-CodexMicroDriver.ps1
   ```

   如果当前目录就是解压后的 `virtual-micro`，运行：

   ```powershell
   .\Install-CodexMicroDriver.ps1
   ```

3. 同意脚本自动弹出的 Windows UAC。末尾出现 `Ready` 即安装成功。
4. 以普通用户身份启动 `AgentController.exe`。点击标题栏的小键盘图标打开 Micro；Micro 默认置顶，右击机身空白处可切换置顶。

不要用管理员身份长期运行 Agent Controller，也不要关闭 Windows 驱动签名强制。

## 升级

- 普通应用或 Codex 更新：退出 Agent Controller，替换应用文件后重新启动。
- **不需要每次更新都重装驱动。** 只有驱动包本身更新、设备消失，或健康检查失败时，才重新运行安装脚本。
- 驱动版本不需要与 Codex 或 Agent Controller 版本一致。

## 验证与排错

安装脚本成功时最后显示 `Ready`。也可检查设备：

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
- Agent Controller Windows 发布包为自包含版本，不需要另装 .NET Runtime。

### 本机签名并安装预编译驱动

- Windows SDK `10.0.26100.0`（提供 SignTool）；
- 固定的 `Microsoft.Windows.WDK.x64` `10.0.26100.6584` NuGet 包（提供 Inf2Cat）；
- 如果该 NuGet 包尚未缓存，需要 Visual Studio/Build Tools 的 MSBuild 联网还原一次。

这条路线不需要 C++ 编译环境、Visual C++ Redistributable 或 .NET Runtime。

### 从源码重新构建

另需 Visual Studio Build Tools 2022 的 **使用 C++ 的桌面开发**、MSVC v143 x64/x86、x64/x86 Spectre 缓解库和 MSBuild。构建 Agent Controller 还需要 .NET SDK 10。

只有驱动安装阶段需要 UAC；不要导入来源不明的证书，也不要分发脚本在本机生成的测试证书或私钥。
