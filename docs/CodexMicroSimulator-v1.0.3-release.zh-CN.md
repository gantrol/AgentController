# Codex Micro Simulator v1.0.3

本版本聚焦界面美化，在保留 v1.0.2 连接、兼容性诊断与闲置灯光恢复能力的基础上，重新设计模拟器的水晶材质与控制符号。

## 界面改进

- 底板改为更通透、克制的水晶面板，收敛装饰层级并强化边缘折射与内部厚度。
- 按键统一为霜白水晶键帽，悬停仅调整照明反馈，不再产生不自然的抬升感。
- 十字键与控制符号改用一致的矢量图形，方向状态更清晰。
- 移除多余螺丝、装饰圆环和外框流光，保持简洁的科技感。
- 统一上方偏左的柔光方向、接触阴影与薄荷色内部余光，使材质关系更自然。

## 下载

- `CodexMicroSimulator-v1.0.3-win-x64-portable.zip`：Windows x64 自包含便携应用，无需另装 .NET Runtime。
- `CodexMicroSimulator-v1.0.3-win-x64-portable.zip.sha256`：便携包 SHA-256。
- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip`：沿用既有未签名开发者驱动包，本版本没有修改驱动二进制。
- `CodexMicroVhfUm-v1.0.0-win-x64-UNSIGNED-DEVELOPER.zip.sha256`：驱动包 SHA-256。

请先按安装教程审计、在本机签名并安装驱动，再完整解压便携包，以普通用户运行 `CodexMicroSimulator.exe`。

## 验证

- 协议、Broker 与桌面端自动化测试全部通过；
- Release 自包含发布构建通过；
- 当前样式通过 WPF 离屏渲染截图检查。

仅支持 Windows 10/11 x64。本独立项目与 OpenAI、Codex 或 Work Louder 没有隶属、授权或背书关系。
