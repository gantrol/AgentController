# Codex Micro Simulator v1.0.1

本版本修复了遇到新版 Codex（ChatGPT）时，仅因该构建尚未进入审核清单就直接拒绝连接的问题。

## 修复

- 新增兼容性三态：已审核构建正常连接；未审核的新构建以第一盏黄灯提示并继续连接；已知构建指纹不匹配仍以红灯阻止。
- 补充 Codex `26.715.4045.0` 的精确 Micro 资源指纹。
- 屏幕模拟器不再套用实体键盘的闲置熄灯表现：收到全槽 `off` 帧时保留最近一次有效灯色；如果启动时已处于闲置，则使用中性蓝色常亮；下一帧真实状态到来后继续实时更新。
- 常态连接文字不显示黄色告警；兼容模式的详细原因只保留在第一盏黄灯的悬停提示中。

## 说明

该兼容策略没有关闭安全检查。能够读取 `app.asar` 但尚未审核的新版本允许尝试连接；已经收录的构建如果资源哈希变化，仍会被判定为不兼容并拒绝连接。

安装与验证步骤见 `CodexMicroSimulator-安装教程.zh-CN.md`。

## 下载

- `CodexMicroSimulator-v1.0.1-win-x64-portable.zip`：Windows x64 自包含便携应用，无需另装 .NET Runtime。
- `CodexMicroSimulator-v1.0.1-win-x64-portable.zip.sha256`：便携包 SHA-256。
- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip`：沿用 v1.0.0 的未签名开发者驱动包，本版本没有修改驱动二进制。
- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip.sha256`：驱动包 SHA-256。

请先按教程审计、在本机签名并安装驱动，再完整解压便携包，以普通用户运行 `CodexMicroSimulator.exe`。

## 验证

- 协议测试 5 项通过；
- Broker 测试 16 项通过；
- 桌面端测试 53 项通过；
- 共 74 项自动化测试全部通过；
- 当前 Codex `26.715.4045.0` 的精确指纹与 HID/Broker 实机链路已验证。

仅支持 Windows 10/11 x64。本独立项目与 OpenAI、Codex 或 Work Louder 没有隶属、授权或背书关系。
