# Codex Micro 驱动安装

[English](./CodexMicroSimulator-installation.md)

适用于 Windows 10/11 x64。驱动只提供手柄/虚拟 Micro 的 HID 通道，**不会读取或
检查 Codex、AgentController 的版本**。INF 中的驱动版本仅供 Windows 识别和更新
驱动包，不是兼容白名单。

## 安装

先退出 AgentController，然后在仓库根目录或解压后的驱动包目录打开 PowerShell：

```powershell
.\virtual-micro\Install-CodexMicroDriver.ps1
```

如果你位于解压后的 `virtual-micro` 目录，则运行：

```powershell
.\Install-CodexMicroDriver.ps1
```

脚本会自行弹出 Windows UAC。点“是”后，它会完成本机签名、安装/更新和健康检查。
不需要先手动打开“管理员 PowerShell”，也不要关闭 Windows 驱动签名强制。

## 验证

安装成功时，脚本最后会显示 `Ready`。也可以运行：

```powershell
Get-CimInstance Win32_PnPSignedDriver |
  Where-Object DeviceName -eq 'Codex Micro Simulator UMDF2 Virtual HID' |
  Select-Object DeviceName, DriverVersion, InfName

Get-PnpDevice -FriendlyName 'Codex Micro Simulator UMDF2 Virtual HID'
```

正常结果是设备只有一个、`Status` 为 `OK`。`DriverVersion` 是 Windows 驱动包版本，
不要求与 AgentController 或 Codex 版本一致。

AgentController 应以普通用户身份运行，不要以管理员身份运行。程序启动时会自动启用
手柄控制；顶部开关只暂停本次运行，退出程序就是完整停用。

## 安装失败

先看 `virtual-micro/driver-install.log`。最常见原因是安装包缺少 SignTool/Inf2Cat 或
驱动构建产物；完整的构建、二次签名和证书说明见
[`UNSIGNED-DRIVER.zh-CN.md`](../virtual-micro/UNSIGNED-DRIVER.zh-CN.md)。

不要导入来源不明的证书，也不要公开脚本创建的本机测试证书或私钥。
