# Codex Micro Simulator 安装与验证教程

[English](./CodexMicroSimulator-installation.md)

## 适用范围

本教程适用于 Windows 10/11 x64，以及以下两个解压目录：

- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER`：未签名的开发者驱动包；
- `CodexMicroSimulator-v1.0.0-win-x64-portable-compatible`：自包含便携模拟器，无需另装 .NET Runtime。

驱动包不是生产签名安装包。安装脚本会在本机创建不可导出的自签名代码签名证书，将公钥证书加入本机 `Root` 与 `TrustedPublisher`，签名 UMDF2 DLL，重新生成并签名 catalog，然后安装虚拟 HID。仅应在你已核验来源的开发或测试电脑上使用。

## 1. 准备环境

需要：

- 管理员权限；
- Windows SDK `10.0.26100.0` 的 `signtool.exe`；
- WDK NuGet 包 `Microsoft.Windows.WDK.x64` `10.0.26100.6584` 的 `Inf2Cat.exe`；
- 已安装并登录的 Codex Windows 应用。

默认工具位置：

```text
C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe
%USERPROFILE%\.nuget\packages\microsoft.windows.wdk.x64\10.0.26100.6584\c\bin\10.0.26100.0\x86\Inf2Cat.exe
```

驱动包自带 `SHA256SUMS.txt`。安装前应使用 `Get-FileHash -Algorithm SHA256` 对照检查包内文件。本次验证共检查 20 个条目，结果为 0 个不匹配。

## 2. 安装虚拟 HID 驱动

以管理员身份打开 PowerShell，进入驱动包目录后运行：

```powershell
cd "$env:USERPROFILE\Downloads\CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER"
.\Install-CodexMicroDriver.ps1
```

脚本按以下顺序执行：

1. 创建或复用 `CN=Codex Micro Simulator Driver` 本机证书；
2. 将公钥证书加入本机受信任根和受信任发布者；
3. 签名 `CodexMicroVhfUm.dll`；
4. 删除旧 catalog，并用 Inf2Cat 重新生成；
5. 签名新 catalog；
6. 安装或刷新 `Root\CodexMicroHidUm`；
7. 检查设备的 PnP 健康状态。

安装日志保存在驱动包根目录的 `driver-install.log`。

## 3. 验证驱动

在 PowerShell 中运行：

```powershell
$device = Get-CimInstance Win32_PnPEntity |
    Where-Object { @($_.HardwareID) -contains 'Root\CodexMicroHidUm' }

$device | Select-Object Name, Status, ConfigManagerErrorCode, PNPDeviceID

pnputil /enum-interfaces /class '{E2A7CB54-8420-4D51-9DD8-D6575B9251D1}'
```

正常结果应满足：

- 设备名为 `Codex Micro Simulator UMDF2 Virtual HID`；
- `Status` 为 `OK`；
- `ConfigManagerErrorCode` 为 `0`；
- 自定义设备接口状态为 `Enabled`。

## 4. 启动便携模拟器

模拟器应以普通用户运行，不要以管理员身份运行：

```powershell
cd "$env:USERPROFILE\Downloads\CodexMicroSimulator-v1.0.0-win-x64-portable-compatible"
.\CodexMicroSimulator.exe
```

应用采用单实例设计。启动后还会出现一个隐藏的 `--micro-broker` 子进程，它负责独占驱动句柄，并在模拟器与其他本机客户端之间协调 HID 输入和灯光输出。

## 5. 三盏状态灯

从上到下：

| 灯 | 含义 | 正常或警告状态 |
|---|---|---|
| 第一盏 | Codex 兼容性 | 已审核构建为蓝色；未收录的新构建为黄色，但仍继续连接；已知构建发生哈希不匹配时为红色并阻止连接 |
| 第二盏 | 虚拟 HID / Broker | 已连接时为中性色；未连接为黄色；运行中故障为红色 |
| 第三盏 | 最近事件 | 就绪时为中性色；已交付为蓝色；结果未知为黄色；发送失败为红色 |

未知 Codex 版本只通过第一盏黄灯提示，常态连接文字不显示额外警告。悬停黄灯仍可查看原因。

## 6. 重新连接与故障排查

右击左下角黑色设置旋钮，可重新执行兼容性检查和 HID/Broker 连接。

如果第三盏灯变红并提示“虚拟 HID 链路尚未就绪”：

1. 先不要继续按其他动作键，以免覆盖真正的首次错误；
2. 悬停第一、第二盏灯读取兼容性和驱动状态；
3. 右击黑色旋钮重新连接；
4. 检查 `driver-install.log`、设备状态和接口状态；
5. 确认没有旧版模拟器进程占用同一个单实例锁。

原版 v1.0.0 会对未写入清单的新 Codex 构建执行硬阻止。例如 Codex `26.715.4045.0` 会显示：

```text
This Codex build has no reviewed Micro compatibility manifest.
```

兼容版改为三态策略：精确审核通过、未知版本黄灯继续、已知异常红灯阻止。它不会直接关闭哈希校验。

## 7. 本次验收结果

- Codex：`26.715.4045.0`；
- UMDF2/VHF 驱动：`1.0.0.5`；
- 设备状态：`OK`，PnP 错误码 `0`；
- 自定义接口：`Enabled`；
- 自动化测试：协议 5 项、Broker 11 项、桌面 47 项，共 63 项全部通过；
- 现场运行：主进程和 `--micro-broker` 子进程稳定运行；
- 窗口状态：`CodexMicroVhfUm / Broker 已连接`；
- Codex Agent 灯光状态已通过驱动输出链路同步到六个模拟器槽位。
