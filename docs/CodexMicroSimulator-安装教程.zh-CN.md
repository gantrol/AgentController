# Codex Micro Simulator 安装与验证教程

## 适用范围

本教程适用于 Windows 10/11 x64，以及以下两个解压目录：

- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER`：未签名的开发者驱动包；
- `CodexMicroSimulator-v1.0.2-win-x64-portable`：自包含便携模拟器，无需另装 .NET Runtime。

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

驱动包自带 `SHA256SUMS.txt`。安装前应使用 `Get-FileHash -Algorithm SHA256` 对照检查包内文件。

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
cd "$env:USERPROFILE\Downloads\CodexMicroSimulator-v1.0.2-win-x64-portable"
.\CodexMicroSimulator.exe
```

应用采用单实例设计。启动后还会出现一个隐藏的 `--micro-broker` 子进程，它负责独占驱动句柄，并在模拟器与其他本机客户端之间协调 HID 输入和灯光输出。

## 5. 三盏状态灯

从上到下：

| 灯 | 含义 | 正常或警告状态 |
|---|---|---|
| 第一盏 | Codex 兼容性 | 精确匹配为蓝色；未收录、哈希变化或无法读取指纹时为黄色，并继续尝试连接 |
| 第二盏 | 虚拟 HID / Broker | 已连接时为中性色；未连接为黄色；运行中故障为红色 |
| 第三盏 | 最近事件 | 就绪时为中性色；已交付为蓝色；结果未知为黄色；发送失败为红色 |

Codex 版本或指纹变化只通过第一盏黄灯提示，常态连接文字不显示额外警告。悬停黄灯仍可查看原因；真正的驱动、Broker 或 HID 故障由后两盏灯按实际状态报告。

## 6. 屏幕灯光为何不会再自动熄灭

Codex 原生 Micro 服务面向实体键盘设计，会在无操作一段时间后给六个 Agent 槽位发送一帧 `off` 以节能。v1.0.0 把这帧直接画到窗口上，因此看起来像全部掉线。

v1.0.2 收到每次熄灯周期的第一帧全槽 `off` 后，会立即通过虚拟 HID 发送一次 `ACT11` 松开事件。Codex 的 Micro 服务会把任意 HID 事件视为灯光活动，恢复它保存的真实灯光模型；当前界面桥接层则明确忽略双键帽的 `ACT11`，因此不会提交、导航、切换任务，也不会留下按键按下状态。收到真实的非全灭帧后，才允许下一次节能唤醒，避免形成发送循环。

程序启动或重新连接时若还没有真实灯光帧，六个槽位保持熄灭，不再补中性蓝色。若已有真实灯色，`off` 到恢复帧之间可短暂保留上一帧，随后完全以 Codex 回传状态为准。

## 7. 重新连接与故障排查

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

v1.0.2 把指纹改为提示性诊断：精确匹配显示蓝灯；未知版本、已知资源变化、指纹读取失败或包布局变化显示第一盏黄灯，但仍继续尝试 HID 连接。只有 Codex 未安装，或真实的驱动/Broker/HID 链路失败，才会阻止正常使用并报告失败。

## 8. 验收标准

- Codex 新版本不再仅因“尚未写入清单”而被直接拒绝；
- 未审核的新构建以第一盏黄灯提示，并继续尝试连接；
- 已知构建发生指纹不一致时同样只亮第一盏黄灯，并继续尝试连接；
- 驱动状态为 `OK`，设备接口为 `Enabled`；
- 主进程和 `--micro-broker` 子进程均在运行；
- 六个 Agent 槽位在 Codex 进入 inactivity 状态后自动请求恢复 Codex 的真实灯色，且每个熄灯周期只发送一次无业务动作的 `ACT11` 松开事件；
- 启动时没有真实灯光快照就保持熄灭，不生成屏幕专用的假灯色；
- 协议、Broker 与桌面端自动化测试全部通过。
