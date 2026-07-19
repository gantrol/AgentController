# Codex Micro Simulator v1.0.2

本版本让屏幕模拟器主动抵消 Codex 面向实体设备的闲置熄灯，并把 Micro 资源指纹检查改为提示性诊断。

## 修复

- Codex 发送六槽 `off` 节能帧后，模拟器立即回送一次 `ACT11` 松开事件，请求 Codex 恢复其真实灯光模型。
- `ACT11` 松开事件会被当前 Codex Micro 灯光服务计为输入活动，但被界面桥接层明确忽略，不会提交、导航、切换任务或留下按下状态。
- 每次熄灯周期只发送一次唤醒事件；收到真实的非全灭灯光帧后，才允许下一次节能唤醒，避免循环发送。
- 启动和重新连接时不再伪造中性蓝灯；尚未收到真实灯光时保持熄灭。
- 未收录的新 Codex、已收录构建的资源哈希变化、指纹无法读取或包布局变化，均只以第一盏黄灯提示并继续尝试连接，不再因指纹异常显示红灯或拒绝连接。
- 常态连接文字不显示黄色告警；诊断原因只放在第一盏黄灯的悬停提示中。真正的驱动、Broker 或 HID 失败仍按实际链路状态报告。

## 下载

- `CodexMicroSimulator-v1.0.2-win-x64-portable.zip`：Windows x64 自包含便携应用，无需另装 .NET Runtime。
- `CodexMicroSimulator-v1.0.2-win-x64-portable.zip.sha256`：便携包 SHA-256。
- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip`：沿用 v1.0.0 的未签名开发者驱动包，本版本没有修改驱动二进制。
- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip.sha256`：驱动包 SHA-256。

请先按安装教程审计、在本机签名并安装驱动，再完整解压便携包，以普通用户运行 `CodexMicroSimulator.exe`。

## 验证

- 协议测试 5 项通过；
- Broker 测试 16 项通过；
- 桌面端测试 57 项通过；
- 共 78 项自动化测试全部通过；
- 当前 Codex `26.715.4045.0` 的灯光活动路径与 `ACT11` 忽略路径已核对；
- 当前 Windows 虚拟 HID、Broker、自动连接和节能熄灯后恢复灯光的实机链路已验证。

仅支持 Windows 10/11 x64。本独立项目与 OpenAI、Codex 或 Work Louder 没有隶属、授权或背书关系。
